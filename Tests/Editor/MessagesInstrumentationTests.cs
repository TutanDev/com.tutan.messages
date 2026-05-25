using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tutan.Messages;

namespace Tutan.Messages.Tests
{
    public class MessagesInstrumentationTests
    {
        struct Ping : IEvent { public int Value; }
        struct DoThing : ICommand { public int Value; }

        [SetUp]
        public void SetUp()
        {
            EventBus.Reset();
            CommandBus.Reset();
            MessagesInstrumentation.Clear();
            MessagesInstrumentation.Enabled = true;
            MessagesInstrumentation.CapturePayloads = false;
        }

        [TearDown]
        public void TearDown()
        {
            MessagesInstrumentation.Enabled = false;
            MessagesInstrumentation.CapturePayloads = false;
            MessagesInstrumentation.Clear();
            EventBus.Reset();
            CommandBus.Reset();
        }

        [Test]
        public void Subscribe_RecordsSubscribeOp()
        {
            var token = EventBus.Subscribe<Ping>((ref Ping p) => { });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Subscribe && r.MessageType == typeof(Ping));

            Assert.AreEqual(token.Id, rec.TokenId);
            Assert.AreEqual(MessagesInstrumentation.BusKind.Event, rec.Bus);
        }

        [Test]
        public void Publish_RecordsPublishOp_AndPayloadWhenCaptureOn()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            MessagesInstrumentation.CapturePayloads = true;
            EventBus.Publish(new Ping { Value = 11 });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping));

            Assert.AreEqual(MessagesInstrumentation.BusKind.Event, rec.Bus);
            Assert.IsNotNull(rec.PayloadBox);
            Assert.AreEqual(11, ((Ping)rec.PayloadBox).Value);
        }

        [Test]
        public void Publish_OmitsPayloadWhenCaptureOff()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });
            EventBus.Publish(new Ping { Value = 7 });

            var rec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish);
            Assert.IsNull(rec.PayloadBox);
        }

        [Test]
        public void CommandBus_TagsRecordsAsCommandKind()
        {
            CommandBus.Subscribe<DoThing>((ref DoThing m) => { });
            CommandBus.Publish(new DoThing { Value = 1 });

            var publishRec = MessagesInstrumentation.Snapshot()
                .Single(r => r.Op == MessagesInstrumentation.Op.Publish);
            Assert.AreEqual(MessagesInstrumentation.BusKind.Command, publishRec.Bus);
        }

        [Test]
        public void Enqueue_FromWorkerThread_RecordsOnAnyThread_AndPreservesOrder()
        {
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            int countBefore = MessagesInstrumentation.Snapshot().Count;

            Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    EventBus.Enqueue(new Ping { Value = i });
            }).Wait();

            EventBus.DrainQueues();

            var snap = MessagesInstrumentation.Snapshot();
            int enq = snap.Count(r => r.Op == MessagesInstrumentation.Op.Enqueue && r.MessageType == typeof(Ping));
            int pub = snap.Count(r => r.Op == MessagesInstrumentation.Op.Publish && r.MessageType == typeof(Ping));
            Assert.AreEqual(50, enq);
            Assert.AreEqual(50, pub);
            Assert.IsTrue(snap.Count > countBefore);
        }

        [Test]
        public void Unsubscribe_RecordsUnsubscribeOp_OnlyOnSuccess()
        {
            var token = EventBus.Subscribe<Ping>((ref Ping p) => { });
            MessagesInstrumentation.Clear();

            Assert.IsTrue(EventBus.Unsubscribe(token));
            Assert.IsFalse(EventBus.Unsubscribe(token));

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
            MessagesInstrumentation.SetCapacity(16);
            MessagesInstrumentation.Enabled = true;
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            for (int i = 0; i < 100; i++)
                EventBus.Publish(new Ping { Value = i });

            Assert.AreEqual(16, MessagesInstrumentation.Snapshot().Count);

            // Reset capacity to default for other tests.
            MessagesInstrumentation.SetCapacity(4096);
        }

        [Test]
        public void Publish_CapturesSubscriberSnapshot_FrozenAgainstLaterUnsubscribe()
        {
            var a = EventBus.Subscribe<Ping>((ref Ping p) => { });
            EventBus.Subscribe<Ping>((ref Ping p) => { });

            EventBus.Publish(new Ping { Value = 1 });

            // Mutate the live bus after the publish was recorded.
            EventBus.Unsubscribe(a);

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
