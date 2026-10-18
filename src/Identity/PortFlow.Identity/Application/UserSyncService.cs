using Microsoft.EntityFrameworkCore;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Persistence;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared.Events;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Time;

namespace PortFlow.Identity.Application;

/// <summary>Đồng bộ bản sao <c>identity.app_user</c> từ token (JIT) hoặc từ Keycloak Admin API (job/thủ công).</summary>
public sealed class UserSyncService(
    IdentityDbContext db,
    ITransactionRunner tx,
    IRolePermissions roles,
    CurrentUserCache currentUsers,
    DomainEventPublisher events,
    EventMetadataFactory eventMetadata,
    IBusinessClock clock) : IUserMirrorSync
{
    public Task<MirrorState> SyncFromTokenAsync(KeycloakUserSnapshot claims, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            // Lần gọi lại sau xung đột: bỏ trạng thái EF của lần trước (transaction đã rollback).
            db.ChangeTracker.Clear();
            var existing = await db.Users.SingleOrDefaultAsync(u => u.Id == claims.Id, token);
            var departmentId = await DepartmentIdOfAsync(claims.DepartmentCode, token);
            var knownRoles = await roles.KnownRolesAsync(claims.Roles, token);
            if (existing is not null && existing.MatchesToken(claims, departmentId, knownRoles))
            {
                await MarkSeenAsync(claims.Id, token);
                return State(existing);
            }
            var user = existing ?? AppUser.FirstSeen(claims.Id);
            var snapshot = new KeycloakUserSnapshot(claims.Id, claims.Username, claims.FullName, claims.Email,
                claims.DepartmentCode, existing?.Active ?? true, claims.Roles, clock.Now(), KeycloakUserSnapshot.JIT);
            var saved = await ApplyAsync(user, snapshot, departmentId, knownRoles, token);
            await MarkSeenAsync(saved.Id, token);
            return State(saved);
        }, ct);

    public Task SyncFromKeycloakAsync(KeycloakUserSnapshot snapshot, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == snapshot.Id, token)
                       ?? AppUser.FirstSeen(snapshot.Id);
            await ApplyAsync(user, snapshot, await DepartmentIdOfAsync(snapshot.DepartmentCode, token),
                await roles.KnownRolesAsync(snapshot.Roles, token), token);
        }, ct);

    public Task<int> DeactivateMissingAsync(IReadOnlySet<Guid> presentInKeycloak, string source, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var count = 0;
            foreach (var user in await db.Users.Where(u => u.Active).ToListAsync(token))
            {
                if (presentInKeycloak.Contains(user.Id))
                {
                    continue;
                }
                await PublishAsync(user.Deactivate(clock.Now(), source, eventMetadata.Create), token);
                currentUsers.Evict(user.Id);
                count++;
            }
            await db.SaveChangesAsync(token);
            return count;
        }, ct);

    private async Task<AppUser> ApplyAsync(AppUser user, KeycloakUserSnapshot snapshot, Guid? departmentId,
        IReadOnlySet<string> knownRoles, CancellationToken ct)
    {
        await ReleaseUsernameHeldByOtherAsync(user.Id, snapshot, ct);
        var sod = RoleAssignmentPolicy.ViolatesSoD(knownRoles, await roles.PermissionsOfAsync(knownRoles, ct));
        var changes = user.ApplySync(snapshot, departmentId, knownRoles, sod, eventMetadata.Create);
        if (db.Entry(user).State == EntityState.Detached)
        {
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);
        await PublishAsync(changes, ct);
        currentUsers.Evict(user.Id);
        return user;
    }

    private async Task ReleaseUsernameHeldByOtherAsync(Guid userId, KeycloakUserSnapshot snapshot, CancellationToken ct)
    {
        var lowered = snapshot.Username.ToLower();
        var stale = await db.Users.SingleOrDefaultAsync(u => u.Username.ToLower() == lowered && u.Id != userId, ct);
        if (stale is null)
        {
            return;
        }
        await PublishAsync(stale.ReleaseUsername(clock.Now(), snapshot.SyncSource, eventMetadata.Create), ct);
        await db.SaveChangesAsync(ct);
        currentUsers.Evict(stale.Id);
    }

    private Task MarkSeenAsync(Guid id, CancellationToken ct)
    {
        var now = clock.Now();
        return db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, now), ct);
    }

    private async Task PublishAsync(IEnumerable<IDomainEvent> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            await events.PublishAsync(change, ct);
        }
    }

    private async Task<Guid?> DepartmentIdOfAsync(string? departmentCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(departmentCode))
        {
            return null;
        }
        var lowered = departmentCode.ToLower();
        return await db.Departments.Where(d => d.Code.ToLower() == lowered).Select(d => (Guid?)d.Id)
            .SingleOrDefaultAsync(ct);
    }

    private static MirrorState State(AppUser user) =>
        new(user.Id, user.Username, user.FullName, user.DepartmentId, user.Active, user.SodViolation);
}
