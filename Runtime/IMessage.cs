namespace Tutan.Messages
{
    /// <summary>
    /// Marker interface for all message structs. The bus API constrains messages
    /// to <c>struct</c>, so dispatch stays allocation-free (generic specialization
    /// plus <c>ref</c>-passing, never boxed). Reference-type fields (strings,
    /// collections, class payloads) are permitted, but note the trade-offs: a
    /// queued message holding references adds GC mark-phase scan cost while it
    /// sits in the queue, and a worker-thread <c>Enqueue</c> copies the struct
    /// shallowly, so any referenced object is shared across threads. Prefer
    /// value-type fields on hot or cross-thread paths.
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
