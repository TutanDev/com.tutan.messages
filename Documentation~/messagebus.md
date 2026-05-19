# MessageBus — Full Documentation

**Namespace:** `Tutan.MessageBus`
**Target:** Unity 2021.3 LTS and newer (Unity 6 / CoreCLR friendly), XR applications

---

## 1. Why a Message Bus

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

`Tutan.MessageBus` is built to avoid every one of these costs in the dispatch
hot path.

### XR-specific design constraints

XR headsets render at 72–120 Hz with hard frame deadlines. A single GC spike
of even 1–2 ms can cause a dropped frame, directly causing user discomfort.
This drives every design decision:

- **Messages are `unmanaged struct`** — no heap allocation, no GC roots.
- **Handlers receive `ref T`** — no struct copy on dispatch.
- **Subscription uses integer tokens** — no delegate equality problems.
- **No multicast delegates** — `Delegate.Combine` allocates.
- **Profiler markers on all public entry points** — visible in Unity Profiler timeline.

---

## 2. Core Concepts

### 2.1 Messages

A message is any `unmanaged struct` implementing `IEvent` or `ICommand` (both
extend `IMessage`). The `unmanaged` constraint guarantees no managed references
(no `string`, no arrays, no class fields), making the struct blittable and
GC-invisible.

```csharp
// Correct — all fields are unmanaged
public struct HandTrackingLost : IEvent
{
    public int HandIndex;       // 0 = left, 1 = right
    public float Timestamp;
    public float Confidence;
}

// Correct — fixed buffers are unmanaged
public struct NetworkPacketReceived : IEvent
{
    public int PacketId;
    public int ByteCount;
    public unsafe fixed byte Header[16];
}

// COMPILE ERROR — string is a managed reference
public struct BadMessage : IEvent
{
    public string Name; // won't compile: not unmanaged
}
```

**If you need string-like data**, use `Unity.Collections.FixedString64Bytes`
(from `com.unity.collections`) or store an integer handle that indexes into
a separate lookup table.

### 2.2 Events vs Commands

- **`IEvent`** — a notification of something that *happened*. Any number of
  handlers may subscribe. Naming: past tense (`PlayerScored`, `OrderPlaced`).
- **`ICommand`** — an *intent* to do something. Exactly one handler may
  subscribe; a second `Subscribe` call throws. Naming: imperative
  (`MovePlayer`, `PlaceOrder`). This enforces the CQRS rule at runtime.

### 2.3 Handlers

```csharp
public delegate void MessageHandler<T>(ref T message) where T : unmanaged, IMessage;
```

The `ref` parameter is critical — it eliminates the struct copy that would
occur with `Action<T>`. Handlers *may* mutate the message (e.g., set a
`Consumed` flag), but use this with care: dispatch order is registration
order, and subsequent handlers will see the mutation.

### 2.4 Subscription Tokens

`Subscribe` returns a `SubscriptionToken`. This is the *only* mechanism for
unsubscription. There is no `-=` operator. This avoids the fragility of
delegate equality checks with lambdas and closures (which generate new
delegate instances and silently fail to unsubscribe).

### 2.5 Dispatch Modes

| Mode      | Method    | Thread Safety | When to Use                                   |
|-----------|-----------|---------------|-----------------------------------------------|
| Immediate | `Publish` | Main only     | Same-frame reactions, UI updates, state sync   |
| Queued    | `Enqueue` | Thread-safe   | Network callbacks, async tasks, worker threads |

Queued messages are dispatched on the main thread when `DrainQueues()` is
called. The bundled `MessageBusHost` calls this in `LateUpdate` and is
auto-instantiated at startup.

---

## 3. API Reference

### 3.1 Subscribe

```csharp
SubscriptionToken Subscribe<T>(MessageHandler<T> handler) where T : unmanaged, IMessage
```

Registers `handler` for messages of type `T`. Returns a token for later
unsubscription. Main thread only.

### 3.2 Unsubscribe

```csharp
bool Unsubscribe(SubscriptionToken token)
```

Removes the subscription identified by `token`. Returns `false` if already
unsubscribed or invalid. Safe to call during dispatch — the handler is
marked inactive and skipped for the remainder of the current dispatch cycle.

### 3.3 Publish (Immediate)

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

### 3.4 Enqueue (Deferred)

```csharp
void Enqueue<T>(in T message) where T : unmanaged, IMessage
```

