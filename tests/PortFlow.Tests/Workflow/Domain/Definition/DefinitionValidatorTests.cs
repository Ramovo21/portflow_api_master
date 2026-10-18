using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Tests.Workflow.Domain.Definition;

public class DefinitionValidatorTests
{
    private static readonly Guid ActiveUser = Guid.NewGuid();
    private static readonly Guid InactiveUser = Guid.NewGuid();

    private readonly DefinitionValidator _validator = new(new FakeConditions(), new FakeDirectory(),
        type => type == "CONTRACT");

    [Fact]
    public async Task ValidDefinitionHasNoViolations()
    {
        Assert.Empty(await _validator.ValidateAsync(Content(Step(1, null), Last(2)), true));
    }

    [Fact]
    public async Task V1_StepsMustBeContiguousFromOne()
    {
        Assert.Contains("V1", await Rules(Content(Step(1, null), Last(3))));
        Assert.Contains("V1", await Rules(Content(Step(1, null), Last(1))));
        Assert.Contains("V1", await Rules(new DefinitionContent("CONTRACT", null, [], null)));
        Assert.Contains("V1", await Rules(new DefinitionContent("contract", null, [Last(1)], null)));
    }

    [Fact]
    public async Task V1_TooManySteps()
    {
        Assert.Contains("V1", await Rules(Content(Enumerable.Range(1, 16).Select(Last).ToArray())));
    }

    [Fact]
    public async Task V2_AtLeastOneUnconditionalStep()
    {
        Assert.Contains("V2", await Rules(Content(Step(1, "amount > 1"),
            new StepDefinition(2, "BGĐ", AssigneeRule.Role("LEGAL"), OnReject.TERMINATE, "amount > 2", null, false))));
    }

    [Fact]
    public async Task V3_AssigneeMustExist()
    {
        Assert.Contains("V3", await Rules(Content(WithAssignee(AssigneeRule.Role("NO_SUCH_ROLE")))));
        Assert.Contains("V3", await Rules(Content(WithAssignee(new AssigneeRule(AssigneeType.USER, InactiveUser.ToString(), null, null)))));
        Assert.Contains("V3", await Rules(Content(WithAssignee(new AssigneeRule(AssigneeType.USER, "not-a-uuid", null, null)))));
        Assert.Contains("V3", await Rules(Content(WithAssignee(new AssigneeRule(AssigneeType.DYNAMIC, "submitter.wife", null, null)))));
        Assert.Contains("V3", await Rules(Content(WithAssignee(new AssigneeRule(AssigneeType.GROUP, null, null, [])))));
        Assert.DoesNotContain("V3", await Rules(Content(WithAssignee(new AssigneeRule(AssigneeType.GROUP, null, null, [ActiveUser])))));
        Assert.DoesNotContain("V3", await Rules(Content(WithAssignee(
            new AssigneeRule(AssigneeType.DYNAMIC, AssigneeRule.DYNAMIC_DEPARTMENT_HEAD, null, null)))));
    }

    [Fact]
    public async Task V4_ConditionMustParse()
    {
        Assert.Contains("V4", await Rules(Content(Step(1, "bad syntax"), Last(2))));
        Assert.Contains("V4", await Rules(Content(Step(1, new string('x', 501)), Last(2))));
    }

    [Fact]
    public async Task V5_RequiresAllOnlyForGroupOrRole()
    {
        var all = new StepDefinition(1, "Duyệt", new AssigneeRule(AssigneeType.DYNAMIC,
            AssigneeRule.DYNAMIC_DEPARTMENT_HEAD, null, null), OnReject.TERMINATE, null, null, true);
        Assert.Contains("V5", await Rules(Content(all)));
    }

    [Fact]
    public async Task V6_SystemAdminRoleCannotApprove()
    {
        Assert.Contains("V6", await Rules(Content(WithAssignee(AssigneeRule.Role("ADMIN")))));
    }

    [Fact]
    public async Task V7_DocumentTypeWithoutHandlerIsWarningOnSaveErrorOnActivate()
    {
        var content = new DefinitionContent("ADVANCE_REQUEST", null, [Last(1)], null);
        var onSave = Assert.Single(await _validator.ValidateAsync(content, false));
        Assert.Equal("V7", onSave.Rule);
        Assert.False(onSave.IsError);
        Assert.True(Assert.Single(await _validator.ValidateAsync(content, true)).IsError);
    }

    [Fact]
    public async Task V8_LastStepShouldTerminateIsOnlyAWarning()
    {
        var violation = Assert.Single(await _validator.ValidateAsync(Content(Step(1, null)), true));
        Assert.Equal("V8", violation.Rule);
        Assert.False(violation.IsError);
    }

    private async Task<List<string>> Rules(DefinitionContent content) =>
        (await _validator.ValidateAsync(content, false)).Where(v => v.IsError).Select(v => v.Rule).ToList();

    private static DefinitionContent Content(params StepDefinition[] steps) => new("CONTRACT", null, steps, null);

    private static StepDefinition Step(int order, string? condition) =>
        new(order, $"Bước {order}", AssigneeRule.Role("SALES_MANAGER"), OnReject.BACK_TO_SUBMITTER, condition, 24, false);

    private static StepDefinition Last(int order) =>
        new(order, $"Bước {order}", AssigneeRule.Role("LEGAL"), OnReject.TERMINATE, null, null, false);

    private static StepDefinition WithAssignee(AssigneeRule rule) =>
        new(1, "Bước 1", rule, OnReject.TERMINATE, null, null, false);

    private sealed class FakeConditions : IConditionEvaluator
    {
        public bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars)
        {
            if (expression is not null && expression.Contains("bad"))
            {
                throw new DomainException(WorkflowError.WF_CONDITION_ERROR, "bad",
                    new Dictionary<string, object?> { ["cause"] = "parse" });
            }
            return true;
        }
    }

    private sealed class FakeDirectory : IAssigneeDirectory
    {
        private static readonly Dictionary<string, IReadOnlySet<string>> Roles = new()
        {
            ["SALES_MANAGER"] = new HashSet<string> { "Workflow.Act" },
            ["LEGAL"] = new HashSet<string> { "Workflow.Act" },
            ["ADMIN"] = new HashSet<string> { "System.Admin", "Workflow.Admin" },
        };

        public Task<IReadOnlySet<string>?> PermissionsOfRoleAsync(string roleCode, CancellationToken ct) =>
            Task.FromResult(Roles.GetValueOrDefault(roleCode));

        public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) => Task.FromResult(userId == ActiveUser);
    }
}
