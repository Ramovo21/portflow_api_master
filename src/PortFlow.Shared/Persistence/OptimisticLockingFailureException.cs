namespace PortFlow.Shared.Persistence;

/// <summary>Version của aggregate không khớp (ngoài <c>DbUpdateConcurrencyException</c> của EF). Map thành 409 SYS_CONCURRENT_MODIFICATION.</summary>
public sealed class OptimisticLockingFailureException(Type entityType, object id)
    : Exception($"Row was updated or deleted by another transaction: {entityType.Name}#{id}");
