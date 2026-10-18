using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortFlow.Identity.Api;
using PortFlow.Identity.Api.Events;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Api.Events;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Runtime;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Application;

/// <summary>Module hồ sơ đăng ký <see cref="IApprovableDocumentHandler"/>; workflow tra theo loại hồ sơ.</summary>
public sealed class HandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IApprovableDocumentHandler> _handlers;

    public HandlerRegistry(IEnumerable<IApprovableDocumentHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.DocumentType);
    }

    public IApprovableDocumentHandler Require(string documentType) =>
        _handlers.GetValueOrDefault(documentType)
        ?? throw new DomainException(WorkflowError.WF_UNKNOWN_DOCUMENT_TYPE, "No module handles document type " + documentType);

    public bool IsRegistered(string documentType) => _handlers.ContainsKey(documentType);

    public IReadOnlyCollection<string> DocumentTypes => _handlers.Keys.ToList();
}

/// <summary>
/// Ghi event <see cref="ActionDenied"/> (audit ATTEMPT_DENIED) trong transaction RIÊNG để không mất khi thao tác chính
/// bị rollback (= REQUIRES_NEW).
/// </summary>
public sealed class DeniedActionAuditor(IServiceScopeFactory scopes)
{
    public async Task RecordAsync(Guid stepInstanceId, Guid userId, string reason, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var metadata = services.GetRequiredService<EventMetadataFactory>();
        var events = services.GetRequiredService<DomainEventPublisher>();
        await services.GetRequiredService<ITransactionRunner>().ExecuteAsync(token =>
            events.PublishAsync(new ActionDenied(metadata.Create(), stepInstanceId, userId, reason), token), ct);
    }
}

/// <summary>= <c>@stepAuth</c>: assignee hoặc người được ủy quyền (không phải người gửi) mới thao tác được bước.</summary>
internal sealed class StepAuthorization(
    DbSession session,
    IDelegationQuery delegations,
    DeniedActionAuditor deniedActions,
    IBusinessClock clock) : IStepAuthorization
{
    public async Task<bool> CanActAsync(Guid stepId, ICurrentUser user, CancellationToken ct = default)
    {
        if (user.IsSystem)
        {
            return false;
        }
        var connection = await session.OpenAsync(ct);
        var step = await connection.QuerySingleOrDefaultAsync<StepRow>(new CommandDefinition("""
            SELECT s.assignee_user_ids AS Assignees, i.document_type AS DocumentType, i.submitter_id AS SubmitterId
            FROM workflow.step_instance s JOIN workflow.workflow_instance i ON i.id = s.instance_id
            WHERE s.id = @stepId
            """, new { stepId }, session.Transaction, cancellationToken: ct))
                   ?? throw new DomainException(WorkflowError.WF_STEP_NOT_FOUND);
        var delegators = await delegations.ActiveDelegatorsOfAsync(user.Id, step.DocumentType, clock.Today(), ct);
        if (StepActorPolicy.MayAct(step.Assignees, step.SubmitterId, user.Id, delegators))
        {
            return true;
        }
        await deniedActions.RecordAsync(stepId, user.Id, "NOT_ASSIGNEE", ct);
        return false;
    }

    private sealed record StepRow
    {
        public Guid[] Assignees { get; init; } = [];

        public string DocumentType { get; init; } = null!;

        public Guid SubmitterId { get; init; }
    }
}

