# Messages — Zero-Alloc Pub/Sub for Unity

A struct-based, ref-passed message bus designed for Unity 6 and XR frame
budgets. Drop it in, publish events, and dispatch never allocates.

## Why

Unity's built-in messaging options (`UnityEvent`, C# `event`, `SendMessage`,
`ScriptableObject` events) all allocate, copy, or reflect in the hot path. On
a 90 Hz XR headset, a single GC spike drops a frame. **Messages dispatches
zero-allocation by design.**

## Quick start

```csharp
using Tutan.Messages;

// 1. Define a message — must be a struct
public struct PlayerScored : IEvent
{
    public int Points;
    public float Timestamp;
}

// 2. Subscribe — get a disposable Subscription back
var subscription = EventBus.Subscribe<PlayerScored>(
    (ref PlayerScored e) => Debug.Log($"+{e.Points}"));

// 3. Publish — zero allocations
EventBus.Publish(new PlayerScored { Points = 100, Timestamp = Time.time });
```

> **Messages must be a `struct`.** Reference-type fields (`string`, arrays,
> class payloads) are allowed, but they carry two costs: a *queued* message that
> holds references adds GC mark-phase scan cost while it waits to be drained, and
> a worker-thread `Enqueue` copies the struct shallowly, so any referenced object
> is shared across threads. Prefer value-type fields on hot or cross-thread
> paths — for string-like data, `Unity.Collections.FixedString*` (e.g.
> `FixedString64Bytes`) or an int handle keeps the message fully value-typed. See
> [`Documentation~/Messages.md`](Documentation~/Messages.md#messages) for details.

```csharp

// 4. Unsubscribe by disposing (no delegate-equality footguns)…
subscription.Dispose();

// …or skip the field entirely: tie the subscription to a MonoBehaviour,
// and it is disposed automatically when the GameObject is destroyed.
EventBus.Subscribe<PlayerScored>((ref PlayerScored e) => Debug.Log($"+{e.Points}"))
        .AddTo(this);
```

That's it. A hidden `[MessagesHost]` is spawned for you at startup, so the bus
is drained every frame with no prefab to drag into a scene and nothing to
configure.

## Features

- **Zero GC in the dispatch hot path.** `ref`-passed handlers, no
  multicast delegates, no struct copies.
- **EventBus (N:M)** for notifications, **CommandBus (N:1)** for intents
  (CQRS-friendly).
- **Editor tooling** — a live **Messages Console** for inspecting bus traffic
  (subscribe/publish/enqueue/drain records, payloads, and the subscribers
  frozen at fire time).
- **Thread-safe `Enqueue`** for network/decode/async callbacks; deferred
  dispatch on the main thread via `DrainQueues()`.
- **Disposable subscriptions** — `Subscribe` returns a `Subscription`;
  dispose it, bundle several in a `SubscriptionBag`, or tie one to a
  GameObject's lifetime with `.AddTo(this)` and forget about `OnDestroy`.
  Explicit lifecycle: no leaked lambdas, no `-=` bugs with closures.
- **Profiler markers** on every entry point. Visible in Unity Profiler timeline.
- **Zero-config draining.** A persistent `[MessagesHost]` is auto-spawned at
  startup to drain both buses every `LateUpdate`. Define
  `TUTAN_MESSAGES_NO_AUTO_HOST` to opt out and own the drain loop yourself.
  Command handlers are bound explicitly at your composition root via
  `CommandBus.Install`.
- **Unity 6.0 (6000.1) and newer.** Works on Windows, Mac, Linux, iOS,
  Android, WebGL, all XR platforms (Quest, PCVR, visionOS).

## Samples

One sample included, importable from the Package Manager:

- **Basic Publish / Subscribe** — a live score in a self-contained scene. A button
  publishes commands, a background thread enqueues commands off the main thread, and
  the score UI listens for events. Covers the CommandBus (N:1), the EventBus (N:M),
  composition-root handler binding via `CommandBus.Install`, and thread-safe
  `Enqueue`/drain in one place. Import the sample, open its scene, and press
  Play — no configuration needed.

## Full documentation

See [`Documentation~/index.md`](Documentation~/index.md) for the complete documentation site —
API reference, threading model, performance characteristics, edge-case
behavior, architectural guidance, and editor tooling.

## Support

- Issues and questions: <https://github.com/TutanDev>
- Contact: andrespino.95@gmail.com
