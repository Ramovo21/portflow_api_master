using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using PortFlow.Shared.Scheduling;

namespace PortFlow.Shared.Outbox;

/// <summary>Mỗi phút gửi lại publication chưa hoàn thành cũ hơn 30s (broker sập → vẫn tới nơi, NFR-03).</summary>
internal sealed class OutboxResubmitJob(IncompleteEventPublications incomplete, IOptions<PortFlowOptions> options)
    : IScheduledJob
{
    public string Name => "outbox-resubmit";

    public JobSchedule Schedule => JobSchedule.WithFixedDelay(options.Value.Outbox.ResubmitInterval);

    public TimeSpan LockAtMostFor => TimeSpan.FromMinutes(5);

    public Task RunAsync(CancellationToken cancellationToken) =>
        incomplete.ResubmitOlderThanAsync(options.Value.Outbox.ResubmitMinAge, cancellationToken);
}

/// <summary>Xóa publication đã hoàn thành quá hạn (mặc định 7 ngày).</summary>
internal sealed class OutboxCleanupJob(EventPublicationRepository repository, IOptions<PortFlowOptions> options)
    : IScheduledJob
{
    public string Name => "outbox-cleanup";

    public JobSchedule Schedule => JobSchedule.ByCron(options.Value.Outbox.CleanupCron);

    public Task RunAsync(CancellationToken cancellationToken) =>
        repository.DeleteCompletedOlderThanAsync(options.Value.Outbox.CompletedRetention, cancellationToken);
}
