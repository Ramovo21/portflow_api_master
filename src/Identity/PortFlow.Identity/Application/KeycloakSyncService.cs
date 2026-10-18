using Microsoft.Extensions.Logging;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Keycloak;

namespace PortFlow.Identity.Application;

public sealed record SyncResult(int Synced, int Deactivated);

/// <summary>Đồng bộ toàn bộ user Keycloak → bản sao; user không còn trong Keycloak bị vô hiệu hóa.</summary>
public sealed class KeycloakSyncService(KeycloakAdminClient keycloak, UserSyncService users,
    ILogger<KeycloakSyncService> log)
{
    public async Task<SyncResult> SyncAllAsync(string source, CancellationToken ct)
    {
        var snapshots = await keycloak.UsersAsync(source, ct);
        foreach (var snapshot in snapshots)
        {
            await users.SyncFromKeycloakAsync(snapshot, ct);
        }
        var present = snapshots.Select(s => s.Id).ToHashSet();
        var deactivated = await users.DeactivateMissingAsync(present, source, ct);
        log.LogInformation(
            "Keycloak user sync ({Source}): {Count} user, {Deactivated} deactivated because they no longer exist in Keycloak",
            source, snapshots.Count, deactivated);
        return new SyncResult(snapshots.Count, deactivated);
    }
}
