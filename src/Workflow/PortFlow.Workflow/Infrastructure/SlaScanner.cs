using Microsoft.Extensions.Logging;
using PortFlow.Shared.Scheduling;
using PortFlow.Workflow.Application;

namespace PortFlow.Workflow.Infrastructure;

/// <summary>Quét bước quá hạn SLA mỗi 5 phút (profile worker), theo lô 500.</summary>
internal sealed class SlaScanner(SlaService sla, ILogger<SlaScanner> log) : IScheduledJob
{
    private const int Batch = 500;

    public string Name => "workflow-sla-scanner";

    public JobSchedule Schedule => JobSchedule.ByCron("0 */5 * * * *");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        int notified;
        do
        {
            notified = await sla.NotifyOverdueAsync(Batch, cancellationToken);
            total += notified;
        }
        while (notified == Batch);
        if (total > 0)
        {
            log.LogInformation("Sent {Total} overdue step notifications", total);
        }
    }
}
