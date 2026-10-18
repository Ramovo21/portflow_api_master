using System.Net;
using System.Text.Json;
using NodaTime;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;
using PortFlow.Tests.Workflow.Testing;

namespace PortFlow.Tests.Workflow;

/// <summary>
/// FR-W04 qua HTTP: ủy quyền (F01) → stepAuth → engine ghi on_behalf_of → inbox/chi tiết (F03).
/// Bước BGĐ yêu cầu cả DIRECTOR và DIRECTOR2 duyệt; DIRECTOR ủy quyền cho LEGAL01.
/// </summary>
public class DelegatedApprovalTests(IntegrationFixture fixture)
    : WorkflowTestBase(fixture, TestApprovableDocuments.TypeAllApprovers, Definition)
{
    private const string Definition = """
        {"documentType": "TEST_DOC_ALL", "resubmitPolicy": "RESTART",
         "steps": [
           {"order": 1, "name": "Ban Giám đốc", "assignee": {"type": "GROUP", "value": "DIRECTOR"},
            "onReject": "TERMINATE", "slaHours": 48, "requiresAllApprovers": true}],
         "onFinalApprove": {"emitESign": false}}
        """;

    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant()
        .InZone(DateTimeZoneProviders.Tzdb["Asia/Ho_Chi_Minh"]).Date;

    public override async Task DisposeAsync()
    {
        await ExecuteAsync("DELETE FROM identity.delegation WHERE @type = ANY (document_types)", new { type = DocumentType });
        await base.DisposeAsync();
    }

    [Fact]
    public async Task DelegateVotesOnBehalfOfDirectorAndStepLeavesTheirInbox()
    {
        await DelegateAsync(SeedUser.DIRECTOR, SeedUser.LEGAL01);
        var instance = await StartAsync();

        Assert.NotEmpty(await InboxItemsAsync(SeedUser.LEGAL01, instance));

        var first = await ApproveAsync(SeedUser.LEGAL01, instance);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("RUNNING", (await ReadJsonAsync(first)).GetProperty("status").GetString());
        Assert.Equal(SeedUser.DIRECTOR.Id, await ScalarAsync<Guid>("""
            SELECT a.on_behalf_of FROM workflow.approval_action a
            JOIN workflow.step_instance s ON s.id = a.step_instance_id
            WHERE s.instance_id = @instance AND a.user_id = @user
            """, new { instance, user = SeedUser.LEGAL01.Id }));

        Assert.Empty(await InboxItemsAsync(SeedUser.LEGAL01, instance));
        Assert.Empty(await InboxItemsAsync(SeedUser.DIRECTOR, instance));
        Assert.NotEmpty(await InboxItemsAsync(SeedUser.DIRECTOR2, instance));

        foreach (var user in new[] { SeedUser.LEGAL01, SeedUser.DIRECTOR })
        {
            var again = await ApproveAsync(user, instance);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal("WF_ALREADY_VOTED", await ErrorCodeAsync(again));
        }

        var last = await ApproveAsync(SeedUser.DIRECTOR2, instance);
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        Assert.Equal("APPROVED", (await ReadJsonAsync(last)).GetProperty("status").GetString());

        var detail = await GetJsonAsync(SeedUser.LEGAL01, $"/api/v1/workflow-instances/{instance}");
        var delegated = detail.GetProperty("history").EnumerateArray()
            .Single(entry => entry.GetProperty("user").GetProperty("id").GetGuid() == SeedUser.LEGAL01.Id);
        Assert.Equal(SeedUser.DIRECTOR.Id, delegated.GetProperty("onBehalfOf").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RevokedDelegationNoLongerGrantsTheStep()
    {
        var delegation = await DelegateAsync(SeedUser.DIRECTOR, SeedUser.LEGAL01);
        var instance = await StartAsync();
        var revoked = await ClientAs(SeedUser.DIRECTOR).SendAsync(Json(HttpMethod.Post,
            $"/api/v1/delegations/{delegation}/revoke", null, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        Assert.Empty(await InboxItemsAsync(SeedUser.LEGAL01, instance));
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(SeedUser.LEGAL01, instance)).StatusCode);
    }

    [Fact]
    public async Task SubmitterCannotApproveTheirOwnDocumentAsDelegate()
    {
        await DelegateAsync(SeedUser.DIRECTOR, SeedUser.SALES01);
        var instance = await StartAsync();

        Assert.Empty(await InboxItemsAsync(SeedUser.SALES01, instance));
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(SeedUser.SALES01, instance)).StatusCode);
    }

    private async Task<string> DelegateAsync(SeedUser from, SeedUser to)
    {
        var body = JsonSerializer.Serialize(new
        {
            toUserId = to.Id,
            documentTypes = new[] { DocumentType },
            validFrom = Today.ToString("yyyy-MM-dd", null),
            validTo = Today.PlusDays(1).ToString("yyyy-MM-dd", null),
            reason = "Nghỉ phép",
        });
        var response = await ClientAs(from).SendAsync(Json(HttpMethod.Post, "/api/v1/delegations", body,
            Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }

    private async Task<Guid> StartAsync() => (await StartAsync(Guid.NewGuid(), null)).InstanceId;

    private async Task<HttpResponseMessage> ApproveAsync(SeedUser user, Guid instance)
    {
        var step = await ScalarAsync<Guid>(
            "SELECT id FROM workflow.step_instance WHERE instance_id = @instance ORDER BY step_order LIMIT 1",
            new { instance });
        return await ActAsync(user, instance, step, "approve", null);
    }
}
