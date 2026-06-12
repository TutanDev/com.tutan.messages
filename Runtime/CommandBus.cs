using System;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Outcome of <see cref="CommandBus.Install"/>. Failure is reported here as a
    /// value, never an exception — check <see cref="Ok"/> and surface
    /// <see cref="Error"/> however the application reports configuration mistakes.
    /// </summary>
    public readonly struct InstallResult
    {
        /// <summary>True when the bindings were validated and swapped in.</summary>
        public bool Ok { get; }

        /// <summary>
        /// Description of what failed, naming the offending command type(s).
        /// Null when <see cref="Ok"/> is true.
        /// </summary>
        public string Error { get; }

        /// <summary>Number of command handlers bound. Zero on failure.</summary>
        public int HandlerCount { get; }

        InstallResult(bool ok, string error, int handlerCount)
        {
            Ok = ok;
            Error = error;
            HandlerCount = handlerCount;
        }

        internal static InstallResult Success(int handlerCount) => new(true, null, handlerCount);
        internal static InstallResult Failure(string error) => new(false, error, 0);
    }

    /// <summary>
    /// Static bus for <see cref="ICommand"/> messages.
    /// N:1 topology: any number of publishers, exactly one handler per command type.
    /// <para>
    /// Handlers are not subscribed ad-hoc. They are declared once at the composition
    /// root through <see cref="Install"/>; the N:1 rule is validated there and a
    /// violation is reported in the returned <see cref="InstallResult"/>, never as an
    /// exception. After install, the only operations are
    /// <see cref="Publish{T}(ref T)"/>, <see cref="Enqueue{T}"/>, and
    /// <see cref="DrainQueues"/>.
    /// </para>
    /// </summary>
    public static class CommandBus
    {
        // volatile: Enqueue is documented thread-safe, so worker threads read this
        // field; volatile keeps a bus swapped in by Reset() or Install promptly
        // visible to them.
        static volatile MessageBus<ICommand> s_bus = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command);

        internal static MessageBus<ICommand> Bus => s_bus;

        /// <summary>
        /// Declare the command handlers for the whole application in one place.
        /// Call once at the composition root. Each command type may be bound at most
        /// once via <see cref="CommandRegistry.Handle{T}"/>.
        /// <para>
        /// On success the new bindings are swapped in atomically and the result's
        /// <see cref="InstallResult.Ok"/> is true. On a duplicate command type or a
        /// null handler, <see cref="InstallResult.Error"/> names the offending
        /// command type(s) and the currently installed bus is left untouched.
        /// </para>
        /// <para>
        /// Calling again rebuilds the bus from scratch (composition-root semantics) —
        /// previously installed handlers are replaced wholesale.
        /// </para>
        /// </summary>
        public static InstallResult Install(Action<CommandRegistry> configure)
        {
            if (configure == null)
                return InstallResult.Failure("CommandBus.Install: configure delegate is null.");

            var registry = new CommandRegistry();
            configure(registry);

            if (registry.HasErrors)
                return InstallResult.Failure(registry.ErrorMessage);

            // Build into a fresh bus and only swap on success, so a failed install never
            // mutates the live bus.
            var fresh = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command);
            registry.ApplyTo(fresh);
            s_bus.Dispose();
            s_bus = fresh;

            return InstallResult.Success(registry.HandlerCount);
        }

        /// <summary>Dispatch a command immediately to its single handler. Main thread only. Zero allocation.</summary>
        public static void Publish<T>(ref T message) where T : unmanaged, ICommand
            => s_bus.Publish(ref message);

        /// <summary>Convenience overload. One struct copy — acceptable for small messages.</summary>
        public static void Publish<T>(T message) where T : unmanaged, ICommand
            => s_bus.Publish(ref message); // ref: the copy already happened into this parameter

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
        /// Call during test teardown.
        /// </summary>
        public static void Reset() { s_bus.Dispose(); s_bus = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command); }

        // Wipe static state on every Enter Play Mode so the bus stays clean
        // when the user has disabled Domain Reload (Project Settings →
        // Editor → Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode() => Reset();
    }
}
