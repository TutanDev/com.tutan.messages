[Home](index) · **Why this library** · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Messages

**Namespace:** `Tutan.Messages`
**Target:** Unity 6.0 (6000.1) and newer (CoreCLR friendly), XR applications

## Why a Message Bus

Direct references between systems create tight coupling that gets worse as a
project grows. A pub/sub message bus solves this by giving systems a common
medium for communication — publishers and subscribers never reference each
other. This is the same idea behind UnityEvent, C# `event`, and ScriptableObject
events, but **none of those mechanisms are appropriate for high-frequency or
XR-sensitive code paths**:

| Mechanism          | Problem for XR / hot path                                   |
|--------------------|-------------------------------------------------------------|
| `UnityEvent`       | Serialization overhead, boxing on value types, slow invoke   |
| `C# event`         | Multicast delegate allocates on `+=` / `-=` with lambdas    |
| `Action<T>`        | Copies struct on invocation (no `ref` parameter support)     |
| `SendMessage()`    | Reflection-based, string-keyed, zero type safety             |
| `ScriptableObject` | Requires asset creation, inspector coupling, not struct-safe |

`Tutan.Messages` is built to avoid every one of these costs in the dispatch
hot path.

### XR-specific design constraints

XR headsets render at 72–120 Hz with hard frame deadlines. A single GC spike
of even 1–2 ms can cause a dropped frame, directly causing user discomfort.
This drives every design decision:

- **Messages are `struct`** — generic specialization plus `ref`-passing keeps
  dispatch allocation-free; the message is never boxed or heap-stored.
- **Handlers receive `ref T`** — no struct copy on dispatch.
- **Subscription uses integer tokens** — no delegate equality problems.
- **No multicast delegates** — `Delegate.Combine` allocates.
- **Profiler markers on all public entry points** — visible in Unity Profiler timeline.

---

## Core Concepts

### Messages

A message is any `struct` implementing `IEvent` or `ICommand` (both extend
`IMessage`). Dispatch is allocation-free for any struct: the bus is generic over
the message type and passes it by `ref`, so the message is never boxed or
heap-stored.

```csharp
// Recommended — all fields are value types (no GC scan, safe across threads)
public struct HandTrackingLost : IEvent
{
    public int HandIndex;       // 0 = left, 1 = right
    public float Timestamp;
    public float Confidence;
}

// Also fine — fixed buffers stay value-typed
public struct NetworkPacketReceived : IEvent
{
    public int PacketId;
    public int ByteCount;
    public unsafe fixed byte Header[16];
}

// Allowed, with caveats — reference-type fields are permitted
public struct LogLine : IEvent
{
    public string Text;   // reference field: see the trade-offs below
    public int Level;
}
```

**Reference-type fields are allowed but not free.** Two caveats apply when a
message carries a `string`, array, collection, or class reference:

- A message sitting in the deferred queue is a GC root — the garbage collector
  must scan it during the mark phase for as long as it waits to be drained. This
  is a scan cost, not an allocation, and dispatch itself stays allocation-free.
- `Enqueue` copies the struct *shallowly*. A worker thread that enqueues a
  message shares any referenced object with the main thread that drains it, so
  treat such payloads as immutable handoffs.

For hot or cross-thread paths, prefer value-only messages. **If you need
string-like data**, use `Unity.Collections.FixedString64Bytes` (from
`com.unity.collections`) or store an integer handle that indexes into a separate
lookup table — both keep the message fully value-typed.

### Events vs Commands

- **`IEvent`** — a notification of something that *happened*. Any number of
  handlers may subscribe. Naming: past tense (`PlayerScored`, `OrderPlaced`).
- **`ICommand`** — an *intent* to do something. Exactly one handler is bound,
  once, at the composition root via `CommandBus.Install`. Naming: imperative
  (`MovePlayer`, `PlaceOrder`). This enforces the CQRS rule: the N:1 constraint
  is validated at install time and a duplicate (or null) handler is reported in
  the returned `InstallResult`, not thrown.

### Handlers

```csharp
public delegate void MessageHandler<T>(ref T message) where T : struct, IMessage;
```

The `ref` parameter is critical — it eliminates the struct copy that would
occur with `Action<T>`. Handlers *may* mutate the message (e.g., set a
`Consumed` flag), but use this with care: dispatch order is registration
order, and subsequent handlers will see the mutation.

### Subscription Handles

`Subscribe` returns a disposable `Subscription`. Disposing it is the *only*
mechanism for unsubscription — directly, via a `SubscriptionBag`, or tied to a
GameObject's lifetime with `.AddTo(this)`. There is no `-=` operator. This
avoids the fragility of delegate equality checks with lambdas and closures
(which generate new delegate instances and silently fail to unsubscribe).

### Dispatch Modes

| Mode      | Method    | Thread Safety | When to Use                                   |
|-----------|-----------|---------------|-----------------------------------------------|
| Immediate | `Publish` | Main only     | Same-frame reactions, UI updates, state sync   |
| Queued    | `Enqueue` | Thread-safe   | Network callbacks, async tasks, worker threads |

Queued messages are dispatched on the main thread when `DrainQueues()` is
called. The bundled `MessagesHost` calls this in `LateUpdate` and is
auto-instantiated at startup — see [Bootstrap](Bootstrap).

---

Next: [API Reference](API-Reference) for the full method surface, or
[Examples](Examples) for end-to-end usage.
