using Dapper;
using Npgsql;

namespace PortFlow.Shared.Scheduling;

/// <summary>
/// Khóa job trên bảng <c>shared.shedlock</c> theo đúng giao thức ShedLock (JdbcTemplateLockProvider, usingDbTime),
/// nên dùng chung được bảng với bản Java.
/// </summary>
public sealed class JobLockProvider(NpgsqlDataSource dataSource)
{
    private static readonly string LockedBy = Environment.MachineName;

    public async Task<bool> TryLockAsync(string name, TimeSpan lockAtMostFor, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var args = new { name, atMost = lockAtMostFor.TotalSeconds, by = LockedBy };
        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shared.shedlock (name, lock_until, locked_at, locked_by)
            VALUES (@name, now() + make_interval(secs => @atMost), now(), @by)
            ON CONFLICT (name) DO NOTHING
            """, args, cancellationToken: ct));
        if (inserted == 1)
        {
            return true;
        }
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shared.shedlock
            SET lock_until = now() + make_interval(secs => @atMost), locked_at = now(), locked_by = @by
            WHERE name = @name AND lock_until <= now()
            """, args, cancellationToken: ct)) == 1;
    }

    public async Task UnlockAsync(string name, TimeSpan lockAtLeastFor, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shared.shedlock
            SET lock_until = CASE WHEN locked_at + make_interval(secs => @atLeast) > now()
                                  THEN locked_at + make_interval(secs => @atLeast) ELSE now() END
            WHERE name = @name AND locked_by = @by
            """, new { name, atLeast = lockAtLeastFor.TotalSeconds, by = LockedBy }, cancellationToken: ct));
    }
}
