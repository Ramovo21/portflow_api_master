using NodaTime;
using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Domain.Runtime;

namespace PortFlow.Tests.Workflow.Domain.Runtime;

public abstract class WorkflowInstanceTestBase
{
    protected static readonly Instant Now = Instant.FromUtc(2026, 11, 10, 2, 0);
    protected static readonly Guid Submitter = Guid.NewGuid();
    protected static readonly Guid Manager = Guid.NewGuid();
    protected static readonly Guid Legal = Guid.NewGuid();
    protected static readonly Guid DirectorA = Guid.NewGuid();
    protected static readonly Guid DirectorB = Guid.NewGuid();
    protected static readonly Guid DirectorC = Guid.NewGuid();
    protected static readonly Guid Outsider = Guid.NewGuid();
    protected static readonly IReadOnlySet<Guid> NoDelegators = new HashSet<Guid>();

    protected static readonly IConditionEvaluator Conditions = new TruthyVariable();

    protected readonly Dictionary<string, List<Guid>> Directory = new()
    {
        ["MANAGER"] = [Manager, Submitter],
        ["LEGAL"] = [Legal],
        ["DIRECTORS"] = [DirectorA, DirectorB, DirectorC],
    };

    protected IAssigneeResolver Resolver => new MapResolver(Directory);

    protected static StepDefinition Step(int order, string role, OnReject onReject, string? condition, bool all) =>
        new(order, $"Bước {order}", AssigneeRule.Role(role), onReject, condition, 24, all);

    protected static WorkflowDefinition Definition(ResubmitPolicy policy, params StepDefinition[] steps) =>
        WorkflowDefinition.Draft(Guid.NewGuid(), 1, new DefinitionContent("TEST_DOC", policy, steps, null), null, Now);

    protected static WorkflowDefinition Contract(bool requiresAll, ResubmitPolicy policy) =>
        Definition(policy,
            Step(1, "MANAGER", OnReject.BACK_TO_SUBMITTER, null, false),
            Step(2, "LEGAL", OnReject.TERMINATE, "big", false),
            Step(3, "DIRECTORS", OnReject.TERMINATE, null, requiresAll));

    protected static Dictionary<string, object?> Vars(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    protected WorkflowInstance Start(WorkflowDefinition definition, Dictionary<string, object?> vars)
    {
        var instance = WorkflowInstance.Create(Guid.NewGuid(), definition, Guid.NewGuid(), Submitter, vars, Now);
        instance.AdvanceAsync(definition, 0, Conditions, Resolver, Context(), Now).GetAwaiter().GetResult();
        return instance;
    }

    protected static ResolveContext Context() => new(Submitter, null, "TEST_DOC", new Dictionary<string, object?>());

    protected static ActResult Approve(WorkflowInstance instance, Guid user) =>
        instance.Act(instance.CurrentStep()!.Id, user, NoDelegators, ActionType.APPROVE, null, Now);

    protected void ApproveAndAdvance(WorkflowInstance instance, WorkflowDefinition definition, Guid user)
    {
        var result = Approve(instance, user);
        if (result.Result == ActResult.Outcome.STEP_APPROVED)
        {
            instance.AdvanceAsync(definition, result.Step.StepOrder, Conditions, Resolver, Context(), Now)
                .GetAwaiter().GetResult();
        }
    }

    protected void Resubmit(WorkflowInstance instance, Guid user, Dictionary<string, object?> vars,
        WorkflowDefinition definition) =>
        instance.ResubmitAsync(user, vars, definition, Conditions, Resolver, Context(), Now).GetAwaiter().GetResult();

    protected static void AssertError(Action call, IErrorCode expected) =>
        Assert.Equal(expected, Assert.Throws<DomainException>(call).Code);

    private sealed class TruthyVariable : IConditionEvaluator
    {
        public bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars) =>
            expression is null || vars.GetValueOrDefault(expression) is true;
    }

    private sealed class MapResolver(Dictionary<string, List<Guid>> directory) : IAssigneeResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(AssigneeRule rule, ResolveContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(directory.GetValueOrDefault(rule.Value!) ?? []);
    }
}

