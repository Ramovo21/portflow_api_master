using Microsoft.Extensions.Logging;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Scheduling;

namespace PortFlow.Identity.Infrastructure.Keycloak;

/// <summary>Job đồng bộ user mỗi 5 phút (profile worker); Keycloak không phản hồi thì bỏ qua lượt này.</summary>
internal sealed class KeycloakUserSyncJob(KeycloakSyncService sync, ILogger<KeycloakUserSyncJob> log) : IScheduledJob
{
    public string Name => "identity-user-sync";

    public JobSchedule Schedule => JobSchedule.WithFixedDelay(TimeSpan.FromMinutes(5));

    public TimeSpan? InitialDelay => TimeSpan.FromSeconds(30);

    public TimeSpan LockAtMostFor => TimeSpan.FromMinutes(5);

    public TimeSpan LockAtLeastFor => TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await sync.SyncAllAsync(KeycloakUserSnapshot.JOB, cancellationToken);
        }
        catch (DomainException idpUnavailable)
        {
            log.LogWarning("Skipping user sync run: {Reason}", idpUnavailable.Message);
        }
    }
}
