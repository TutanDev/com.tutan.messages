using System.Collections.Generic;
using NUnit.Framework;
using Tutan.Messages;
using GCConstraint = UnityEngine.TestTools.Constraints.Is;

namespace Tutan.Messages.Tests
{
    // ── RelaxedConstraintTests ───────────────────────────────────────────
    //
    // Since 1.3.0 messages only need `where T : struct` (was `unmanaged`), so a
    // message may carry reference-type fields. These tests prove such messages
    // compile and round-trip through both the immediate and deferred paths, and
    // that dispatch of a plain value-type message stays allocation-free.

    public class RelaxedConstraintTests
    {
        // Reference-type fields — would not have compiled under the old
        // `unmanaged` constraint.
        struct LogLine : IEvent { public string Text; public int Level; }
        struct BatchUpdate : IEvent { public List<int> Ids; }

        // Plain value-type message for the allocation assertion.
        struct Tick : IEvent { public int Frame; }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void Publish_MessageWithStringField_DeliversPayloadIntact()
        {
            string received = null;
            int level = -1;
            EventBus.Subscribe<LogLine>((ref LogLine m) => { received = m.Text; level = m.Level; });

            EventBus.Publish(new LogLine { Text = "hello", Level = 3 });

            Assert.AreEqual("hello", received);
            Assert.AreEqual(3, level);
        }

        [Test]
        public void EnqueueDrain_MessageWithCollectionField_PassesSameReference()
        {
            var payload = new List<int> { 1, 2, 3 };
            List<int> received = null;
            EventBus.Subscribe<BatchUpdate>((ref BatchUpdate m) => received = m.Ids);

            EventBus.Enqueue(new BatchUpdate { Ids = payload });
            Assert.IsNull(received); // deferred — not yet delivered

            EventBus.DrainQueues();

            // The struct copy is shallow, so the handler sees the very same list.
            Assert.AreSame(payload, received);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
        }

        [Test]
        public void Publish_ValueTypeMessage_DoesNotAllocate()
        {
            int sum = 0;
            EventBus.Subscribe<Tick>((ref Tick m) => sum += m.Frame);

            // Warm up: first publish JITs the generic instantiation and may grow
            // the subscriber list. Exclude that from the measurement.
            var warm = new Tick { Frame = 1 };
            EventBus.Publish(ref warm);

            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var m = new Tick { Frame = i };
                    EventBus.Publish(ref m);
                }
            }, GCConstraint.Not.AllocatingGCMemory());

            // Guard against the loop being optimized away.
            Assert.Greater(sum, 0);
        }
    }
}
