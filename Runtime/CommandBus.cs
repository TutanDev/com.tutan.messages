using System;
using UnityEngine;

namespace Tutan.MessageBus
{
    /// <summary>
    /// Static bus for <see cref="ICommand"/> messages.
    /// N:1 topology: any number of publishers, exactly one subscriber per command type.
    /// A second Subscribe for the same command type throws at runtime.
    /// </summary>
    public static class CommandBus
    {
        static MessageBus<ICommand> s_bus = new MessageBus<ICommand>();

        /// <summary>
        /// Subscribe to a command. Throws <see cref="InvalidOperationException"/>
        /// if a handler is already registered for <typeparamref name="T"/>.
        /// </summary>
        public static SubscriptionToken Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, ICommand
        {
            if (s_bus.GetSubscriberCount<T>() > 0)
                throw new InvalidOperationException(
                    $"CommandBus: '{typeof(T).Name}' already has a handler. " +
                    "Commands must have exactly one handler.");
            return s_bus.Subscribe(handler);
        }

        /// <summary>Remove a subscription by token. Returns false if already unsubscribed or invalid.</summary>
        public static bool Unsubscribe(SubscriptionToken token) => s_bus.Unsubscribe(token);

        /// <summary>Dispatch a command immediately to its single handler. Main thread only. Zero allocation.</summary>
        public static void Publish<T>(ref T message) where T : unmanaged, ICommand
            => s_bus.Publish(ref message);

        /// <summary>Convenience overload. One struct copy — acceptable for small messages.</summary>
        public static void Publish<T>(T message) where T : unmanaged, ICommand
            => s_bus.Publish(message);

        /// <summary>Enqueue a command for deferred dispatch on the next DrainQueues() call. Thread-safe.</summary>
        public static void Enqueue<T>(in T message) where T : unmanaged, ICommand
            => s_bus.Enqueue(in message);

        /// <summary>Process all queued commands. Call once per frame from MessageBusHost or a PlayerLoop callback.</summary>
        public static void DrainQueues() => s_bus.DrainQueues();

        /// <summary>Number of active subscriptions for message type T.</summary>
        public static int GetSubscriberCount<T>() where T : unmanaged, ICommand => s_bus.GetSubscriberCount<T>();

        /// <summary>Number of registered channel types.</summary>
        public static int ChannelCount => s_bus.ChannelCount;

        /// <summary>
        /// Clear all subscriptions and queued messages.
        /// Call during test teardown or scene transitions.
        /// </summary>
        public static void Reset() { s_bus.Dispose(); s_bus = new MessageBus<ICommand>(); }

        // Wipe static state on every Enter Play Mode so the bus stays clean
        // when the user has disabled Domain Reload (Project Settings →
        // Editor → Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode() => Reset();
    }
}
