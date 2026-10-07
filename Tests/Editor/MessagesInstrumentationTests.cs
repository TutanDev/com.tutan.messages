using System.Linq;
using System.Threading;
using NUnit.Framework;
using Tutan.Messages;
using UnityEngine.TestTools.Constraints;
using GCConstraint = UnityEngine.TestTools.Constraints.Is;

namespace Tutan.Messages.Tests
{
    // Instrumentation is global state that an open Messages Console also drives
    // (Enabled, RecordDrains), so each test saves it, runs in a known state and
    // restores it. The ring buffer is cleared around each test, which cannot be
    // undone: the console keeps the rows it already shows but never sees the
    // tests' records.
    public class MessagesInstrumentationTests
    {
        struct Ping : IEvent { public int Value; }
        struct DoThing : ICommand { public int Value; }

        interface IGameCommand : ICommand { }
        struct Jump : IGameCommand { }

        bool _prevEnabled;
        bool _prevRecordDrains;
        int _prevCapacity;

        [SetUp]
        public void SetUp()
        {
            _prevEnabled = MessagesInstrumentation.Enabled;
            _prevRecordDrains = MessagesInstrumentation.RecordDrains;
            _prevCapacity = MessagesInstrumentation.Capacity;

            // Back to the default: project code may have shrunk the ring buffer,
            // and the tests count on it holding every record they write.
            if (_prevCapacity != 4096)
                MessagesInstrumentation.SetCapacity(4096);

            EventBus.Reset();
            CommandBus.Reset();
            MessagesInstrumentation.Clear();
            MessagesInstrumentation.RecordDrains = false;
            MessagesInstrumentation.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Runs even when a test fails midway, so a shrunken capacity can never
            // leak into the next test.
            if (MessagesInstrumentation.Capacity != _prevCapacity)
                MessagesInstrumentation.SetCapacity(_prevCapacity);
            MessagesInstrumentation.Enabled = _prevEnabled;
            MessagesInstrumentation.RecordDrains = _prevRecordDrains;
            MessagesInstrumentation.Clear();
            EventBus.Reset();
            CommandBus.Reset();
        }

        [Test]
        public void Subscribe_RecordsSubscribeOp()
        {
            var subscription = EventBus.Subscribe<Ping>((ref Ping p) => { });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Subscribe && r.MessageType == typeof(Ping));

            Assert.AreEqual(subscription.Token.Id, rec.TokenId);
            Assert.AreEqual(MessagesInstrumentation.BusKind.Event, rec.Bus);
        }

