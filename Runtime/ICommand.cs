namespace Tutan.MessageBus
{
    /// <summary>
    /// Marker for command messages. Intent — exactly one handler.
    /// Naming convention: imperative verb (MovePlayer, PlaceOrder).
    ///
    /// The single-handler rule is enforced by <see cref="CommandBus"/>, not by
    /// the core <see cref="MessageBus{TBase}"/>. Using <c>MessageBus&lt;ICommand&gt;</c>
    /// directly allows multiple handlers per command type.
    /// </summary>
    public interface ICommand : IMessage { }
}
