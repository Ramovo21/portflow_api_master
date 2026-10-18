using NodaTime;
using PortFlow.Shared.Events;

namespace PortFlow.Identity.Api.Events;

[Externalized("portflow.events::identity.user-provisioned")]
public sealed record UserProvisioned(EventMetadata Metadata, Guid UserId, string Username, Guid? DepartmentId,
    IReadOnlySet<string> Roles) : IDomainEvent;

[Externalized("portflow.events::identity.user-deactivated")]
public sealed record UserDeactivated(EventMetadata Metadata, Guid UserId, string Username) : IDomainEvent;

[Externalized("portflow.events::identity.user-roles-changed")]
public sealed record UserRolesChanged(EventMetadata Metadata, Guid UserId, IReadOnlySet<string> OldRoles,
    IReadOnlySet<string> NewRoles) : IDomainEvent;

[Externalized("portflow.events::identity.sod-violation-detected")]
public sealed record SodViolationDetected(EventMetadata Metadata, Guid UserId, IReadOnlySet<string> Roles) : IDomainEvent;

[Externalized("portflow.events::identity.delegation-created")]
public sealed record DelegationCreated(EventMetadata Metadata, Guid Id, Guid FromUserId, Guid ToUserId,
    IReadOnlySet<string> DocumentTypes, LocalDate ValidFrom, LocalDate ValidTo) : IDomainEvent;

[Externalized("portflow.events::identity.delegation-revoked")]
public sealed record DelegationRevoked(EventMetadata Metadata, Guid Id, Guid FromUserId, Guid ToUserId) : IDomainEvent;
