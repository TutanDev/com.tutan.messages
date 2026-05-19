namespace Tutan.MessageBus
{
    /// <summary>
    /// Marker interface for all message structs. Constrained to unmanaged at
    /// the bus API level to guarantee no hidden heap references inside messages.
    /// </summary>
    public interface IMessage { }
}
