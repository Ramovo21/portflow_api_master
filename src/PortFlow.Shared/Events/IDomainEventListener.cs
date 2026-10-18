namespace PortFlow.Shared.Events;

/// <summary>
/// Listener trong tiến trình (= <c>@ApplicationModuleListener</c>): chạy bất đồng bộ sau khi transaction phát event commit,
/// trong transaction riêng; được ghi vào <c>shared.event_publication</c> nên lỗi sẽ được job resubmit chạy lại.
/// </summary>
public interface IDomainEventListener<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