public class WorkflowInstanceInterpreterTests : WorkflowInstanceTestBase
{
    [Fact]
    public void ThreeSequentialStepsEndApproved()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars(("big", true)));

        var first = instance.CurrentStep()!;
        Assert.Equal(1, first.StepOrder);
        Assert.Equal(Now + Duration.FromHours(24), first.DueAt);
        ApproveAndAdvance(instance, definition, Manager);
        Assert.Equal(2, instance.CurrentStepOrder);
        ApproveAndAdvance(instance, definition, Legal);
        Assert.Equal(3, instance.CurrentStepOrder);
        ApproveAndAdvance(instance, definition, DirectorB);

        Assert.Equal(InstanceStatus.APPROVED, instance.Status);
        Assert.Equal(Now, instance.CompletedAt);
        Assert.Null(instance.CurrentStep());
        Assert.Equal([Decision.APPROVED, Decision.APPROVED, Decision.APPROVED],
            instance.StepsOfRound(1).Select(s => s.Decision!.Value));
    }

    [Fact]
    public void StepWithFalseConditionIsSkipped()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars(("big", false)));

        ApproveAndAdvance(instance, definition, Manager);

        Assert.Equal(3, instance.CurrentStepOrder);
        Assert.Equal([StepStatus.COMPLETED, StepStatus.SKIPPED, StepStatus.PENDING],
            instance.StepsOfRound(1).Select(s => s.Status));
    }

    [Fact]
    public void AllConditionalStepsFalseExceptFirst()
    {
        var definition = Definition(ResubmitPolicy.RESTART,
            Step(1, "MANAGER", OnReject.BACK_TO_SUBMITTER, null, false),
            Step(2, "LEGAL", OnReject.TERMINATE, "big", false),
            Step(3, "DIRECTORS", OnReject.TERMINATE, "huge", false));
        var instance = Start(definition, Vars());

        ApproveAndAdvance(instance, definition, Manager);

        Assert.Equal(InstanceStatus.APPROVED, instance.Status);
        Assert.Equal([StepStatus.COMPLETED, StepStatus.SKIPPED, StepStatus.SKIPPED],
            instance.StepsOfRound(1).Select(s => s.Status));
    }

    [Fact]
    public void NoApplicableStepAutoApprovesOnStart()
    {
        var definition = Definition(ResubmitPolicy.RESTART, Step(1, "LEGAL", OnReject.TERMINATE, "big", false));
        Assert.Equal(InstanceStatus.APPROVED, Start(definition, Vars()).Status);
    }

    [Fact]
    public void ContextVarsAreNormalizedForConditions()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars(("amount", 1200), ("big", true)));
        Assert.Equal(1200m, instance.ContextVars["amount"]);
    }
}

public class WorkflowInstanceResolverTests : WorkflowInstanceTestBase
{
    [Fact]
    public void SubmitterIsNeverAnAssignee()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());
        Assert.Equal([Manager], instance.CurrentStep()!.AssigneeUserIds);
    }

    [Fact]
    public async Task EmptyAssigneesFailInsteadOfSkipping()
    {
        Directory["MANAGER"] = [Submitter];
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = WorkflowInstance.Create(Guid.NewGuid(), definition, Guid.NewGuid(), Submitter, Vars(), Now);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            instance.AdvanceAsync(definition, 0, Conditions, Resolver, Context(), Now));
        Assert.Equal(WorkflowError.WF_NO_ASSIGNEE, ex.Code);
        Assert.Empty(instance.Steps);
    }
}

