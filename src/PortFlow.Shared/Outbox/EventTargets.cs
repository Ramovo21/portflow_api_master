using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Shared.Events;
using PortFlow.Shared.Messaging;

namespace PortFlow.Shared.Outbox;

public sealed record DomainEventListenerRegistration(Type EventType, Type HandlerType)
{
    public string ListenerId => HandlerType.FullName!;
}

public static class DomainEventListeners
{
    /// <summary>Đăng ký listener trong tiến trình (= <c>@ApplicationModuleListener</c>) cho <typeparamref name="TEvent"/>.</summary>
    public static IServiceCollection AddDomainEventListener<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventListener<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new DomainEventListenerRegistration(typeof(TEvent), typeof(THandler)));
        return services;
    }
}

/// <summary>Xác định các đích nhận một event: listener trong tiến trình khớp kiểu + externalizer nếu có <see cref="ExternalizedAttribute"/>.</summary>
internal sealed class EventTargets(IEnumerable<DomainEventListenerRegistration> listeners)
{
    public static readonly string ExternalizerId = typeof(RabbitEventExternalizer).FullName!;

    private readonly Dictionary<string, DomainEventListenerRegistration> _byId =
        listeners.ToDictionary(l => l.ListenerId);

    public IReadOnlyList<string> ListenerIdsFor(Type eventType)
    {
        var ids = _byId.Values.Where(l => l.EventType.IsAssignableFrom(eventType)).Select(l => l.ListenerId).ToList();
        if (Externalization(eventType) is not null)
        {
            ids.Add(ExternalizerId);
        }
        return ids;
    }

    public DomainEventListenerRegistration? Listener(string listenerId) => _byId.GetValueOrDefault(listenerId);

    public static ExternalizedAttribute? Externalization(Type eventType) =>
        eventType.GetCustomAttribute<ExternalizedAttribute>();

    public static string TypeName(Type eventType) => $"{eventType.FullName}, {eventType.Assembly.GetName().Name}";

    public static Type? ResolveType(string typeName) => Type.GetType(typeName, throwOnError: false);
}
