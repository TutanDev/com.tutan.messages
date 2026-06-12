using NUnit.Framework;
using Tutan.Messages;
using UnityEngine;

namespace Tutan.Messages.Tests
{
    // ── SubscriptionTests ────────────────────────────────────────────────

    public class SubscriptionTests
    {
        struct PlayerMoved : IEvent { }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void Subscribe_DeliversUntilDisposed()
        {
            int callCount = 0;
            var subscription = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++);

            Assert.IsTrue(subscription.IsActive);
            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(1, callCount);

            subscription.Dispose();

            Assert.IsFalse(subscription.IsActive);
            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(0, EventBus.GetSubscriberCount<PlayerMoved>());
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var subscription = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { });

            subscription.Dispose();
            subscription.Dispose(); // second call is a no-op, not an error

            Assert.AreEqual(0, EventBus.GetSubscriberCount<PlayerMoved>());
        }

        [Test]
        public void DisposingACopy_UnsubscribesOnce()
        {
            int callCount = 0;
            var original = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++);
            var copy = original;

            copy.Dispose();
            original.Dispose(); // token already removed — harmless

            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Dispose_AfterBusReset_IsNoOp_AndDoesNotTouchNewSubscriptions()
        {
            // The handle captures the bus *instance*, so disposing a pre-Reset
            // subscription must not remove a post-Reset subscription that happens
            // to reuse the same token id on the replacement bus.
            var stale = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { });

            EventBus.Reset();

            int callCount = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++);
            stale.Dispose();

            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(1, callCount);
        }
    }

    // ── SubscriptionBagTests ─────────────────────────────────────────────

    public class SubscriptionBagTests
    {
        struct PlayerMoved : IEvent { }
        struct OrderPlaced : IEvent { }

        [SetUp]    public void SetUp()    => EventBus.Reset();
        [TearDown] public void TearDown() => EventBus.Reset();

        [Test]
        public void Dispose_UnsubscribesEverything()
        {
            int a = 0, b = 0;
            var bag = new SubscriptionBag();
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => a++).AddTo(bag);
            EventBus.Subscribe<OrderPlaced>((ref OrderPlaced m) => b++).AddTo(bag);
            Assert.AreEqual(2, bag.Count);

            bag.Dispose();

            EventBus.Publish(new PlayerMoved());
            EventBus.Publish(new OrderPlaced());
            Assert.AreEqual(0, a);
            Assert.AreEqual(0, b);
            Assert.AreEqual(0, bag.Count);
        }

        [Test]
        public void Bag_IsReusableAfterDispose()
        {
            var bag = new SubscriptionBag();
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { }).AddTo(bag);
            bag.Dispose();

            int callCount = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++).AddTo(bag);
            Assert.AreEqual(1, bag.Count);

            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(1, callCount);

            bag.Dispose();
            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void AddTo_ReturnsTheSameSubscription_ForFluentChaining()
        {
            var bag = new SubscriptionBag();
            var subscription = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { });

            var returned = subscription.AddTo(bag);

            Assert.AreEqual(subscription.Token, returned.Token);
            Assert.IsTrue(returned.IsActive);
        }

        [Test]
        public void Add_IgnoresInactiveSubscriptions()
        {
            var bag = new SubscriptionBag();
            var subscription = EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { });
            subscription.Dispose();

            bag.Add(subscription);

            Assert.AreEqual(0, bag.Count);
        }
    }

    // ── SubscriptionAnchorTests ──────────────────────────────────────────

    public class SubscriptionAnchorTests
    {
        struct PlayerMoved : IEvent { }

        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            EventBus.Reset();
            _go = new GameObject("SubscriptionAnchorTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            EventBus.Reset();
        }

        [Test]
        public void AddToGameObject_AttachesOneAnchor_AndReusesIt()
        {
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { }).AddTo(_go);
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { }).AddTo(_go);

            var anchors = _go.GetComponents<SubscriptionAnchor>();
            Assert.AreEqual(1, anchors.Length);
            Assert.AreEqual(2, anchors[0].Bag.Count);
        }

        [Test]
        public void AnchorBagDispose_UnsubscribesItsSubscriptions()
        {
            // OnDestroy → Bag.Dispose() only fires in play mode; in this edit-mode
            // test we drive the bag directly and verify the disposal path itself.
            int callCount = 0;
            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => callCount++).AddTo(_go);

            _go.GetComponent<SubscriptionAnchor>().Bag.Dispose();

            EventBus.Publish(new PlayerMoved());
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void AddToComponent_AnchorsToItsGameObject()
        {
            var component = _go.AddComponent<BoxCollider>();

            EventBus.Subscribe<PlayerMoved>((ref PlayerMoved m) => { }).AddTo(component);

            Assert.IsNotNull(_go.GetComponent<SubscriptionAnchor>());
        }
    }
}
