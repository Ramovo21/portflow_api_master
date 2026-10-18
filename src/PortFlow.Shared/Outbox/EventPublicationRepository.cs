using Dapper;
using Npgsql;
using NodaTime;

namespace PortFlow.Shared.Outbox;

/// <summary>
/// Bảng <c>shared.event_publication</c> (cùng cấu trúc Event Publication Registry v2 của Spring Modulith):
/// mỗi event × mỗi đích (listener trong tiến trình hoặc externalizer RabbitMQ) là một dòng.
/// </summary>
internal sealed class EventPublicationRepository(NpgsqlDataSource dataSource)
{
    public static Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction? tx,
        IEnumerable<StoredPublication> publications, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shared.event_publication
              (id, listener_id, event_type, serialized_event, publication_date, status, completion_attempts)
            VALUES (@Id, @ListenerId, @EventType, @SerializedEvent, now(), 'PUBLISHED', 0)
            """, publications, tx, cancellationToken: ct));

    public static Task MarkCompletedAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, Guid id,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shared.event_publication SET completion_date = now(), status = 'COMPLETED'
            WHERE id = @id AND completion_date IS NULL
            """, new { id }, tx, cancellationToken: ct));

    public async Task MarkCompletedAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await MarkCompletedAsync(connection, null, id, ct);
    }

    public async Task MarkFailedAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shared.event_publication SET status = 'FAILED'
            WHERE id = @id AND completion_date IS NULL
            """, new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<StoredPublication>> ClaimIncompleteOlderThanAsync(TimeSpan age, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<StoredPublication>(new CommandDefinition("""
            UPDATE shared.event_publication
            SET status = 'RESUBMITTED', last_resubmission_date = now(),
                completion_attempts = coalesce(completion_attempts, 0) + 1
            WHERE completion_date IS NULL AND publication_date < now() - make_interval(secs => @seconds)
            RETURNING id AS Id, listener_id AS ListenerId, event_type AS EventType, serialized_event AS SerializedEvent
            """, new { seconds = age.TotalSeconds }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> DeleteCompletedOlderThanAsync(TimeSpan age, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM shared.event_publication
            WHERE completion_date IS NOT NULL AND completion_date < now() - make_interval(secs => @seconds)
            """, new { seconds = age.TotalSeconds }, cancellationToken: ct));
    }

    public long CountIncomplete()
    {
        using var connection = dataSource.OpenConnection();
        return connection.ExecuteScalar<long>("SELECT count(*) FROM shared.event_publication WHERE completion_date IS NULL");
    }

    public async Task<long> CountIncompleteAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM shared.event_publication WHERE completion_date IS NULL", cancellationToken: ct));
    }

    public async Task<IReadOnlyList<IncompletePublication>> IncompleteAsync(int limit, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<IncompletePublication>(new CommandDefinition("""
            SELECT id AS Id, listener_id AS ListenerId, event_type AS EventType, publication_date AS PublishedAt,
                   status AS Status, completion_attempts AS CompletionAttempts,
                   last_resubmission_date AS LastResubmittedAt
            FROM shared.event_publication
            WHERE completion_date IS NULL
            ORDER BY publication_date
            LIMIT @limit
            """, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }
}

internal sealed record StoredPublication(Guid Id, string ListenerId, string EventType, string SerializedEvent);

public sealed record IncompletePublication(Guid Id, string ListenerId, string EventType, Instant PublishedAt,
    string? Status, int? CompletionAttempts, Instant? LastResubmittedAt);
