using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Collects <see cref="Subscription"/>s so a whole group can be unsubscribed
    /// with one <see cref="Dispose"/> (or <see cref="Clear"/>) call — one bag per
    /// system instead of one <see cref="Subscription"/> field per subscription.
    /// Reusable after disposal: <c>Add</c> works again and a later Dispose
    /// releases the new batch.
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

        /// <summary>Same as <see cref="Clear"/>; the bag stays usable afterwards (unlike most <see cref="IDisposable"/> types).</summary>
        public void Dispose() => Clear();
    }

    /// <summary>
    /// Hidden component that ties a <see cref="SubscriptionBag"/> to a GameObject's
    /// lifetime. Added automatically by <c>Subscription.AddTo(gameObject)</c> —
    /// never add it by hand. It is hidden from the Inspector and never saved into
    /// a scene or prefab (its bag is runtime-only state, so a serialized copy would
    /// just be an empty component).
    /// </summary>
    /// <remarks>
    /// Disposal rides Unity's <c>OnDestroy</c>, which Unity only invokes on
    /// components whose GameObject has been active at least once. A subscription
    /// anchored to a GameObject that is destroyed without ever having been active
    /// (e.g. a pooled instance created inactive) is therefore not disposed — hold
    /// the <see cref="Subscription"/> or use a <see cref="SubscriptionBag"/> for
    /// those lifetimes. The anchor is <c>[ExecuteAlways]</c>, so destroying its
    /// GameObject disposes the bag in edit mode too (<c>AddTo</c> called from an
    /// <c>[ExecuteAlways]</c> script).
    /// </remarks>
    [AddComponentMenu("")]
    [ExecuteAlways]
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
        /// once; subsequent calls reuse it). See the anchor's remarks for the
        /// never-activated GameObject caveat.
        /// </summary>
        public static Subscription AddTo(this Subscription subscription, GameObject gameObject)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            if (!gameObject.TryGetComponent<SubscriptionAnchor>(out var anchor))
            {
                anchor = gameObject.AddComponent<SubscriptionAnchor>();
                // Runtime-only bookkeeping: keep it out of the Inspector, and out of
                // saved scenes/prefabs when AddTo runs in edit mode ([ExecuteAlways]).
                anchor.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor;
            }
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
