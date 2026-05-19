// ============================================================================
// MessageBus.cs — Zero-allocation Pub/Sub for Unity 6 / XR
//
// Supports:
//   - Immediate (synchronous) dispatch within current frame
//   - Queued dispatch for cross-frame / cross-thread decoupling
//   - Struct messages passed by ref (zero GC in hot path)
//   - Deterministic subscription lifecycle via tokens
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;

namespace Tutan.MessageBus
{
    /// <summary>
    /// Handler delegate. Ref parameter avoids struct copy on dispatch.
    /// </summary>
    public delegate void MessageHandler<T>(ref T message) where T : unmanaged, IMessage;

    // ── Channel (per-message-type storage) ───────────────────────────────

    /// <summary>
    /// Non-generic base for heterogeneous storage in the channel dictionary.
    /// </summary>
    internal abstract class ChannelBase
    {
        public abstract void DrainQueue();
        public abstract int SubscriberCount { get; }
        public abstract bool RemoveEntry(int tokenId);
    }

    /// <summary>
    /// Typed channel holding subscriptions and a pending queue for type T.
    /// Internal — never exposed to consumers.
    /// </summary>
    internal sealed class Channel<T> : ChannelBase where T : unmanaged, IMessage
    {
        internal struct Entry
        {
            public int TokenId;
            public MessageHandler<T> Handler;
            public bool Active;
        }

        // Subscriptions — flat list, holes marked inactive. Compacted lazily.
        internal readonly List<Entry> Entries = new(8);
        int _dirtyCount;  // tracks inactive entries for compaction heuristic
        int _activeCount; // tracks active entries for O(1) SubscriberCount

        // Queue for deferred dispatch — allocated on first Enqueue call.
        ConcurrentQueue<T> _pendingQueue;

        // Re-entrancy depth. >0 means we are inside at least one Publish call.
        // CompactIfNeeded is deferred until depth returns to 0 so that outer
        // dispatch iterations are never invalidated by list mutations.
        int _dispatchDepth;

        public override int SubscriberCount => _activeCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish(ref T message)
        {
            _dispatchDepth++;
            var entries = Entries;
            int count = entries.Count;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry.Active)
                {
                    try
                    {
                        entry.Handler(ref message);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't break dispatch chain. A broken handler
                        // must not cascade into a broken frame.
                        Debug.LogException(ex);
                    }
                }
            }

