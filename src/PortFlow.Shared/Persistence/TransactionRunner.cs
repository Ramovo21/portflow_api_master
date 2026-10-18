namespace PortFlow.Shared.Persistence;

/// <summary>Tương đương <c>TransactionTemplate</c>/<c>@Transactional</c> của Spring trên <see cref="DbSession"/> của scope hiện tại.</summary>
public interface ITransactionRunner
{
    /// <summary>REQUIRED: tham gia transaction đang có, nếu chưa có thì mở mới; lỗi → rollback.</summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    /// <summary>MANDATORY: ném lỗi nếu không chạy trong transaction.</summary>
    void RequireTransaction();
}

internal sealed class TransactionRunner(DbSession session) : ITransactionRunner
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (session.InTransaction)
        {
            return await work(cancellationToken);
        }
        await session.BeginAsync(cancellationToken);
        T result;
        try
        {
            result = await work(cancellationToken);
        }
        catch
        {
            await session.RollbackAsync();
            throw;
        }
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(async ct =>
        {
            await work(ct);
            return null;
        }, cancellationToken);

    public void RequireTransaction()
    {
        if (!session.InTransaction)
        {
            throw new InvalidOperationException("No existing transaction found for operation requiring a transaction");
        }
    }
}
