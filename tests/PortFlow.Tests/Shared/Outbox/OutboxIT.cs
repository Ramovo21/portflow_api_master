using System.Net;
using PortFlow.Tests.Support;

namespace PortFlow.Tests.Shared.Outbox;

public class OutboxIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task CommittedEventReachesConsumerExactlyOnce()
    {
        var widgetId = await CreateWidgetAsync("ok-" + Guid.NewGuid());

        await Eventually.UntilAsync(async () => await ReceivedAsync(widgetId) == 1, TimeSpan.FromSeconds(15));
        await Eventually.UntilAsync(async () => await IncompleteForAsync(widgetId) == 0, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RolledBackTransactionPublishesNothing()
    {
        var name = "rollback-" + Guid.NewGuid();
        var response = await Http.SendAsync(CreateWidget(name, fail: true));
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        Assert.Equal(0, await CountAsync(
            "SELECT count(*) FROM shared.event_publication WHERE serialized_event LIKE @name", new { name = $"%{name}%" }));
    }

    [Fact]
    public async Task EventSurvivesBrokerOutageAndIsDeliveredAfterResubmit()
    {
        // Làm nóng kết nối/kênh để lần gửi sau thật sự bị kẹt khi broker treo
        var warmUp = await CreateWidgetAsync("warm-" + Guid.NewGuid());
        await Eventually.UntilAsync(async () => await ReceivedAsync(warmUp) == 1, TimeSpan.FromSeconds(15));

        Guid widgetId;
        await Fixture.Rabbit.PauseAsync();
        try
        {
            widgetId = await CreateWidgetAsync("outage-" + Guid.NewGuid());
            // Chờ quá confirm timeout (3s ở profile test): publication phải còn "chưa hoàn thành"
            var id = widgetId;
            await Eventually.UntilAsync(async () => await IncompleteForAsync(id) == 1, TimeSpan.FromSeconds(20),
                pollDelay: TimeSpan.FromSeconds(4));
        }
        finally
        {
            await Fixture.Rabbit.UnpauseAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync("/api/v1/admin/outbox")).StatusCode);
        var resubmit = await Http.SendAsync(Json(HttpMethod.Post, "/api/v1/admin/outbox/resubmit", null,
            Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Accepted, resubmit.StatusCode);

        await Eventually.UntilAsync(async () => await IncompleteForAsync(widgetId) == 0 && await ReceivedAsync(widgetId) >= 1,
            TimeSpan.FromSeconds(30));
        await Eventually.DuringAsync(async () => await ReceivedAsync(widgetId) == 1, TimeSpan.FromSeconds(2));
    }

    private async Task<Guid> CreateWidgetAsync(string name)
    {
        var response = await Http.SendAsync(CreateWidget(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private Task<long> ReceivedAsync(Guid widgetId) =>
        CountAsync("SELECT count(*) FROM test_fixture.received_event WHERE widget_id = @widgetId", new { widgetId });

    private Task<long> IncompleteForAsync(Guid widgetId) =>
        CountAsync("""
            SELECT count(*) FROM shared.event_publication
            WHERE completion_date IS NULL AND serialized_event LIKE @id
            """, new { id = $"%{widgetId}%" });
}
