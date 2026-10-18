using System.Net;
using System.Text.Json;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Web;

public class IdentityAdminIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task CreatesAndUpdatesDepartmentAndRejectsDuplicateCode()
    {
        var code = "D" + Guid.NewGuid().ToString()[..6].ToUpperInvariant();
        var created = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/admin/departments",
            $$"""{"code":"{{code}}","name":"Phòng thử","headUserId":"{{SeedUser.LEGAL01.Id}}"}""", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(SeedUser.LEGAL01.Id, body.GetProperty("headUserId").GetGuid());
        var id = body.GetProperty("id").GetString();

        var updated = await Http.SendAsync(Json(HttpMethod.Put, $"/api/v1/admin/departments/{id}",
            $$"""{"code":"{{code}}","name":"Phòng thử 2","parentId":"{{SeedUser.DEPT_BGD}}"}"""));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await ReadJsonAsync(updated);
        Assert.Equal("Phòng thử 2", updatedBody.GetProperty("name").GetString());
        Assert.Equal(SeedUser.DEPT_BGD, updatedBody.GetProperty("parentId").GetGuid());
        Assert.Equal(1, updatedBody.GetProperty("version").GetInt64());

        var duplicate = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/admin/departments",
            $$"""{"code":"{{code}}","name":"Trùng"}""", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("AUTH_DEPARTMENT_CODE_EXISTS", await ErrorCodeAsync(duplicate));

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/v1/admin/departments/{Guid.NewGuid()}")).StatusCode);

        var blank = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/admin/departments",
            """{"code":"","name":"x"}""", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableContent, blank.StatusCode);
        Assert.Equal("code", (await ReadJsonAsync(blank)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task ListsRolesAndPermissionsAndProtectsSystemRoles()
    {
        var roles = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/roles"));
        var codes = roles.EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        foreach (var expected in new[] { "SALES", "SALES_MANAGER", "OPERATIONS", "OPERATIONS_LEAD", "ACCOUNTANT",
                     "CHIEF_ACCOUNTANT", "LEGAL", "DIRECTOR", "ADMIN" })
        {
            Assert.Contains(expected, codes);
        }

        var sales = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/roles/SALES"));
        Assert.Equal("DEPARTMENT", sales.GetProperty("dataScope").GetString());
        Assert.Contains("Contract.Submit", Strings(sales.GetProperty("permissions")));
        Assert.True(sales.GetProperty("system").GetBoolean());

        var admin = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/roles/ADMIN"));
        Assert.DoesNotContain("Workflow.Act", Strings(admin.GetProperty("permissions")));

        var permissions = await Http.GetAsync("/api/v1/admin/permissions");
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        Assert.Contains("Workflow.Act", Strings(await ReadJsonAsync(permissions)));

        var immutable = await Http.SendAsync(Json(HttpMethod.Put, "/api/v1/admin/roles/SALES",
            """{"name":"x","permissions":["Contract.Read"],"dataScope":"ALL"}"""));
        Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);
        Assert.Equal("AUTH_SYSTEM_ROLE_IMMUTABLE", await ErrorCodeAsync(immutable));
    }

    [Fact]
    public async Task UpdatingCustomRoleTakesEffectImmediately()
    {
        var code = "AUDITOR_" + Guid.NewGuid().ToString()[..6].ToUpperInvariant();
        await ExecuteAsync("""
            INSERT INTO identity.role (code, name, permissions, data_scope, system)
            VALUES (@code, 'Kiểm toán', '{}', 'DEPARTMENT', false)
            """, new { code });
        var userId = Guid.NewGuid();
        var user = ClientWith(TestJwtIssuer.BearerFor(userId, "aud-" + userId.ToString()[..8], "PC", code));
        Assert.Equal(0, (await ReadJsonAsync(await user.GetAsync("/api/v1/me"))).GetProperty("permissions").GetArrayLength());

        var updated = await Http.SendAsync(Json(HttpMethod.Put, $"/api/v1/admin/roles/{code}",
            """{"name":"Kiểm toán","permissions":["Audit.Read"],"dataScope":"ALL"}"""));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(updated)).GetProperty("version").GetInt64());

        var me = await ReadJsonAsync(await user.GetAsync("/api/v1/me"));
        Assert.Equal("Audit.Read", me.GetProperty("permissions")[0].GetString());
        Assert.Equal("ALL", me.GetProperty("dataScope").GetString());

        Assert.Equal(HttpStatusCode.UnprocessableContent, (await Http.SendAsync(Json(HttpMethod.Put,
            $"/api/v1/admin/roles/{code}", """{"name":"x","permissions":["Bogus.Perm"],"dataScope":"ALL"}"""))).StatusCode);
    }

    [Fact]
    public async Task ReadsAndUpdatesDocumentTypes()
    {
        var all = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/document-types"));
        Assert.Equal(4, all.GetArrayLength());
        Assert.True((await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/document-types/CONTRACT")))
            .GetProperty("requiresESign").GetBoolean());

        var updated = await Http.SendAsync(Json(HttpMethod.Put, "/api/v1/admin/document-types/ADDENDUM", """
            {"name":"Phụ lục hợp đồng","requiresESign":true,"workflowKey":"ADDENDUM","active":true,
             "submitRequirements":{"attachments":["SIGNED_SCAN"]}}
            """));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await ReadJsonAsync(updated);
        Assert.True(body.GetProperty("requiresESign").GetBoolean());
        Assert.Equal("SIGNED_SCAN", body.GetProperty("submitRequirements").GetProperty("attachments")[0].GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/v1/admin/document-types/NOPE")).StatusCode);
    }

    [Fact]
    public async Task ListsUserMirrorWithFilters()
    {
        var directors = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/users?role=DIRECTOR&active=true"));
        var director = directors.EnumerateArray().Single(u => u.GetProperty("username").GetString() == "director");
        Assert.Equal("BGD", director.GetProperty("departmentCode").GetString());
        Assert.Equal("DIRECTOR", director.GetProperty("roles")[0].GetString());
        Assert.DoesNotContain(directors.EnumerateArray(), u => u.GetProperty("username").GetString() == "sales01");

        var binh = await ReadJsonAsync(await Http.GetAsync("/api/v1/admin/users?q=" + Uri.EscapeDataString("Bình")));
        Assert.Equal("salesmgr", binh[0].GetProperty("username").GetString());

        var kt = await ReadJsonAsync(await Http.GetAsync($"/api/v1/admin/users?departmentId={SeedUser.DEPT_KT}"));
        Assert.Contains(kt.EnumerateArray(), u => u.GetProperty("username").GetString() == "ops01");
    }

    [Fact]
    public async Task ManualSyncReportsUnavailableKeycloak()
    {
        var response = await Http.PostAsync("/api/v1/admin/users/sync", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("AUTH_IDP_UNAVAILABLE", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task UserLookupRequiresRoleParameter()
    {
        var sales = ClientAs(SeedUser.SALES01);
        var legal = await ReadJsonAsync(await sales.GetAsync("/api/v1/users/lookup?role=LEGAL"));
        Assert.Contains(legal.EnumerateArray(), u => u.GetProperty("username").GetString() == "legal01");
        Assert.Equal(HttpStatusCode.BadRequest, (await sales.GetAsync("/api/v1/users/lookup")).StatusCode);
    }

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToList();
}
