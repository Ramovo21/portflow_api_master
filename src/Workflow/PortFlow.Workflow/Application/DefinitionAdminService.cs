using Microsoft.EntityFrameworkCore;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Workflow.Api.Events;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Domain.Runtime;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Application;

public sealed record SavedDefinition(WorkflowDefinition Definition, IReadOnlyList<Violation> Warnings);

public sealed record ActivatedDefinition(WorkflowDefinition Definition, long RunningInstancesOnOldVersions);

public sealed record SimulatedAssignee(Guid Id, string? FullName);

public sealed record SimulatedStep(int Order, string? Name, bool Applies, string? Reason,
    IReadOnlyList<SimulatedAssignee> Assignees);

public sealed record Simulation(IReadOnlyList<SimulatedStep> Path, IReadOnlyList<string> Warnings);

/// <summary>Quản trị định nghĩa quy trình: soạn nháp, kiểm tra V1–V8, kích hoạt version mới, mô phỏng đường duyệt.</summary>
public sealed class DefinitionAdminService
{
    private readonly WorkflowDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly DefinitionValidator _validator;
    private readonly IConditionEvaluator _conditions;
    private readonly IAssigneeResolver _resolver;
    private readonly IUserDirectory _users;
    private readonly DomainEventPublisher _events;
    private readonly EventMetadataFactory _eventMetadata;
    private readonly IBusinessClock _clock;
    private readonly WorkflowExceptionTranslator _translator;

