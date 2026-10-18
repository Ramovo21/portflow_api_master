using NodaTime;

namespace PortFlow.Shared.Events;

public sealed record EventMetadata(Guid EventId, Instant OccurredAt, Guid ActorId, string ActorName, string CorrelationId);
