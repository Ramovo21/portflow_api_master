using System.Net;
using PortFlow.Tests.Support;

namespace PortFlow.Tests.Shared.Web;

public class ErrorHandlingIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task DomainExceptionBecomesProblemDetailWithCorrelationId()
    {
        var request = CreateWidget("w-" + Guid.NewGuid(), fail: true);
        request.Headers.Add("X-Correlation-Id", "cid-42");
        var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal("application/problem+json", ContentType(response)?.MediaType);
        Assert.Equal("cid-42", response.Headers.GetValues("X-Correlation-Id").Single());
        var body = await ReadJsonAsync(response);
        Assert.Equal("SYS_VALIDATION", body.GetProperty("errorCode").GetString());
        Assert.Equal("https://portflow.local/errors/SYS_VALIDATION", body.GetProperty("type").GetString());
        Assert.Equal("cid-42", body.GetProperty("correlationId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, body.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task MappedConstraintViolationUsesModuleErrorCode()
    {
        var name = "dup-" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Http.SendAsync(CreateWidget(name))).StatusCode);

        var response = await Http.SendAsync(CreateWidget(name));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("WIDGET_DUPLICATE", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UnmappedConstraintViolationFallsBackToDataIntegrity()
    {
        var response = await Http.SendAsync(CreateWidget(new string('x', 60)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("SYS_DATA_INTEGRITY", body.GetProperty("errorCode").GetString());
        Assert.Equal("ck_widget_name_len", body.GetProperty("constraint").GetString());
    }

    [Fact]
    public async Task StaleVersionReturnsConcurrentModification()
    {
        var created = await ReadJsonAsync(await Http.SendAsync(CreateWidget("v-" + Guid.NewGuid())));
        var id = created.GetProperty("id").GetString();

        var response = await Http.SendAsync(Json(HttpMethod.Put, $"/test/widgets/{id}?version=99", "{\"name\":\"renamed\"}"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SYS_CONCURRENT_MODIFICATION", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task SuccessfulUpdateIncrementsVersion()
    {
        var created = await ReadJsonAsync(await Http.SendAsync(CreateWidget("u-" + Guid.NewGuid())));
        Assert.Equal(0, created.GetProperty("version").GetInt64());
        var id = created.GetProperty("id").GetString();

        var response = await Http.SendAsync(Json(HttpMethod.Put, $"/test/widgets/{id}?version=0",
            "{\"name\":\"renamed-" + Guid.NewGuid() + "\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response)).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task FrameworkErrorsAlsoCarryErrorCode()
    {
        var notFound = await Http.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("SYS_NOT_FOUND", (await ReadJsonAsync(notFound)).GetProperty("errorCode").GetString());

        var badJson = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", "{not json", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);
        Assert.Equal("SYS_BAD_REQUEST", (await ReadJsonAsync(badJson)).GetProperty("errorCode").GetString());
    }
}
