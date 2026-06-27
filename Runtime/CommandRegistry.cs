using System;
using System.Collections.Generic;

namespace Tutan.Messages
{
    /// <summary>
    /// Builder used at the composition root to declare the single handler for each
    /// command type. Obtained inside the <see cref="CommandBus.Install"/> callback —
    /// it cannot be constructed directly.
    /// <para>
    /// N:1 is enforced here as a value, not an exception: registering a second handler
    /// for the same command type (or a null handler) records an error that surfaces as
    /// a failed <see cref="InstallResult"/> instead of throwing.
    /// </para>
    /// </summary>
    public sealed class CommandRegistry
    {
        // Bindings are deferred as closures that capture T, so they can be applied to a
        // freshly built bus only once the whole batch is known to be error-free.
        readonly List<Action<MessageBus<ICommand>>> _apply = new();
        readonly HashSet<Type> _seen = new();
        readonly List<string> _errors = new();

        internal CommandRegistry() { }

        /// <summary>
        /// Bind <paramref name="handler"/> as the one handler for command type
        /// <typeparamref name="T"/>. Fluent — chain calls for each command. A duplicate
        /// command type or a null handler is recorded as an install error rather than thrown.
        /// </summary>
        public CommandRegistry Handle<T>(MessageHandler<T> handler) where T : struct, ICommand
        {
            if (handler == null)
            {
                _errors.Add($"'{typeof(T).Name}': handler is null.");
                return this;
            }

            if (!_seen.Add(typeof(T)))
            {
                _errors.Add($"'{typeof(T).Name}': more than one handler registered. Commands are N:1.");
                return this;
            }

            _apply.Add(bus => bus.Subscribe(handler));
            return this;
        }

        internal bool HasErrors => _errors.Count > 0;

        internal int HandlerCount => _apply.Count;

        internal string ErrorMessage => "CommandBus.Install failed:" + Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", _errors);

        internal void ApplyTo(MessageBus<ICommand> bus)
        {
            foreach (var apply in _apply) apply(bus);
        }
    }
}
