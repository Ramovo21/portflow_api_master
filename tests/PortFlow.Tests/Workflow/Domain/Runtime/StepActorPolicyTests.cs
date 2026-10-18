using PortFlow.Workflow.Domain.Runtime;

namespace PortFlow.Tests.Workflow.Domain.Runtime;

public class StepActorPolicyTests
{
    private static readonly Guid Submitter = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();
    private static readonly Guid[] Assignees = [A, B, C];

    [Fact]
    public void AssigneeRepresentsThemselvesFirstThenDelegatorsInAssignmentOrder()
    {
        Assert.Equal([B, A, C], StepActorPolicy.Represented(Assignees, Submitter, B, new HashSet<Guid> { C, A }));
    }

    [Fact]
    public void OutsiderRepresentsOnlyAssignedDelegators()
    {
        Assert.Equal([C], StepActorPolicy.Represented(Assignees, Submitter, Outsider, new HashSet<Guid> { C, Guid.NewGuid() }));
        Assert.False(StepActorPolicy.MayAct(Assignees, Submitter, Outsider, new HashSet<Guid>()));
    }

    [Fact]
    public void SubmitterRepresentsNobodyEvenWithDelegation()
    {
        Assert.Empty(StepActorPolicy.Represented(Assignees, Submitter, Submitter, new HashSet<Guid> { A, B }));
        Assert.Empty(StepActorPolicy.Represented([Submitter], Submitter, Submitter, new HashSet<Guid>()));
    }

    [Fact]
    public void MissingActorRepresentsNobody()
    {
        Assert.False(StepActorPolicy.MayAct(Assignees, Submitter, null, new HashSet<Guid> { A }));
    }
}
