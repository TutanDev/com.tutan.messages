[Home](index.md) · [Why](Messages.md) · [API Reference](API-Reference.md) · [Examples](Examples.md) · [Threading](Threading.md) · [Performance](Performance.md) · **Edge Cases** · [Architecture](Architecture.md) · [Bootstrap](Bootstrap.md) · [Editor](Editor.md)

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
inactive immediately, and its delegate reference is set to `null` so the
handler (and anything its closure captures) can be collected. The inactive
entry is skipped for the remainder of the current dispatch. Compaction occurs
after dispatch completes.

## Enqueue During Drain

`DrainQueue` is bounded by the number of messages pending when the drain
started. A handler that enqueues a message of the *same* type during dispatch
therefore extends the **next** frame's drain, not the current one — a
self-perpetuating handler (one that enqueues on every receipt) carries the same
backlog from frame to frame, drained once per frame, instead of hanging the
frame in an infinite drain loop. A handler that calls `DrainQueues` itself can
pull some of those new messages into the current frame; the work per frame
stays bounded.

## Handler Exceptions

Exceptions in a handler are caught and logged via `Debug.LogException`.
Dispatch continues to the next handler. A broken handler must never cascade
into a broken frame.

## No Channel

`Publish<T>` returns immediately if no channel exists for `T` — nothing has
subscribed to or enqueued `T` since the last reset. Cost: one lock-free
`ConcurrentDictionary.TryGetValue` call.

A channel outlives its subscribers: once every subscription to `T` is disposed
(or after `T` was only ever enqueued), `Publish<T>` still finds the channel and
walks its entry list, skipping inactive entries, inside the `Messages.Publish`
profiler marker. It stays allocation-free.

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
  (`Awake` of a bootstrap object is the usual place for subscriptions; see
  below for when to install commands).

## Queued Commands and `CommandBus.Install`

A successful `CommandBus.Install`, like `Reset`, replaces the bus, so commands
still queued for the next drain are discarded rather than dispatched — including
on the first install. A command enqueued from another object's `Awake` (whose
order relative to your installer's `Awake` is undefined) or from a worker
thread that started early is lost. The editor and development builds log a
warning naming the discarded types. Install before anything enqueues: from a
`BeforeSceneLoad` `[RuntimeInitializeOnLoadMethod]`, or from a bootstrap object
with an early Script Execution Order. Before a deliberate re-install, call
`CommandBus.DrainQueues()` first if the queued commands matter. Do not install
from inside a command handler: the drain in progress still delivers the rest of
that command type's backlog to the replaced handler, and only the other types'
queues are discarded.

## Unbound Commands

`Install` rejects a second handler for a command type but does not require one.
Publishing or enqueuing a command that has no handler is a silent no-op. For
commands that must be handled, assert `CommandBus.GetSubscriberCount<T>() == 1`
once after `Install`.

## Subscriptions After `Reset()`

The static `EventBus.Reset()` / `CommandBus.Reset()` / `CommandBus.Install`
replace the bus instance. A `Subscription` taken before the swap targets the
discarded bus, so disposing it is a harmless no-op — it can never remove a
subscription that was made on the replacement bus.
