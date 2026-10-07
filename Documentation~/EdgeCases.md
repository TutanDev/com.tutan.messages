[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · **Edge Cases** · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Behavioral Edge Cases

## Reentrant Publish

A handler calling `Publish<T>` for the *same* message type `T` will re-enter
the dispatch loop. This is handled correctly via an integer depth counter
(`_dispatchDepth`):

- `_dispatchDepth` is incremented on entry and decremented on exit.
- Compaction runs only when `_dispatchDepth` returns to zero, ensuring list
  compaction never mutates the entry list while an outer dispatch is still
  iterating it.
- Each nested call captures its own `count` at entry, so entries appended
  during inner dispatch (past the captured count) won't fire for that level.
- Entries removed (marked inactive) during inner dispatch are skipped by both
  inner and outer iterations.

Avoid deep reentrant chains — they consume stack proportionally to depth.

## Subscribe During Dispatch

If a handler calls `Subscribe<T>` for the message type currently being
dispatched, the new handler is appended to the list. It will **not** receive
the current message (the iteration count was captured before the append). It
will receive subsequent messages.

## Unsubscribe During Dispatch

Disposing a `Subscription` from inside a handler is safe. The entry is marked
inactive immediately. The delegate reference is set to `null` to release the
GC root. The inactive entry is skipped for the remainder of the current
dispatch. Compaction occurs after dispatch completes.

## Enqueue During Drain

`DrainQueue` is bounded by the number of messages pending when the drain
started. A handler that enqueues a message of the *same* type during dispatch
therefore extends the **next** frame's drain, not the current one — a
self-perpetuating handler (one that enqueues on every receipt) degrades to one
message per frame instead of hanging the frame in an infinite drain loop.

## Handler Exceptions

Exceptions in a handler are caught and logged via `Debug.LogException`.
Dispatch continues to the next handler. A broken handler must never cascade
into a broken frame.

## Zero Subscribers

`Publish<T>` returns immediately if no channel exists for `T`. Cost: one
lock-free `ConcurrentDictionary.TryGetValue` call.

## `AddTo(gameObject)` on a Never-Activated GameObject

`AddTo(this)` / `AddTo(gameObject)` dispose the subscription from the hidden
`SubscriptionAnchor`'s `OnDestroy`. Unity only calls `OnDestroy` on components
whose GameObject has been active at least once, so a subscription anchored to a
GameObject that is created inactive and destroyed without ever being activated
(a pooled instance, a disabled prefab instance) is **not** disposed — its
handler keeps running against a destroyed object. Hold the `Subscription` (or
use a `SubscriptionBag`) for those lifetimes.

## Bus Reset on Enter Play Mode

`EventBus` and `CommandBus` reset themselves at
`RuntimeInitializeLoadType.SubsystemRegistration` so that state never leaks
between play sessions when Domain Reload is disabled. Consequences:

- Subscriptions made in edit mode (e.g. from `[InitializeOnLoad]` editor code)
  and handlers installed before entering Play mode are dropped.
- Unity does not order `SubsystemRegistration` callbacks across types, so a
  `Subscribe`/`Install` from *your own* `SubsystemRegistration` callback may run
  before or after the reset. Subscribe and install at `BeforeSceneLoad` or later
  (`Awake` of a bootstrap object is the usual place).

## Subscriptions After `Reset()`

The static `EventBus.Reset()` / `CommandBus.Reset()` / `CommandBus.Install`
replace the bus instance. A `Subscription` taken before the swap targets the
discarded bus, so disposing it is a harmless no-op — it can never remove a
subscription that was made on the replacement bus.
