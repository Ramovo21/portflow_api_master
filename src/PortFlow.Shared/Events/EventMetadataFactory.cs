using PortFlow.Shared.Id;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Shared.Web;

namespace PortFlow.Shared.Events;

public sealed class EventMetadataFactory(ICurrentUserProvider users, IBusinessClock clock)
{
    public EventMetadata Create() => Create(users.Current());

    public EventMetadata Create(ICurrentUser actor) =>
        new(UuidV7.Next(), clock.Now(), actor.Id, actor.FullName, CorrelationId.CurrentOrNew());
}
