using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Web;

/// <summary>Bảng (endpoint, user) → status cho toàn hệ thống; module mới thêm dòng vào đây.</summary>
public class SecurityMatrixIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    public static TheoryData<string, string, SeedUser?, int> Matrix() => new()
    {
        { "GET", "/api/v1/me", null, 401 },
        { "GET", "/api/v1/me", SeedUser.SALES01, 200 },
        { "GET", "/api/v1/me", SeedUser.OPS01, 200 },
        { "GET", "/api/v1/me", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/users/lookup?role=LEGAL", null, 401 },
        { "GET", "/api/v1/users/lookup?role=LEGAL", SeedUser.ACC01, 200 },
        { "GET", "/api/v1/admin/users", null, 401 },
        { "GET", "/api/v1/admin/users", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/users", SeedUser.DIRECTOR, 403 },
        { "GET", "/api/v1/admin/users", SeedUser.SALES01, 403 },
        { "POST", "/api/v1/admin/users/sync", SeedUser.SALESMGR, 403 },
        { "GET", "/api/v1/admin/departments", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/departments", SeedUser.LEGAL01, 403 },
        { "GET", "/api/v1/admin/roles", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/roles", SeedUser.CHIEFACC, 403 },
        { "GET", "/api/v1/admin/permissions", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/permissions", SeedUser.OPS01, 403 },
        { "GET", "/api/v1/admin/document-types", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/document-types", SeedUser.DIRECTOR2, 403 },
        { "GET", "/api/v1/delegations", SeedUser.DIRECTOR, 200 },
        { "GET", "/api/v1/delegations", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/delegations", SeedUser.SALES01, 403 },
        { "GET", "/api/v1/delegations", SeedUser.LEGAL01, 200 },
        { "GET", "/api/v1/delegations", SeedUser.SALESMGR, 200 },
        { "GET", "/api/v1/delegations", SeedUser.CHIEFACC, 200 },
        { "GET", "/api/v1/delegations", SeedUser.ACC01, 403 },
        { "GET", "/api/v1/admin/outbox", SeedUser.ADMIN, 200 },
        { "GET", "/api/v1/admin/outbox", SeedUser.ACC01, 403 },
        { "GET", "/api/v1/admin/outbox", null, 401 },
        { "GET", "/actuator/health", null, 200 },
        { "GET", "/actuator/prometheus", null, 401 },
        { "GET", "/api/health", null, 200 },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task EnforcesMatrix(string method, string path, SeedUser? user, int expectedStatus)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var client = user is null ? Anonymous : ClientAs(user);
        Assert.Equal(expectedStatus, (int)(await client.SendAsync(request)).StatusCode);
    }
}