Adds `message` to the internal queue for type `T`. Thread-safe via
`ConcurrentDictionary` + `ConcurrentQueue`; safe to race with main-thread
`Subscribe`/`Publish`/`DrainQueues`. Channel
creation uses a brief lock; the enqueue itself is lock-free
(`ConcurrentQueue<T>`). Messages are dispatched on the next `DrainQueues()`
call.

### 3.5 DrainQueues

```csharp
void DrainQueues()
```

Processes all pending queued messages across all channels. Call once per
frame. Main thread only. By default `MessageBusHost` does this for you.

### 3.6 Diagnostics

```csharp
int GetSubscriberCount<T>() where T : unmanaged, IMessage
int ChannelCount { get; }
```

### 3.7 Lifecycle

```csharp
void Reset()    // Clears all subscriptions and queues. Use on scene transitions or test teardown.
```

---

## 4. Usage Examples

### 4.1 Basic Publish / Subscribe

```csharp
using Tutan.MessageBus;
using UnityEngine;

// ── Define an event ──────────────────────────────────────────────
public struct PlayerTeleported : IEvent
{
    public Vector3 Origin;
    public Vector3 Destination;
    public float Duration;
}

// ── Subscriber ────────────────────────────────────────────────────
public class FadeController : MonoBehaviour
{
    SubscriptionToken _token;

    void OnEnable()
    {
        _token = EventBus.Subscribe<PlayerTeleported>(OnTeleported);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe(_token);
    }

    void OnTeleported(ref PlayerTeleported msg)
    {
        StartFade(msg.Duration);
    }

    void StartFade(float duration) { /* ... */ }
}

// ── Publisher ─────────────────────────────────────────────────────
public class TeleportSystem : MonoBehaviour
{
    public void ExecuteTeleport(Vector3 origin, Vector3 dest, float duration)
    {
        var msg = new PlayerTeleported
        {
            Origin = origin,
            Destination = dest,
            Duration = duration
        };
        EventBus.Publish(ref msg);
    }
}
```

`TeleportSystem` has no reference to `FadeController`. If tomorrow you add a
`TeleportAudioHandler`, a `TeleportAnalyticsLogger`, or remove `FadeController`
entirely, `TeleportSystem` does not change. That is the decoupling the bus
provides.

### 4.2 Queued Dispatch from a Worker Thread

```csharp
public struct FrameDecoded : IEvent
{
    public int FrameIndex;
    public int Width;
    public int Height;
    public long TimestampTicks;
}

public class VideoStreamReceiver
{
    // Called on a network/decode thread — NOT the main thread
    void OnFrameReady(int index, int w, int h)
    {
        var msg = new FrameDecoded
        {
            FrameIndex = index,
            Width = w,
            Height = h,
            TimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp()
        };

        // Enqueue is thread-safe. The message will be dispatched
        // on the main thread during the next DrainQueues() call.
        EventBus.Enqueue(msg);
    }
}

public class VideoTextureUpdater : MonoBehaviour
{
    SubscriptionToken _token;

    void OnEnable()  => _token = EventBus.Subscribe<FrameDecoded>(OnFrameDecoded);
    void OnDisable() => EventBus.Unsubscribe(_token);

    // Guaranteed to run on the main thread (dispatched via DrainQueues)
    void OnFrameDecoded(ref FrameDecoded msg)
    {
        // Safe to touch Unity API here
        UpdateTexture(msg.FrameIndex, msg.Width, msg.Height);
    }

    void UpdateTexture(int i, int w, int h) { /* ... */ }
}
```

### 4.3 Commands (N:1 Dispatch)

```csharp
public struct PlaceOrder : ICommand
{
    public int ItemId;
    public int Quantity;
}

public class OrderHandler : MonoBehaviour
{
    SubscriptionToken _token;
    void OnEnable()  => _token = CommandBus.Subscribe<PlaceOrder>(Handle);
    void OnDisable() => CommandBus.Unsubscribe(_token);

    void Handle(ref PlaceOrder cmd)
    {
        // Single handler — guaranteed by CommandBus
        ProcessOrder(cmd.ItemId, cmd.Quantity);
    }

    void ProcessOrder(int itemId, int qty) { /* ... */ }
}

// Caller — same publish API
CommandBus.Publish(new PlaceOrder { ItemId = 7, Quantity = 3 });
```

### 4.4 Scene Transition Cleanup

```csharp
public class SceneLoader : MonoBehaviour
{
    async void LoadScene(string sceneName)
    {
        // Reset the bus before unloading — prevents stale subscriptions
        // from destroyed MonoBehaviours receiving messages during the
        // transition frame.
        EventBus.Reset();
        CommandBus.Reset();

        await SceneManager.LoadSceneAsync(sceneName);

        // New scene's OnEnable calls will re-subscribe.
    }
}
```

