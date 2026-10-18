using Microsoft.EntityFrameworkCore;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Domain.Runtime;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Application;

public sealed record Person(Guid Id, string? FullName);

public sealed record StepView(Guid? Id, int Order, string? Name, string State, string? Decision, Person? ActedBy,
    Instant? CompletedAt, string? Comment, IReadOnlyList<Person> Assignees, Instant? StartedAt, Instant? DueAt,
    bool Overdue, string? AssigneeHint, string? OnReject, bool RequiresAll);

public sealed record HistoryEntry(Instant At, Person User, Person? OnBehalfOf, string Action, int StepOrder,
    string StepName, int Round, string? Comment);

public sealed record InstanceView(Guid Id, string DocumentType, Guid DocumentId, DocumentSummary Document, string Status,
    int Round, int DefinitionVersion, Person Submitter, Instant SubmittedAt, Instant? CompletedAt, long? Version,
    IReadOnlyList<StepView> Steps, int RemainingSteps, Guid? CurrentStepId, bool CanAct,
    IReadOnlyList<HistoryEntry> History);

/// <summary>Màn chi tiết luồng duyệt: người tham gia (gửi, được giao, ủy quyền, đã bỏ phiếu) hoặc người có scope hồ sơ.</summary>
public sealed class InstanceQueryService(
    WorkflowDbContext db,
    IConditionEvaluator conditions,
    IUserDirectory users,
    IDelegationQuery delegations,
    IScopePermissionEvaluator scope,
    IBusinessClock clock)
{
    public async Task<InstanceView> DetailAsync(Guid instanceId, ICurrentUser user, CancellationToken ct)
    {
        var instance = await db.Instances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == instanceId, ct)
                       ?? throw new DomainException(WorkflowError.WF_INSTANCE_NOT_FOUND);
        if (!await IsParticipantAsync(instance, user, ct) && !await InScopeAsync(instance, user, ct))
        {
            throw new DomainException(SysErrorCode.SYS_FORBIDDEN, "You do not have permission to view this workflow");
        }
        return await ViewAsync(instance, user, ct);
    }

    public async Task<IReadOnlyList<InstanceView>> ByDocumentAsync(string documentType, Guid documentId,
        ICurrentUser user, CancellationToken ct)
    {
        var found = await db.Instances.AsNoTracking()
            .Where(i => i.DocumentType == documentType && i.DocumentId == documentId)
            .OrderByDescending(i => i.SubmittedAt)
            .ToListAsync(ct);
        if (found.Count == 0)
        {
            return [];
        }
        var participant = false;
        foreach (var instance in found)
        {
            if (await IsParticipantAsync(instance, user, ct))
            {
                participant = true;
                break;
            }
        }
        if (!participant && !await InScopeAsync(found[0], user, ct))
        {
            throw new DomainException(SysErrorCode.SYS_FORBIDDEN, "You do not have permission to view the workflow of this document");
        }
        var views = new List<InstanceView>();
        foreach (var instance in found)
        {
            views.Add(await ViewAsync(instance, user, ct));
        }
        return views;
    }

    private async Task<bool> IsParticipantAsync(WorkflowInstance instance, ICurrentUser user, CancellationToken ct)
    {
        if (user.Id == instance.SubmitterId)
        {
            return true;
        }
        var delegators = await delegations.ActiveDelegatorsOfAsync(user.Id, instance.DocumentType, clock.Today(), ct);
        return instance.Steps.Any(step => step.IsAssignee(user.Id)
                                          || StepActorPolicy.MayAct(step.AssigneeUserIds, instance.SubmitterId, user.Id, delegators)
                                          || step.Actions.Any(action => action.UserId == user.Id));
    }

    private async Task<bool> InScopeAsync(WorkflowInstance instance, ICurrentUser user, CancellationToken ct)
    {
        try
        {
            return await scope.HasPermissionAsync(user, instance.DocumentId, instance.DocumentType, ct);
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private async Task<InstanceView> ViewAsync(WorkflowInstance instance, ICurrentUser user, CancellationToken ct)
    {
        var definition = await WorkflowEngine.PinnedDefinitionAsync(db, instance, ct);
        var roundSteps = instance.StepsOfRound(instance.Round).ToDictionary(step => step.StepOrder);
        var people = new HashSet<Guid> { instance.SubmitterId };
        foreach (var step in instance.Steps)
        {
            people.UnionWith(step.AssigneeUserIds);
            foreach (var action in step.Actions)
            {
                people.Add(action.UserId);
                if (action.OnBehalfOf is { } onBehalfOf)
                {
                    people.Add(onBehalfOf);
                }
            }
        }
        var names = await users.FullNamesAsync(people, ct);
        var now = clock.Now();
        var steps = definition.Steps
            .Select(stepDefinition => StepViewOf(instance, stepDefinition, roundSteps.GetValueOrDefault(stepDefinition.Order),
                names, now))
            .ToList();
        var remaining = steps.Count(step => step.State == "REMAINING");
        var history = instance.Steps
            .SelectMany(step => step.Actions.Select(action => HistoryOf(step, action, names)))
            .OrderBy(entry => entry.At)
            .ToList();
        var current = instance.CurrentStep();
        var canAct = current is not null && await CanActAsync(instance, current, user, ct);
        return new InstanceView(instance.Id, instance.DocumentType, instance.DocumentId, WorkflowEngine.SummaryOf(instance),
            instance.Status.ToString(), instance.Round, instance.DefinitionVersion,
            new Person(instance.SubmitterId, names.GetValueOrDefault(instance.SubmitterId)), instance.SubmittedAt,
            instance.CompletedAt, instance.Version, steps, remaining, current?.Id, canAct, history);
    }

    private StepView StepViewOf(WorkflowInstance instance, StepDefinition definition, StepInstance? step,
        IReadOnlyDictionary<Guid, string> names, Instant now)
    {
        if (step is null)
        {
            var state = instance.Status.IsActive() ? (Applies(definition, instance) ? "REMAINING" : "SKIPPED") : "NOT_REACHED";
            return new StepView(null, definition.Order, definition.Name, state, null, null, null, null, [], null, null,
                false, definition.Assignee?.Hint(), definition.OnReject?.ToString(), definition.RequiresAll);
        }
        var stepState = step.Status switch
        {
            StepStatus.PENDING => "CURRENT",
            StepStatus.COMPLETED => "DONE",
            StepStatus.SKIPPED => "SKIPPED",
            _ => "CANCELLED",
        };
        var assignees = step.AssigneeUserIds.Select(id => new Person(id, names.GetValueOrDefault(id))).ToList();
        var overdue = step.Status == StepStatus.PENDING && step.DueAt is { } due && due < now;
        return new StepView(step.Id, step.StepOrder, step.Name, stepState, step.Decision?.ToString(),
            step.ActedBy is { } actor ? new Person(actor, names.GetValueOrDefault(actor)) : null, step.CompletedAt,
            step.Comment, assignees, step.StartedAt, step.DueAt, overdue, null, step.OnReject.ToString(), step.RequiresAll);
    }

    private bool Applies(StepDefinition definition, WorkflowInstance instance)
    {
        try
        {
            return conditions.Evaluate(definition.Condition, instance.ContextVars);
        }
        catch (DomainException)
        {
            return true;
        }
    }

    private async Task<bool> CanActAsync(WorkflowInstance instance, StepInstance step, ICurrentUser user,
        CancellationToken ct)
    {
        if (instance.Status != InstanceStatus.RUNNING || user.Id == instance.SubmitterId)
        {
            return false;
        }
        var delegators = await delegations.ActiveDelegatorsOfAsync(user.Id, instance.DocumentType, clock.Today(), ct);
        return StepActorPolicy.HasPendingVote(step, instance.SubmitterId, user.Id, delegators);
    }

    private static HistoryEntry HistoryOf(StepInstance step, ApprovalAction action, IReadOnlyDictionary<Guid, string> names) =>
        new(action.At, new Person(action.UserId, names.GetValueOrDefault(action.UserId)),
            action.OnBehalfOf is { } onBehalfOf ? new Person(onBehalfOf, names.GetValueOrDefault(onBehalfOf)) : null,
            action.Action.ToString(), step.StepOrder, step.Name, step.Round, action.Comment);
}
