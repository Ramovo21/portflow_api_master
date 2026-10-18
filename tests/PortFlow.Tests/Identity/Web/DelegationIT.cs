using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using PortFlow.Identity.Api;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Web;

public class DelegationIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant()
        .InZone(DateTimeZoneProviders.Tzdb["Asia/Ho_Chi_Minh"]).Date;

    [Fact]
    public async Task DirectorDelegatesToDirector2ThenRevokes()
    {
        var from = Today.PlusYears(30);
        var response = await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from, from.PlusDays(5), "[\"CONTRACT\"]");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(SeedUser.DIRECTOR.Id, body.GetProperty("fromUserId").GetGuid());
        Assert.False(body.GetProperty("revoked").GetBoolean());
        var id = body.GetProperty("id").GetString();

        Assert.Equal([SeedUser.DIRECTOR.Id], await DelegatorsAsync(SeedUser.DIRECTOR2.Id, "CONTRACT", from.PlusDays(1)));
        Assert.Empty(await DelegatorsAsync(SeedUser.DIRECTOR2.Id, "PAYMENT_SHEET", from.PlusDays(1)));
        Assert.Empty(await DelegatorsAsync(SeedUser.DIRECTOR2.Id, "CONTRACT", from.PlusDays(6)));

        Assert.Equal(HttpStatusCode.Forbidden, (await RevokeAsync(SeedUser.DIRECTOR2, id)).StatusCode);
        var revoked = await RevokeAsync(SeedUser.DIRECTOR, id);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.True((await ReadJsonAsync(revoked)).GetProperty("revoked").GetBoolean());
        Assert.Empty(await DelegatorsAsync(SeedUser.DIRECTOR2.Id, "CONTRACT", from.PlusDays(1)));
    }

    [Fact]
    public async Task OverlappingDelegationIsRejected()
    {
        var from = Today.PlusYears(40);
        Assert.Equal(HttpStatusCode.Created,
            (await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from, from.PlusDays(10), "[]")).StatusCode);
        var response = await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from.PlusDays(10), from.PlusDays(20), "[]");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("AUTH_DELEGATION_OVERLAP", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task CannotDelegateOnBehalfOfSomeoneElse()
    {
        var from = Today.PlusYears(50);
        var response = await CreateAsync(SeedUser.DIRECTOR2, SeedUser.DIRECTOR.Id, SeedUser.DIRECTOR2.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminMayManageDelegationsForOthers()
    {
        var from = Today.PlusYears(60);
        var response = await CreateAsync(SeedUser.ADMIN, SeedUser.DIRECTOR2.Id, SeedUser.DIRECTOR.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(SeedUser.DIRECTOR2.Id, (await ReadJsonAsync(response)).GetProperty("fromUserId").GetGuid());
    }

    [Fact]
    public async Task InvalidRequestsAreValidationErrors()
    {
        var from = Today.PlusYears(70);
        var self = await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.UnprocessableContent, self.StatusCode);
        Assert.Equal("SYS_VALIDATION", await ErrorCodeAsync(self));
        Assert.Equal(HttpStatusCode.UnprocessableContent,
            (await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from.PlusDays(2), from, "[]")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableContent,
            (await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from, from, "[\"INVOICE\"]")).StatusCode);
    }

    [Fact]
    public async Task RoleWithoutDelegationManageIsForbidden()
    {
        var from = Today.PlusYears(80);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CreateAsync(SeedUser.SALES01, null, SeedUser.SALESMGR.Id, from, from, "[]")).StatusCode);
    }

    [Fact]
    public async Task DepartmentHeadsMayDelegateTheirOwnWork()
    {
        var from = Today.PlusYears(90);
        var legal = await CreateAsync(SeedUser.LEGAL01, null, SeedUser.DIRECTOR.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.Created, legal.StatusCode);
        Assert.Equal(SeedUser.LEGAL01.Id, (await ReadJsonAsync(legal)).GetProperty("fromUserId").GetGuid());
        Assert.Equal(HttpStatusCode.Created,
            (await CreateAsync(SeedUser.SALESMGR, null, SeedUser.SALES01.Id, from, from, "[\"CONTRACT\"]")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await CreateAsync(SeedUser.CHIEFACC, null, SeedUser.ACC01.Id, from, from, "[\"PAYMENT_SHEET\"]")).StatusCode);
    }

    [Fact]
    public async Task ResponseCarriesNamesOfBothParties()
    {
        var from = Today.PlusYears(100);
        var response = await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.DIRECTOR2.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(SeedUser.DIRECTOR.FullName, body.GetProperty("fromUserName").GetString());
        Assert.Equal(SeedUser.DIRECTOR2.FullName, body.GetProperty("toUserName").GetString());
    }

    [Fact]
    public async Task CannotDelegateToSomeoneWithoutApprovalPermission()
    {
        var from = Today.PlusYears(110);
        var response = await CreateAsync(SeedUser.DIRECTOR, null, SeedUser.ADMIN.Id, from, from, "[]");
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal("SYS_VALIDATION", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ApproverLookupListsOnlyUsersWhoCanAct()
    {
        var legal = ClientAs(SeedUser.LEGAL01);
        var all = await ReadJsonAsync(await legal.GetAsync("/api/v1/users/lookup/approvers"));
        var ids = all.EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(SeedUser.DIRECTOR2.Id, ids);
        Assert.DoesNotContain(SeedUser.ADMIN.Id, ids);

        var hai = await ReadJsonAsync(await legal.GetAsync("/api/v1/users/lookup/approvers?q=" + Uri.EscapeDataString("Hải")));
        Assert.Equal(SeedUser.DIRECTOR2.Id, hai[0].GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientAs(SeedUser.SALES01).GetAsync("/api/v1/users/lookup/approvers")).StatusCode);
    }

    [Fact]
    public async Task ActiveDelegationShowsInMeOfDelegate()
    {
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(SeedUser.ADMIN, SeedUser.ACC01.Id, SeedUser.CHIEFACC.Id,
            Today, Today.PlusDays(3), "[\"PAYMENT_SHEET\"]")).StatusCode);

        var me = await ReadJsonAsync(await ClientAs(SeedUser.CHIEFACC).GetAsync("/api/v1/me"));
        var actingFor = me.GetProperty("actingFor")[0];
        Assert.Equal(SeedUser.ACC01.Id, actingFor.GetProperty("userId").GetGuid());
        Assert.Equal("Phạm Thị Dung", actingFor.GetProperty("fullName").GetString());
        Assert.Equal("PAYMENT_SHEET", actingFor.GetProperty("documentTypes")[0].GetString());
        Assert.Equal(Iso(Today.PlusDays(3)), actingFor.GetProperty("validTo").GetString());

        Assert.Equal(HttpStatusCode.OK, (await ClientAs(SeedUser.ADMIN).GetAsync("/api/v1/delegations")).StatusCode);
    }

    private Task<HttpResponseMessage> CreateAsync(SeedUser actor, Guid? from, Guid to, LocalDate validFrom,
        LocalDate validTo, string documentTypes)
    {
        var fromJson = from is null ? "null" : $"\"{from}\"";
        return ClientAs(actor).SendAsync(Json(HttpMethod.Post, "/api/v1/delegations", $$"""
            {"fromUserId": {{fromJson}}, "toUserId": "{{to}}", "documentTypes": {{documentTypes}},
             "validFrom": "{{Iso(validFrom)}}", "validTo": "{{Iso(validTo)}}", "reason": "Đi công tác"}
            """, Guid.NewGuid().ToString()));
    }

    private Task<HttpResponseMessage> RevokeAsync(SeedUser actor, string? id) =>
        ClientAs(actor).SendAsync(Json(HttpMethod.Post, $"/api/v1/delegations/{id}/revoke", null, Guid.NewGuid().ToString()));

    private async Task<IReadOnlySet<Guid>> DelegatorsAsync(Guid delegateId, string documentType, LocalDate onDate)
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDelegationQuery>()
            .ActiveDelegatorsOfAsync(delegateId, documentType, onDate);
    }

    private static string Iso(LocalDate date) => LocalDatePattern.Iso.Format(date);
}
