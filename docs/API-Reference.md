[Home](index) · [Why](Messages) · **API Reference** · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# API Reference

All methods are exposed on the static `EventBus` and `CommandBus` types (and
on the underlying `Messages` instance they wrap). Signatures are identical
across both buses; the only behavioural difference is that `CommandBus`
enforces a single subscriber per message type.

## Subscribe

```csharp
SubscriptionToken Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, IMessage
```

Registers `handler` for messages of type `T`. Returns a token for later
unsubscription. Main thread only.

## Unsubscribe

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

## Lifecycle

```csharp
void Reset()    // Clears all subscriptions and queues. Use on scene transitions or test teardown.
```

---

See [Threading](Threading) for the full main-thread vs thread-safe contract,
and [Edge Cases](EdgeCases) for behaviour during reentrant dispatch.
