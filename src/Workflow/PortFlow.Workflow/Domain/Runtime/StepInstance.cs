using NodaTime;
using PortFlow.Shared.Id;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Domain.Runtime;

public sealed class StepInstance
{
    private readonly List<ApprovalAction> _actions = [];

    private StepInstance()
    {
    }

    private StepInstance(StepDefinition definition, int round, StepStatus status, IReadOnlyList<Guid> assignees)
    {
        Id = UuidV7.Next();
        StepOrder = definition.Order;
        Round = round;
        Name = definition.Name!;
        AssigneeUserIds = assignees.ToArray();
        RequiresAll = definition.RequiresAll;
        OnReject = definition.OnReject ?? Definition.OnReject.TERMINATE;
        Status = status;
    }

    public Guid Id { get; private set; }

    public int StepOrder { get; private set; }

    public int Round { get; private set; }

    public string Name { get; private set; } = null!;

    public Guid[] AssigneeUserIds { get; private set; } = [];

    public bool RequiresAll { get; private set; }

    public OnReject OnReject { get; private set; }

    public StepStatus Status { get; private set; }

    public Instant? StartedAt { get; private set; }

    public Instant? DueAt { get; private set; }

    public Instant? CompletedAt { get; private set; }

    public Guid? ActedBy { get; private set; }

    public Decision? Decision { get; private set; }

    public string? Comment { get; private set; }

    public Instant? OverdueNotifiedAt { get; private set; }

    /// <summary>Theo thời gian (= <c>@OrderBy("at ASC")</c>).</summary>
    public IReadOnlyList<ApprovalAction> Actions => _actions.OrderBy(action => action.At).ToList();

    internal static StepInstance Pending(StepDefinition definition, int round, IReadOnlyList<Guid> assignees, Instant now,
        Instant? dueAt) =>
        new(definition, round, StepStatus.PENDING, assignees) { StartedAt = now, DueAt = dueAt };

    internal static StepInstance Skipped(StepDefinition definition, int round, Instant now) =>
        new(definition, round, StepStatus.SKIPPED, []) { CompletedAt = now };

    internal StepInstance CarryOver(int newRound, Instant now)
    {
        var copy = new StepInstance
        {
            Id = UuidV7.Next(),
            StepOrder = StepOrder,
            Round = newRound,
            Name = Name,
            AssigneeUserIds = AssigneeUserIds.ToArray(),
            RequiresAll = RequiresAll,
            OnReject = OnReject,
            Status = Status,
            CompletedAt = now,
        };
        if (Status == StepStatus.COMPLETED)
        {
            copy.StartedAt = StartedAt;
            copy.ActedBy = ActedBy;
            copy.Decision = Decision;
            copy.Comment = Comment;
            copy._actions.Add(new ApprovalAction(ActedBy!.Value, null, ActionType.CARRIED_OVER,
                $"Giữ kết quả duyệt round {Round}", now));
        }
        return copy;
    }

    public bool IsAssignee(Guid? userId) => userId is { } id && AssigneeUserIds.Contains(id);

    public bool HasVoteFrom(Guid voter) => _actions.Any(action => action.Voter == voter);

    internal bool RecordApproval(Guid userId, Guid? onBehalfOf, string? comment, Instant now)
    {
        var vote = new ApprovalAction(userId, onBehalfOf, ActionType.APPROVE, comment, now);
        _actions.Add(vote);
        if (!RequiresAll || AssigneeUserIds.All(ApprovedVoters().Contains))
        {
            vote.MarkDecisive();
            Complete(userId, Runtime.Decision.APPROVED, comment, now);
            return true;
        }
        return false;
    }

    internal void RecordVeto(Guid userId, Guid? onBehalfOf, ActionType action, string? comment, Instant now)
    {
        var vote = new ApprovalAction(userId, onBehalfOf, action, comment, now);
        vote.MarkDecisive();
        _actions.Add(vote);
        Complete(userId, action == ActionType.REJECT ? Runtime.Decision.REJECTED : Runtime.Decision.REVISION_REQUESTED,
            comment, now);
    }

    internal void Cancel(Instant now)
    {
        if (Status == StepStatus.PENDING)
        {
            Status = StepStatus.CANCELLED;
            CompletedAt = now;
        }
    }

    internal void MarkOverdueNotified(Instant now) => OverdueNotifiedAt = now;

    private void Complete(Guid actor, Decision result, string? note, Instant now)
    {
        Status = StepStatus.COMPLETED;
        Decision = result;
        ActedBy = actor;
        Comment = note;
        CompletedAt = now;
    }

    private HashSet<Guid> ApprovedVoters() =>
        _actions.Where(action => action.Action == ActionType.APPROVE).Select(action => action.Voter).ToHashSet();
}
