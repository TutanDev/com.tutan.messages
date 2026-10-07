using System.Collections.Generic;
using NUnit.Framework;
using Tutan.Messages;
using UnityEngine.TestTools.Constraints;
using GCConstraint = UnityEngine.TestTools.Constraints.Is;

namespace Tutan.Messages.Tests
{
    // ── QueueDrainTests ──────────────────────────────────────────────────
    //
    // DrainQueues is bounded to the backlog present when it starts, so a handler
    // that keeps enqueuing cannot hang the frame, and a steady queued stream
    // reuses the queue's storage instead of allocating every frame. Each
    // DrainQueues call below stands in for one frame of the auto-host.

    public class QueueDrainTests
    {
        struct Ping : IEvent { public int Value; }

        // 16 bytes: a typical small message for the allocation tests.
        struct Sample : IEvent { public long A, B; }

        bool _prevInstrumentation;

        [SetUp]
        public void SetUp()
        {
            // An open Messages Console enables instrumentation, which boxes every
            // enqueue and publish; the allocation tests must measure the bus alone.
            _prevInstrumentation = MessagesInstrumentation.Enabled;
            MessagesInstrumentation.Enabled = false;
            EventBus.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            MessagesInstrumentation.Enabled = _prevInstrumentation;
            EventBus.Reset();
        }

        [Test]
        public void SelfEnqueueingHandler_IsDrainedOncePerDrainQueuesCall()
        {
            // A drain that loops until the queue is empty would never return here.
            // The cap turns that hang into a failed assertion.
            const int runawayCap = 1000;
            int dispatched = 0;
            EventBus.Subscribe<Ping>((ref Ping m) =>
            {
                dispatched++;
                if (dispatched < runawayCap)
                    EventBus.Enqueue(new Ping());
            });

            for (int i = 0; i < 5; i++)
                EventBus.Enqueue(new Ping());

            // Each call dispatches only the backlog it started with; the copies the
            // handler re-enqueues wait for the next call.
            for (int call = 1; call <= 4; call++)
            {
                EventBus.DrainQueues();
                Assert.AreEqual(5 * call, dispatched, $"After DrainQueues call {call}");
            }
        }

        [Test]
        public void ReentrantDrainQueues_FromHandler_DeliversEveryMessageOnce()
        {
            var seen = new List<int>();
            EventBus.Subscribe<Ping>((ref Ping m) =>
            {
                seen.Add(m.Value);
                if (m.Value == 0)
                {
                    EventBus.Enqueue(new Ping { Value = 100 });
                    EventBus.DrainQueues();
                }
            });

            for (int i = 0; i < 5; i++)
                EventBus.Enqueue(new Ping { Value = i });

            EventBus.DrainQueues();
            EventBus.DrainQueues(); // whatever the nested drain deferred

            // How the work splits between the outer and the nested drain is an
            // implementation detail; both must return, losing and repeating nothing.
            seen.Sort();
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 100 }, seen);
        }

        [Test]
        public void EnqueueDrain_SteadyStream_DoesNotAllocateAfterWarmUp()
        {
            long sum = 0;
            EventBus.Subscribe<Sample>((ref Sample m) => sum += m.A);

            // Warm-up: JIT the generic paths and let the queue grow its storage past
            // the per-frame volume, as an early backlog would.
            for (int i = 0; i < 2048; i++)
                EventBus.Enqueue(new Sample { A = 1 });
            EventBus.DrainQueues();
            for (int frame = 0; frame < 10; frame++)
                EnqueueAndDrainFrame(100);

            // Reading ConcurrentQueue.Count for the drain budget froze the queue's
            // segments once it held more than two, so every later frame allocated
            // fresh ones.
            Assert.That(() => EnqueueAndDrainFrame(100), GCConstraint.Not.AllocatingGCMemory());
            Assert.Greater(sum, 0L);
        }

        [Test]
        public void EnqueueDrain_AfterBurst_SettlesWithoutAllocating()
        {
            long sum = 0;
            EventBus.Subscribe<Sample>((ref Sample m) => sum += m.A);

            EnqueueAndDrainFrame(1);   // JIT warm-up
            EnqueueAndDrainFrame(200); // burst: the queue grows new segments
            for (int frame = 0; frame < 5; frame++)
                EnqueueAndDrainFrame(40);

            Assert.That(() => EnqueueAndDrainFrame(40), GCConstraint.Not.AllocatingGCMemory());
            Assert.Greater(sum, 0L);
        }

        // One simulated frame: a batch of enqueues, then the host's drain.
        static void EnqueueAndDrainFrame(int count)
        {
            for (int i = 0; i < count; i++)
                EventBus.Enqueue(new Sample { A = 1, B = i });
            EventBus.DrainQueues();
        }
    }
}
