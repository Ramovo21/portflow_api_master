namespace PortFlow.Workflow.Domain.Runtime;

public enum ActionType
{
    APPROVE,
    REJECT,
    REQUEST_REVISION,
    CARRIED_OVER,
}

public enum Decision
{
    APPROVED,
    REJECTED,
    REVISION_REQUESTED,
}

public enum InstanceStatus
{
    RUNNING,
    REVISION_REQUESTED,
    APPROVED,
    REJECTED,
    CANCELLED,
}

public enum StepStatus
{
    PENDING,
    COMPLETED,
    SKIPPED,
    CANCELLED,
}

public static class InstanceStatusExtensions
{
    public static readonly IReadOnlyList<InstanceStatus> Active = [InstanceStatus.RUNNING, InstanceStatus.REVISION_REQUESTED];

    public static bool IsActive(this InstanceStatus status) =>
        status is InstanceStatus.RUNNING or InstanceStatus.REVISION_REQUESTED;
}

public sealed record ActResult(StepInstance Step, Guid? OnBehalfOf, ActResult.Outcome Result)
{
    public enum Outcome
    {
        VOTE_RECORDED,
        STEP_APPROVED,
        REJECTED,
        REVISION_REQUESTED,
    }
}