public class WorkflowInstanceGroupApprovalTests : WorkflowInstanceTestBase
{
    [Fact]
    public void AnyCompletesOnFirstApproval()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());
        ApproveAndAdvance(instance, definition, Manager);

        var result = Approve(instance, DirectorC);

        Assert.Equal(ActResult.Outcome.STEP_APPROVED, result.Result);
        Assert.True(Assert.Single(result.Step.Actions).Decisive);
    }

    [Fact]
    public void AllNeedsEveryAssignee()
    {
        var definition = Contract(true, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());
        ApproveAndAdvance(instance, definition, Manager);

        Assert.Equal(ActResult.Outcome.VOTE_RECORDED, Approve(instance, DirectorA).Result);
        Assert.Equal(ActResult.Outcome.VOTE_RECORDED, Approve(instance, DirectorB).Result);
        AssertError(() => Approve(instance, DirectorA), WorkflowError.WF_ALREADY_VOTED);
        var last = Approve(instance, DirectorC);

        Assert.Equal(ActResult.Outcome.STEP_APPROVED, last.Result);
        Assert.Equal(DirectorC, Assert.Single(last.Step.Actions, a => a.Decisive).UserId);
    }

    [Fact]
    public void OneRejectEndsAllGroupStep()
    {
        var definition = Contract(true, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());
        ApproveAndAdvance(instance, definition, Manager);
        Approve(instance, DirectorA);

        var result = instance.Vote(instance.CurrentStep()!.Id, DirectorB, null, ActionType.REJECT, "Không đồng ý", Now);

        Assert.Equal(ActResult.Outcome.REJECTED, result.Result);
        Assert.Equal(InstanceStatus.REJECTED, instance.Status);
    }
}

public class WorkflowInstanceOnRejectTests : WorkflowInstanceTestBase
{
    [Theory]
    [InlineData(OnReject.TERMINATE)]
    [InlineData(OnReject.BACK_TO_SUBMITTER)]
    public void RejectFollowsStepConfiguration(OnReject onReject)
    {
        var definition = Definition(ResubmitPolicy.RESTART, Step(1, "MANAGER", onReject, null, false));
        var instance = Start(definition, Vars());

        instance.Vote(instance.CurrentStep()!.Id, Manager, null, ActionType.REJECT, "Sai giá", Now);

        if (onReject == OnReject.TERMINATE)
        {
            Assert.Equal(InstanceStatus.REJECTED, instance.Status);
            Assert.NotNull(instance.CompletedAt);
        }
        else
        {
            Assert.Equal(InstanceStatus.REVISION_REQUESTED, instance.Status);
            Assert.Null(instance.CompletedAt);
        }
        Assert.Equal(Decision.REJECTED, instance.StepsOfRound(1)[0].Decision);
    }

    [Fact]
    public void OnRejectEnumIsCoveredByTheory() =>
        Assert.Equal(2, Enum.GetValues<OnReject>().Length);

    [Fact]
    public void RequestRevisionAlwaysGoesBackToSubmitter()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars(("big", true)));
        ApproveAndAdvance(instance, definition, Manager);

        var result = instance.Vote(instance.CurrentStep()!.Id, Legal, null, ActionType.REQUEST_REVISION,
            "Bổ sung điều khoản phạt", Now);

        Assert.Equal(ActResult.Outcome.REVISION_REQUESTED, result.Result);
        Assert.Equal(InstanceStatus.REVISION_REQUESTED, instance.Status);
        Assert.Equal(2, instance.LastRevisionStepOrder());
    }
}

public class WorkflowInstanceResubmitTests : WorkflowInstanceTestBase
{
    private WorkflowInstance RevisionAtLegal(WorkflowDefinition definition, Dictionary<string, object?> vars)
    {
        var instance = Start(definition, vars);
        ApproveAndAdvance(instance, definition, Manager);
        instance.Vote(instance.CurrentStep()!.Id, Legal, null, ActionType.REQUEST_REVISION, "Sửa điều khoản", Now);
        return instance;
    }

    [Fact]
    public void RestartGoesBackToStepOneInNewRound()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = RevisionAtLegal(definition, Vars(("big", true)));

        Resubmit(instance, Submitter, Vars(("big", true)), definition);

