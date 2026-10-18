using System.Net;
using PortFlow.Tests.Support;

namespace PortFlow.Tests.Workflow;

/// <summary>Chế độ demo workflow (chỉ dev) mặc định tắt: endpoint trả 404, không có loại hồ sơ DEMO.</summary>
public class WorkflowDemoDisabledTests(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task DemoEndpointsAreNotFoundWhenDisabled()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/v1/dev/workflow-demo")).StatusCode);
        var submit = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/dev/workflow-demo",
            """{"title": "x", "amount": 1}""", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/v1/dev/workflow-demo")).StatusCode);
    }
}
