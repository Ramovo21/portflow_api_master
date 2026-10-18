using PortFlow.Identity.Domain;

namespace PortFlow.Tests.Identity.Domain;

public class RoleAssignmentPolicyTests
{
    private static readonly Dictionary<string, string[]> Seed = new()
    {
        ["SALES"] = ["Contract.Create", "Workflow.Act"],
        ["SALES_MANAGER"] = ["Contract.Cancel", "Workflow.Act"],
        ["OPERATIONS"] = ["Volume.Record", "Workflow.Act"],
        ["OPERATIONS_LEAD"] = ["Volume.ReopenPeriod", "Workflow.Act"],
        ["ACCOUNTANT"] = ["PaymentSheet.Create", "Workflow.Act"],
        ["CHIEF_ACCOUNTANT"] = ["PaymentSheet.Create", "Workflow.Act"],
        ["LEGAL"] = ["Contract.Read", "Workflow.Act"],
        ["DIRECTOR"] = ["Audit.ReadAll", "Workflow.Act"],
        ["ADMIN"] = ["User.Admin", "Role.Admin", "Workflow.Admin"],
    };

    public static TheoryData<string> ApproverRoles() => new(Seed.Keys.Where(role => role != "ADMIN"));

    [Theory]
    [MemberData(nameof(ApproverRoles))]
    public void AdminCombinedWithAnyApproverRoleViolates(string approverRole)
    {
        var roles = new HashSet<string> { "ADMIN", approverRole };
        Assert.True(RoleAssignmentPolicy.ViolatesSoD(roles, PermissionsOf(roles)));
    }

    [Theory]
    [MemberData(nameof(ApproverRoles))]
    public void ApproverRoleAloneIsFine(string approverRole)
    {
        var roles = new HashSet<string> { approverRole };
        Assert.False(RoleAssignmentPolicy.ViolatesSoD(roles, PermissionsOf(roles)));
    }

    [Fact]
    public void AdminAloneIsFine()
    {
        var roles = new HashSet<string> { "ADMIN" };
        Assert.False(RoleAssignmentPolicy.ViolatesSoD(roles, PermissionsOf(roles)));
    }

    [Fact]
    public void NoRolesIsFine()
    {
        Assert.False(RoleAssignmentPolicy.ViolatesSoD(new HashSet<string>(), new HashSet<string>()));
    }

    private static HashSet<string> PermissionsOf(IEnumerable<string> roles) =>
        roles.SelectMany(role => Seed[role]).ToHashSet();
}
