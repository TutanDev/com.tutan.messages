using System;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Static bus for <see cref="ICommand"/> messages.
    /// N:1 topology: any number of publishers, exactly one handler per command type.
    /// <para>
    /// Handlers are not subscribed ad-hoc. They are declared once at the composition
    /// root through <see cref="TryInstall"/>; the N:1 rule is validated there and a
    /// violation is reported as a return value, never an exception. After install, the
    /// only operations are <see cref="Publish{T}(ref T)"/>, <see cref="Enqueue{T}"/>,
    /// and <see cref="DrainQueues"/>.
    /// </para>
    /// </summary>
    public static class CommandBus
    {
        static MessageBus<ICommand> s_bus = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command);

        internal static MessageBus<ICommand> Bus => s_bus;

        /// <summary>
        /// Declare the command handlers for the whole application in one place.
        /// Call once at the composition root. Each command type may be bound at most
        /// once via <see cref="CommandRegistry.Handle{T}"/>.
        /// <para>
        /// Returns <c>true</c> and swaps in the new bindings atomically on success.
        /// On a duplicate command type or null handler, returns <c>false</c>, sets
        /// <paramref name="error"/> to a description naming the offending command
        /// type(s), and leaves the currently installed bus untouched.
        /// </para>
        /// <para>
        /// Calling again rebuilds the bus from scratch (composition-root semantics) —
        /// previously installed handlers are replaced wholesale.
        /// </para>
        /// </summary>
        public static bool TryInstall(out string error, Action<CommandRegistry> configure)
        {
            if (configure == null)
            {
                error = "CommandBus.TryInstall: configure delegate is null.";
                return false;
            }

            var registry = new CommandRegistry();
            configure(registry);

            if (registry.HasErrors)
            {
                error = registry.ErrorMessage;
                return false;
            }

            // Build into a fresh bus and only swap on success, so a failed install never
            // mutates the live bus.
            var fresh = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command);
            registry.ApplyTo(fresh);
            s_bus.Dispose();
            s_bus = fresh;

            error = null;
            return true;
        }

        /// <summary>Dispatch a command immediately to its single handler. Main thread only. Zero allocation.</summary>
        public static void Publish<T>(ref T message) where T : unmanaged, ICommand
            => s_bus.Publish(ref message);

        /// <summary>Convenience overload. One struct copy — acceptable for small messages.</summary>
        public static void Publish<T>(T message) where T : unmanaged, ICommand
            => s_bus.Publish(message);

        /// <summary>Enqueue a command for deferred dispatch on the next DrainQueues() call. Thread-safe.</summary>
        public static void Enqueue<T>(in T message) where T : unmanaged, ICommand
            => s_bus.Enqueue(in message);

        /// <summary>Process all queued commands. Call once per frame from MessagesHost or a PlayerLoop callback.</summary>
        public static void DrainQueues() => s_bus.DrainQueues();

        /// <summary>Number of active handlers for command type T (0 or 1).</summary>
        public static int GetSubscriberCount<T>() where T : unmanaged, ICommand => s_bus.GetSubscriberCount<T>();

        /// <summary>Number of registered channel types.</summary>
        public static int ChannelCount => s_bus.ChannelCount;

        /// <summary>
        /// Clear all handlers and queued messages.
        /// Call during test teardown or scene transitions.
        /// </summary>
        public static void Reset() { s_bus.Dispose(); s_bus = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command); }

        // Wipe static state on every Enter Play Mode so the bus stays clean
        // when the user has disabled Domain Reload (Project Settings →
        // Editor → Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode() => Reset();
    }
}
