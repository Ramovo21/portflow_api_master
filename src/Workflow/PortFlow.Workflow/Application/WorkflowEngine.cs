using Microsoft.EntityFrameworkCore;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Time;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Api.Events;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Domain.Runtime;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Application;

public sealed record ActOnStepCommand(Guid? InstanceId, Guid StepId, Guid UserId, ActionType Action, string? Comment,
    long? ExpectedVersion);

public sealed record StepActionResult(Guid InstanceId, string Status, long? Version, StepInstance? CompletedStep,
    StepInstance? CurrentStep);

/// <summary>Bộ máy chạy quy trình (= WorkflowEngine của bản Java). Mọi thao tác chạy trong một transaction.</summary>
public sealed class WorkflowEngine(
    WorkflowDbContext db,
    ITransactionRunner tx,
    HandlerRegistry handlers,
    IConditionEvaluator conditions,
    IAssigneeResolver resolver,
    IUserDirectory users,
    IDocumentTypeQuery documentTypes,
    IDelegationQuery delegations,
    DeniedActionAuditor deniedActions,
    DomainEventPublisher events,
    EventMetadataFactory eventMetadata,
    IBusinessClock clock,
    WorkflowExceptionTranslator translator) : IWorkflowService
{
    public Task<WorkflowStartResult> StartAsync(StartWorkflowCommand command, CancellationToken ct = default) =>
        tx.ExecuteAsync(async token =>
        {
            var handler = handlers.Require(command.DocumentType);
            if (await ActiveInstances(command.DocumentType, command.DocumentId).AnyAsync(token))
            {
                throw new DomainException(WorkflowError.WF_ALREADY_RUNNING);
            }
            var definition = await FindActiveDefinitionAsync(command.DocumentType, token)
                             ?? throw new DomainException(WorkflowError.WF_NO_ACTIVE_DEFINITION,
                                 $"Document type {command.DocumentType} has no active workflow definition");
            var now = clock.Now();
            var submitter = await users.FindAsync(command.SubmitterId, token);
            var vars = await EnrichAsync(command.ContextVars, command.DocumentType, command.SubmitterId, token);
            var instance = WorkflowInstance.Create(UuidV7.Next(), definition, command.DocumentId, command.SubmitterId,
                vars, now);
            var summary = await handler.SummarizeAsync(command.DocumentId, token);
            instance.AttachSummary(SummaryMap(summary));
            var pending = new List<IDomainEvent>();
            var opened = await instance.AdvanceAsync(definition, 0, conditions, resolver,
                ResolveContextOf(instance, submitter), now, token);
            await AfterAdvanceAsync(instance, definition, handler, summary, opened, now, pending, token);
            db.Instances.Add(instance);
            await translator.SaveAsync(db, token);
            await PublishAsync(pending, token);
            return StartResult(instance);
        }, ct);

    public Task<WorkflowStartResult> ResubmitAsync(string documentType, Guid documentId, Guid userId,
        IReadOnlyDictionary<string, object?>? contextVars, CancellationToken ct = default) =>
        tx.ExecuteAsync(async token =>
        {
            var handler = handlers.Require(documentType);
            var instance = await ActiveInstances(documentType, documentId).FirstOrDefaultAsync(token)
                           ?? throw new DomainException(WorkflowError.WF_INVALID_STATE,
                               "Document has no workflow awaiting revision");
            var definition = await PinnedDefinitionAsync(instance, token);
            var now = clock.Now();
            var submitter = await users.FindAsync(instance.SubmitterId, token);
            var vars = await EnrichAsync(contextVars, documentType, instance.SubmitterId, token);
            var opened = await instance.ResubmitAsync(userId, vars, definition, conditions, resolver,
                ResolveContextOf(instance, submitter), now, token);
            var summary = await handler.SummarizeAsync(documentId, token);
            instance.AttachSummary(SummaryMap(summary));
            var pending = new List<IDomainEvent>();
            await AfterAdvanceAsync(instance, definition, handler, summary, opened, now, pending, token);
            await translator.SaveAsync(db, token);
            await PublishAsync(pending, token);
            return StartResult(instance);
        }, ct);

    public Task<StepActionResult> ActAsync(ActOnStepCommand command, CancellationToken ct = default) =>
        tx.ExecuteAsync(async token =>
        {
            var instance = await FindByStepIdAsync(command.StepId, token);
            if (instance is null || (command.InstanceId is { } expected && instance.Id != expected))
            {
                throw new DomainException(WorkflowError.WF_STEP_NOT_FOUND);
            }
            if (command.ExpectedVersion is { } version && version != instance.Version)
            {
                throw new DomainException(WorkflowError.WF_CONFLICT, "Document has changed, please reload");
            }
            var step = instance.StepById(command.StepId);
            var now = clock.Now();
            var result = await ActAsync(instance, step, command, now, token);
            var definition = await PinnedDefinitionAsync(instance, token);
            var handler = handlers.Require(instance.DocumentType);
            var summary = SummaryOf(instance);
            var pending = new List<IDomainEvent>();
            if (result.Result != ActResult.Outcome.VOTE_RECORDED)
            {
                pending.Add(StepCompleted(instance, step, result.OnBehalfOf));
            }
            switch (result.Result)
            {
                case ActResult.Outcome.STEP_APPROVED:
                    await translator.SaveAsync(db, token);
                    var submitter = await users.FindAsync(instance.SubmitterId, token);
                    var opened = await instance.AdvanceAsync(definition, step.StepOrder, conditions, resolver,
                        ResolveContextOf(instance, submitter), now, token);
                    await AfterAdvanceAsync(instance, definition, handler, summary, opened, now, pending, token);
                    break;
                case ActResult.Outcome.REJECTED:
                    await handler.OnRejectedAsync(instance.DocumentId, step.Comment, token);
                    pending.Add(new DocumentRejected(eventMetadata.Create(), instance.Id, instance.DocumentType,
                        instance.DocumentId, instance.SubmitterId, command.UserId, step.Comment, summary));
                    break;
                case ActResult.Outcome.REVISION_REQUESTED:
                    await handler.OnRevisionRequestedAsync(instance.DocumentId, step.Comment, token);
                    pending.Add(new RevisionRequested(eventMetadata.Create(), instance.Id, instance.DocumentType,
                        instance.DocumentId, instance.SubmitterId, command.UserId, step.StepOrder, step.Comment, summary));
                    break;
            }
            await translator.SaveAsync(db, token);
            await PublishAsync(pending, token);
            return new StepActionResult(instance.Id, instance.Status.ToString(), instance.Version,
                step.Status == StepStatus.COMPLETED ? step : null, instance.CurrentStep());
        }, ct);

    public async Task<WorkflowProgress?> ProgressOfAsync(string documentType, Guid documentId,
        CancellationToken ct = default)
    {
        var instance = await db.Instances.AsNoTracking()
            .Where(i => i.DocumentType == documentType && i.DocumentId == documentId)
            .OrderByDescending(i => i.SubmittedAt)
            .FirstOrDefaultAsync(ct);
        return instance is null ? null : Progress(instance, await PinnedDefinitionAsync(instance, ct));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkflowProgress>> ProgressOfAsync(string documentType,
        IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, WorkflowProgress>();
        if (documentIds.Count == 0)
        {
            return result;
        }
        var ids = documentIds.ToList();
        var definitions = new Dictionary<Guid, WorkflowDefinition>();
        var found = await db.Instances.AsNoTracking()
            .Where(i => i.DocumentType == documentType && ids.Contains(i.DocumentId))
            .OrderByDescending(i => i.SubmittedAt)
            .ToListAsync(ct);
        foreach (var instance in found)
        {
            if (result.ContainsKey(instance.DocumentId))
            {
                continue;
            }
            if (!definitions.TryGetValue(instance.DefinitionId, out var definition))
            {
                definition = await PinnedDefinitionAsync(instance, ct);
                definitions[instance.DefinitionId] = definition;
            }
            result[instance.DocumentId] = Progress(instance, definition);
        }
        return result;
    }

    public async Task<IReadOnlyList<WorkflowHistoryEntry>> HistoryOfAsync(string documentType, Guid documentId,
        CancellationToken ct = default)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(i => i.DocumentType == documentType && i.DocumentId == documentId)
            .ToListAsync(ct);
        return instances
            .SelectMany(instance => instance.Steps.SelectMany(step => step.Actions.Select(action =>
                new WorkflowHistoryEntry(instance.Id, action.At, step.Round, step.StepOrder, step.Name,
                    action.Action.ToString(), action.UserId, action.OnBehalfOf, action.Comment))))
            .OrderBy(entry => entry.At)
            .ToList();
    }

    public Task CancelAsync(string documentType, Guid documentId, Guid? userId, string? reason,
        CancellationToken ct = default) =>
        tx.ExecuteAsync(async token =>
        {
            var instance = await ActiveInstances(documentType, documentId).FirstOrDefaultAsync(token);
            if (instance is null)
            {
                return;
            }
            instance.Cancel(userId, reason, clock.Now());
            await translator.SaveAsync(db, token);
            await events.PublishAsync(new WorkflowCancelled(eventMetadata.Create(), instance.Id, documentType,
                documentId, reason), token);
        }, ct);

    internal Task<WorkflowDefinition> PinnedDefinitionAsync(WorkflowInstance instance, CancellationToken ct) =>
        PinnedDefinitionAsync(db, instance, ct);

    internal static async Task<WorkflowDefinition> PinnedDefinitionAsync(WorkflowDbContext db, WorkflowInstance instance,
        CancellationToken ct) =>
        await db.Definitions.SingleOrDefaultAsync(d => d.Id == instance.DefinitionId, ct)
        ?? throw new InvalidOperationException($"Definition {instance.DefinitionId} missing");

    internal static DocumentSummary SummaryOf(WorkflowInstance instance)
    {
        if (instance.ContextVars.GetValueOrDefault(WorkflowInstance.SummaryKey) is not IReadOnlyDictionary<string, object?> map)
        {
            return DocumentSummary.Empty();
        }
        var amount = map.GetValueOrDefault("amount");
        return new DocumentSummary(Text(map.GetValueOrDefault("number")), Text(map.GetValueOrDefault("title")),
            Text(map.GetValueOrDefault("customerName")),
            amount is null ? null : decimal.Parse(Convert.ToString(amount, System.Globalization.CultureInfo.InvariantCulture)!,
                System.Globalization.CultureInfo.InvariantCulture),
            Text(map.GetValueOrDefault("currency")));
    }

    private IQueryable<WorkflowInstance> ActiveInstances(string documentType, Guid documentId) =>
        db.Instances.Where(i => i.DocumentType == documentType && i.DocumentId == documentId
                                && (i.Status == InstanceStatus.RUNNING || i.Status == InstanceStatus.REVISION_REQUESTED));

    private Task<WorkflowDefinition?> FindActiveDefinitionAsync(string documentType, CancellationToken ct) =>
        db.Definitions.SingleOrDefaultAsync(d => d.DocumentType == documentType && d.Status == DefinitionStatus.ACTIVE, ct);

    private async Task<WorkflowInstance?> FindByStepIdAsync(Guid stepId, CancellationToken ct)
    {
        var instanceId = await db.StepInstances.Where(s => s.Id == stepId)
            .Select(s => (Guid?)EF.Property<Guid>(s, "InstanceId"))
            .SingleOrDefaultAsync(ct);
        return instanceId is null ? null : await db.Instances.SingleOrDefaultAsync(i => i.Id == instanceId, ct);
    }

    private async Task<ActResult> ActAsync(WorkflowInstance instance, StepInstance step, ActOnStepCommand command,
        Instant now, CancellationToken ct)
    {
        var delegators = await delegations.ActiveDelegatorsOfAsync(command.UserId, instance.DocumentType, clock.Today(), ct);
        try
        {
            return instance.Act(step.Id, command.UserId, delegators, command.Action, command.Comment, now);
        }
        catch (DomainException denied) when (Equals(denied.Code, WorkflowError.WF_NOT_ASSIGNEE))
        {
            await deniedActions.RecordAsync(step.Id, command.UserId, "NOT_ASSIGNEE", ct);
            throw;
        }
    }

    private async Task AfterAdvanceAsync(WorkflowInstance instance, WorkflowDefinition definition,
        IApprovableDocumentHandler handler, DocumentSummary summary, StepInstance? opened, Instant now,
        List<IDomainEvent> pending, CancellationToken ct)
    {
        if (opened is not null)
        {
            await handler.OnStepStartedAsync(instance.DocumentId, opened.StepOrder, ct);
            pending.Add(new StepAssigned(eventMetadata.Create(), instance.Id, instance.DocumentType, instance.DocumentId,
                instance.Round, opened.StepOrder, opened.Name, opened.AssigneeUserIds, opened.DueAt, instance.SubmitterId,
                summary));
            return;
        }
        var eSign = definition.EmitESignOnFinalApprove && await RequiresESignAsync(instance.DocumentType, ct);
        await handler.OnApprovedAsync(instance.DocumentId, new ApprovalOutcome(instance.Id, now, eSign), ct);
        pending.Add(new DocumentApproved(eventMetadata.Create(), instance.Id, instance.DocumentType, instance.DocumentId,
            instance.SubmitterId, eSign, summary));
    }

    private StepCompleted StepCompleted(WorkflowInstance instance, StepInstance step, Guid? onBehalfOf) =>
        new(eventMetadata.Create(), instance.Id, instance.DocumentType, instance.DocumentId, step.Round, step.StepOrder,
            step.Name, step.ActedBy, onBehalfOf, step.Decision!.Value.ToString(), step.Comment);

    private async Task<bool> RequiresESignAsync(string documentType, CancellationToken ct)
    {
        try
        {
            return (await documentTypes.GetAsync(documentType, ct)).RequiresESign;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private async Task PublishAsync(IEnumerable<IDomainEvent> pending, CancellationToken ct)
    {
        foreach (var @event in pending)
        {
            await events.PublishAsync(@event, ct);
        }
    }

    private async Task<Dictionary<string, object?>> EnrichAsync(IReadOnlyDictionary<string, object?>? contextVars,
        string documentType, Guid submitterId, CancellationToken ct)
    {
        var vars = new Dictionary<string, object?>();
        foreach (var (key, value) in contextVars ?? new Dictionary<string, object?>())
        {
            if (key != WorkflowInstance.SummaryKey && value is not null)
            {
                vars[key] = value;
            }
        }
        vars["docType"] = documentType;
        vars.Remove("department");
        if (await users.DepartmentCodeOfAsync(submitterId, ct) is { } code)
        {
            vars["department"] = code;
        }
        return vars;
    }

    internal static ResolveContext ResolveContextOf(WorkflowInstance instance, UserRef? submitter) =>
        new(instance.SubmitterId, submitter?.DepartmentId, instance.DocumentType, instance.ContextVars);

    private static WorkflowProgress Progress(WorkflowInstance instance, WorkflowDefinition definition)
    {
        var current = instance.CurrentStep();
        var done = instance.StepsOfRound(instance.Round)
            .Count(step => step.Status == StepStatus.SKIPPED || step.Decision == Decision.APPROVED);
        return new WorkflowProgress(instance.Id, instance.Status.ToString(), instance.Round, current?.StepOrder,
            current?.Name, current?.AssigneeUserIds ?? [], definition.Steps.Count, done);
    }

    private static Dictionary<string, object?> SummaryMap(DocumentSummary? summary) => summary is null
        ? new Dictionary<string, object?>()
        : new Dictionary<string, object?>
        {
            ["number"] = summary.Number,
            ["title"] = summary.Title,
            ["customerName"] = summary.CustomerName,
            ["amount"] = summary.Amount,
            ["currency"] = summary.Currency,
        };

    private static string? Text(object? value) => value switch
    {
        null => null,
        decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static WorkflowStartResult StartResult(WorkflowInstance instance)
    {
        var current = instance.CurrentStep();
        return new WorkflowStartResult(instance.Id, instance.Status.ToString(), instance.Round, current?.Id,
            current?.StepOrder, current?.Name, current?.AssigneeUserIds ?? [], current?.DueAt);
    }
}
