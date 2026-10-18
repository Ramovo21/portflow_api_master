using System.Text.Json;
using PortFlow.Shared.Config;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Outbox;
using PortFlow.Shared.Persistence;

namespace PortFlow.Shared.Events;

/// <summary>
/// Application service gọi sau khi lưu aggregate để ghi event vào <c>shared.event_publication</c> CÙNG transaction;
/// event chỉ được phát đi sau khi transaction commit (QĐ-07). Bắt buộc có transaction (MANDATORY).
/// </summary>
public sealed class DomainEventPublisher
{
    private readonly DbSession _session;
    private readonly ITransactionRunner _tx;
    private readonly EventTargets _targets;
    private readonly EventPublicationDispatcher _dispatcher;

    internal DomainEventPublisher(DbSession session, ITransactionRunner tx, EventTargets targets,
        EventPublicationDispatcher dispatcher)
    {
        _session = session;
        _tx = tx;
        _targets = targets;
        _dispatcher = dispatcher;
    }

    public Task PublishFromAsync(IAggregateRoot aggregate, CancellationToken ct = default) =>
        PublishAsync(aggregate.PullEvents(), ct);

    public Task PublishAsync(IDomainEvent @event, CancellationToken ct = default) => PublishAsync([@event], ct);

    private async Task PublishAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        _tx.RequireTransaction();
        if (events.Count == 0)
        {
            return;
        }
        var publications = new List<(StoredPublication Stored, IDomainEvent Event)>();
        foreach (var @event in events)
        {
            var type = @event.GetType();
            var serialized = JsonSerializer.Serialize(@event, type, JsonConfig.Options);
            foreach (var listenerId in _targets.ListenerIdsFor(type))
            {
                publications.Add((new StoredPublication(Guid.NewGuid(), listenerId, EventTargets.TypeName(type), serialized),
                    @event));
            }
        }
        if (publications.Count == 0)
        {
            return;
        }
        var connection = await _session.OpenAsync(ct);
        await EventPublicationRepository.InsertAsync(connection, _session.Transaction,
            publications.Select(p => p.Stored), ct);
        _session.AfterCommit(() =>
        {
            foreach (var (stored, @event) in publications)
            {
                _dispatcher.Enqueue(new Publication(stored.Id, stored.ListenerId, @event));
            }
            return Task.CompletedTask;
        });
    }
}
