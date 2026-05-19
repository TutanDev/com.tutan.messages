// ============================================================================
// MessageBusInstrumentation.cs — Optional pub/sub observation layer
//
// All Record* methods are decorated with [Conditional("UNITY_EDITOR"),
// Conditional("TUTAN_MESSAGEBUS_DEBUG")] so the C# compiler strips every
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

namespace Tutan.MessageBus
{
    public static class MessageBusInstrumentation
    {
        public enum BusKind : byte
        {
            Event = 0,
            Command = 1
        }

        public enum Op : byte
        {
            Publish,
            Enqueue,
            Subscribe,
            Unsubscribe,
            DrainStart,
            DrainEnd
        }

        public readonly struct Record
        {
            public readonly long TimestampTicks;
            public readonly int Frame;
            public readonly int ThreadId;
            public readonly BusKind Bus;
            public readonly Op Op;
            public readonly Type MessageType;
            public readonly int TokenId;
            public readonly object PayloadBox;
            public readonly string HandlerTarget;
            public readonly string HandlerMethod;

            internal Record(
                long ticks, int frame, int threadId,
                BusKind bus, Op op, Type messageType,
                int tokenId, object payloadBox,
                string handlerTarget, string handlerMethod)
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
            }
        }

        /// <summary>Master toggle. When false, every Record* call returns immediately.</summary>
        public static bool Enabled;

        /// <summary>When true, Publish/Enqueue records box the struct payload into <see cref="Record.PayloadBox"/>. Otherwise null.</summary>
        public static bool CapturePayloads;

        /// <summary>When true, <see cref="Op.DrainStart"/> / <see cref="Op.DrainEnd"/> are appended to the buffer. Off by default — drains fire every frame and would flood the ring buffer.</summary>
        public static bool RecordDrains;

        // Frame counter — updated by MessageBusHost from main thread so worker
        // threads can read it without touching UnityEngine.Time.
        internal static int CurrentFrame;

        const int DefaultCapacity = 4096;
        static Record[] s_buffer = new Record[DefaultCapacity];
        static int s_head;       // next write index
        static int s_count;      // current valid records (<= buffer.Length)
        static long s_totalEver; // monotonic, survives wraparound
        static readonly object s_lock = new object();

        public static int Capacity => s_buffer.Length;
        public static int Count { get { lock (s_lock) return s_count; } }
        public static long TotalEver => Interlocked.Read(ref s_totalEver);

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

        public static List<Record> Snapshot()
        {
            lock (s_lock)
            {
                var list = new List<Record>(s_count);
                int start = (s_head - s_count + s_buffer.Length) % s_buffer.Length;
                for (int i = 0; i < s_count; i++)
                    list.Add(s_buffer[(start + i) % s_buffer.Length]);
                return list;
            }
        }

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

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]
        internal static void RecordPublish<T>(BusKind bus, ref T message) where T : unmanaged, IMessage
        {
            if (!Enabled) return;
            object payload = CapturePayloads ? (object)message : null;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Publish, typeof(T), 0, payload, null, null));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]
        internal static void RecordEnqueue<T>(BusKind bus, in T message) where T : unmanaged, IMessage
        {
            if (!Enabled) return;
            object payload = CapturePayloads ? (object)message : null;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Enqueue, typeof(T), 0, payload, null, null));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]
        internal static void RecordSubscribe(BusKind bus, Type messageType, int tokenId, Delegate handler)
        {
            if (!Enabled) return;
            string target = handler?.Target?.GetType().Name;
            string method = handler?.Method?.Name;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Subscribe, messageType, tokenId, null, target, method));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]
        internal static void RecordUnsubscribe(BusKind bus, Type messageType, int tokenId)
        {
            if (!Enabled) return;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, Op.Unsubscribe, messageType, tokenId, null, null, null));
        }

        [Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]
        internal static void RecordDrain(BusKind bus, bool start)
        {
            if (!Enabled || !RecordDrains) return;
            Append(new Record(
                DateTime.UtcNow.Ticks, CurrentFrame, Thread.CurrentThread.ManagedThreadId,
                bus, start ? Op.DrainStart : Op.DrainEnd, null, 0, null, null, null));
        }

        static void Append(Record record)
        {
            lock (s_lock)
            {
                s_buffer[s_head] = record;
                s_head = (s_head + 1) % s_buffer.Length;
                if (s_count < s_buffer.Length) s_count++;
            }
            Interlocked.Increment(ref s_totalEver);
        }
    }
}
