using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using PortFlow.Shared.Scheduling;

namespace PortFlow.Shared.Messaging;

/// <summary>Xóa bản ghi inbox quá hạn (mặc định 14 ngày), theo lô 5.000 dòng.</summary>
internal sealed class InboxCleanupJob(JdbcInboxGuard inbox, IOptions<PortFlowOptions> options) : IScheduledJob
{
    private const int Batch = 5_000;

    public string Name => "inbox-cleanup";

    public JobSchedule Schedule => JobSchedule.ByCron(options.Value.Inbox.CleanupCron);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (await inbox.DeleteOlderThanAsync(options.Value.Inbox.Retention, Batch, cancellationToken) == Batch)
        {
        }
    }
}
