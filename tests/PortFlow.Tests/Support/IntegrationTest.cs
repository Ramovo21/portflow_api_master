using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Support;

/// <summary>
/// = AbstractIntegrationTest: HttpClient tới app thật (TestServer) + truy vấn DB trực tiếp.
/// <see cref="Http"/> mặc định đăng nhập là <c>admin</c> (= <c>@WithPortflowUser("admin")</c>).
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public abstract class IntegrationTest(IntegrationFixture fixture) : IAsyncLifetime
{
    private NpgsqlDataSource _db = null!;
    private readonly List<HttpClient> _clients = [];

    protected IntegrationFixture Fixture { get; } = fixture;

    protected HttpClient Http { get; set; } = null!;

    /// <summary>Client không gửi token.</summary>
    protected HttpClient Anonymous { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        Http = ClientAs(SeedUser.ADMIN);
        Anonymous = Track(Fixture.App.CreateClient());
        _db = Fixture.NewDataSource();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _db.DisposeAsync();
    }

    protected HttpClient ClientAs(SeedUser user) => ClientWith(TestJwtIssuer.Bearer(user));

    protected HttpClient ClientWith(string authorization)
    {
        var client = Track(Fixture.App.CreateClient());
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorization);
        return client;
    }

    protected async Task<long> CountAsync(string sql, object? args = null)
    {
        await using var connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(sql, args);
    }

    protected async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await _db.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    protected async Task<List<T>> ListAsync<T>(string sql, object? args = null)
    {
        await using var connection = await _db.OpenConnectionAsync();
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }

    protected async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    /// <summary>Số event (theo tên kiểu) đã ghi vào outbox cho một user.</summary>
    protected Task<long> EventsForAsync(string eventType, Guid userId) =>
        CountAsync("""
            SELECT count(DISTINCT serialized_event) FROM shared.event_publication
            WHERE event_type LIKE @type AND serialized_event LIKE @user
            """, new { type = $"%.{eventType},%", user = $"%{userId}%" });

    protected static HttpRequestMessage Json(HttpMethod method, string url, string? json, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return request;
    }

    protected static HttpRequestMessage CreateWidget(string name, bool fail = false, string? key = null) =>
        Json(HttpMethod.Post, $"/test/widgets?fail={fail.ToString().ToLowerInvariant()}",
            JsonSerializer.Serialize(new { name }), key ?? Guid.NewGuid().ToString());

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    protected static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    protected static MediaTypeHeaderValue? ContentType(HttpResponseMessage response) => response.Content.Headers.ContentType;

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }
}
