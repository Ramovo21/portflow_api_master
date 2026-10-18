using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using PortFlow.Shared.Config;
using PortFlow.Shared.Events;
using PortFlow.Shared.Messaging;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Fixture;
using RabbitMQ.Client;

namespace PortFlow.Tests.Shared.Messaging;

public class InboxIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task DuplicateMessagesAreProcessedOnce()
    {
        var widgetId = Guid.NewGuid();
        var @event = new TestWidgetCreated(new EventMetadata(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant(),
            SystemUser.SystemId, "Hệ thống", "cid-inbox"), widgetId, "dup");
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, JsonConfig.Options);

        var factory = new ConnectionFactory
        {
            HostName = Fixture.Rabbit.Hostname,
            Port = Fixture.Rabbit.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
        };
        await using (var connection = await factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            for (var i = 0; i < 2; i++)
            {
                await channel.BasicPublishAsync(RabbitTopology.EventsExchange, "test.widget-created", body);
            }
        }

        await Eventually.UntilAsync(async () => await ReceivedAsync(widgetId) == 1, TimeSpan.FromSeconds(15));
        await Eventually.DuringAsync(async () => await ReceivedAsync(widgetId) == 1, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GuardReportsFirstSightingPerConsumer()
    {
        var messageId = Guid.NewGuid().ToString();
        Assert.True(await FirstSeenAsync(messageId, "a"));
        Assert.False(await FirstSeenAsync(messageId, "a"));
        Assert.True(await FirstSeenAsync(messageId, "b"));
    }

    [Fact]
    public async Task GuardRequiresATransaction()
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxGuard>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => inbox.FirstSeenAsync(Guid.NewGuid().ToString(), "a"));
    }

    private async Task<bool> FirstSeenAsync(string messageId, string consumer)
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        var tx = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxGuard>();
        return await tx.ExecuteAsync(ct => inbox.FirstSeenAsync(messageId, consumer, ct));
    }

    private Task<long> ReceivedAsync(Guid widgetId) =>
        CountAsync("SELECT count(*) FROM test_fixture.received_event WHERE widget_id = @widgetId", new { widgetId });
}
