using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Static bus for <see cref="IEvent"/> messages. Supports any number of handlers per event type (fan-out).
    /// </summary>
    public static class EventBus
    {
        // volatile: Enqueue is documented thread-safe, so worker threads read this
        // field; volatile keeps a bus swapped in by Reset() promptly visible to them.
        static volatile MessageBus<IEvent> s_bus = new MessageBus<IEvent>(MessagesInstrumentation.BusKind.Event);

        internal static MessageBus<IEvent> Bus => s_bus;

        /// <summary>
        /// Register a handler for event type T. Returns a disposable
        /// <see cref="Subscription"/> — dispose it directly, via a
        /// <see cref="SubscriptionBag"/>, or tie it to a GameObject's lifetime with
        /// <c>.AddTo(this)</c>.
        /// </summary>
        public static Subscription Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, IEvent
            => s_bus.Subscribe(handler);

        /// <summary>Dispatch an event immediately to all subscribers. Main thread only. Zero allocation.</summary>
        public static void Publish<T>(ref T message) where T : unmanaged, IEvent
            => s_bus.Publish(ref message);

        /// <summary>Convenience overload. One struct copy — acceptable for small messages.</summary>
        public static void Publish<T>(T message) where T : unmanaged, IEvent
            => s_bus.Publish(ref message); // ref: the copy already happened into this parameter

        /// <summary>Enqueue an event for deferred dispatch on the next DrainQueues() call. Thread-safe.</summary>
        public static void Enqueue<T>(in T message) where T : unmanaged, IEvent
            => s_bus.Enqueue(in message);

        /// <summary>Process all queued events. Call once per frame from MessagesHost or a PlayerLoop callback.</summary>
        public static void DrainQueues() => s_bus.DrainQueues();

        /// <summary>Number of active subscriptions for message type T.</summary>
        public static int GetSubscriberCount<T>() where T : unmanaged, IEvent => s_bus.GetSubscriberCount<T>();

        /// <summary>Number of registered channel types.</summary>
        public static int ChannelCount => s_bus.ChannelCount;

        /// <summary>
        /// Clear all subscriptions and queued messages.
        /// Call during test teardown.
        /// </summary>
        public static void Reset() { s_bus.Dispose(); s_bus = new MessageBus<IEvent>(MessagesInstrumentation.BusKind.Event); }

        // Wipe static state on every Enter Play Mode so the bus stays clean
        // when the user has disabled Domain Reload (Project Settings →
        // Editor → Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode() => Reset();
    }
}
