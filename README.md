# MessageBus — Zero-Alloc Pub/Sub for Unity

A struct-based, ref-passed, GC-free message bus designed for Unity 6 and XR
frame budgets. Drop it in, publish events, never allocate.

## Why

Unity's built-in messaging options (`UnityEvent`, C# `event`, `SendMessage`,
`ScriptableObject` events) all allocate, copy, or reflect in the hot path. On
a 90 Hz XR headset, a single GC spike drops a frame. **MessageBus dispatches
zero-allocation by design.**

## Quick start

```csharp
using Tutan.MessageBus;

// 1. Define a message — must be unmanaged struct
public struct PlayerScored : IEvent
{
    public int Points;
    public float Timestamp;
}

// 2. Subscribe — get a token back
var token = EventBus.Subscribe<PlayerScored>(
    (ref PlayerScored e) => Debug.Log($"+{e.Points}"));

// 3. Publish — zero allocations
EventBus.Publish(new PlayerScored { Points = 100, Timestamp = Time.time });

// 4. Unsubscribe by token (no delegate-equality footguns)
EventBus.Unsubscribe(token);
```

That's it. The bus is auto-bootstrapped at startup — no manual setup, no
prefab to drag into your scene.

## Features

- **Zero GC in the dispatch hot path.** `ref`-passed handlers, no
  multicast delegates, no struct copies.
- **EventBus (N:M)** for notifications, **CommandBus (N:1)** for intents
  (CQRS-friendly).
- **Thread-safe `Enqueue`** for network/decode/async callbacks; deferred
  dispatch on the main thread via `DrainQueues()`.
- **Subscription tokens** — explicit lifecycle, no leaked lambdas, no
  `-=` bugs with closures.
- **Profiler markers** on every entry point. Visible in Unity Profiler timeline.
- **Auto-bootstrap** via `RuntimeInitializeOnLoad`. Opt out with the
  `TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP` scripting define if you prefer
  manual control.
- **Unity 6.0 (6000.1) and newer.** Works on Windows, Mac, Linux, iOS,
  Android, WebGL, all XR platforms (Quest, PCVR, visionOS).

## Samples

Three samples included, importable from the Package Manager:

- **BasicPubSub** — minimal EventBus and CommandBus usage.
- **ThreadedDispatch** — enqueue from a worker thread, drain on main.
- **XRHandGesture** — one input source, multiple decoupled subscribers.

## Full documentation

See `Documentation~/messagebus.md` for the complete API reference, threading
model, performance characteristics, edge-case behavior, and architectural
guidance.

## Support

- Issues and questions: <https://github.com/TutanDev>
- Contact: andrespino.95@gmail.com