        [Test]
        public void Publish_RecordsPublishOp_AndPayload()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            EventBus.Publish(new Ping { Value = 11 });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping));

            Assert.AreEqual(MessagesInstrumentation.BusKind.Event, rec.Bus);
            Assert.IsNotNull(rec.PayloadBox);
            Assert.AreEqual(11, ((Ping)rec.PayloadBox).Value);
        }

        [Test]
        public void CommandBus_TagsRecordsAsCommandKind()
        {
            CommandBus.Install(r => r.Handle<DoThing>((ref DoThing m) => { }));
            CommandBus.Publish(new DoThing { Value = 1 });

            var publishRec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish);
            Assert.AreEqual(MessagesInstrumentation.BusKind.Command, publishRec.Bus);
        }

        [Test]
        public void StandaloneBus_TagsRecordsByItsBaseType()
        {
            // A bus built with the public constructor has no facade to tag it, so
            // the kind comes from TBase: ICommand, or an interface deriving from it,
            // is Command; anything else is Event.
            Assert.AreEqual(MessagesInstrumentation.BusKind.Command,
                PublishedKind(new MessageBus<ICommand>(), new DoThing()));
            Assert.AreEqual(MessagesInstrumentation.BusKind.Command,
                PublishedKind(new MessageBus<IGameCommand>(), new Jump()));
            Assert.AreEqual(MessagesInstrumentation.BusKind.Event,
                PublishedKind(new MessageBus<IEvent>(), new Ping()));
        }

        static MessagesInstrumentation.BusKind PublishedKind<TBase, T>(MessageBus<TBase> bus, T message)
            where TBase : IMessage
            where T : struct, TBase
        {
            try
            {
                MessagesInstrumentation.Clear();
                bus.Publish(message);
                return MessagesInstrumentation.Snapshot()
                    .Single(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(T))
                    .Bus;
            }
            finally
            {
                bus.Dispose();
            }
        }

        [Test]
        public void Enqueue_FromWorkerThread_RecordsOnAnyThread_AndPreservesOrder()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // A dedicated thread rather than the pool, so its id is known.
            var worker = new Thread(() =>
            {
                for (int i = 0; i < 50; i++)
                    EventBus.Enqueue(new Ping { Value = i });
            });
            worker.Start();
            worker.Join();

            EventBus.DrainQueues();

            var snap = MessagesInstrumentation.Snapshot();
            var enqueues = snap.Where(r => r.Op == MessagesInstrumentation.Op.Enqueue && r.MessageType == typeof(Ping)).ToList();
            var publishes = snap.Where(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping)).ToList();
            Assert.AreEqual(50, enqueues.Count);
            Assert.AreEqual(50, publishes.Count);
            Assert.AreNotEqual(mainThreadId, worker.ManagedThreadId);

            for (int i = 0; i < 50; i++)
            {
                // Enqueues carry the worker's thread id; the drain dispatches them on
                // the main thread, in the same order.
                Assert.AreEqual(worker.ManagedThreadId, enqueues[i].ThreadId, $"Enqueue record {i}");
                Assert.AreEqual(i, ((Ping)enqueues[i].PayloadBox).Value, $"Enqueue record {i}");
                Assert.AreEqual(mainThreadId, publishes[i].ThreadId, $"Publish record {i}");
                Assert.AreEqual(i, ((Ping)publishes[i].PayloadBox).Value, $"Publish record {i}");
            }
        }

        [Test]
        public void Unsubscribe_RecordsUnsubscribeOp_OnlyOnSuccess()
        {
            var subscription = EventBus.Subscribe<Ping>((ref Ping p) => { });
            var copy = subscription;
            MessagesInstrumentation.Clear();

            subscription.Dispose();
            // The copy still references the bus, so this reaches its Unsubscribe,
            // which finds the token already removed and must not record again.
            copy.Dispose();

            int count = MessagesInstrumentation.Snapshot()
                .Count(r => r.Op == MessagesInstrumentation.Op.Unsubscribe);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Disabled_CapturesNothing()
        {
            MessagesInstrumentation.Enabled = false;
            EventBus.Subscribe<Ping>((ref Ping p) => { });
            EventBus.Publish(new Ping { Value = 5 });

            Assert.AreEqual(0, MessagesInstrumentation.Snapshot().Count);
        }

        [Test]
        public void RingBuffer_WrapsAroundAtCapacity()
        {
            MessagesInstrumentation.SetCapacity(16); // TearDown restores the previous capacity
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            for (int i = 0; i < 100; i++)
                EventBus.Publish(new Ping { Value = i });

            // Oldest first: only the last 16 publishes survive the wraparound.
            var snap = MessagesInstrumentation.Snapshot();
            Assert.AreEqual(16, snap.Count);
            Assert.AreEqual(84, ((Ping)snap[0].PayloadBox).Value);
            Assert.AreEqual(99, ((Ping)snap[15].PayloadBox).Value);
        }

        [Test]
        public void SetCapacity_DiscardsRecords_WithoutAllocating()
        {
            EventBus.Publish(new Ping { Value = 1 });
            Assert.AreEqual(1, MessagesInstrumentation.Count);

            // The ring buffer is reallocated by the next recorded operation, not
            // here, so code that only configures instrumentation allocates nothing.
            Assert.That(() => MessagesInstrumentation.SetCapacity(64), GCConstraint.Not.AllocatingGCMemory());
            Assert.AreEqual(64, MessagesInstrumentation.Capacity);
            Assert.AreEqual(0, MessagesInstrumentation.Count);
            Assert.AreEqual(0, MessagesInstrumentation.Snapshot().Count);

            EventBus.Publish(new Ping { Value = 2 });
            var rec = MessagesInstrumentation.Snapshot().Single();
            Assert.AreEqual(2, ((Ping)rec.PayloadBox).Value);

            MessagesInstrumentation.SetCapacity(1);
            Assert.AreEqual(16, MessagesInstrumentation.Capacity); // clamped to the minimum
        }

        [Test]
        public void Publish_CapturesSubscriberSnapshot_FrozenAgainstLaterUnsubscribe()
        {
            var a = EventBus.Subscribe<Ping>((ref Ping p) => { });
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            EventBus.Publish(new Ping { Value = 1 });

            // Mutate the live bus after the publish was recorded.
            a.Dispose();

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping));

            // The snapshot is frozen at publish time: still two subscribers,
            // even though the live bus now has one.
            Assert.IsNotNull(rec.Subscribers);
            Assert.AreEqual(2, rec.Subscribers.Length);
            Assert.AreEqual(1, EventBus.GetSubscriberCount<Ping>());
        }

        [Test]
        public void Publish_WithNoSubscribers_CapturesEmptySnapshot()
        {
            EventBus.Publish(new Ping { Value = 1 });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping));

            Assert.IsNotNull(rec.Subscribers);
            Assert.AreEqual(0, rec.Subscribers.Length);
        }

        [Test]
        public void Enqueue_CapturesSubscriberSnapshot()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            EventBus.Enqueue(new Ping { Value = 1 });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Enqueue && r.MessageType == typeof(Ping));

            Assert.IsNotNull(rec.Subscribers);
            Assert.AreEqual(1, rec.Subscribers.Length);
        }

        [Test]
        public void EnumerateSubscriptions_ListsActiveHandlers()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            var subs = EventBus.Bus.EnumerateSubscriptions()
                .First(s => s.MessageType == typeof(Ping));

            int n = subs.Entries.Count();
            Assert.AreEqual(2, n);
        }
    }
}
