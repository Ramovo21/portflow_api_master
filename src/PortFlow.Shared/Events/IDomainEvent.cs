namespace PortFlow.Shared.Events;

public interface IDomainEvent
{
    EventMetadata Metadata { get; }
}
