namespace Tutan.MessageBus
{
    /// <summary>
    /// Marker for event messages. Notification — any number of handlers.
    /// Naming convention: past tense (PlayerMoved, OrderPlaced).
    /// </summary>
    public interface IEvent : IMessage { }
}
