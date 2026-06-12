namespace Tutan.Messages
{
    /// <summary>
    /// Marker interface for all message structs. Constrained to unmanaged at
    /// the bus API level to guarantee no hidden heap references inside messages.
    /// </summary>
    public interface IMessage { }

    /// <summary>
    /// Marker for event messages. Notification — any number of handlers.
    /// Naming convention: past tense (PlayerMoved, OrderPlaced).
    /// </summary>
    public interface IEvent : IMessage { }

    /// <summary>
    /// Marker for command messages. Intent — exactly one handler.
    /// Naming convention: imperative verb (MovePlayer, PlaceOrder).
    ///
    /// The single-handler rule is enforced by <see cref="CommandBus"/>, not by
    /// the core <see cref="MessageBus{TBase}"/>. Handlers are declared once at the
    /// composition root via <see cref="CommandBus.Install"/>, which validates the
    /// N:1 rule and reports a violation as a return value rather than throwing. Using
    /// <c>MessageBus&lt;ICommand&gt;</c> directly allows multiple handlers per command type.
    /// </summary>
    public interface ICommand : IMessage { }
}
