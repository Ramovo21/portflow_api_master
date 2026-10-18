using NodaTime;
using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Domain.Runtime;

/// <summary>
/// Một lần chạy quy trình duyệt cho một hồ sơ, ghim vào một version định nghĩa. Aggregate tự quyết mọi chuyển
/// trạng thái: mở bước kế tiếp (bỏ qua bước có điều kiện sai), bỏ phiếu, gửi lại sau yêu cầu sửa, hủy.
/// </summary>
public sealed class WorkflowInstance : AggregateRoot<Guid>
{
    public const string SummaryKey = "_summary";

    private readonly List<StepInstance> _steps = [];
    private Dictionary<string, object?>? _contextVars;

    private WorkflowInstance()
    {
    }

    public string DocumentType { get; private set; } = null!;

    public Guid DocumentId { get; private set; }

    public Guid DefinitionId { get; private set; }

    public int DefinitionVersion { get; private set; }

    public int? CurrentStepOrder { get; private set; }

    public int Round { get; private set; }

    public InstanceStatus Status { get; private set; }

    public string ContextVarsJson { get; private set; } = "{}";

    public Guid SubmitterId { get; private set; }

    public Instant SubmittedAt { get; private set; }

    public Instant? CompletedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public Instant CreatedAt { get; private set; }

    public Instant? UpdatedAt { get; private set; }

    /// <summary>Theo round rồi thứ tự bước (= <c>@OrderBy("round ASC, stepOrder ASC")</c>).</summary>
    public IReadOnlyList<StepInstance> Steps => _steps.OrderBy(s => s.Round).ThenBy(s => s.StepOrder).ToList();

    public IReadOnlyDictionary<string, object?> ContextVars => _contextVars ??= WorkflowJson.ReadVars(ContextVarsJson);

    public static WorkflowInstance Create(Guid id, WorkflowDefinition definition, Guid documentId, Guid submitterId,
        IReadOnlyDictionary<string, object?>? contextVars, Instant now)
    {
        var instance = new WorkflowInstance
        {
            Id = id,
            DocumentType = definition.DocumentType,
            DocumentId = documentId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            Round = 1,
            Status = InstanceStatus.RUNNING,
            SubmitterId = submitterId,
            SubmittedAt = now,
            CreatedAt = now,
        };
        instance.SetContextVars(contextVars);
        instance.Touch(now);
        return instance;
    }

    /// <summary>Mở bước áp dụng đầu tiên sau <paramref name="afterOrder"/>; hết bước → APPROVED (trả null).</summary>
    public async Task<StepInstance?> AdvanceAsync(WorkflowDefinition definition, int afterOrder,
        IConditionEvaluator conditions, IAssigneeResolver resolver, ResolveContext resolveContext, Instant now,
        CancellationToken ct = default)
    {
        RequireDefinition(definition);
        foreach (var next in definition.StepsAfter(afterOrder))
        {
            if (!conditions.Evaluate(next.Condition, ContextVars))
            {
                _steps.Add(StepInstance.Skipped(next, Round, now));
                continue;
            }
            var resolved = next.Assignee is null
                ? []
                : await resolver.ResolveAsync(next.Assignee, resolveContext, ct);
            var assignees = resolved.Where(id => id != SubmitterId && id != Guid.Empty).Distinct().ToList();
            if (assignees.Count == 0)
            {
                throw new DomainException(WorkflowError.WF_NO_ASSIGNEE,
                    $"No approver could be found for step '{next.Name}' (submitter excluded)",
                    new Dictionary<string, object?> { ["step"] = next.Name, ["stepOrder"] = next.Order });
            }
            Instant? dueAt = next.SlaHours is { } hours ? now + Duration.FromHours(hours) : null;
            var opened = StepInstance.Pending(next, Round, assignees, now, dueAt);
            _steps.Add(opened);
            CurrentStepOrder = next.Order;
            Status = InstanceStatus.RUNNING;
            Touch(now);
            return opened;
        }
        Status = InstanceStatus.APPROVED;
        CompletedAt = now;
        Touch(now);
        return null;
    }

