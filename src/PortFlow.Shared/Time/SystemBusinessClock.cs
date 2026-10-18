using Microsoft.Extensions.Options;
using NodaTime;
using PortFlow.Shared.Config;

namespace PortFlow.Shared.Time;

public sealed class SystemBusinessClock(IOptions<PortFlowOptions> options) : IBusinessClock
{
    private readonly IClock _clock = SystemClock.Instance;

    public DateTimeZone Zone { get; } = DateTimeZoneProviders.Tzdb[options.Value.BusinessZone];

    public Instant Now() => _clock.GetCurrentInstant();
}
