using System.Text.Json;
using Microsoft.Extensions.Options;
using NodaTime.Text;
using PortFlow.Shared.Config;
using PortFlow.Shared.Events;
using RabbitMQ.Client;

namespace PortFlow.Shared.Messaging;

/// <summary>
/// Gửi event ra RabbitMQ và CHỜ broker xác nhận (publisher confirm, = ConfirmingRabbitMessagingTemplate):
/// broker lỗi/treo → ném lỗi → publication chưa hoàn thành → job resubmit gửi lại.
/// </summary>
public sealed class RabbitEventExternalizer(RabbitConnectionProvider connections, IOptions<PortFlowOptions> options)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(IDomainEvent @event, ExternalizedAttribute target, CancellationToken ct)
    {
        var metadata = @event.Metadata;
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), JsonConfig.Options);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = metadata.EventId.ToString(),
            CorrelationId = metadata.CorrelationId,
            Type = @event.GetType().Name,
            Headers = new Dictionary<string, object?>
            {
                ["eventType"] = @event.GetType().Name,
                ["x-occurred-at"] = InstantPattern.ExtendedIso.Format(metadata.OccurredAt),
            },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.Rabbit.ConfirmTimeout);
        await _gate.WaitAsync(timeout.Token);
        try
        {
            var channel = await ChannelAsync(timeout.Token);
            await channel.BasicPublishAsync(target.Exchange, target.RoutingKey, mandatory: false, properties, body,
                timeout.Token);
        }
        catch
        {
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> ChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }
        await ResetChannelAsync();
        var connection = await connections.GetConnectionAsync(ct);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch
            {
                // kênh đã hỏng – bỏ qua
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
