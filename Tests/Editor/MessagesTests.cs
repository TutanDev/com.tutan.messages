using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tutan.Messages;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tutan.Messages.Tests
{
    // ── CommandBusTests ──────────────────────────────────────────────────

    public class CommandBusTests
    {
        struct MovePlayer : ICommand { public int Value; }
        struct PlaceOrder : ICommand { public int Value; }

        [SetUp]    public void SetUp()    => CommandBus.Reset();
        [TearDown] public void TearDown() => CommandBus.Reset();

        [Test]
        public void Install_ThenPublish_DeliversMessageToHandler()
        {
            int received = -1;
            var result = CommandBus.Install(
                r => r.Handle<MovePlayer>((ref MovePlayer m) => received = m.Value));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(1, result.HandlerCount);
            CommandBus.Publish(new MovePlayer { Value = 42 });

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Install_ThenEnqueueDrain_DeliversMessage()
        {
            int received = -1;
            CommandBus.Install(
                r => r.Handle<MovePlayer>((ref MovePlayer m) => received = m.Value));

            CommandBus.Enqueue(new MovePlayer { Value = 99 });
            Assert.AreEqual(-1, received); // deferred — not yet delivered

            CommandBus.DrainQueues();
            Assert.AreEqual(99, received);
        }

        [Test]
        public void DuplicateHandler_FailsInstall_NoThrow()
        {
            // Two handlers for the same command type — reported, not thrown.
            var result = CommandBus.Install(r => r
                .Handle<PlaceOrder>((ref PlaceOrder m) => { })
                .Handle<PlaceOrder>((ref PlaceOrder m) => { }));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, result.HandlerCount);
            StringAssert.Contains(nameof(PlaceOrder), result.Error);
            // Atomic: a failed install leaves the live bus untouched.
            Assert.AreEqual(0, CommandBus.GetSubscriberCount<PlaceOrder>());
        }

        [Test]
        public void NullHandler_FailsInstall()
        {
            var result = CommandBus.Install(
                r => r.Handle<MovePlayer>(null));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains(nameof(MovePlayer), result.Error);
            Assert.AreEqual(0, CommandBus.GetSubscriberCount<MovePlayer>());
        }

        [Test]
        public void Reinstall_ReplacesPreviousHandlers()
        {
            int first = 0, second = 0;
            CommandBus.Install(r => r.Handle<MovePlayer>((ref MovePlayer m) => first++));
            CommandBus.Install(r => r.Handle<MovePlayer>((ref MovePlayer m) => second++));

            CommandBus.Publish(new MovePlayer { Value = 1 });

            Assert.AreEqual(0, first);  // the first handler was replaced
            Assert.AreEqual(1, second);
        }

        [Test]
        public void Reset_RemovesHandlers()
        {
            int callCount = 0;
            CommandBus.Install(r => r.Handle<MovePlayer>((ref MovePlayer m) => callCount++));

            CommandBus.Reset();
            CommandBus.Publish(new MovePlayer { Value = 1 });

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Install_DiscardsCommandsQueuedBeforeIt_AndWarns()
        {
            // Install swaps in a fresh bus, so a command enqueued before it — even
            // before the first install — is dropped. The editor says so.
            CommandBus.Enqueue(new MovePlayer { Value = 7 });

            int received = -1;
            LogAssert.Expect(LogType.Warning,
                new Regex(@"CommandBus\.Install discarded queued commands.*\(MovePlayer\)"));
            CommandBus.Install(r => r.Handle<MovePlayer>((ref MovePlayer m) => received = m.Value));
            CommandBus.DrainQueues();

            Assert.AreEqual(-1, received);
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
            Subscription subA = default;
            subA = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) =>
            {
                subA.Dispose();
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

    public class MessagesConcurrencyTests
    {
        struct ConcurrentMsg : IEvent { public int Value; }

        // Fresh channel types for the first-use race below.
        struct SharedA : IEvent { }
        struct SharedB : IEvent { }
        struct WorkerOnlyC : IEvent { }
        struct WorkerOnlyD : IEvent { }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void Enqueue_FromWorkerThread_WhileMainDrains_DeliversAll()
        {
            // A worker streams into an existing channel while the main thread keeps
            // draining it: every message arrives exactly once.
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

        [Test]
        public void NewChannels_CreatedByWorkerAndMainAtOnce_LoseNothing()
        {
            // Regression: pre-0.3.0, channel storage was a plain Dictionary, which a
            // worker-thread Enqueue could grow while a main-thread call used it. Here
            // the worker's first Enqueue of each type races the main thread creating
            // the same channels (Subscribe) and rebuilding its drain list
            // (DrainQueues). Every interleaving has one correct outcome, so the
            // assertions hold however the threads are scheduled.
            const int rounds = 200;

            for (int round = 0; round < rounds; round++)
            {
                EventBus.Reset();
                int a = 0, b = 0;

                using var barrier = new Barrier(2);
                var worker = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    EventBus.Enqueue(new WorkerOnlyC());
                    EventBus.Enqueue(new SharedA());
                    EventBus.Enqueue(new WorkerOnlyD());
                    EventBus.Enqueue(new SharedB());
                });

                barrier.SignalAndWait();
                // Both handlers exist before the first drain, so no message can be
                // drained before its handler is subscribed.
                EventBus.Subscribe<SharedB>((ref SharedB m) => b++);
                EventBus.Subscribe<SharedA>((ref SharedA m) => a++);
                while (!worker.IsCompleted)
                    EventBus.DrainQueues();
                worker.Wait();
                EventBus.DrainQueues(); // final flush

                Assert.AreEqual(4, EventBus.ChannelCount, $"Round {round}: a channel was lost or duplicated.");
                Assert.AreEqual(1, a, $"Round {round}: SharedA was lost or delivered twice.");
                Assert.AreEqual(1, b, $"Round {round}: SharedB was lost or delivered twice.");
            }
        }

        [Test]
        public void Enqueue_FirstUseOfChannel_FromManyThreadsAtOnce_DropsNoMessages()
        {
            // Regression: pre-0.14.0, the per-channel queue was lazily created with
            // a non-atomic `??=`. Threads racing on the FIRST Enqueue of a type
            // could each create a queue, losing the loser's message. The race only
            // exists at channel-queue birth, so reset and re-race many times.
            const int threads = 4;
            const int iterations = 200;

            for (int iter = 0; iter < iterations; iter++)
            {
                EventBus.Reset();
                int received = 0;
                EventBus.Subscribe<ConcurrentMsg>((ref ConcurrentMsg m) => received++);

                using var barrier = new Barrier(threads);
                var tasks = new Task[threads];
                for (int t = 0; t < threads; t++)
                {
                    tasks[t] = Task.Run(() =>
                    {
                        barrier.SignalAndWait();
                        EventBus.Enqueue(new ConcurrentMsg { Value = 1 });
                    });
                }

                Task.WaitAll(tasks);
                EventBus.DrainQueues();

                Assert.AreEqual(threads, received, $"Iteration {iter}: a first-enqueue message was lost.");
            }
        }
    }

}
