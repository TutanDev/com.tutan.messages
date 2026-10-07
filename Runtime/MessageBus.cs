// ============================================================================
// MessageBus.cs — Zero-allocation Pub/Sub for Unity 6 / XR
//
// Supports:
//   - Immediate (synchronous) dispatch within current frame
//   - Queued dispatch for cross-frame / cross-thread decoupling
//   - Struct messages passed by ref (zero GC in hot path)
//   - Deterministic subscription lifecycle via disposable Subscription handles
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Handler delegate. The <c>ref</c> parameter avoids a struct copy per handler
    /// on dispatch. Handlers may mutate the message; later handlers in the same
    /// dispatch observe the mutation.
    /// </summary>
    public delegate void MessageHandler<T>(ref T message) where T : struct, IMessage;

    /// <summary>
    /// Editor / development-build check that the main-thread-only entry points
    /// really are on the main thread. Calling Publish/Subscribe/Dispose from a
    /// worker thread (the classic mistake: an <c>async</c> continuation that
    /// resumed on the thread pool) would otherwise corrupt the subscription list
    /// silently. The check is <c>[Conditional]</c>, so release player builds
    /// strip every call site — zero cost on the hot path.
    /// </summary>
    internal static class MainThreadGuard
    {
        static int s_mainThreadId = -1;

        // SubsystemRegistration always runs on the main thread, before user code.
        // In the editor, InitializeOnLoadMethod also captures it after every domain
        // reload (also main thread), so edit-mode callers — EditMode tests,
        // [ExecuteAlways] scripts, editor tools — are checked too, not only after
        // Play Mode has been entered once.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void Capture() => s_mainThreadId = Thread.CurrentThread.ManagedThreadId;

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void AssertMainThread(string operation)
        {
            // -1 = not captured yet (a call made before the capture callbacks
            // run). Skip the check rather than guess which thread is "main".
            if (s_mainThreadId == -1 || Thread.CurrentThread.ManagedThreadId == s_mainThreadId)
                return;

            Debug.LogError(
                $"Messages: {operation} is main-thread only but was called from thread " +
                $"{Thread.CurrentThread.ManagedThreadId}. Use Enqueue to send messages from a worker thread.");
        }
    }

    // ── Channel (per-message-type storage) ───────────────────────────────

    /// <summary>
    /// Non-generic base for heterogeneous storage in the channel dictionary.
    /// </summary>
    internal abstract class ChannelBase
    {
        public abstract void DrainQueue(MessagesInstrumentation.BusKind kind);
        public abstract int SubscriberCount { get; }
        public abstract bool RemoveEntry(int tokenId);
        internal abstract IEnumerable<(int TokenId, Delegate Handler)> EnumerateEntries();

        // True while queued messages await the next drain. Thread-safe, O(1).
        // Diagnostics only (CommandBus.Install's discarded-queue warning).
        public abstract bool HasPending { get; }

        // Non-generic dispatch seam for the editor synthetic-publish path, where
        // the message type is only known at runtime. Lets the bus avoid
        // MakeGenericMethod reflection by unboxing once inside Channel<T>.
        internal abstract void PublishBoxed(object message);
    }

    /// <summary>
    /// Typed channel holding subscriptions and a pending queue for type T.
    /// Internal — never exposed to consumers.
    /// </summary>
    internal sealed class Channel<T> : ChannelBase where T : struct, IMessage
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

        // Drain budget bookkeeping, used instead of ConcurrentQueue.Count: with more
        // than two segments, Count freezes them for observation, so the next Enqueue
        // starts a fresh 32-slot segment, and a steady queued stream would allocate
        // on every drain. _enqueued is bumped (any thread) only after the message
        // is in the queue; _dequeued is main-thread only. Their difference never
        // exceeds what the queue holds; an increment that lands late is drained a
        // frame later.
        int _enqueued;
        int _dequeued;

        // True while DrainQueue runs on this channel (main thread only). A handler
        // that calls DrainQueues again would otherwise start a nested drain of this
        // same channel with a fresh budget; one that also enqueues its own type
        // would recurse once per message until the stack overflows.
        bool _draining;

        // Re-entrancy depth. >0 means we are inside at least one Publish call.
        // CompactIfNeeded is deferred until depth returns to 0 so that outer
        // dispatch iterations are never invalidated by list mutations.
        int _dispatchDepth;

        public override int SubscriberCount => _activeCount;

        public override bool HasPending
        {
            get
            {
                var queue = Volatile.Read(ref _pendingQueue);
                return queue != null && !queue.IsEmpty;
            }
        }

        public void Publish(ref T message)
        {
            // finally: an exception escaping the loop (anything outside the
            // per-handler catch) must not leave _dispatchDepth stuck above 0,
            // or compaction would be disabled for this channel forever.
            _dispatchDepth++;
            try
            {
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
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0)
                    CompactIfNeeded();
            }
        }

        internal override void PublishBoxed(object message)
        {
            // The channel is keyed by typeof(T) and the caller looked it up by
            // message.GetType(), so this unbox is always valid.
            var m = (T)message;
            Publish(ref m);
        }

        public void Enqueue(in T message)
        {
            // Lazy init must be a CAS, not `??=`: two worker threads racing on the
            // first Enqueue of this type would otherwise each create a queue, and
            // the loser's message would be silently lost.
            var queue = Volatile.Read(ref _pendingQueue);
            if (queue == null)
            {
                var fresh = new ConcurrentQueue<T>();
                queue = Interlocked.CompareExchange(ref _pendingQueue, fresh, null) ?? fresh;
            }
            queue.Enqueue(message);
            // After the Enqueue, never before: the drain budget must not count a
            // message that is not in the queue yet.
            Interlocked.Increment(ref _enqueued);
        }

        public override void DrainQueue(MessagesInstrumentation.BusKind kind)
        {
            // Volatile so a queue created by a worker thread is visible here at
            // the latest one frame after its first Enqueue. IsEmpty is the O(1),
            // allocation-free fast path for the common nothing-queued case.
            var queue = Volatile.Read(ref _pendingQueue);
            if (queue == null || queue.IsEmpty || _draining) return;

            // Bound the drain to the backlog present when this call started — an
            // unbounded loop would let a handler that enqueues its own type on
            // every receipt hang the frame forever. What it enqueues waits for the
            // next drain. A DrainQueues call made from inside a handler skips this
            // channel (_draining) and drains the others, so re-entrancy cannot
            // extend the bound either.
            // unchecked: both counters may wrap; the difference stays correct.
            int budget = unchecked(Volatile.Read(ref _enqueued) - _dequeued);
            _draining = true;
            try
            {
                while (budget-- > 0 && queue.TryDequeue(out var msg))
                {
                    _dequeued = unchecked(_dequeued + 1);

                    // A drained message is being dispatched now, so record it as a
                    // Publish — immediate Publish records in MessageBus.Publish, but
                    // that path is bypassed here, so the dispatch would otherwise be
                    // invisible to the Messages Console. [Conditional]-stripped in
                    // release, same as every other Record* call.
                    MessagesInstrumentation.RecordPublish(kind, ref msg, this);
                    Publish(ref msg);
                }
            }
            finally
            {
                // finally: same reason as _dispatchDepth in Publish — an escaping
                // exception must not leave this channel undrainable forever.
                _draining = false;
            }
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

        internal override IEnumerable<(int TokenId, Delegate Handler)> EnumerateEntries()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.Active)
                    yield return (e.TokenId, e.Handler);
            }
        }

        void CompactIfNeeded()
        {
            // Compact only when there are at least 4 dead entries AND they make
            // up at least a quarter of the list — small absolute counts aren't
            // worth an O(n) RemoveAll, and on large lists a low dead ratio
            // would make every few unsubscribes pay a full-list sweep.
            if (_dirtyCount < 4 || _dirtyCount < Entries.Count / 4) return;

            Entries.RemoveAll(static e => !e.Active);
            _dirtyCount = 0;
            Debug.Assert(_activeCount == Entries.Count, "Messages: _activeCount desynced after compaction.");
        }
    }

    // ── MessageBus<TBase> ──────────────────────────────────────────────

    /// <summary>
    /// Generic message bus parameterized by message base type (ICommand or IEvent).
    /// Wrapped by the static <see cref="EventBus"/>/<see cref="CommandBus"/> facades,
    /// which own the singleton instances and add bus-specific rules.
    ///
    /// Thread safety: Publish(), Subscribe(), Subscription.Dispose(), and DrainQueues()
    /// are main-thread only. Enqueue() is thread-safe and may race with any
    /// main-thread call: channel creation goes through a ConcurrentDictionary,
    /// and the message lands in a per-channel ConcurrentQueue.
    /// <para>
    /// One carve-out: replacing the bus wholesale — the static <c>EventBus.Reset</c>/
    /// <c>CommandBus.Reset</c>/<c>CommandBus.Install</c> swap — is *not* safe to race
    /// against a worker-thread Enqueue, which can land its message in the discarded
    /// instance. Quiesce workers before resetting or re-installing; both are
    /// composition-root operations, not steady-state ones. See Documentation~/Threading.md.
    /// </para>
    /// </summary>
    public class MessageBus<TBase> : IDisposable, ISubscriptionOwner where TBase : IMessage
    {
        // ConcurrentDictionary so worker-thread Enqueue can race safely with
        // main-thread Subscribe/Publish/DrainQueues. Lookups are lock-free;
        // GetOrAdd is thread-safe. Per-channel Entry list mutations remain
        // main-thread only (Subscribe/Unsubscribe/Publish/DrainQueue).
        readonly ConcurrentDictionary<Type, ChannelBase> _channels = new(concurrencyLevel: 2, capacity: 32);

        // Cached channel list for DrainQueues. Enumerating a ConcurrentDictionary
        // allocates a class enumerator, and DrainQueues runs every frame — this
        // list keeps the steady-state drain allocation-free. Main thread only
        // (like DrainQueues itself); rebuilt when the channel set changes.
        readonly List<ChannelBase> _drainList = new();

        int _nextTokenId;

        // Guards only the double-Dispose(bool) path — it is not a "bus is unusable"
        // flag, and the entry points deliberately do not throw ObjectDisposedException.
        // The static buses (EventBus/CommandBus) always swap in a fresh instance on
        // Reset/Install, so a disposed bus is never published to again; reuse of a
        // disposed instance isn't a real scenario.
        bool _disposed;

        // Used by the optional instrumentation layer to tag records as
        // originating from the Event or Command bus.
        readonly MessagesInstrumentation.BusKind _instrumentationKind;

        static readonly ProfilerMarker s_publishMarker = new("Messages.Publish");
        static readonly ProfilerMarker s_enqueueMarker = new("Messages.Enqueue");
        static readonly ProfilerMarker s_drainMarker = new("Messages.DrainQueues");

        /// <summary>
        /// Create a standalone bus instance. The static <see cref="EventBus"/> /
        /// <see cref="CommandBus"/> facades cover the common case; construct one
        /// directly only when you need an isolated bus (e.g. per test fixture or
        /// per subsystem). Its instrumentation records are tagged
        /// <see cref="MessagesInstrumentation.BusKind.Command"/> when
        /// <typeparamref name="TBase"/> is or derives from <see cref="ICommand"/>,
        /// <see cref="MessagesInstrumentation.BusKind.Event"/> otherwise.
        /// </summary>
        public MessageBus() : this(typeof(ICommand).IsAssignableFrom(typeof(TBase))
            ? MessagesInstrumentation.BusKind.Command
            : MessagesInstrumentation.BusKind.Event) { }

        internal MessageBus(MessagesInstrumentation.BusKind kind)
        {
            _instrumentationKind = kind;
        }

        /// <summary>
        /// Register a handler for message type T. Returns a disposable
        /// <see cref="Subscription"/> bound to this bus instance — dispose it
        /// (directly, via a <see cref="SubscriptionBag"/>, or with <c>AddTo</c>) to
        /// unsubscribe. Because the handle captures the bus instance, disposing it
        /// after the issuing bus was replaced (the static <c>EventBus.Reset</c>/
        /// <c>CommandBus.Reset</c>/<c>CommandBus.Install</c> swap) is a harmless
        /// no-op rather than a risk to an unrelated subscription on the replacement bus.
        /// Main thread only. Virtual so subclasses can add pre-subscribe guards.
        /// </summary>
        public virtual Subscription Subscribe<T>(MessageHandler<T> handler) where T : struct, TBase
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            MainThreadGuard.AssertMainThread("Subscribe");

            var channel = GetOrCreateChannel<T>();
            int tokenId = ++_nextTokenId;
            Debug.Assert(tokenId != 0, "SubscriptionToken ID wrapped to invalid sentinel.");
            channel.AddEntry(tokenId, handler);

            MessagesInstrumentation.RecordSubscribe(_instrumentationKind, typeof(T), tokenId, handler);

            return new Subscription(this, new SubscriptionToken(tokenId, typeof(T)));
        }

        bool ISubscriptionOwner.Unsubscribe(SubscriptionToken token) => Unsubscribe(token);

        /// <summary>
        /// Remove a subscription by token. Safe to call during dispatch.
        /// Returns false if the token was already unsubscribed or invalid.
        /// Reached through <see cref="Subscription.Dispose"/> — not public API.
        /// </summary>
        internal bool Unsubscribe(SubscriptionToken token)
        {
            if (!token.IsValid) return false;
            MainThreadGuard.AssertMainThread("Subscription.Dispose");
            if (!_channels.TryGetValue(token.MessageType, out var channelBase)) return false;

            bool removed = channelBase.RemoveEntry(token.Id);
            if (removed)
                MessagesInstrumentation.RecordUnsubscribe(_instrumentationKind, token.MessageType, token.Id);
            return removed;
        }

        /// <summary>
        /// Dispatch message to all subscribers immediately (within current frame).
        /// Main thread only. Zero allocation.
        /// </summary>
        public void Publish<T>(ref T message) where T : struct, TBase
        {
            MainThreadGuard.AssertMainThread("Publish");

            // Look up the channel first so instrumentation can snapshot the
            // subscribers as they are at this instant. channelBase is null when
            // nothing has ever subscribed — the publish is still recorded.
            _channels.TryGetValue(typeof(T), out var channelBase);
            MessagesInstrumentation.RecordPublish(_instrumentationKind, ref message, channelBase);

            if (channelBase == null) return;

            using var _ = s_publishMarker.Auto();
            ((Channel<T>)channelBase).Publish(ref message);
        }

        /// <summary>
        /// Convenience overload for fire-and-forget publishing.
        /// One struct copy (caller → parameter), acceptable for small messages.
        /// </summary>
        public void Publish<T>(T message) where T : struct, TBase
        {
            Publish(ref message);
        }

        /// <summary>
        /// Publish a boxed message whose concrete type is only known at runtime —
        /// the editor synthetic-publish path. One unbox inside the channel; never
        /// used on the hot path. Main thread only. A type with no channel (nobody
        /// has subscribed) is recorded and then a no-op, same as the generic path.
        /// </summary>
        internal void PublishBoxed(object message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            MainThreadGuard.AssertMainThread("Publish");

            var type = message.GetType();
            _channels.TryGetValue(type, out var channelBase);
            MessagesInstrumentation.RecordPublishBoxed(_instrumentationKind, type, message, channelBase);

            if (channelBase == null) return;

            using var _ = s_publishMarker.Auto();
            channelBase.PublishBoxed(message);
        }

        /// <summary>
        /// Enqueue a message for deferred dispatch on the next DrainQueues() call.
        /// Thread-safe. Use from worker threads, async callbacks, network handlers.
        /// The struct is copied (shallowly) into a per-type <c>ConcurrentQueue</c>;
        /// the first Enqueue of a type allocates its channel and queue.
        /// </summary>
        public void Enqueue<T>(in T message) where T : struct, TBase
        {
            using var _ = s_enqueueMarker.Auto();

            var channel = GetOrCreateChannel<T>();
            MessagesInstrumentation.RecordEnqueue(_instrumentationKind, in message, channel);
            channel.Enqueue(message);
        }

        /// <summary>
        /// Process all queued messages across all channels. Call once per frame
        /// from a MonoBehaviour or PlayerLoop callback. Main thread only.
        /// </summary>
        public void DrainQueues()
        {
            MainThreadGuard.AssertMainThread("DrainQueues");
            using var _ = s_drainMarker.Auto();

            MessagesInstrumentation.RecordDrain(_instrumentationKind, start: true);

            // Channels are only ever added (Reset/Dispose clear the drain list
            // explicitly), so a count mismatch is the complete staleness signal.
            // A channel added by a worker-thread Enqueue racing this check is
            // picked up next frame at the latest.
            if (_drainList.Count != _channels.Count)
            {
                _drainList.Clear();
                foreach (var kvp in _channels)
                    _drainList.Add(kvp.Value);
            }

            for (int i = 0; i < _drainList.Count; i++)
                _drainList[i].DrainQueue(_instrumentationKind);

            MessagesInstrumentation.RecordDrain(_instrumentationKind, start: false);
        }

        /// <summary>Number of active subscriptions for message type <typeparamref name="T"/>.</summary>
        public int GetSubscriberCount<T>() where T : struct, TBase
        {
            return _channels.TryGetValue(typeof(T), out var ch) ? ch.SubscriberCount : 0;
        }

        /// <summary>
        /// Non-generic counterpart of <see cref="GetSubscriberCount{T}"/> for callers
        /// that only have a runtime <see cref="Type"/> (editor tooling, diagnostics).
        /// </summary>
        public int GetSubscriberCount(Type type)
        {
            return _channels.TryGetValue(type, out var ch) ? ch.SubscriberCount : 0;
        }

        /// <summary>
        /// Number of message types with a channel on this bus — every type that has
        /// been subscribed to or enqueued at least once since the last reset.
        /// </summary>
        public int ChannelCount => _channels.Count;

        /// <summary>
        /// Names of the message types whose queues still hold undrained messages,
        /// comma-separated, or null when every queue is empty. Diagnostics for the
        /// static facades' bus swap — allocates; never call it per frame.
        /// </summary>
        internal string DescribePendingQueues()
        {
            System.Text.StringBuilder names = null;
            foreach (var kvp in _channels)
            {
                if (!kvp.Value.HasPending) continue;
                if (names == null) names = new System.Text.StringBuilder();
                else names.Append(", ");
                names.Append(kvp.Key.Name);
            }
            return names?.ToString();
        }

        /// <summary>
        /// Walk all active subscriptions. Editor-only diagnostic — allocates
        /// per call. Not part of the public API.
        /// </summary>
        internal IEnumerable<(Type MessageType, IEnumerable<(int TokenId, Delegate Handler)> Entries)> EnumerateSubscriptions()
        {
            foreach (var kvp in _channels)
                yield return (kvp.Key, kvp.Value.EnumerateEntries());
        }

        Channel<T> GetOrCreateChannel<T>() where T : struct, TBase
        {
            // Fast path: lock-free read. Slow path on first use of T allocates a
            // Channel<T>; if two threads race, GetOrAdd ensures only one survives
            // — the loser's channel is unreachable and GC'd.
            if (_channels.TryGetValue(typeof(T), out var existing))
                return (Channel<T>)existing;

            var channel = new Channel<T>();
            return (Channel<T>)_channels.GetOrAdd(typeof(T), channel);
        }

        /// <summary>
        /// Drop all subscriptions and queued messages and mark the instance disposed.
        /// The static facades call this when swapping in a fresh bus on
        /// <c>Reset</c>/<c>Install</c>; a disposed instance is not meant to be
        /// reused — to clear a bus you keep, use <see cref="Reset"/> instead.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Standard dispose pattern: override to release subclass state, and call
        /// the base implementation.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                _channels.Clear();
                // Must accompany every _channels.Clear(): a stale drain list
                // whose count happens to match the rebuilt channel set would
                // silently drain the old channels instead of the new ones.
                _drainList.Clear();
            }
        }

        /// <summary>
        /// Clear all subscriptions and queued messages on <em>this</em> instance, in place.
        /// <para>
        /// Note the difference from the static <c>EventBus.Reset</c>/<c>CommandBus.Reset</c>,
        /// which dispose this instance and swap in a fresh one. After that swap, existing
        /// <see cref="Subscription"/> handles target the discarded bus and their Dispose
        /// becomes a harmless no-op (see SubscriptionTests.Dispose_AfterBusReset_*).
        /// Clearing in place here leaves the instance live and reusable.
        /// </para>
        /// </summary>
        public void Reset()
        {
            _channels.Clear();
            _drainList.Clear();
        }
    }
}