        Assert.Equal(2, instance.Round);
        Assert.Equal(InstanceStatus.RUNNING, instance.Status);
        Assert.Equal(1, instance.CurrentStep()!.StepOrder);
        Assert.Equal(2, instance.CurrentStep()!.Round);
        Assert.Equal(2, instance.StepsOfRound(1).Count);
    }

    [Fact]
    public void ResumeGoesBackToRequestingStepAndCarriesEarlierApprovals()
    {
        var definition = Contract(false, ResubmitPolicy.RESUME_AT_REQUESTER);
        var instance = RevisionAtLegal(definition, Vars(("big", true)));

        Resubmit(instance, Submitter, Vars(("big", true)), definition);

        Assert.Equal(2, instance.CurrentStep()!.StepOrder);
        var carried = instance.StepsOfRound(2)[0];
        Assert.Equal(StepStatus.COMPLETED, carried.Status);
        var action = Assert.Single(carried.Actions);
        Assert.Equal(ActionType.CARRIED_OVER, action.Action);
        Assert.Equal(Manager, action.UserId);
    }

    [Fact]
    public void ResumeFallsBackToRestartWhenSkippedStepNowApplies()
    {
        var definition = Definition(ResubmitPolicy.RESUME_AT_REQUESTER,
            Step(1, "LEGAL", OnReject.BACK_TO_SUBMITTER, "big", false),
            Step(2, "MANAGER", OnReject.BACK_TO_SUBMITTER, null, false));
        var instance = Start(definition, Vars(("big", false)));
        instance.Vote(instance.CurrentStep()!.Id, Manager, null, ActionType.REQUEST_REVISION, "Sửa", Now);

        Resubmit(instance, Submitter, Vars(("big", true)), definition);

        Assert.Equal(1, instance.CurrentStep()!.StepOrder);
    }

    [Fact]
    public void OnlySubmitterMayResubmit()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = RevisionAtLegal(definition, Vars(("big", true)));

        AssertError(() => Resubmit(instance, Outsider, Vars(), definition), WorkflowError.WF_NOT_SUBMITTER);
    }
}

public class WorkflowInstanceGuardTests : WorkflowInstanceTestBase
{
    [Fact]
    public void OutsiderIsNotAssignee()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        AssertError(() => Approve(instance, Outsider), WorkflowError.WF_NOT_ASSIGNEE);
        AssertError(() => Approve(instance, Submitter), WorkflowError.WF_NOT_ASSIGNEE);
    }

    [Fact]
    public void DelegateActsOnBehalfOfAssignee()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        var result = instance.Act(instance.CurrentStep()!.Id, Outsider, new HashSet<Guid> { Manager },
            ActionType.APPROVE, null, Now);

        Assert.Equal(Manager, result.OnBehalfOf);
        var action = Assert.Single(result.Step.Actions);
        Assert.Equal(Manager, action.Voter);
        Assert.Equal(Outsider, action.UserId);
    }

    [Fact]
    public void DelegationForSomeoneNotAssignedGrantsNothing()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        AssertError(() => instance.Act(instance.CurrentStep()!.Id, Outsider, new HashSet<Guid> { Legal },
            ActionType.APPROVE, null, Now), WorkflowError.WF_NOT_ASSIGNEE);
    }

    [Fact]
    public void SubmitterCannotActEvenAsDelegate()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        AssertError(() => instance.Act(instance.CurrentStep()!.Id, Submitter, new HashSet<Guid> { Manager },
            ActionType.APPROVE, null, Now), WorkflowError.WF_NOT_ASSIGNEE);
    }

    [Fact]
    public void AssigneeWhoIsAlsoDelegateVotesForSelfFirstThenForDelegator()
    {
        var definition = Contract(true, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());
        ApproveAndAdvance(instance, definition, Manager);
        var step = instance.CurrentStep()!.Id;
        var delegators = new HashSet<Guid> { DirectorB };

        var own = instance.Act(step, DirectorA, delegators, ActionType.APPROVE, null, Now);
        var delegated = instance.Act(step, DirectorA, delegators, ActionType.APPROVE, null, Now);

        Assert.Null(own.OnBehalfOf);
        Assert.Equal(DirectorB, delegated.OnBehalfOf);
        AssertError(() => instance.Act(step, DirectorA, delegators, ActionType.APPROVE, null, Now),
            WorkflowError.WF_ALREADY_VOTED);
        Assert.False(StepActorPolicy.HasPendingVote(instance.StepById(step), Submitter, DirectorA, delegators));
        Assert.True(StepActorPolicy.HasPendingVote(instance.StepById(step), Submitter, DirectorC, NoDelegators));
    }

    [Fact]
    public void OldStepIsNotActive()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars(("big", true)));
        var firstStep = instance.CurrentStep()!.Id;
        ApproveAndAdvance(instance, definition, Manager);

        AssertError(() => instance.Vote(firstStep, Manager, null, ActionType.APPROVE, null, Now),
            WorkflowError.WF_STEP_NOT_ACTIVE);
    }

    [Fact]
    public void OldRoundStepIsNotActive()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());
        var roundOneStep = instance.CurrentStep()!.Id;
        instance.Vote(roundOneStep, Manager, null, ActionType.REQUEST_REVISION, "Sửa", Now);
        Resubmit(instance, Submitter, Vars(), definition);

        AssertError(() => instance.Vote(roundOneStep, Manager, null, ActionType.APPROVE, null, Now),
            WorkflowError.WF_STEP_NOT_ACTIVE);
    }

    [Fact]
    public void RejectAndRevisionNeedComment()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());
        var step = instance.CurrentStep()!.Id;

        AssertError(() => instance.Vote(step, Manager, null, ActionType.REJECT, "  ", Now),
            WorkflowError.WF_COMMENT_REQUIRED);
        AssertError(() => instance.Vote(step, Manager, null, ActionType.REQUEST_REVISION, null, Now),
            WorkflowError.WF_COMMENT_REQUIRED);
    }

    [Fact]
    public void UnknownStepIsNotFound()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        AssertError(() => instance.Vote(Guid.NewGuid(), Manager, null, ActionType.APPROVE, null, Now),
            WorkflowError.WF_STEP_NOT_FOUND);
    }
}

