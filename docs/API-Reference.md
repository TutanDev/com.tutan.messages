[Home](index) · [Why](Messages) · **API Reference** · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# API Reference

The dispatch methods — `Publish`, `Enqueue`, `DrainQueues` — are identical
across `EventBus` and `CommandBus` (and the underlying `MessageBus<TBase>`
instance they wrap). The buses differ only in how handlers are registered: `EventBus` is a
mutable N:M bus you `Subscribe`/`Unsubscribe` at any time, while `CommandBus`
handlers are declared once at the composition root via `TryInstall` — see
[CommandBus](#commandbus).

## Subscribe (EventBus)

```csharp
SubscriptionToken Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, IMessage
```

Registers `handler` for messages of type `T`. Returns a token for later
unsubscription. Main thread only. `EventBus` only — for commands, see
[CommandBus](#commandbus).

## Unsubscribe (EventBus)

```csharp
bool Unsubscribe(SubscriptionToken token)
```

Removes the subscription identified by `token`. Returns `false` if already
unsubscribed or invalid. Safe to call during dispatch — the handler is
marked inactive and skipped for the remainder of the current dispatch cycle.

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
`Subscribe`/`Publish`/`DrainQueues`. Channel creation uses a brief lock; the
enqueue itself is lock-free (`ConcurrentQueue<T>`). Messages are dispatched
on the next `DrainQueues()` call.

## DrainQueues

```csharp
void DrainQueues()
```

Processes all pending queued messages across all channels. Call once per
frame. Main thread only. By default `MessagesHost` does this for you — see
[Bootstrap](Bootstrap).

## Diagnostics

```csharp
int GetSubscriberCount<T>() where T : unmanaged, IMessage
int ChannelCount { get; }
```

## CommandBus

`CommandBus` shares `Publish`, `Enqueue`, `DrainQueues`, `Reset`,
`GetSubscriberCount<T>`, and `ChannelCount` with `EventBus`. It has **no**
`Subscribe`/`Unsubscribe`. The single handler for each command type is declared
once at the composition root:

```csharp
bool TryInstall(out string error, Action<CommandRegistry> configure)
```

Returns `true` and atomically swaps in the new bindings on success. On a
duplicate command type or a null handler it returns `false`, sets `error` to a
message naming the offending type(s), and leaves the live bus untouched. Calling
it again rebuilds the bus from scratch (composition-root semantics).

Inside the callback, bind each command with the `CommandRegistry` builder:

```csharp
CommandRegistry Handle<T>(MessageHandler<T> handler) where T : unmanaged, ICommand
```

```csharp
bool ok = CommandBus.TryInstall(out string error, r => r
    .Handle<PlaceOrder>(orderHandler.Handle)
    .Handle<MovePlayer>(movement.Handle));

if (!ok) Debug.LogError(error); // names the offending command type(s)
```

For multi-app builds that vary by feature set or backend, express that variation at the
composition root: select which handlers to bind and which backend to inject into them,
then funnel them all through one `TryInstall`. See `decisions/CommandBus.md`.

## Lifecycle

```csharp
void Reset()    // Clears all subscriptions and queues. Use on scene transitions or test teardown.
```

---

See [Threading](Threading) for the full main-thread vs thread-safe contract,
and [Edge Cases](EdgeCases) for behaviour during reentrant dispatch.