    /// <summary>APR-01/FR-W04: aggregate tự quyết actor bỏ phiếu cho ai theo <see cref="StepActorPolicy"/>.</summary>
    public ActResult Act(Guid stepId, Guid actorId, IReadOnlySet<Guid> delegators, ActionType action, string? comment,
        Instant now)
    {
        var voter = StepActorPolicy.NextVoter(StepById(stepId), SubmitterId, actorId, delegators)
                    ?? throw new DomainException(WorkflowError.WF_NOT_ASSIGNEE);
        return Vote(stepId, actorId, voter == actorId ? null : voter, action, comment, now);
    }

    internal ActResult Vote(Guid stepId, Guid? actorId, Guid? onBehalfOf, ActionType? action, string? comment,
        Instant now)
    {
        var step = StepById(stepId);
        if (Status != InstanceStatus.RUNNING)
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE,
                $"Workflow is in state {Status}, does not accept approval actions");
        }
        if (step.Status != StepStatus.PENDING || CurrentStepOrder is not { } current || step.StepOrder != current
            || step.Round != Round)
        {
            throw new DomainException(WorkflowError.WF_STEP_NOT_ACTIVE);
        }
        var voter = onBehalfOf ?? actorId;
        if (actorId is not { } actor || actor == SubmitterId || !step.IsAssignee(voter))
        {
            throw new DomainException(WorkflowError.WF_NOT_ASSIGNEE);
        }
        if (action is not { } type || type == ActionType.CARRIED_OVER)
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE, "Invalid action: " + action);
        }
        var note = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (type != ActionType.APPROVE && note is null)
        {
            throw new DomainException(WorkflowError.WF_COMMENT_REQUIRED, "Rejection or revision request requires a comment");
        }
        if (step.HasVoteFrom(voter!.Value))
        {
            throw new DomainException(WorkflowError.WF_ALREADY_VOTED);
        }
        Touch(now);
        switch (type)
        {
            case ActionType.APPROVE:
                return new ActResult(step, onBehalfOf, step.RecordApproval(actor, onBehalfOf, note, now)
                    ? ActResult.Outcome.STEP_APPROVED
                    : ActResult.Outcome.VOTE_RECORDED);
            case ActionType.REJECT:
                step.RecordVeto(actor, onBehalfOf, type, note, now);
                if (step.OnReject == OnReject.TERMINATE)
                {
                    Status = InstanceStatus.REJECTED;
                    CompletedAt = now;
                    return new ActResult(step, onBehalfOf, ActResult.Outcome.REJECTED);
                }
                Status = InstanceStatus.REVISION_REQUESTED;
                return new ActResult(step, onBehalfOf, ActResult.Outcome.REVISION_REQUESTED);
            default:
                step.RecordVeto(actor, onBehalfOf, type, note, now);
                Status = InstanceStatus.REVISION_REQUESTED;
                return new ActResult(step, onBehalfOf, ActResult.Outcome.REVISION_REQUESTED);
        }
    }

    public async Task<StepInstance?> ResubmitAsync(Guid? userId, IReadOnlyDictionary<string, object?>? newContextVars,
        WorkflowDefinition definition, IConditionEvaluator conditions, IAssigneeResolver resolver,
        ResolveContext resolveContext, Instant now, CancellationToken ct = default)
    {
        RequireDefinition(definition);
        if (Status != InstanceStatus.REVISION_REQUESTED)
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE, "Resubmission is only allowed while awaiting revision");
        }
        if (userId is null || userId != SubmitterId)
        {
            throw new DomainException(WorkflowError.WF_NOT_SUBMITTER);
        }
        var revisionOrder = LastRevisionStepOrder();
        var previousRound = StepsOfRound(Round);
        Round++;
        SetContextVars(newContextVars);
        Status = InstanceStatus.RUNNING;
        Touch(now);
        var afterOrder = 0;
        if (definition.ResubmitPolicy == ResubmitPolicy.RESUME_AT_REQUESTER
            && CanResume(previousRound, revisionOrder, definition, conditions))
        {
            foreach (var step in previousRound.Where(step => step.StepOrder < revisionOrder))
            {
                _steps.Add(step.CarryOver(Round, now));
            }
            afterOrder = revisionOrder - 1;
        }
        return await AdvanceAsync(definition, afterOrder, conditions, resolver, resolveContext, now, ct);
    }

    /// <summary>Hủy; <paramref name="userId"/> null = hệ thống (vd hồ sơ bị hủy), ngược lại phải là người gửi.</summary>
    public void Cancel(Guid? userId, string? reason, Instant now)
    {
        if (!Status.IsActive())
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE, "Workflow has already ended");
        }
        if (userId is not null && userId != SubmitterId)
        {
            throw new DomainException(WorkflowError.WF_NOT_SUBMITTER);
        }
        _steps.ForEach(step => step.Cancel(now));
        Status = InstanceStatus.CANCELLED;
        CancelReason = reason;
        CompletedAt = now;
        Touch(now);
    }

    public void MarkOverdueNotified(Guid stepId, Instant now)
    {
        StepById(stepId).MarkOverdueNotified(now);
        Touch(now);
    }

    public void AttachSummary(IReadOnlyDictionary<string, object?> summary)
    {
        var vars = new Dictionary<string, object?>(ContextVars) { [SummaryKey] = summary };
        SetContextVars(vars);
    }

    public StepInstance StepById(Guid stepId) =>
        _steps.FirstOrDefault(step => step.Id == stepId) ?? throw new DomainException(WorkflowError.WF_STEP_NOT_FOUND);

    public StepInstance? CurrentStep() => _steps.FirstOrDefault(step => step.Status == StepStatus.PENDING);

    public IReadOnlyList<StepInstance> StepsOfRound(int round) =>
        _steps.Where(step => step.Round == round).OrderBy(step => step.StepOrder).ToList();

    public int LastRevisionStepOrder()
    {
        var orders = StepsOfRound(Round)
            .Where(step => step.Decision is Runtime.Decision.REJECTED or Runtime.Decision.REVISION_REQUESTED)
            .Select(step => step.StepOrder)
            .ToList();
        return orders.Count > 0
            ? orders.Max()
            : throw new DomainException(WorkflowError.WF_INVALID_STATE, "Revision-requested step not found");
    }

    private bool CanResume(IReadOnlyList<StepInstance> previousRound, int revisionOrder, WorkflowDefinition definition,
        IConditionEvaluator conditions)
    {
        foreach (var earlier in definition.StepsAfter(0))
        {
            if (earlier.Order >= revisionOrder)
            {
                break;
            }
            var before = previousRound.FirstOrDefault(step => step.StepOrder == earlier.Order);
            if (before is null)
            {
                return false;
            }
            var approved = before.Status == StepStatus.COMPLETED && before.Decision == Runtime.Decision.APPROVED;
            var stillSkipped = before.Status == StepStatus.SKIPPED && !conditions.Evaluate(earlier.Condition, ContextVars);
            if (!approved && !stillSkipped)
            {
                return false;
            }
        }
        return true;
    }

    private void RequireDefinition(WorkflowDefinition definition)
    {
        if (definition.Id != DefinitionId)
        {
            throw new ArgumentException($"Instance {Id} pins definition {DefinitionId}, not {definition.Id}");
        }
    }

    private void Touch(Instant now) => UpdatedAt = now;

    private void SetContextVars(IReadOnlyDictionary<string, object?>? vars)
    {
        _contextVars = WorkflowJson.NormalizeVars(vars);
        ContextVarsJson = WorkflowJson.WriteVars(_contextVars);
    }
}
