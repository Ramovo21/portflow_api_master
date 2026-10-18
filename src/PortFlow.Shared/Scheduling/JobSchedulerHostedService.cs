using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Shared.Config;
using PortFlow.Shared.Web;

namespace PortFlow.Shared.Scheduling;

/// <summary>Chạy các <see cref="IScheduledJob"/> khi <c>Portflow:Jobs:Enabled=true</c> (profile worker/dev).</summary>
internal sealed class JobSchedulerHostedService(
    IEnumerable<ScheduledJobRegistration> registrations,
    IServiceScopeFactory scopes,
    JobLockProvider locks,
    IOptions<PortFlowOptions> options,
    ILogger<JobSchedulerHostedService> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Jobs.Enabled)
        {
            return Task.CompletedTask;
        }
        return Task.WhenAll(registrations.Select(r => LoopAsync(r.JobType, stoppingToken)));
    }

    private async Task LoopAsync(Type jobType, CancellationToken stoppingToken)
    {
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            await using (var probe = scopes.CreateAsyncScope())
            {
                var job = (IScheduledJob)probe.ServiceProvider.GetRequiredService(jobType);
                delay = first && job.InitialDelay is { } initial ? initial : job.Schedule.DelayFrom(DateTimeOffset.Now);
            }
            first = false;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await RunOnceAsync(jobType, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(Type jobType, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var job = (IScheduledJob)scope.ServiceProvider.GetRequiredService(jobType);
        var cid = "job-" + Guid.NewGuid();
        var previousCid = CorrelationId.Set(cid);
        using var logScope = log.BeginScope(new Dictionary<string, object> { [CorrelationId.LogKey] = cid });
        try
        {
            if (!await locks.TryLockAsync(job.Name, job.LockAtMostFor, ct))
            {
                log.LogDebug("Job {Job} is locked by another instance", job.Name);
                return;
            }
            try
            {
                await job.RunAsync(ct);
            }
            finally
            {
                await locks.UnlockAsync(job.Name, job.LockAtLeastFor, CancellationToken.None);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Job {Job} failed", job.Name);
        }
        finally
        {
            CorrelationId.Set(previousCid);
        }
    }
}
