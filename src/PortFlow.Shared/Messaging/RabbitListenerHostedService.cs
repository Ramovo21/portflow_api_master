using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using PortFlow.Shared.Events;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Web;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PortFlow.Shared.Messaging;

/// <summary>
/// Chạy các listener đã đăng ký khi <c>Portflow:Rabbit:Listener:AutoStartup=true</c> (profile worker/dev).
/// Lỗi → thử lại trong bộ nhớ (mặc định 4 lần, 1s×3, tối đa 60s) rồi reject không requeue → vào <c>&lt;queue&gt;.dlq</c>.
/// </summary>
internal sealed class RabbitListenerHostedService(
    IEnumerable<RabbitListenerRegistration> registrations,
    RabbitConnectionProvider connections,
    IServiceScopeFactory scopes,
    IOptions<PortFlowOptions> options,
    ILogger<RabbitListenerHostedService> log) : BackgroundService
{
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(5);

    private readonly List<IChannel> _channels = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Rabbit.Listener.AutoStartup)
        {
            return;
        }
        foreach (var registration in registrations)
        {
            await StartAsync(registration, stoppingToken);
        }
    }

    private async Task StartAsync(RabbitListenerRegistration registration, CancellationToken stoppingToken)
    {
        var settings = options.Value.Rabbit.Listener;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await connections.GetConnectionAsync(stoppingToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, settings.Prefetch, false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(registration, channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(registration.Queue, autoAck: false, consumer, stoppingToken);
                _channels.Add(channel);
                log.LogInformation("Listening on queue {Queue} with {Handler}", registration.Queue,
                    registration.HandlerType.Name);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Cannot start listener on {Queue}, retrying in {Delay}", registration.Queue,
                    StartRetryDelay);
                await Task.Delay(StartRetryDelay, stoppingToken);
            }
        }
    }

    private async Task OnMessageAsync(RabbitListenerRegistration registration, IChannel channel,
        BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        object? message;
        try
        {
            message = JsonSerializer.Deserialize(delivery.Body.Span, registration.EventType, JsonConfig.Options);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "Cannot convert message {MessageId} from {Queue} – rejecting", delivery.BasicProperties.MessageId,
                registration.Queue);
            message = null;
        }
        if (message is null)
        {
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, ct);
            return;
        }

        var settings = options.Value.Rabbit.Listener;
        var delay = settings.InitialInterval;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await InvokeAsync(registration, message, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                return;
            }
            catch (Exception ex) when (attempt < settings.MaxAttempts && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Listener {Handler} failed (attempt {Attempt}/{Max}), retrying in {Delay}",
                    registration.HandlerType.Name, attempt, settings.MaxAttempts, delay);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromTicks(Math.Min((long)(delay.Ticks * settings.Multiplier), settings.MaxInterval.Ticks));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Listener {Handler} failed after {Attempts} attempts – message sent to {Queue}.dlq",
                    registration.HandlerType.Name, attempt, registration.Queue);
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
                return;
            }
        }
    }

    private async Task InvokeAsync(RabbitListenerRegistration registration, object message, CancellationToken ct)
    {
        var cid = (message as IDomainEvent)?.Metadata.CorrelationId;
        var previousCid = CorrelationId.Set(cid);
        using var logScope = cid is null ? null : log.BeginScope(new Dictionary<string, object> { [CorrelationId.LogKey] = cid });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            var handle = typeof(IEventConsumer<>).MakeGenericType(registration.EventType)
                .GetMethod(nameof(IEventConsumer<object>.HandleAsync))!;
            Task Handle(CancellationToken token) => (Task)handle.Invoke(handler, [message, token])!;

            if (registration.IdempotentConsumer is not { } consumer)
            {
                await Handle(ct);
                return;
            }
            var tx = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxGuard>();
            var eventId = ((IDomainEvent)message).Metadata.EventId.ToString();
            await tx.ExecuteAsync(async token =>
            {
                if (!await inbox.FirstSeenAsync(eventId, consumer, token))
                {
                    log.LogDebug("Skip duplicate event {EventId} for consumer {Consumer}", eventId, consumer);
                    return;
                }
                await Handle(token);
            }, ct);
        }
        finally
        {
            CorrelationId.Set(previousCid);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var channel in _channels)
        {
            try
            {
                await channel.CloseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Error closing listener channel");
            }
        }
    }
}