    public DefinitionAdminService(WorkflowDbContext db, ITransactionRunner tx, IConditionEvaluator conditions,
        IAssigneeDirectory directory, IAssigneeResolver resolver, HandlerRegistry handlers, IUserDirectory users,
        DomainEventPublisher events, EventMetadataFactory eventMetadata, IBusinessClock clock,
        WorkflowExceptionTranslator translator)
    {
        _db = db;
        _tx = tx;
        _validator = new DefinitionValidator(conditions, directory, handlers.IsRegistered);
        _conditions = conditions;
        _resolver = resolver;
        _users = users;
        _events = events;
        _eventMetadata = eventMetadata;
        _clock = clock;
        _translator = translator;
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(string? documentType, DefinitionStatus? status,
        CancellationToken ct)
    {
        var query = _db.Definitions.AsNoTracking();
        if (documentType is not null)
        {
            query = query.Where(d => d.DocumentType == documentType);
        }
        if (status is { } wanted)
        {
            query = query.Where(d => d.Status == wanted);
        }
        return await query.OrderBy(d => d.DocumentType).ThenByDescending(d => d.Version).ToListAsync(ct);
    }

    public Task<WorkflowDefinition> GetAsync(Guid id, CancellationToken ct) => FindAsync(id, ct);

    public Task<SavedDefinition> CreateAsync(ICurrentUser actor, DefinitionContent content, LocalDate? effectiveFrom,
        CancellationToken ct) =>
        _tx.ExecuteAsync(async token =>
        {
            var warnings = await RequireValidAsync(content, false, token);
            var version = await MaxVersionAsync(content.DocumentType!, token) + 1;
            var draft = WorkflowDefinition.Draft(UuidV7.Next(), version, content, actor.Id, _clock.Now());
            draft.Update(content, effectiveFrom, actor.Id, _clock.Now());
            _db.Definitions.Add(draft);
            await _translator.SaveAsync(_db, token);
            return new SavedDefinition(draft, warnings);
        }, ct);

    public Task<SavedDefinition> UpdateAsync(ICurrentUser actor, Guid id, DefinitionContent content,
        LocalDate? effectiveFrom, CancellationToken ct) =>
        _tx.ExecuteAsync(async token =>
        {
            var definition = await FindAsync(id, token);
            var normalized = content.WithDocumentType(content.DocumentType ?? definition.DocumentType);
            if (definition.Status != DefinitionStatus.DRAFT)
            {
                throw new DomainException(WorkflowError.WF_DEFINITION_NOT_EDITABLE);
            }
            var warnings = await RequireValidAsync(normalized, false, token);
            definition.Update(normalized, effectiveFrom, actor.Id, _clock.Now());
            await _translator.SaveAsync(_db, token);
            return new SavedDefinition(definition, warnings);
        }, ct);

    public Task<WorkflowDefinition> CloneAsDraftAsync(ICurrentUser actor, Guid id, CancellationToken ct) =>
        _tx.ExecuteAsync(async token =>
        {
            var source = await FindAsync(id, token);
            var version = await MaxVersionAsync(source.DocumentType, token) + 1;
            var copy = source.CloneAs(UuidV7.Next(), version, actor.Id, _clock.Now());
            _db.Definitions.Add(copy);
            await _translator.SaveAsync(_db, token);
            return copy;
        }, ct);

    public async Task<IReadOnlyList<Violation>> ValidateAsync(Guid id, CancellationToken ct) =>
        await _validator.ValidateAsync((await FindAsync(id, ct)).Content, true, ct);

    public Task<ActivatedDefinition> ActivateAsync(ICurrentUser actor, Guid id, CancellationToken ct) =>
        _tx.ExecuteAsync(async token =>
        {
            var definition = await FindAsync(id, token);
            await RequireValidAsync(definition.Content, true, token);
            var active = await _db.Definitions.SingleOrDefaultAsync(
                d => d.DocumentType == definition.DocumentType && d.Status == DefinitionStatus.ACTIVE, token);
            if (active is not null && active.Id != definition.Id)
            {
                active.Retire(actor.Id, _clock.Now());
                await _translator.SaveAsync(_db, token);
            }
            definition.Activate(actor.Id, _clock.Now());
            await _translator.SaveAsync(_db, token);
            var runningOnOld = await _db.Instances.LongCountAsync(i => i.DocumentType == definition.DocumentType
                && i.DefinitionId != definition.Id
                && (i.Status == InstanceStatus.RUNNING || i.Status == InstanceStatus.REVISION_REQUESTED), token);
            await _events.PublishAsync(new WorkflowDefinitionActivated(_eventMetadata.Create(actor), definition.Id,
                definition.DocumentType, definition.Version, runningOnOld), token);
            return new ActivatedDefinition(definition, runningOnOld);
        }, ct);

    public async Task<long> RunningInstancesAsync(Guid id, CancellationToken ct)
    {
        await FindAsync(id, ct);
        return await _db.Instances.LongCountAsync(i => i.DefinitionId == id
            && (i.Status == InstanceStatus.RUNNING || i.Status == InstanceStatus.REVISION_REQUESTED), ct);
    }

    public async Task<Simulation> SimulateAsync(Guid id, IReadOnlyDictionary<string, object?>? context,
        Guid? submitterId, CancellationToken ct)
    {
        var definition = await FindAsync(id, ct);
        var vars = new Dictionary<string, object?>();
        foreach (var (key, value) in WorkflowJson.NormalizeVars(context))
        {
            if (value is not null)
            {
                vars[key] = value;
            }
        }
        vars["docType"] = definition.DocumentType;
        var submitter = submitterId is { } sid ? await _users.FindAsync(sid, ct) : null;
        if (submitterId is { } submitterUser)
        {
            vars.Remove("department");
            if (await _users.DepartmentCodeOfAsync(submitterUser, ct) is { } code)
            {
                vars["department"] = code;
            }
        }
        var resolveContext = new ResolveContext(submitterId, submitter?.DepartmentId, definition.DocumentType, vars);
        var path = new List<SimulatedStep>();
        var warnings = new List<string>();
        foreach (var step in definition.Steps)
        {
            bool applies;
            try
            {
                applies = _conditions.Evaluate(step.Condition, vars);
            }
            catch (DomainException ex)
            {
                warnings.Add($"Step {step.Order} ({step.Name}): {ex.Message} – {ex.Args.GetValueOrDefault("cause")}");
                path.Add(new SimulatedStep(step.Order, step.Name, false, "condition-error", []));
                continue;
            }
            if (!applies)
            {
                path.Add(new SimulatedStep(step.Order, step.Name, false, "condition=false", []));
                continue;
            }
            var resolved = step.Assignee is null ? [] : await _resolver.ResolveAsync(step.Assignee, resolveContext, ct);
            var assignees = resolved.Where(assignee => assignee != submitterId).Distinct().ToList();
            if (assignees.Count == 0)
            {
                warnings.Add($"Step {step.Order} ({step.Name}): no approver – submission will fail with "
                             + WorkflowError.WF_NO_ASSIGNEE.Code);
            }
            var names = await _users.FullNamesAsync(assignees, ct);
            path.Add(new SimulatedStep(step.Order, step.Name, true, null,
                assignees.Select(assignee => new SimulatedAssignee(assignee, names.GetValueOrDefault(assignee))).ToList()));
        }
        return new Simulation(path, warnings);
    }

    private async Task<IReadOnlyList<Violation>> RequireValidAsync(DefinitionContent content, bool forActivation,
        CancellationToken ct)
    {
        var violations = await _validator.ValidateAsync(content, forActivation, ct);
        var errors = violations.Where(v => v.IsError).Select(v => v.ToProblemError()).ToList();
        if (errors.Count > 0)
        {
            throw new DomainException(WorkflowError.WF_DEFINITION_INVALID, "Workflow definition is not valid",
                new Dictionary<string, object?> { ["errors"] = errors });
        }
        return violations.Where(v => !v.IsError).ToList();
    }

    private async Task<int> MaxVersionAsync(string documentType, CancellationToken ct) =>
        await _db.Definitions.Where(d => d.DocumentType == documentType).MaxAsync(d => (int?)d.Version, ct) ?? 0;

    private async Task<WorkflowDefinition> FindAsync(Guid id, CancellationToken ct) =>
        await _db.Definitions.SingleOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new DomainException(WorkflowError.WF_DEFINITION_NOT_FOUND);
}
