using Microsoft.Extensions.DependencyInjection;
using PortFlow.Shared.Events;

namespace PortFlow.Shared.Messaging;

/// <summary>Handler tiêu thụ một loại event từ RabbitMQ (= method <c>@RabbitListener</c>).</summary>
public interface IEventConsumer<in TEvent>
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

/// <summary>Khai báo một listener: queue nguồn, kiểu event, handler và tên consumer cho inbox (nếu idempotent).</summary>
public sealed record RabbitListenerRegistration(string Queue, Type EventType, Type HandlerType, string? IdempotentConsumer);

public static class RabbitListeners
{
    /// <summary>
    /// Đăng ký <c>THandler</c> tiêu thụ <paramref name="queue"/>. Có <paramref name="idempotentConsumer"/>
    /// (= <c>@IdempotentConsumer("tên")</c>) thì mỗi message chạy trong transaction + inbox, khóa chống trùng là
    /// <c>metadata.eventId</c>; correlation-id lấy từ payload.
    /// </summary>
    public static IServiceCollection AddRabbitListener<TEvent, THandler>(this IServiceCollection services, string queue,
        string? idempotentConsumer = null)
        where THandler : class, IEventConsumer<TEvent>
    {
        if (idempotentConsumer is not null && !typeof(IDomainEvent).IsAssignableFrom(typeof(TEvent)))
        {
            throw new InvalidOperationException(
                $"Idempotent consumer '{idempotentConsumer}' must consume a DomainEvent, got {typeof(TEvent).Name}");
        }
        services.AddScoped<THandler>();
        services.AddSingleton(new RabbitListenerRegistration(queue, typeof(TEvent), typeof(THandler), idempotentConsumer));
        return services;
    }
}
