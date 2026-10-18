using System.Net;
using System.Text.RegularExpressions;
using PortFlow.Tests.Support;

namespace PortFlow.Tests;

public class PortFlowApplicationIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task HealthReportsDatabaseBrokerAndStorage()
    {
        var response = await Http.GetAsync("/actuator/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("UP", body.GetProperty("status").GetString());
        var components = body.GetProperty("components");
        Assert.Equal("UP", components.GetProperty("db").GetProperty("status").GetString());
        Assert.Equal("UP", components.GetProperty("rabbit").GetProperty("status").GetString());
        Assert.Equal("UP", components.GetProperty("minio").GetProperty("status").GetString());
    }

    [Fact]
    public async Task EchoesValidCorrelationIdAndGeneratesOtherwise()
    {
        var valid = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        valid.Headers.Add("X-Correlation-Id", "abc-123");
        var response = await Http.SendAsync(valid);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-Id").Single());

        var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        invalid.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad value; Injected: 1");
        response = await Http.SendAsync(invalid);
        Assert.Matches(new Regex("^[0-9a-f-]{36}$"), response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task ExposesPrometheusMetrics()
    {
        var response = await Http.GetAsync("/actuator/prometheus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("outbox_incomplete_count", await response.Content.ReadAsStringAsync());
    }
}
