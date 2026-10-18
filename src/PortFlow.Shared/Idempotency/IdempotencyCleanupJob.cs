using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using PortFlow.Shared.Scheduling;

namespace PortFlow.Shared.Idempotency;

/// <summary>Xóa idempotency key quá hạn (mặc định 48h), theo lô 5.000 dòng.</summary>
internal sealed class IdempotencyCleanupJob(
    IdempotencyRepository repository,
    IOptions<PortFlowOptions> options,
    ILogger<IdempotencyCleanupJob> log) : IScheduledJob
{
    private const int Batch = 5_000;

    public string Name => "idempotency-cleanup";

    public JobSchedule Schedule => JobSchedule.ByCron(options.Value.Idempotency.CleanupCron);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var retention = options.Value.Idempotency.Retention;
        var total = 0;
        int deleted;
        do
        {
            deleted = await repository.DeleteOlderThanAsync(retention, Batch, cancellationToken);
            total += deleted;
        }
        while (deleted == Batch);
        if (total > 0)
        {
            log.LogInformation("Removed {Total} idempotency records older than {Retention}", total, retention);
        }
    }
}
