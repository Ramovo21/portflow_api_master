using System.Text.Json;
using Microsoft.Extensions.Logging;
using PortFlow.Shared.Config;
using PortFlow.Shared.Events;

namespace PortFlow.Shared.Outbox;

/// <summary>Gửi lại publication chưa hoàn thành (= IncompleteEventPublications của Spring Modulith).</summary>
internal sealed class IncompleteEventPublications(
    EventPublicationRepository repository,
    EventPublicationDispatcher dispatcher,
    ILogger<IncompleteEventPublications> log)
{
    public async Task ResubmitOlderThanAsync(TimeSpan age, CancellationToken ct)
    {
        foreach (var stored in await repository.ClaimIncompleteOlderThanAsync(age, ct))
        {
            var type = EventTargets.ResolveType(stored.EventType);
            if (type is null || JsonSerializer.Deserialize(stored.SerializedEvent, type, JsonConfig.Options)
                    is not IDomainEvent @event)
            {
                log.LogError("Cannot restore event {EventType} of publication {Id}", stored.EventType, stored.Id);
                continue;
            }
            await dispatcher.DispatchAsync(new Publication(stored.Id, stored.ListenerId, @event), ct);
        }
    }
}
