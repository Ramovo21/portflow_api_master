using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Tests.Workflow.Domain;

public class WorkflowJsonTests
{
    [Fact]
    public void MinimalDefinitionGetsSchemaDefaults()
    {
        var content = WorkflowJson.ReadDefinition("""
            {"documentType": "CONTRACT", "steps": [
              {"order": 2, "name": "B", "assignee": {"type": "ROLE", "value": "LEGAL"}, "onReject": "TERMINATE"},
              {"order": 1, "name": "A", "assignee": {"type": "ROLE", "value": "LEGAL"}, "onReject": "TERMINATE",
               "condition": "  "}]}
            """);
        Assert.Equal(ResubmitPolicy.RESTART, content.Policy);
        Assert.False(content.EmitESign);
        Assert.Equal([1, 2], content.StepList.Select(s => s.Order));
        Assert.False(content.StepList[0].RequiresAll);
        Assert.Null(content.StepList[0].Condition);
        Assert.Equal(AssigneeScope.ANY, content.StepList[0].Assignee!.Scope);
        Assert.Equal(WorkflowJson.Write(content), WorkflowJson.Write(WorkflowJson.ReadDefinition(WorkflowJson.Write(content))));
    }

    [Fact]
    public void UnknownPropertyIsRejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            WorkflowJson.ReadDefinition("{\"documentType\": \"CONTRACT\", \"stpes\": []}"));
        Assert.Equal(WorkflowError.WF_DEFINITION_INVALID, ex.Code);
    }

    [Fact]
    public void NumbersInContextAreDecimal()
    {
        var vars = WorkflowJson.ReadVars("{\"amount\": 1200000000, \"rate\": 0.1, \"tier\": \"A\"}");
        Assert.Equal(1200000000m, vars["amount"]);
        Assert.Equal(0.1m, vars["rate"]);
        Assert.Equal("A", vars["tier"]);
    }

    [Fact]
    public void NormalizesClrNumbersToDecimal()
    {
        var vars = WorkflowJson.NormalizeVars(new Dictionary<string, object?> { ["a"] = 1200, ["b"] = 0.5d, ["c"] = 3L });
        Assert.Equal(1200m, vars["a"]);
        Assert.Equal(0.5m, vars["b"]);
        Assert.Equal(3m, vars["c"]);
    }
}
