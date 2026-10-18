using Dapper;
using PortFlow.Shared.Persistence;

namespace PortFlow.Shared.Idempotency;

internal sealed class IdempotencyRepository(DbSession session)
{
    public async Task<bool> TryInsertAsync(string key, Guid userId, string method, string path, string requestHash,
        CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shared.idempotency_request (key, user_id, method, path, request_hash)
            VALUES (@key, @userId, @method, @path, @hash)
            ON CONFLICT (key, user_id) DO NOTHING
            """, new { key, userId, method, path, hash = requestHash }, session.Transaction, cancellationToken: ct)) == 1;
    }

    public async Task<StoredRequest?> FindAsync(string key, Guid userId, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<StoredRequest>(new CommandDefinition("""
            SELECT request_hash AS RequestHash, status AS Status, response_status AS ResponseStatus,
                   response_body AS ResponseBody
            FROM shared.idempotency_request WHERE key = @key AND user_id = @userId
            """, new { key, userId }, session.Transaction, cancellationToken: ct));
    }

    public async Task CompleteAsync(string key, Guid userId, int responseStatus, string? responseBodyJson,
        CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shared.idempotency_request
            SET status = 'COMPLETED', response_status = @status, response_body = @body, completed_at = now()
            WHERE key = @key AND user_id = @userId
            """, new { status = responseStatus, body = responseBodyJson, key, userId }, session.Transaction,
            cancellationToken: ct));
    }

    public async Task SetLockTimeoutAsync(TimeSpan timeout, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT set_config('lock_timeout', @value, true)",
            new { value = (long)timeout.TotalMilliseconds + "ms" }, session.Transaction, cancellationToken: ct));
    }

    public async Task<int> DeleteOlderThanAsync(TimeSpan age, int batchSize, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM shared.idempotency_request
            WHERE ctid IN (SELECT ctid FROM shared.idempotency_request
                           WHERE created_at < now() - make_interval(secs => @seconds) LIMIT @limit)
            """, new { seconds = age.TotalSeconds, limit = batchSize }, session.Transaction, cancellationToken: ct));
    }

    internal sealed record StoredRequest(string RequestHash, string Status, int? ResponseStatus, string? ResponseBody)
    {
        public bool Completed => Status == "COMPLETED";
    }
}