/// <summary>User bị vô hiệu hóa: bước đang chờ không còn ai active để duyệt → phát cảnh báo cho Admin.</summary>
internal sealed class AssigneeDeactivationListener(
    DbSession session,
    IUserDirectory users,
    DomainEventPublisher events,
    EventMetadataFactory eventMetadata,
    ILogger<AssigneeDeactivationListener> log) : IDomainEventListener<UserDeactivated>
{
    public async Task HandleAsync(UserDeactivated @event, CancellationToken cancellationToken)
    {
        var connection = await session.OpenAsync(cancellationToken);
        var steps = await connection.QueryAsync<PendingStep>(new CommandDefinition("""
            SELECT s.id AS Id, s.instance_id AS InstanceId, s.assignee_user_ids AS Assignees,
                   i.document_type AS DocumentType, i.document_id AS DocumentId
            FROM workflow.step_instance s JOIN workflow.workflow_instance i ON i.id = s.instance_id
            WHERE s.status = 'PENDING' AND s.assignee_user_ids @> ARRAY[@userId]::uuid[]
            """, new { userId = @event.UserId }, session.Transaction, cancellationToken: cancellationToken));
        foreach (var step in steps)
        {
            var nobodyLeft = true;
            foreach (var assignee in step.Assignees.Where(id => id != @event.UserId))
            {
                if (await users.IsActiveAsync(assignee, cancellationToken))
                {
                    nobodyLeft = false;
                    break;
                }
            }
            if (!nobodyLeft)
            {
                continue;
            }
            log.LogWarning("Pending step {StepId} of {DocumentType} {DocumentId} has no active assignee after user {UserId} was deactivated",
                step.Id, step.DocumentType, step.DocumentId, @event.UserId);
            await events.PublishAsync(new StepAssigneeDeactivated(eventMetadata.Create(), step.InstanceId, step.Id,
                step.DocumentType, step.DocumentId, @event.UserId), cancellationToken);
        }
    }

    private sealed record PendingStep
    {
        public Guid Id { get; init; }

        public Guid InstanceId { get; init; }

        public Guid[] Assignees { get; init; } = [];

        public string DocumentType { get; init; } = null!;

        public Guid DocumentId { get; init; }
    }
}

/// <summary>Báo quá hạn SLA đúng một lần cho mỗi bước đang chờ.</summary>
public sealed class SlaService(
    DbSession session,
    WorkflowDbContext db,
    ITransactionRunner tx,
    DomainEventPublisher events,
    EventMetadataFactory eventMetadata,
    IBusinessClock clock,
    WorkflowExceptionTranslator translator,
    ILogger<SlaService> log)
{
    public async Task<int> NotifyOverdueAsync(int batchSize, CancellationToken ct = default)
    {
        var connection = await session.OpenAsync(ct);
        var overdue = (await connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT id FROM workflow.step_instance
            WHERE status = 'PENDING' AND overdue_notified_at IS NULL AND due_at < @now
            ORDER BY due_at LIMIT @limit
            """, new { now = clock.Now(), limit = batchSize }, session.Transaction, cancellationToken: ct))).ToList();
        var notified = 0;
        foreach (var stepId in overdue)
        {
            try
            {
                notified += await tx.ExecuteAsync(token => NotifyOneAsync(stepId, token), ct) ? 1 : 0;
            }
            catch (Exception ex) when (ex is DomainException or DbUpdateException)
            {
                db.ChangeTracker.Clear();
                log.LogWarning("Skip overdue notification for step {StepId} (will retry next run): {Reason}", stepId, ex.Message);
            }
        }
        return notified;
    }

    private async Task<bool> NotifyOneAsync(Guid stepId, CancellationToken ct)
    {
        var instanceId = await db.StepInstances.Where(s => s.Id == stepId)
            .Select(s => (Guid?)EF.Property<Guid>(s, "InstanceId")).SingleOrDefaultAsync(ct);
        var instance = instanceId is null ? null : await db.Instances.SingleOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null)
        {
            return false;
        }
        var step = instance.StepById(stepId);
        if (step.Status != StepStatus.PENDING || step.OverdueNotifiedAt is not null)
        {
            return false;
        }
        instance.MarkOverdueNotified(stepId, clock.Now());
        await translator.SaveAsync(db, ct);
        await events.PublishAsync(new StepOverdue(eventMetadata.Create(), instance.Id, step.Id, step.Name,
            step.AssigneeUserIds, step.DueAt), ct);
        return true;
    }
}
