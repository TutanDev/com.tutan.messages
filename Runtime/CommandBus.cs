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
    /// N:1 topology: any number of publishers, at most one handler per command type
    /// (a second binding is rejected at install).
    /// <para>
    /// Nothing requires every command type to be bound: publishing or enqueuing a
    /// command that has no handler is a silent no-op. For commands that must be
    /// handled, assert <c>GetSubscriberCount&lt;T&gt;() == 1</c> once after
    /// <see cref="Install"/>.
    /// </para>
    /// <para>
    /// Handlers are not subscribed ad-hoc. They are declared once at the composition
    /// root through <see cref="Install"/>; the N:1 rule is validated there and a
    /// violation is reported in the returned <see cref="InstallResult"/>, never as an
    /// exception. After install, dispatch goes through
    /// <see cref="Publish{T}(ref T)"/>, <see cref="Enqueue{T}"/>, and
    /// <see cref="DrainQueues"/>; there is no ad-hoc <c>Subscribe</c>.
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
        /// <para>
        /// A successful install, like <see cref="Reset"/>, discards the commands still
        /// queued on the live bus — including on the first install, so a command
        /// enqueued before it is lost (the editor and development builds log a
        /// warning). Install before anything enqueues: from a <c>BeforeSceneLoad</c>
        /// <c>[RuntimeInitializeOnLoadMethod]</c>, or from a bootstrap object with an
        /// early Script Execution Order.
        /// </para>
        /// <para>
        /// Do not install from inside a command handler: the drain in progress still
        /// delivers the rest of that command type's backlog to the replaced handler,
        /// and only the other types' queues are discarded.
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
            WarnIfDiscardingQueued(s_bus);
            s_bus.Dispose();
            s_bus = fresh;

            return InstallResult.Success(registry.HandlerCount);
        }

        // Install order relative to an early Enqueue (e.g. another object's Awake)
        // is easy to get wrong and the loss is otherwise silent. Reset does not
        // warn: discarding the queue is its whole purpose (test teardown, Enter
        // Play Mode). Stripped from release players, like MainThreadGuard.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void WarnIfDiscardingQueued(MessageBus<ICommand> bus)
        {
            string pending = bus.DescribePendingQueues();
            if (pending == null) return;

            Debug.LogWarning(
                $"Messages: CommandBus.Install discarded queued commands that had not been drained yet ({pending}). " +
                "Install before anything enqueues, e.g. from a BeforeSceneLoad [RuntimeInitializeOnLoadMethod].");
        }

        /// <summary>Dispatch a command immediately to its handler (a silent no-op if none is bound). Main thread only. Zero allocation.</summary>
        public static void Publish<T>(ref T message) where T : struct, ICommand
            => s_bus.Publish(ref message);

        /// <summary>Convenience overload. One struct copy — acceptable for small messages.</summary>
        public static void Publish<T>(T message) where T : struct, ICommand
            => s_bus.Publish(ref message); // ref: the copy already happened into this parameter

        /// <summary>Enqueue a command for deferred dispatch on the next DrainQueues() call. Thread-safe.</summary>
        public static void Enqueue<T>(in T message) where T : struct, ICommand
            => s_bus.Enqueue(in message);

        /// <summary>Process all queued commands. Call once per frame from MessagesHost or a PlayerLoop callback.</summary>
        public static void DrainQueues() => s_bus.DrainQueues();

        /// <summary>Number of active handlers for command type T (0 or 1).</summary>
        public static int GetSubscriberCount<T>() where T : struct, ICommand => s_bus.GetSubscriberCount<T>();

        /// <summary>Number of registered channel types.</summary>
        public static int ChannelCount => s_bus.ChannelCount;

        /// <summary>
        /// Clear all handlers and queued messages: commands still waiting for the
        /// next drain are discarded, not dispatched. Call during test teardown.
        /// </summary>
        public static void Reset() { s_bus.Dispose(); s_bus = new MessageBus<ICommand>(MessagesInstrumentation.BusKind.Command); }

        // Wipe static state on every Enter Play Mode so the bus stays clean
        // when the user has disabled Domain Reload (Project Settings →
        // Editor → Enter Play Mode Options). Runs at SubsystemRegistration, so
        // anything subscribed or installed earlier — or from another
        // SubsystemRegistration callback, whose order relative to this one is
        // undefined — is dropped. Install/subscribe at BeforeSceneLoad or later.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode() => Reset();
    }
}