            _dispatchDepth--;
            if (_dispatchDepth == 0)
                CompactIfNeeded();
        }

        public void Enqueue(in T message)
        {
            (_pendingQueue ??= new ConcurrentQueue<T>()).Enqueue(message);
        }

        public override void DrainQueue()
        {
            if (_pendingQueue == null) return;
            while (_pendingQueue.TryDequeue(out var msg))
                Publish(ref msg);
        }

        public void AddEntry(int tokenId, MessageHandler<T> handler)
        {
            Entries.Add(new Entry
            {
                TokenId = tokenId,
                Handler = handler,
                Active = true
            });
            _activeCount++;
        }

        public override bool RemoveEntry(int tokenId)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].TokenId == tokenId && Entries[i].Active)
                {
                    var e = Entries[i];
                    e.Active = false;
                    e.Handler = null; // release delegate reference
                    Entries[i] = e;
                    _dirtyCount++;
                    _activeCount--;

                    if (_dispatchDepth == 0)
                        CompactIfNeeded();

                    return true;
                }
            }
            return false;
        }

        void CompactIfNeeded()
        {
            if (_dirtyCount == 0) return;
            if (_dirtyCount < 4 && _dirtyCount < Entries.Count / 4) return;

            Entries.RemoveAll(static e => !e.Active);
            _dirtyCount = 0;
            Debug.Assert(_activeCount == Entries.Count,
                "MessageBus: _activeCount desynced after compaction.");
        }
    }

    // ── MessageBus<TBase> ────────────────────────────────────────────────

    /// <summary>
    /// Generic message bus parameterized by message base type (ICommand or IEvent).
    /// Subclasses own their singletons and may add bus-specific rules.
    ///
    /// Thread safety: Publish(), Subscribe(), Unsubscribe(), and DrainQueues()
    /// are main-thread only. Enqueue() is thread-safe and may race with any
    /// main-thread call: channel creation goes through a ConcurrentDictionary,
    /// and the message lands in a per-channel ConcurrentQueue.
    /// </summary>
    public class MessageBus<TBase> : IDisposable where TBase : IMessage
    {
        // ConcurrentDictionary so worker-thread Enqueue can race safely with
        // main-thread Subscribe/Publish/DrainQueues. Lookups are lock-free;
        // GetOrAdd is thread-safe. Per-channel Entry list mutations remain
        // main-thread only (Subscribe/Unsubscribe/Publish/DrainQueue).
        readonly ConcurrentDictionary<Type, ChannelBase> _channels = new(concurrencyLevel: 2, capacity: 32);
        int _nextTokenId;
        bool _disposed;

        static readonly ProfilerMarker s_publishMarker = new("MessageBus.Publish");
        static readonly ProfilerMarker s_drainMarker = new("MessageBus.DrainQueues");

        public MessageBus() { }

        /// <summary>
        /// Register a handler for message type T. Returns a token for unsubscription.
        /// Main thread only. Virtual so subclasses can add pre-subscribe guards.
        /// </summary>
        public virtual SubscriptionToken Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, TBase
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var channel = GetOrCreateChannel<T>();
            int tokenId = ++_nextTokenId;
            Debug.Assert(tokenId != 0, "SubscriptionToken ID wrapped to invalid sentinel.");
            channel.AddEntry(tokenId, handler);

            return new SubscriptionToken(tokenId, typeof(T));
        }

        /// <summary>
        /// Remove a subscription by token. Safe to call during dispatch.
        /// Returns false if the token was already unsubscribed or invalid.
        /// </summary>
        public bool Unsubscribe(SubscriptionToken token)
        {
            if (!token.IsValid) return false;
            if (!_channels.TryGetValue(token.MessageType, out var channelBase)) return false;

            return channelBase.RemoveEntry(token.Id);
        }

        /// <summary>
        /// Dispatch message to all subscribers immediately (within current frame).
        /// Main thread only. Zero allocation.
        /// </summary>
        public void Publish<T>(ref T message) where T : unmanaged, TBase
        {
            if (!_channels.TryGetValue(typeof(T), out var channelBase)) return;

            using var _ = s_publishMarker.Auto();
            ((Channel<T>)channelBase).Publish(ref message);
        }

        /// <summary>
        /// Convenience overload for fire-and-forget publishing.
        /// One struct copy (caller → parameter), acceptable for small messages.
        /// </summary>
        public void Publish<T>(T message) where T : unmanaged, TBase
        {
            Publish(ref message);
        }

        /// <summary>
        /// Enqueue a message for deferred dispatch on the next DrainQueues() call.
        /// Thread-safe. Use from worker threads, async callbacks, network handlers.
        /// </summary>
        public void Enqueue<T>(in T message) where T : unmanaged, TBase
        {
            GetOrCreateChannel<T>().Enqueue(message);
        }

        /// <summary>
        /// Process all queued messages across all channels. Call once per frame
        /// from a MonoBehaviour or PlayerLoop callback. Main thread only.
        /// </summary>
        public void DrainQueues()
        {
            using var _ = s_drainMarker.Auto();

            foreach (var kvp in _channels)
            {
                kvp.Value.DrainQueue();
            }
        }

        public int GetSubscriberCount<T>() where T : unmanaged, TBase
        {
            return _channels.TryGetValue(typeof(T), out var ch) ? ch.SubscriberCount : 0;
        }

        public int ChannelCount => _channels.Count;

        Channel<T> GetOrCreateChannel<T>() where T : unmanaged, TBase
        {
            // Fast path: lock-free read. Slow path on first use of T allocates a
            // Channel<T>; if two threads race, GetOrAdd ensures only one survives
            // — the loser's channel is unreachable and GC'd.
            if (_channels.TryGetValue(typeof(T), out var existing))
                return (Channel<T>)existing;

            var channel = new Channel<T>();
            return (Channel<T>)_channels.GetOrAdd(typeof(T), channel);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
                _channels.Clear();
        }

        /// <summary>
        /// Clear all subscriptions and queued messages. Useful for scene transitions.
        /// </summary>
        public void Reset() => _channels.Clear();
    }
}
