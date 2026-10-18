using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PortFlow.Shared.Persistence;

/// <summary>
/// Một kết nối + transaction cho mỗi scope (request, message, job): EF Core DbContext của mọi module và truy vấn
/// Dapper ở shared đều chạy trên kết nối này, nên chung một transaction như JPA + JdbcClient trong Spring.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource, ILogger<DbSession> logger) : IAsyncDisposable
{
    private readonly List<Func<bool, Task>> _afterCompletion = [];
    private readonly HashSet<DbContext> _enlisted = [];

    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public NpgsqlTransaction? Transaction { get; private set; }

    public bool InTransaction => Transaction is not null;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (Connection.State != System.Data.ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }
        return Connection;
    }

    /// <summary>Đăng ký callback chạy sau khi transaction hiện tại kết thúc (true = đã commit). Không có transaction → bỏ qua.</summary>
    public bool AfterCompletion(Func<bool, Task> callback)
    {
        if (!InTransaction)
        {
            return false;
        }
        _afterCompletion.Add(callback);
        return true;
    }

    public bool AfterCommit(Func<Task> callback) =>
        AfterCompletion(committed => committed ? callback() : Task.CompletedTask);

    /// <summary>DbContext đã dùng transaction hiện tại; được gỡ ra khi transaction kết thúc.</summary>
    internal void Enlist(DbContext context) => _enlisted.Add(context);

    internal async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (InTransaction)
        {
            throw new InvalidOperationException("A transaction is already active in this DbSession");
        }
        await OpenAsync(cancellationToken);
        Transaction = await Connection.BeginTransactionAsync(cancellationToken);
    }

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        var tx = Transaction ?? throw new InvalidOperationException("No active transaction");
        try
        {
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await EndAsync(tx, committed: false);
            throw;
        }
        await EndAsync(tx, committed: true);
    }

    internal async Task RollbackAsync()
    {
        var tx = Transaction;
        if (tx is null)
        {
            return;
        }
        try
        {
            await tx.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rollback failed");
        }
        await EndAsync(tx, committed: false);
    }

    private async Task EndAsync(NpgsqlTransaction tx, bool committed)
    {
        Transaction = null;
        foreach (var context in _enlisted)
        {
            // Không để DbContext giữ transaction đã kết thúc ("Transaction is already completed").
            await context.Database.UseTransactionAsync(null);
        }
        _enlisted.Clear();
        await tx.DisposeAsync();
        var callbacks = _afterCompletion.ToList();
        _afterCompletion.Clear();
        foreach (var callback in callbacks)
        {
            try
            {
                await callback(committed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "After-completion callback failed");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (InTransaction)
        {
            await RollbackAsync();
        }
        await Connection.DisposeAsync();
    }
}
