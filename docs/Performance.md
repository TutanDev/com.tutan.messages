[Home](index) · [Why](MessageBus) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · **Performance** · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Performance Characteristics

| Operation       | Allocation | Complexity        | Notes                                 |
|-----------------|------------|-------------------|---------------------------------------|
| `Publish`       | Zero       | O(n) subscribers  | `ref` dispatch, no delegate combine   |
| `Enqueue`       | Amortized  | O(1)              | Queue grows; pre-warm to avoid        |
| `Subscribe`     | Amortized  | O(1)              | List.Add; one-time per subscription   |
| `Unsubscribe`   | Zero       | O(n) subscribers  | Linear scan by token ID               |
| `DrainQueues`   | Zero       | O(channels × msgs)| One dictionary iteration per frame    |
| Channel lookup  | Zero       | O(1)              | Dictionary<Type, ChannelBase>         |

"Amortized" means the backing collection (`List<T>` / `Queue<T>`) may resize.
This happens during initialization, never during sustained runtime if
pre-warmed.

## Pre-warming

To eliminate all runtime allocation, pre-warm channels at startup:

```csharp
void Awake()
{
    // Force channel creation with a dummy subscribe/unsubscribe.
    // The channel's internal List is allocated once. The per-channel
    // ConcurrentQueue is lazy — it is allocated on the first Enqueue call,
    // so pre-warm that separately if your app uses queued dispatch.
    EventBus.Unsubscribe(EventBus.Subscribe<HandGesture>((ref HandGesture _) => { }));
    EventBus.Unsubscribe(EventBus.Subscribe<FrameDecoded>((ref FrameDecoded _) => { }));
    // ... repeat for all message types used in the application
}
```
