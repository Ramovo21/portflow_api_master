using Microsoft.EntityFrameworkCore;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Identity.Api.Events;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Persistence;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;

namespace PortFlow.Identity.Application;

public sealed record DelegationCommand(Guid? FromUserId, Guid? ToUserId, IReadOnlySet<string>? DocumentTypes,
    LocalDate? ValidFrom, LocalDate? ValidTo, string? Reason);

/// <summary>FR-W04 – ủy quyền duyệt có thời hạn.</summary>
public sealed class DelegationService(
    IdentityDbContext db,
    ITransactionRunner tx,
    IUserDirectory users,
    DocumentTypeService documentTypes,
    IdentityQueries queries,
    DomainEventPublisher events,
    EventMetadataFactory eventMetadata,
    IBusinessClock clock) : IDelegationQuery
{
    public Task<Delegation> CreateAsync(ICurrentUser actor, DelegationCommand command, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var fromUserId = command.FromUserId ?? actor.Id;
            if (fromUserId != actor.Id && !IsAdmin(actor))
            {
                throw new DomainException(AuthErrorCode.AUTH_FORBIDDEN, "You can only delegate your own work");
            }
            var delegation = Delegation.Grant(UuidV7.Next(), fromUserId, command.ToUserId, command.DocumentTypes,
                command.ValidFrom, command.ValidTo, command.Reason, clock.Today());
            await CheckParticipantsAsync(delegation, token);
            db.Delegations.Add(delegation);
            await db.SaveChangesAsync(token);
            await events.PublishAsync(new DelegationCreated(eventMetadata.Create(), delegation.Id, delegation.FromUserId,
                delegation.ToUserId, delegation.DocumentTypes.ToHashSet(), delegation.ValidFrom, delegation.ValidTo), token);
            return delegation;
        }, ct);

    public async Task<IReadOnlyList<Delegation>> VisibleToAsync(ICurrentUser actor, CancellationToken ct)
    {
        var query = IsAdmin(actor)
            ? db.Delegations
            : db.Delegations.Where(d => d.FromUserId == actor.Id || d.ToUserId == actor.Id);
        return await query.OrderByDescending(d => d.ValidFrom).ToListAsync(ct);
    }

    public Task<Delegation> RevokeAsync(ICurrentUser actor, Guid id, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var delegation = await db.Delegations.SingleOrDefaultAsync(d => d.Id == id, token)
                             ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Delegation not found");
            if (delegation.FromUserId != actor.Id && !IsAdmin(actor))
            {
                throw new DomainException(AuthErrorCode.AUTH_FORBIDDEN, "Only the delegator can revoke it");
            }
            if (delegation.Revoke())
            {
                await db.SaveChangesAsync(token);
                await events.PublishAsync(new DelegationRevoked(eventMetadata.Create(), delegation.Id,
                    delegation.FromUserId, delegation.ToUserId), token);
            }
            return delegation;
        }, ct);

    public async Task<IReadOnlySet<Guid>> ActiveDelegatorsOfAsync(Guid delegateId, string documentType,
        LocalDate onDate, CancellationToken ct = default)
    {
        var active = await ActiveToAsync(delegateId, onDate, ct);
        return active.Where(d => d.Covers(documentType, onDate)).Select(d => d.FromUserId).ToHashSet();
    }

    public Task<List<Delegation>> ActiveForAsync(Guid delegateId, CancellationToken ct) =>
        ActiveToAsync(delegateId, clock.Today(), ct);

    private Task<List<Delegation>> ActiveToAsync(Guid delegateId, LocalDate onDate, CancellationToken ct) =>
        db.Delegations.AsNoTracking()
            .Where(d => d.ToUserId == delegateId && !d.Revoked && d.ValidFrom <= onDate && d.ValidTo >= onDate)
            .ToListAsync(ct);

    private async Task CheckParticipantsAsync(Delegation delegation, CancellationToken ct)
    {
        if (!await users.IsActiveAsync(delegation.FromUserId, ct) || !await users.IsActiveAsync(delegation.ToUserId, ct))
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "The delegator or delegate is not active");
        }
        // SoD: không ủy quyền cho người không có quyền duyệt (vd. ADMIN) – stepAuth chỉ xét assignee/ủy quyền.
        if (!await queries.HoldsPermissionAsync(delegation.ToUserId, Permission.WORKFLOW_ACT, ct))
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "The delegate has no approval permission");
        }
        foreach (var code in delegation.DocumentTypes.Order(StringComparer.Ordinal))
        {
            if (!await documentTypes.ExistsAsync(code, ct))
            {
                throw new DomainException(SysErrorCode.SYS_VALIDATION, "Document type does not exist: " + code);
            }
        }
    }

    private static bool IsAdmin(ICurrentUser actor) => actor.Permissions.Contains(Permission.USER_ADMIN);
}
