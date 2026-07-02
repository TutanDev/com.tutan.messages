// ============================================================================
// MessagesInstrumentation.cs — Optional pub/sub observation layer
//
// All Record* methods are decorated with [Conditional("UNITY_EDITOR"),
// Conditional("TUTAN_MESSAGES_DEBUG")] so the C# compiler strips every
// call site in release player builds. When the call sites are compiled in,
// they short-circuit on `!Enabled` so the steady-state cost is one branch.
//
// The ring buffer is thread-safe: worker-thread Enqueue paths may append
// from any thread. The editor window polls Snapshot() from the main thread.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Tutan.Messages
{
    /// <summary>
    /// Optional observation layer over both buses: while <see cref="Enabled"/> is
    /// true, every subscribe/unsubscribe/publish/enqueue (and optionally drain) is
    /// appended to a thread-safe ring buffer that the Messages Console — or your
    /// own diagnostics via <see cref="Snapshot()"/> — can poll. Compiled into
    /// editor and <c>TUTAN_MESSAGES_DEBUG</c> builds only; in release player
    /// builds every hook call site is stripped and this type is never touched.
    /// </summary>
    public static class MessagesInstrumentation
    {
        /// <summary>Which bus produced a record.</summary>
        public enum BusKind : byte
        {
            Event = 0,
            Command = 1
        }

        /// <summary>The bus operation a record captures.</summary>
        public enum Op : byte
        {
            Publish,
            Enqueue,
            Subscribe,
            Unsubscribe,
            DrainStart,
            DrainEnd
        }

        /// <summary>
        /// One subscriber as it existed at the moment a message was published or
        /// enqueued. Captured into <see cref="Record.Subscribers"/> so the debugger
        /// shows who was actually listening at fire time, not who is listening now.
        /// </summary>
        public readonly struct Subscriber
        {
            /// <summary>The subscription's token id, as shown by Subscribe records.</summary>
            public readonly int TokenId;

            /// <summary>Full type name of the handler's target object, or "(static)".</summary>
            public readonly string Target;

            /// <summary>Name of the handler method.</summary>
            public readonly string Method;

            internal Subscriber(int tokenId, string target, string method)
            {
                TokenId = tokenId;
                Target = target;
                Method = method;
            }
        }

        /// <summary>One captured bus operation, frozen at the instant it happened.</summary>
        public readonly struct Record
        {
            /// <summary>UTC timestamp (<see cref="DateTime.Ticks"/>) of the operation.</summary>
            public readonly long TimestampTicks;

            /// <summary>Main-thread frame count at the time of the operation (see <see cref="SyncFrame"/>).</summary>
            public readonly int Frame;

            /// <summary>Managed id of the thread the operation ran on.</summary>
            public readonly int ThreadId;

            /// <summary>Which bus produced the record.</summary>
            public readonly BusKind Bus;

            /// <summary>The captured operation.</summary>
            public readonly Op Op;

            /// <summary>The message type; null for drain records.</summary>
            public readonly Type MessageType;

            /// <summary>Subscription token id for Subscribe/Unsubscribe records; 0 otherwise.</summary>
            public readonly int TokenId;

            /// <summary>The boxed message for Publish/Enqueue records; null otherwise.</summary>
            public readonly object PayloadBox;

            /// <summary>Handler target type name for Subscribe records; null otherwise.</summary>
            public readonly string HandlerTarget;

            /// <summary>Handler method name for Subscribe records; null otherwise.</summary>
            public readonly string HandlerMethod;

            /// <summary>
            /// Frozen snapshot of the subscribers at the instant of a Publish/Enqueue
            /// record. Null for ops where it does not apply (Subscribe, Drain, …).
            /// </summary>
            public readonly Subscriber[] Subscribers;

            internal Record(
                long ticks, int frame, int threadId,
                BusKind bus, Op op, Type messageType,
                int tokenId, object payloadBox,
                string handlerTarget, string handlerMethod,
                Subscriber[] subscribers)
            {
                TimestampTicks = ticks;
                Frame = frame;
                ThreadId = threadId;
                Bus = bus;
                Op = op;
                MessageType = messageType;
                TokenId = tokenId;
                PayloadBox = payloadBox;
                HandlerTarget = handlerTarget;
                HandlerMethod = handlerMethod;
                Subscribers = subscribers;
            }
        }

        /// <summary>Master toggle. When false, every Record* call returns immediately.</summary>
        public static bool Enabled;

        /// <summary>When true, <see cref="Op.DrainStart"/> / <see cref="Op.DrainEnd"/> are appended to the buffer. Off by default — drains fire every frame and would flood the ring buffer.</summary>
        public static bool RecordDrains;

        // Frame counter — updated by MessagesHost from main thread so worker
        // threads can read it without touching UnityEngine.Time.
        internal static int CurrentFrame;

        /// <summary>
        /// Stamp the current main-thread frame onto subsequent records. Call once
        /// per frame from the main thread (the auto-host does this in LateUpdate).
        /// [Conditional] so the call — and the Time.frameCount read passed to it —
        /// is stripped from release player builds. This is also the only static
        /// member the host touches, so stripping it means the type is never
        /// initialized in release and the ring buffer below is never allocated.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        public static void SyncFrame(int frame) => CurrentFrame = frame;

        const int DefaultCapacity = 4096;
        static Record[] s_buffer = new Record[DefaultCapacity];
        static int s_head;       // next write index
        static int s_count;      // current valid records (<= buffer.Length)
        static long s_totalEver; // monotonic, survives wraparound
        static readonly object s_lock = new object();

        /// <summary>Ring-buffer capacity, in records.</summary>
        public static int Capacity => s_buffer.Length;

        /// <summary>Number of valid records currently in the ring buffer.</summary>
        public static int Count { get { lock (s_lock) return s_count; } }

        /// <summary>
        /// Monotonic count of records ever appended. Unlike <see cref="Count"/> it
        /// survives ring wraparound, so incremental consumers can diff it against
        /// their last processed total to know how many trailing records are new.
        /// </summary>
        public static long TotalEver => Interlocked.Read(ref s_totalEver);

        /// <summary>Resize the ring buffer, discarding all current records. Minimum 16.</summary>
        public static void SetCapacity(int capacity)
        {
            if (capacity < 16) capacity = 16;
            lock (s_lock)
            {
                s_buffer = new Record[capacity];
                s_head = 0;
                s_count = 0;
            }
        }

        /// <summary>
        /// Copy the ring buffer's records, oldest first. Allocates a new list per
        /// call — poll it off the hot path.
        /// </summary>
        public static List<Record> Snapshot() => Snapshot(out _);

        /// <summary>
        /// Copy the ring buffer's records (oldest first) and report the value of
        /// <see cref="TotalEver"/> that pairs exactly with them. Both are read under
        /// one lock: reading <see cref="TotalEver"/> separately can run ahead of a
        /// snapshot taken a moment later while worker threads are appending, which
        /// makes an incremental consumer duplicate the records in between and drop
        /// an equal number of older ones.
        /// </summary>
        public static List<Record> Snapshot(out long totalEver)
        {
            lock (s_lock)
            {
                totalEver = s_totalEver;
                var list = new List<Record>(s_count);
                int start = (s_head - s_count + s_buffer.Length) % s_buffer.Length;
                for (int i = 0; i < s_count; i++)
                    list.Add(s_buffer[(start + i) % s_buffer.Length]);
                return list;
            }
        }

        /// <summary>Discard all records. <see cref="TotalEver"/> is unaffected.</summary>
        public static void Clear()
        {
            lock (s_lock)
            {
                s_head = 0;
                s_count = 0;
                Array.Clear(s_buffer, 0, s_buffer.Length);
            }
        }

        // ── Internal Record* hooks ───────────────────────────────────────
        // [Conditional] strips these at the call site in release builds.

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordPublish<T>(BusKind bus, ref T message, ChannelBase channel) where T : struct, IMessage
        {
            if (!Enabled) return;
            object payload =  message;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Publish, typeof(T), 0, payload, null, null, CaptureSubscribers(channel)));
        }

        // Non-generic counterpart to RecordPublish, for the editor synthetic-publish
        // path where the message arrives already boxed and its type is a runtime value.
        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordPublishBoxed(BusKind bus, Type messageType, object payload, ChannelBase channel)
        {
            if (!Enabled) return;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Publish, messageType, 0, payload, null, null, CaptureSubscribers(channel)));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordEnqueue<T>(BusKind bus, in T message, ChannelBase channel) where T : struct, IMessage
        {
            if (!Enabled) return;
            object payload = message;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Enqueue, typeof(T), 0, payload, null, null, CaptureSubscribers(channel)));
        }

        static readonly Subscriber[] s_noSubscribers = Array.Empty<Subscriber>();

        // Freeze the channel's current handlers into immutable strings. Resolving
        // Target/Method here (not at display time) is what makes this a true
        // point-in-time snapshot: the delegates may later be unsubscribed or GC'd.
        //
        // Enqueue can run on a worker thread while the main thread mutates the
        // entry list, so enumeration is best-effort — instrumentation must never
        // throw into the bus. A torn read just yields a slightly incomplete list.
        static Subscriber[] CaptureSubscribers(ChannelBase channel)
        {
            if (channel == null) return s_noSubscribers;
            try
            {
                List<Subscriber> list = null;
                foreach (var (tokenId, handler) in channel.EnumerateEntries())
                {
                    (list ??= new List<Subscriber>()).Add(new Subscriber(
                        tokenId,
                        handler?.Target?.GetType().FullName ?? "(static)",
                        handler?.Method?.Name ?? "?"));
                }
                return list != null ? list.ToArray() : s_noSubscribers;
            }
            catch
            {
                return s_noSubscribers;
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordSubscribe(BusKind bus, Type messageType, int tokenId, Delegate handler)
        {
            if (!Enabled) return;
            string target = handler?.Target?.GetType().Name;
            string method = handler?.Method?.Name;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Subscribe, messageType, tokenId, null, target, method, null));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordUnsubscribe(BusKind bus, Type messageType, int tokenId)
        {
            if (!Enabled) return;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Unsubscribe, messageType, tokenId, null, null, null, null));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
        internal static void RecordDrain(BusKind bus, bool start)
        {
            if (!Enabled || !RecordDrains) return;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, start ? Op.DrainStart : Op.DrainEnd, null, 0, null, null, null, null));
        }

        static void Append(Record record)
        {
            lock (s_lock)
            {
                s_buffer[s_head] = record;
                s_head = (s_head + 1) % s_buffer.Length;
                if (s_count < s_buffer.Length) s_count++;
                // Inside the lock so TotalEver can never run ahead of the buffer
                // contents — the console's incremental catch-up pairs the two.
                // Still Interlocked: TotalEver reads it without taking the lock.
                Interlocked.Increment(ref s_totalEver);
            }
        }
    }
}
