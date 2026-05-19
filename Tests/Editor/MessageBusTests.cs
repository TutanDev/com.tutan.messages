using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tutan.MessageBus;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tutan.MessageBus.Tests
{
    // ── CommandBusTests ──────────────────────────────────────────────────

    public class CommandBusTests
    {
        struct MovePlayer : ICommand { public int Value; }
        struct PlaceOrder : ICommand { public int Value; }

        [SetUp]    public void SetUp()    => CommandBus.Reset();
        [TearDown] public void TearDown() => CommandBus.Reset();

        [Test]
        public void Publish_DeliversMessageToSubscriber()
        {
            int received = -1;
            CommandBus.Subscribe<MovePlayer>((ref MovePlayer m) => received = m.Value);

            CommandBus.Publish(new MovePlayer { Value = 42 });

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Unsubscribe_PreventsFutureDelivery()
        {
            int callCount = 0;
            var token = CommandBus.Subscribe<MovePlayer>((ref MovePlayer m) => callCount++);

            CommandBus.Unsubscribe(token);
            CommandBus.Publish(new MovePlayer { Value = 1 });

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Enqueue_ThenDrainQueues_DeliversMessage()
        {
            int received = -1;
            CommandBus.Subscribe<MovePlayer>((ref MovePlayer m) => received = m.Value);

            CommandBus.Enqueue(new MovePlayer { Value = 99 });
            Assert.AreEqual(-1, received); // deferred — not yet delivered

            CommandBus.DrainQueues();
            Assert.AreEqual(99, received);
        }

        [Test]
        public void DoubleUnsubscribe_ReturnsFalse_NoException()
        {
            var token = CommandBus.Subscribe<MovePlayer>((ref MovePlayer m) => { });

            bool first  = CommandBus.Unsubscribe(token);
            bool second = CommandBus.Unsubscribe(token);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
        }

        [Test]
        public void DuplicateSubscribe_Throws()
        {
            CommandBus.Subscribe<PlaceOrder>((ref PlaceOrder m) => { });

            Assert.Throws<InvalidOperationException>(
                () => CommandBus.Subscribe<PlaceOrder>((ref PlaceOrder m) => { }));
        }
    }

    // ── EventBusTests ─────────────────────────────────────────────────────

    public class EventBusTests
    {
        struct PlayerMoved : IEvent { public int Value; }
        struct OrderPlaced : IEvent { public int Value; }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void MultipleSubscribers_AllReceiveMessage()
        {
            int a = 0, b = 0, c = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => a++);
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => b++);
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => c++);

            EventBus.Publish(new PlayerMoved());

            Assert.AreEqual(1, a);
            Assert.AreEqual(1, b);
            Assert.AreEqual(1, c);
        }

        [Test]
        public void UnsubscribeDuringDispatch_DoesNotCorruptIteration()
        {
            int bCallCount = 0;
            SubscriptionToken tokenA = default;
            tokenA = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) =>
            {
                EventBus.Unsubscribe(tokenA);
            });
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => bCallCount++);

            EventBus.Publish(new PlayerMoved());

            Assert.AreEqual(1, bCallCount);
        }

        [Test]
        public void HandlerException_DoesNotBreakDispatchChain()
        {
            int bCallCount = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) =>
                throw new Exception("Test exception from handler"));
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => bCallCount++);

            LogAssert.Expect(LogType.Exception, new Regex("Test exception from handler"));
            EventBus.Publish(new PlayerMoved());

            Assert.AreEqual(1, bCallCount);
        }

        [Test]
        public void TypeIsolation_HandlerOnlyReceivesItsOwnMessageType()
        {
            int callCount = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++);

            EventBus.Publish(new OrderPlaced { Value = 1 });

            Assert.AreEqual(0, callCount);
        }
    }

    // ── SubscriptionTokenTests ───────────────────────────────────────────

    public class SubscriptionTokenTests
    {
        struct MsgA : IEvent { }
        struct MsgB : IEvent { }

        [Test]
        public void Equality_DistinguishesTokens_WithSameIdButDifferentMessageType()
        {
            // Construct two tokens with identical Id but different MessageType.
            // Uses the internal constructor (visible via InternalsVisibleTo) to
            // create the collision deterministically.
            var a = new SubscriptionToken(42, typeof(MsgA));
            var b = new SubscriptionToken(42, typeof(MsgB));

            Assert.AreNotEqual(a, b);
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
            Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_TreatsIdenticalTokens_AsEqual()
        {
            var a = new SubscriptionToken(7, typeof(MsgA));
            var b = new SubscriptionToken(7, typeof(MsgA));

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }
    }

    // ── ConcurrencyTests ─────────────────────────────────────────────────

    public class MessageBusConcurrencyTests
    {
        struct ConcurrentMsg : IEvent { public int Value; }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void Enqueue_FromWorkerThread_RacingWithMainSubscribe_DoesNotCorruptDictionary()
        {
            // Regression: pre-1.3, Subscribe used a lock-free path while
            // Enqueue mutated _channels under a lock. Concurrent first-time
            // use of new channel types could corrupt the Dictionary.
            // ConcurrentDictionary now makes this safe.

            const int iterations = 5000;
            int received = 0;
            EventBus.Subscribe<ConcurrentMsg>((ref ConcurrentMsg m) => Interlocked.Increment(ref received));

            var worker = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                    EventBus.Enqueue(new ConcurrentMsg { Value = i });
            });

            // Main thread keeps draining while worker enqueues.
            while (!worker.IsCompleted)
                EventBus.DrainQueues();
            EventBus.DrainQueues(); // final flush

            Assert.AreEqual(iterations, received);
        }
    }

}
