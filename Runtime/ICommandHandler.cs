namespace Tutan.Messages
{
    /// <summary>
    /// Non-generic discovery seam for command handlers. Implement the generic
    /// <see cref="ICommandHandler{T}"/> instead — this base exists only so editor
    /// tooling can find every handler in one pass via
    /// <c>TypeCache.GetTypesDerivedFrom&lt;ICommandHandler&gt;()</c> (TypeCache does
    /// not reliably enumerate implementors of an open generic interface).
    /// </summary>
    public interface ICommandHandler { }

    /// <summary>
    /// Declares that the implementing type handles command <typeparamref name="T"/>.
    /// Commands are N:1 — exactly one handler per command type.
    ///
    /// This is a <b>declarative</b> contract for tooling and self-documentation; it
    /// does <b>not</b> auto-register. Bind the handler at the composition root, where
    /// <see cref="Handle"/> matches the bus's <c>MessageHandler&lt;T&gt;</c> delegate
    /// and so can be passed straight through:
    /// <code>
    /// CommandBus.TryInstall(out var error, r => r.Handle&lt;T&gt;(instance.Handle));
    /// </code>
    /// The Commands window (<c>Window ▸ Tutan ▸ Commands</c>) audits these
    /// declarations, flagging any command with zero handlers (orphan) or more
    /// than one (an N:1 violation).
    /// </summary>
    public interface ICommandHandler<T> : ICommandHandler where T : unmanaged, ICommand
    {
        void Handle(ref T command);
    }
}