public class WorkflowInstanceStateMachineTests : WorkflowInstanceTestBase
{
    [Fact]
    public void TerminalInstancesAcceptNoTransition()
    {
        var definition = Definition(ResubmitPolicy.RESTART, Step(1, "MANAGER", OnReject.TERMINATE, null, false));
        var approved = Start(definition, Vars());
        var step = approved.CurrentStep()!.Id;
        ApproveAndAdvance(approved, definition, Manager);

        Assert.Equal(InstanceStatus.APPROVED, approved.Status);
        AssertError(() => approved.Vote(step, Manager, null, ActionType.APPROVE, null, Now), WorkflowError.WF_INVALID_STATE);
        AssertError(() => approved.Cancel(Submitter, "rút", Now), WorkflowError.WF_INVALID_STATE);
        AssertError(() => Resubmit(approved, Submitter, Vars(), definition), WorkflowError.WF_INVALID_STATE);
    }

    [Fact]
    public void RunningCannotBeResubmitted()
    {
        var definition = Contract(false, ResubmitPolicy.RESTART);
        var instance = Start(definition, Vars());

        AssertError(() => Resubmit(instance, Submitter, Vars(), definition), WorkflowError.WF_INVALID_STATE);
    }

    [Fact]
    public void RevisionRequestedAcceptsNoApproval()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());
        var step = instance.CurrentStep()!.Id;
        instance.Vote(step, Manager, null, ActionType.REQUEST_REVISION, "Sửa", Now);

        AssertError(() => instance.Vote(step, Manager, null, ActionType.APPROVE, null, Now), WorkflowError.WF_INVALID_STATE);
    }

    [Fact]
    public void CancelBySubmitterCancelsPendingStep()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());

        AssertError(() => instance.Cancel(Outsider, "x", Now), WorkflowError.WF_NOT_SUBMITTER);
        instance.Cancel(Submitter, "Rút lại", Now);

        Assert.Equal(InstanceStatus.CANCELLED, instance.Status);
        Assert.Equal([StepStatus.CANCELLED], instance.Steps.Select(s => s.Status));
    }

    [Fact]
    public void RevisionRequestedCanBeCancelledBySystem()
    {
        var instance = Start(Contract(false, ResubmitPolicy.RESTART), Vars());
        instance.Vote(instance.CurrentStep()!.Id, Manager, null, ActionType.REQUEST_REVISION, "Sửa", Now);

        instance.Cancel(null, "Hợp đồng bị hủy", Now);

        Assert.Equal(InstanceStatus.CANCELLED, instance.Status);
    }
}
