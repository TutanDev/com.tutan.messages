[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · **Performance** · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Performance Characteristics

| Operation       | Allocation | Complexity        | Notes                                 |
|-----------------|------------|-------------------|---------------------------------------|
| `Publish`       | Zero       | O(n) subscribers  | `ref` dispatch, no delegate combine   |
| `Enqueue`       | Amortized  | O(1)              | Queue grows; pre-warm to avoid        |
| `Subscribe`     | Amortized  | O(1)              | List.Add; one-time per subscription   |
| `Subscription.Dispose` | Zero | O(n) subscribers | Linear scan by internal id           |
| `DrainQueues`   | Zero       | O(channels × msgs)| Iterates a cached channel list; rebuilt only when a new channel type appears |
| Channel lookup  | Zero       | O(1)              | Lock-free `ConcurrentDictionary<Type, ChannelBase>` read |

"Amortized" means the backing collection may grow: the subscriber `List<T>`
on `Subscribe`, and the per-type `ConcurrentQueue<T>` (which grows by adding
segments) when the queued backlog exceeds anything seen before. A steady backlog
reuses the existing segment, so this settles after warm-up. The first
`Subscribe`/`Enqueue` of a message type also allocates that type's channel.

Dispatch is allocation-free for any message `struct` — the bus is generic over
the message type and passes it by `ref`, so the message is never boxed. A
message that carries reference-type fields (`string`, arrays, collections, class
payloads) still dispatches without allocating, but while it sits in the deferred
queue it is a GC root: the collector must scan it during the mark phase,
proportional to the queued backlog. This is a scan cost, not an allocation.
Prefer value-only fields on hot paths to keep the GC entirely out of the loop.

## Pre-warming

To eliminate all runtime allocation, pre-warm channels at startup:

```csharp
void Awake()
{
    // Force channel creation with a dummy subscribe/dispose.
    // The channel's internal List is allocated once. The per-channel
    // ConcurrentQueue is lazy — it is allocated on the first Enqueue call,
    // so pre-warm that separately if your app uses queued dispatch.
    EventBus.Subscribe<HandGesture>((ref HandGesture _) => { }).Dispose();
    EventBus.Subscribe<FrameDecoded>((ref FrameDecoded _) => { }).Dispose();
    // ... repeat for all message types used in the application

    // Queued types: one Enqueue creates the channel's ConcurrentQueue. Do it
    // before anything subscribes (the drain then dispatches to nobody), and note
    // that DrainQueues also flushes anything else already queued.
    EventBus.Enqueue(default(FrameDecoded));
    EventBus.DrainQueues();
}
```

The `Subscribe(...).Dispose()` lambdas above allocate their delegates once, at
warm-up — that is the point of doing it in `Awake`.
