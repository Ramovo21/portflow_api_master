using System.Net;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Fixture;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Web;

public class AuthorizationIT : IntegrationTest
{
    private static readonly Guid DocOfKd = Guid.NewGuid();
    private static readonly Guid DocOfPc = Guid.NewGuid();

    public AuthorizationIT(IntegrationFixture fixture) : base(fixture)
    {
        TestSecuredFixture.Owners[DocOfKd] = SeedUser.DEPT_KD;
        TestSecuredFixture.Owners[DocOfPc] = SeedUser.DEPT_PC;
    }

    [Fact]
    public async Task DepartmentScopedUserReadsOwnDepartmentDocument()
    {
        Assert.Equal(HttpStatusCode.OK, (await ClientAs(SeedUser.SALES01).GetAsync($"/test/secured/docs/{DocOfKd}")).StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedUserCannotReadOtherDepartmentDocument()
    {
        var response = await ClientAs(SeedUser.SALES01).GetAsync($"/test/secured/docs/{DocOfPc}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AllScopeUserReadsAnyDepartment()
    {
        Assert.Equal(HttpStatusCode.OK, (await ClientAs(SeedUser.LEGAL01).GetAsync($"/test/secured/docs/{DocOfKd}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientAs(SeedUser.DIRECTOR).GetAsync($"/test/secured/docs/{DocOfKd}")).StatusCode);
    }

    [Fact]
    public async Task MissingPermissionIsForbiddenBeforeScope()
    {
        var response = await ClientAs(SeedUser.ADMIN).GetAsync($"/test/secured/docs/{DocOfKd}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task MissingDocumentIsNotFound()
    {
        var response = await ClientAs(SeedUser.LEGAL01).GetAsync($"/test/secured/docs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SYS_NOT_FOUND", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ApproverCanReachApprovalEndpoint()
    {
        Assert.Equal(HttpStatusCode.OK,
            (await ClientAs(SeedUser.LEGAL01).PostAsync($"/test/secured/steps/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task PlainAdminCannotApprove()
    {
        var response = await ClientAs(SeedUser.ADMIN).PostAsync($"/test/secured/steps/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ApprovalWithoutTokenIsUnauthenticated()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.PostAsync($"/test/secured/steps/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task AdminWithApproverRoleIsFlaggedAndBlockedWithSodViolation()
    {
        var id = Guid.NewGuid();
        var client = ClientWith(TestJwtIssuer.BearerFor(id, "sod-" + id.ToString()[..8], "IT", "ADMIN", "DIRECTOR"));

        var response = await client.PostAsync($"/test/secured/steps/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_SOD_VIOLATION", await ErrorCodeAsync(response));
        Assert.True(await ScalarAsync<bool>("SELECT sod_violation FROM identity.app_user WHERE id = @id", new { id }));
        Assert.True(await EventsForAsync("SodViolationDetected", id) > 0);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test/secured/docs/{DocOfKd}")).StatusCode);

        // User tạm mang role DIRECTOR: tắt đi để không lọt vào nhóm duyệt DIRECTOR của test workflow
        await ExecuteAsync("UPDATE identity.app_user SET active = false WHERE id = @id", new { id });
    }
}
