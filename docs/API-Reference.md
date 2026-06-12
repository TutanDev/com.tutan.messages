[Home](index) · [Why](Messages) · **API Reference** · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# API Reference

The dispatch methods — `Publish`, `Enqueue`, `DrainQueues` — are identical
across `EventBus` and `CommandBus` (and the underlying `MessageBus<TBase>`
instance they wrap). The buses differ only in how handlers are registered:
`EventBus` is a mutable N:M bus you subscribe to at any time (and unsubscribe
by disposing the returned `Subscription`), while `CommandBus` handlers are
declared once at the composition root via `Install` — see
[CommandBus](#commandbus).

## Subscribe (EventBus)

```csharp
Subscription Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, IMessage
```

Registers `handler` for messages of type `T`. Returns a disposable
[`Subscription`](#subscription-lifetime) — disposing it is how you
unsubscribe. Main thread only. `EventBus` only — for commands, see
[CommandBus](#commandbus).

## Publish (Immediate)

```csharp
void Publish<T>(ref T message)  where T : unmanaged, IMessage
void Publish<T>(T message)      where T : unmanaged, IMessage  // convenience, one copy
```

Dispatches `message` synchronously to all active subscribers of type `T`. The
`ref` overload is zero-copy. The value overload copies once (caller →
parameter) and is acceptable for small messages when the `ref` call site is
inconvenient.

Main thread only. If no subscribers exist for `T`, returns immediately
(dictionary lookup cost only).

## Enqueue (Deferred)

```csharp
void Enqueue<T>(in T message) where T : unmanaged, IMessage
```

Adds `message` to the internal queue for type `T`. Thread-safe via
`ConcurrentDictionary` + `ConcurrentQueue`; safe to race with main-thread
`Subscribe`/`Publish`/`DrainQueues` *and* with concurrent `Enqueue` calls from
other worker threads (channel creation goes through `GetOrAdd`; the per-channel
queue's lazy creation is a compare-and-swap). Messages are dispatched on the
next `DrainQueues()` call.

## DrainQueues

```csharp
void DrainQueues()
```

Processes all pending queued messages across all channels. Call once per
frame. Main thread only. By default `MessagesHost` does this for you — see
[Bootstrap](Bootstrap).

## Subscription Lifetime

```csharp
struct Subscription : IDisposable        // Dispose() unsubscribes; idempotent
class  SubscriptionBag : IDisposable     // Dispose() unsubscribes everything Added
Subscription AddTo(this Subscription, SubscriptionBag bag)
Subscription AddTo(this Subscription, GameObject gameObject)  // dies with the GameObject
Subscription AddTo(this Subscription, Component component)    // dies with its GameObject
```

`Subscribe` returns a `Subscription` — a disposable struct pairing the
subscription's identity with the bus instance that issued it. Disposing it
unsubscribes. Three ways to manage that:

- **Hold it and dispose explicitly** — for dynamic lifetimes (e.g. a view that
  subscribes in `OnEnable` and unsubscribes in `OnDisable`).
- **Collect several in a `SubscriptionBag`** — one `Dispose()` releases the
  group; the bag is reusable afterward.
- **Tie it to a GameObject** with `.AddTo(this)` — disposal happens in
  `OnDestroy` via one hidden `SubscriptionAnchor` component per GameObject, so
  the common MonoBehaviour pattern collapses to:

```csharp
void Awake()
{
    EventBus.Subscribe<PlayerMoved>(OnMoved).AddTo(this);
}
```

No handle field, no `OnDestroy`. Details and trade-offs in
[Examples](Examples#subscription-lifetimes). Notes:

- `Subscription` is a struct around an id plus an existing reference —
  `Subscribe` allocates nothing beyond the handler entry itself. The bag and
  the anchor component each allocate once at creation.
- Because the handle captures the bus *instance*, disposing a `Subscription`
  taken before a `Reset()` is a no-op — it can never remove an unrelated
  subscription on the replacement bus.
- `Dispose` is main thread only, safe to call during dispatch (the handler is
  marked inactive and skipped for the remainder of the current dispatch
  cycle), and idempotent.

## Diagnostics

```csharp
int GetSubscriberCount<T>() where T : unmanaged, IMessage
int ChannelCount { get; }
```

## CommandBus

`CommandBus` shares `Publish`, `Enqueue`, `DrainQueues`, `Reset`,
`GetSubscriberCount<T>`, and `ChannelCount` with `EventBus`. It has **no**
`Subscribe`. The single handler for each command type is declared
once at the composition root:

```csharp
InstallResult Install(Action<CommandRegistry> configure)
```

On success the new bindings are swapped in atomically. On a duplicate command
type or a null handler the live bus is left untouched and the failure is
reported in the result — never as an exception. Calling it again rebuilds the
bus from scratch (composition-root semantics).

```csharp
readonly struct InstallResult
{
    bool   Ok           // true when the bindings were validated and swapped in
    string Error        // names the offending command type(s); null when Ok
    int    HandlerCount // number of handlers bound; 0 on failure
}
```

Inside the callback, bind each command with the `CommandRegistry` builder:

```csharp
CommandRegistry Handle<T>(MessageHandler<T> handler) where T : unmanaged, ICommand
```

```csharp
var result = CommandBus.Install(r => r
    .Handle<PlaceOrder>(orderHandler.Handle)
    .Handle<MovePlayer>(movement.Handle));

if (!result.Ok) Debug.LogError(result.Error); // names the offending command type(s)
```

For multi-app builds that vary by feature set or backend, express that variation at the
composition root: select which handlers to bind and which backend to inject into them,
then funnel them all through one `Install`.

## Lifecycle

```csharp
void Reset()    // Clears all subscriptions and queues. Use on test teardown.
```

---

See [Threading](Threading) for the full main-thread vs thread-safe contract,
and [Edge Cases](EdgeCases) for behaviour during reentrant dispatch.
