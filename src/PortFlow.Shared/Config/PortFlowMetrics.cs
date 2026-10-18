using System.Diagnostics.Metrics;

namespace PortFlow.Shared.Config;

/// <summary>Metric hạ tầng F00 (xuất ra <c>/actuator/prometheus</c>).</summary>
public sealed class PortFlowMetrics : IDisposable
{
    public const string MeterName = "PortFlow";

    public PortFlowMetrics(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(MeterName);
        IdempotencyReplays = Meter.CreateCounter<long>("idempotency.replay.count",
            description: "Number of requests replayed from an Idempotency-Key");
        InboxDuplicates = Meter.CreateCounter<long>("inbox.duplicate.count",
            description: "Number of duplicate messages skipped by the consumer");
    }

    public Meter Meter { get; }

    public Counter<long> IdempotencyReplays { get; }

    public Counter<long> InboxDuplicates { get; }

    public void Dispose() => Meter.Dispose();
}
