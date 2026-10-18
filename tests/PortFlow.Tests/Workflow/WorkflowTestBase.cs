using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;
using PortFlow.Workflow.Api;

namespace PortFlow.Tests.Workflow;

/// <summary>Chuẩn bị loại hồ sơ test + định nghĩa ACTIVE qua API admin, và các helper gọi engine/HTTP.</summary>
public abstract class WorkflowTestBase(IntegrationFixture fixture, string documentType, string definition)
    : IntegrationTest(fixture)
{
    protected string DocumentType { get; } = documentType;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync("""
            INSERT INTO identity.document_type (code, name, requires_esign, workflow_key)
            VALUES (@type, 'Hồ sơ test', true, @type) ON CONFLICT DO NOTHING
            """, new { type = DocumentType });
        var active = await CountAsync("""
            SELECT count(*) FROM workflow.workflow_definition WHERE document_type = @type AND status = 'ACTIVE'
            """, new { type = DocumentType });
        if (active == 0)
        {
            var created = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/admin/workflow-definitions", definition,
                Guid.NewGuid().ToString()));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await ReadJsonAsync(created);
            Assert.Equal("DRAFT", body.GetProperty("status").GetString());
            var activated = await Admin(HttpMethod.Post,
                $"/api/v1/admin/workflow-definitions/{body.GetProperty("id").GetString()}/activate");
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        }
    }

    public override async Task DisposeAsync()
    {
        await ExecuteAsync("DELETE FROM identity.document_type WHERE code = @type", new { type = DocumentType });
        await base.DisposeAsync();
    }

    protected async Task<WorkflowStartResult> StartAsync(Guid documentId, IReadOnlyDictionary<string, object?>? vars,
        SeedUser? submitter = null)
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowService>().StartAsync(
            new StartWorkflowCommand(DocumentType, documentId, (submitter ?? SeedUser.SALES01).Id, vars));
    }

    protected async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> call) where TService : notnull
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected Task<HttpResponseMessage> Admin(HttpMethod method, string url, string? json = null) =>
        Http.SendAsync(Json(method, url, json, Guid.NewGuid().ToString()));

    protected Task<HttpResponseMessage> ActAsync(SeedUser user, Guid instance, Guid step, string action,
        string? comment, string? key = null)
    {
        var body = comment is null ? "{}" : JsonSerializer.Serialize(new { comment });
        return ClientAs(user).SendAsync(Json(HttpMethod.Post,
            $"/api/v1/workflow-instances/{instance}/steps/{step}/{action}", body, key ?? Guid.NewGuid().ToString()));
    }

    protected Task<Guid> CurrentStepAsync(Guid instance) =>
        ScalarAsync<Guid>("SELECT id FROM workflow.step_instance WHERE instance_id = @instance AND status = 'PENDING'",
            new { instance });

    protected Task<long> InstanceEventsAsync(string eventType, Guid instance) =>
        CountAsync("""
            SELECT count(DISTINCT serialized_event) FROM shared.event_publication
            WHERE event_type LIKE @type AND serialized_event LIKE @instance
            """, new { type = $"%.{eventType},%", instance = $"%{instance}%" });

    protected async Task<JsonElement> GetJsonAsync(SeedUser user, string url)
    {
        var response = await ClientAs(user).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected async Task<List<JsonElement>> InboxItemsAsync(SeedUser user, Guid instance)
    {
        var page = await GetJsonAsync(user, $"/api/v1/inbox?docType={DocumentType}&size=100");
        return page.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("instanceId").GetGuid() == instance)
            .ToList();
    }
}
