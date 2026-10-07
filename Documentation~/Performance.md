[Home](index.md) · [Why](Messages.md) · [API Reference](API-Reference.md) · [Examples](Examples.md) · [Threading](Threading.md) · **Performance** · [Edge Cases](EdgeCases.md) · [Architecture](Architecture.md) · [Bootstrap](Bootstrap.md) · [Editor](Editor.md)

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

These figures describe the bus itself. In the editor, while the Messages Console
is open (or whenever `MessagesInstrumentation.Enabled` is true), each recorded
`Publish`/`Enqueue` also allocates its record's boxed payload and subscriber
snapshot — see [Editor Tooling](Editor.md#messages-console). Profile with the
window closed.

Dispatch is allocation-free for any message `struct` — the bus is generic over
the message type and passes it by `ref`, so the message is never boxed. A
message that carries reference-type fields (`string`, arrays, collections, class
payloads) still dispatches without allocating, but while it sits in the deferred
queue it is reachable from the bus, so the collector must trace its reference
fields during the mark phase, proportional to the queued backlog. This is a scan
cost, not an allocation.
Prefer value-only fields on hot paths to keep the GC entirely out of the loop.

## Pre-warming

To remove first-use allocation (channel creation, the queue's lazy creation),
pre-warm channels at startup:

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

Pre-warming does not cover everything:

- **Subscriber lists grow.** A type's subscriber list starts with room for 8
  entries, so a 9th entry (and every doubling after it) reallocates the list.
  Disposed entries keep their slot until the list is compacted.
- **Queues grow with the backlog.** The queue adds segments whenever the
  backlog exceeds anything seen before. To pre-size it, enqueue about twice
  your expected peak backlog once at startup, then drain: segments double in
  size, so this leaves one large enough to hold the peak.
- **Handler delegates are allocated at the call site.** Converting a method
  group or a capturing lambda to `MessageHandler<T>` allocates a delegate every
  time the conversion runs — subscribe at startup, not per frame.
