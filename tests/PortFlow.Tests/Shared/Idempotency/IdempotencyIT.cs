using System.Net;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Shared.Idempotency;

public class IdempotencyIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    private const string Replayed = "Idempotent-Replayed";

    [Fact]
    public async Task MissingOrMalformedKeyIsRejected()
    {
        var response = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body("no-key")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SYS_IDEMPOTENCY_KEY_REQUIRED", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());

        response = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body("bad-key"), "not-a-uuid"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SYS_IDEMPOTENCY_KEY_REQUIRED", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());

        Assert.Equal(0, await WidgetsAsync("no-key") + await WidgetsAsync("bad-key"));
    }

    [Fact]
    public async Task SameKeyAndBodyReplaysTheStoredResponse()
    {
        var key = Guid.NewGuid().ToString();
        var name = "replay-" + key;

        var first = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body(name), key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.Headers.Contains(Replayed));

        var second = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body(name), key));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(Replayed).Single());

        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, await WidgetsAsync(name));
    }

    [Fact]
    public async Task SameKeyWithDifferentBodyIsRejected()
    {
        var key = Guid.NewGuid().ToString();
        var created = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body("a-" + key), key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await Http.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body("b-" + key), key));
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal("SYS_IDEMPOTENCY_KEY_REUSED", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());
        Assert.Equal(0, await WidgetsAsync("b-" + key));
    }

    [Fact]
    public async Task FailedUseCaseRollsBackTheKeySoTheClientCanRetry()
    {
        var key = Guid.NewGuid().ToString();
        var name = "retry-" + key;

        var failed = await Http.SendAsync(CreateWidget(name, fail: true, key: key));
        Assert.Equal(HttpStatusCode.UnprocessableContent, failed.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM shared.idempotency_request WHERE key = @key", new { key }));
        Assert.Equal(0, await WidgetsAsync(name));

        var retried = await Http.SendAsync(CreateWidget(name, key: key));
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains(Replayed));
        Assert.Equal(1, await WidgetsAsync(name));
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKeyCreateExactlyOneWidget()
    {
        var key = Guid.NewGuid().ToString();
        var name = "concurrent-" + key;

        var statuses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var client = ClientAs(SeedUser.ADMIN);
            var response = await client.SendAsync(Json(HttpMethod.Post, "/test/widgets", Body(name), key));
            return (int)response.StatusCode;
        }));

        Assert.Equal(1, await WidgetsAsync(name));
        Assert.All(statuses, s => Assert.True(s is 201 or 409, $"unexpected status {s}"));
        Assert.Contains(201, statuses);
    }

    private Task<long> WidgetsAsync(string name) =>
        CountAsync("SELECT count(*) FROM test_fixture.widget WHERE name = @name", new { name });

    private static string Body(string name) => "{\"name\":\"" + name + "\"}";
}
