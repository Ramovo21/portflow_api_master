using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Shared.Domain;

namespace PortFlow.Shared.Persistence;

public static class EntityFrameworkSetup
{
    /// <summary>
    /// Đăng ký DbContext của một module trên kết nối của <see cref="DbSession"/> (chung transaction với phần còn lại),
    /// đặt tên cột snake_case và dùng NodaTime. Schema do Flyway quản lý – không dùng EF migration.
    /// </summary>
    public static IServiceCollection AddPortFlowDbContext<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<DbSession>().Connection, npgsql => npgsql.UseNodaTime())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<DbSessionInterceptor>()));
        return services;
    }

    /// <summary>Gọi trong <c>OnModelCreating</c>: mọi <see cref="AggregateRoot{TId}"/> lấy <c>Version</c> làm concurrency token.</summary>
    public static ModelBuilder ApplyAggregateRootConventions(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(entity.ClrType) && entity.BaseType is null)
            {
                modelBuilder.Entity(entity.ClrType).Property(nameof(IAggregateRoot.Version)).IsConcurrencyToken();
            }
        }
        return modelBuilder;
    }
}

/// <summary>
/// Trước SaveChanges: gắn DbContext vào transaction của <see cref="DbSession"/> và tăng <c>Version</c> như JPA <c>@Version</c>.
/// </summary>
internal sealed class DbSessionInterceptor(DbSession session) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await JoinSessionTransactionAsync(context, cancellationToken);
            BumpVersions(context);
        }
        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        throw new NotSupportedException("Use SaveChangesAsync: DbSession is async-only");
    }

    private async Task JoinSessionTransactionAsync(DbContext context, CancellationToken cancellationToken)
    {
        var current = context.Database.CurrentTransaction?.GetDbTransaction();
        if (!ReferenceEquals(current, session.Transaction))
        {
            await context.Database.UseTransactionAsync(session.Transaction, cancellationToken);
        }
        if (session.Transaction is not null)
        {
            session.Enlist(context);
        }
    }

    /// <summary>Mọi concurrency token kiểu long (Version, RowVersion…) = JPA <c>@Version</c>: thêm → 0, sửa → +1.</summary>
    private static void BumpVersions(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }
            foreach (var property in entry.Properties)
            {
                if (!property.Metadata.IsConcurrencyToken
                    || (property.Metadata.ClrType != typeof(long) && property.Metadata.ClrType != typeof(long?)))
                {
                    continue;
                }
                property.CurrentValue = entry.State == EntityState.Added
                    ? 0L
                    : ((long?)property.OriginalValue ?? 0L) + 1;
            }
        }
    }
}
