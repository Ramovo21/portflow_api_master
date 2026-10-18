using PortFlow.Shared.Events;

namespace PortFlow.Shared.Domain;

/// <summary>Phần không generic của aggregate để hạ tầng (EF interceptor, publisher) thao tác chung.</summary>
public interface IAggregateRoot
{
    long? Version { get; }

    IReadOnlyList<IDomainEvent> PullEvents();
}

public abstract class AggregateRoot<TId> : IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id)
    {
        Id = id;
    }

    public TId Id { get; protected set; } = default!;

    /// <summary>Optimistic lock (= <c>@Version Long</c>): null = chưa lưu; EF interceptor gán 0 khi thêm và +1 khi sửa.</summary>
    public long? Version { get; internal set; }

    protected void Raise(IDomainEvent @event) => _domainEvents.Add(@event);

    public IReadOnlyList<IDomainEvent> PullEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
