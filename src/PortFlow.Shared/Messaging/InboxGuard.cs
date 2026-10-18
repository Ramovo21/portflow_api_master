using Dapper;
using PortFlow.Shared.Config;
using PortFlow.Shared.Persistence;

namespace PortFlow.Shared.Messaging;

public interface IInboxGuard
{
    /// <summary>true nếu lần đầu thấy messageId cho consumer này. Bắt buộc gọi trong transaction của consumer.</summary>
    Task<bool> FirstSeenAsync(string messageId, string consumer, CancellationToken ct = default);
}

internal sealed class JdbcInboxGuard(DbSession session, ITransactionRunner tx, PortFlowMetrics metrics) : IInboxGuard
{
    public async Task<bool> FirstSeenAsync(string messageId, string consumer, CancellationToken ct = default)
    {
        tx.RequireTransaction();
        var connection = await session.OpenAsync(ct);
        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shared.inbox_message (message_id, consumer) VALUES (@id, @consumer)
            ON CONFLICT (message_id, consumer) DO NOTHING
            """, new { id = messageId, consumer }, session.Transaction, cancellationToken: ct)) == 1;
        if (!inserted)
        {
            metrics.InboxDuplicates.Add(1);
        }
        return inserted;
    }

    public async Task<int> DeleteOlderThanAsync(TimeSpan age, int batchSize, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM shared.inbox_message
            WHERE ctid IN (SELECT ctid FROM shared.inbox_message
                           WHERE processed_at < now() - make_interval(secs => @seconds) LIMIT @limit)
            """, new { seconds = age.TotalSeconds, limit = batchSize }, session.Transaction, cancellationToken: ct));
    }
}
