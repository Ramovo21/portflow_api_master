using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortFlow.Shared.Events;
using PortFlow.Shared.Messaging;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Web;

namespace PortFlow.Shared.Outbox;

internal sealed record Publication(Guid Id, string ListenerId, IDomainEvent Event);

/// <summary>
/// Sau commit: chuyển từng publication tới đích (externalizer RabbitMQ hoặc listener trong tiến trình) ở nền,
/// thành công thì đánh dấu <c>completion_date</c>; lỗi thì để lại cho job resubmit (at-least-once).
/// </summary>
internal sealed class EventPublicationDispatcher(
    EventTargets targets,
    EventPublicationRepository repository,
    RabbitEventExternalizer externalizer,
    IServiceScopeFactory scopes,
    ILogger<EventPublicationDispatcher> log) : BackgroundService
{
    private readonly Channel<Publication> _queue = Channel.CreateUnbounded<Publication>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Publication publication) => _queue.Writer.TryWrite(publication);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var publication in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(publication, stoppingToken);
        }
    }

    public async Task DispatchAsync(Publication publication, CancellationToken ct)
    {
        var previousCid = CorrelationId.Set(publication.Event.Metadata.CorrelationId);
        try
        {
            if (publication.ListenerId == EventTargets.ExternalizerId)
            {
                await ExternalizeAsync(publication, ct);
            }
            else
            {
                await InvokeListenerAsync(publication, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Event publication {Id} to {Listener} failed – left incomplete for resubmission",
                publication.Id, publication.ListenerId);
            try
            {
                await repository.MarkFailedAsync(publication.Id, CancellationToken.None);
            }
            catch (Exception markEx)
            {
                log.LogWarning(markEx, "Cannot mark publication {Id} as failed", publication.Id);
            }
        }
        finally
        {
            CorrelationId.Set(previousCid);
        }
    }

    private async Task ExternalizeAsync(Publication publication, CancellationToken ct)
    {
        var target = EventTargets.Externalization(publication.Event.GetType())
                     ?? throw new InvalidOperationException($"{publication.Event.GetType()} is not externalized");
        await externalizer.PublishAsync(publication.Event, target, ct);
        await repository.MarkCompletedAsync(publication.Id, ct);
    }

    private async Task InvokeListenerAsync(Publication publication, CancellationToken ct)
    {
        var registration = targets.Listener(publication.ListenerId)
                           ?? throw new InvalidOperationException($"No listener registered as {publication.ListenerId}");
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
        var handle = typeof(IDomainEventListener<>).MakeGenericType(registration.EventType)
            .GetMethod(nameof(IDomainEventListener<IDomainEvent>.HandleAsync))!;
        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        var tx = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
        await tx.ExecuteAsync(async token =>
        {
            await (Task)handle.Invoke(handler, [publication.Event, token])!;
            await EventPublicationRepository.MarkCompletedAsync(session.Connection, session.Transaction,
                publication.Id, token);
        }, ct);
    }
}
