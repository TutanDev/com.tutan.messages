using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Collects <see cref="Subscription"/>s so a whole group can be unsubscribed
    /// with one <see cref="Dispose"/> (or <see cref="Clear"/>) call — one bag per
    /// system instead of one token field per subscription. Reusable after
    /// disposal: <c>Add</c> works again and a later Dispose releases the new batch.
    /// Main thread only, like the Subscribe/Unsubscribe calls it wraps.
    /// </summary>
    public sealed class SubscriptionBag : IDisposable
    {
        readonly List<Subscription> _subscriptions = new();

        /// <summary>Number of subscriptions currently held.</summary>
        public int Count => _subscriptions.Count;

        /// <summary>Track <paramref name="subscription"/>; inactive handles are ignored.</summary>
        public void Add(Subscription subscription)
        {
            if (subscription.IsActive)
                _subscriptions.Add(subscription);
        }

        /// <summary>Dispose every held subscription and empty the bag.</summary>
        public void Clear()
        {
            for (int i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Dispose();
            _subscriptions.Clear();
        }

        public void Dispose() => Clear();
    }

    /// <summary>
    /// Hidden component that ties a <see cref="SubscriptionBag"/> to a GameObject's
    /// lifetime. Added automatically by <c>Subscription.AddTo(gameObject)</c> —
    /// never add it by hand.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SubscriptionAnchor : MonoBehaviour
    {
        internal SubscriptionBag Bag { get; } = new SubscriptionBag();

        void OnDestroy() => Bag.Dispose();
    }

    /// <summary>
    /// Fluent scoping helpers: <c>EventBus.Subscribe&lt;T&gt;(h).AddTo(this)</c>.
    /// </summary>
    public static class SubscriptionExtensions
    {
        /// <summary>Track the subscription in <paramref name="bag"/> and return it unchanged.</summary>
        public static Subscription AddTo(this Subscription subscription, SubscriptionBag bag)
        {
            if (bag == null) throw new ArgumentNullException(nameof(bag));
            bag.Add(subscription);
            return subscription;
        }

        /// <summary>
        /// Tie the subscription to <paramref name="gameObject"/>'s lifetime: it is
        /// disposed when the GameObject is destroyed. Attaches one hidden
        /// <see cref="SubscriptionAnchor"/> per GameObject (the component allocates
        /// once; subsequent calls reuse it).
        /// </summary>
        public static Subscription AddTo(this Subscription subscription, GameObject gameObject)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            if (!gameObject.TryGetComponent<SubscriptionAnchor>(out var anchor))
                anchor = gameObject.AddComponent<SubscriptionAnchor>();
            anchor.Bag.Add(subscription);
            return subscription;
        }

        /// <summary>Tie the subscription to the lifetime of <paramref name="component"/>'s GameObject.</summary>
        public static Subscription AddTo(this Subscription subscription, Component component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            return subscription.AddTo(component.gameObject);
        }
    }
}