---

## 5. Threading Model

```
 Worker Thread              Main Thread
 ─────────────              ───────────
      │                          │
  Enqueue(msg)              LateUpdate()
      │                          │
 lock(_queueLock)           DrainQueues()
  GetOrCreate channel            │
 release lock                    │
      │                     channel.DrainQueue()
  ConcurrentQueue.Enqueue        │
                            ConcurrentQueue.TryDequeue
                                 │
                            Publish(ref msg)  ◄── synchronous dispatch
                                 │
                            handler1(ref msg)
                            handler2(ref msg)
                            ...
```

- `Publish`, `Subscribe`, `Unsubscribe`, `DrainQueues` — **main thread only**.
  Lock-free reads via `ConcurrentDictionary<Type, ChannelBase>`.
- `Enqueue` — **thread-safe**. Channel lookup/creation goes through
  `ConcurrentDictionary.GetOrAdd`; the message itself lands in a per-channel
  `ConcurrentQueue<T>`. Safe to race against any main-thread call.

---

## 6. Performance Characteristics

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

### 6.1 Pre-warming

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

---

## 7. Behavioral Edge Cases

### 7.1 Reentrant Publish

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

### 7.2 Subscribe During Dispatch

If a handler calls `Subscribe<T>` for the message type currently being
dispatched, the new handler is appended to the list. It will **not** receive
the current message (the iteration count was captured before the append). It
will receive subsequent messages.

### 7.3 Unsubscribe During Dispatch

Safe. The entry is marked inactive immediately. The delegate reference is set
to `null` to release the GC root. The inactive entry is skipped for the
remainder of the current dispatch. Compaction occurs after dispatch completes.

### 7.4 Handler Exceptions

Exceptions in a handler are caught and logged via `Debug.LogException`.
Dispatch continues to the next handler. A broken handler must never cascade
into a broken frame.

### 7.5 Zero Subscribers

`Publish<T>` returns immediately if no channel exists for `T`. Cost: one
`Dictionary.TryGetValue` call.

---

## 8. Architectural Guidance

The bus is at its best when systems treat events as *system-state notifications*
and commands as *intents to act*. A few rules of thumb that scale well:

1. **Reactors do not publish events.** Pure consumers (UI, audio, haptics)
   subscribe to events and react. They should not in turn publish events,
   which leads to dependency cycles.
2. **Activity classes do not publish or subscribe.** A class that performs a
   step inside a larger workflow has no knowledge of the broader system state
   needed to decide whether something is event-worthy. Let the owner decide.
3. **Cross-system cycles are usually a smell.** If A publishes to B and B
   publishes back to A in the same frame, that's a direct method call dressed
   up — use a method.
4. **Use commands when intent must reach exactly one handler.** This is
   common for "do this work" requests with no fan-out.
5. **Use events for fan-out notifications.** Many subscribers, none of whom
   must run; any of whom may stop existing without breaking the publisher.

---

## 9. When NOT to Use the Bus

The bus is for decoupled N:M communication. Do not use it for:

- **Direct 1:1 calls within the same subsystem.** A controller calling its own
  helper is a direct method call, not a message. Using the bus adds latency
  and indirection with no decoupling benefit.
- **High-frequency per-bone / per-vertex data.** If you need to broadcast 26
  hand joint positions at 90 Hz, the per-message overhead (dictionary lookup
  + iteration) is unnecessary. Use a shared `NativeArray` or ring buffer and
  publish a single `HandTrackingFrameReady` event as a notification that new
  data is available.
- **Ordered pipelines with strict sequencing guarantees.** The bus dispatches
  in registration order, but this is an implementation detail, not a contract.
  If your pipeline requires A→B→C ordering, model it as explicit method calls
  or a dedicated pipeline abstraction.

---

## 10. Auto-bootstrap and Opting Out

By default, the package creates a hidden persistent `[MessageBusHost]`
GameObject at startup via `RuntimeInitializeOnLoad`. This GameObject lives
across scene loads and calls `DrainQueues()` for both buses in `LateUpdate`.

**To opt out** (e.g., to drain from a custom PlayerLoop callback, or to host
the drainer under your own scene root), add this to your Player settings:

```
Scripting Define Symbols:  TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP
```

When disabled, attach the `MessageBusHost` component to a persistent
GameObject yourself, or call `CommandBus.DrainQueues()` and
`EventBus.DrainQueues()` from your own update logic.
