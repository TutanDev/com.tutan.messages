# Messages — Zero-Alloc Pub/Sub for Unity

A struct-based, ref-passed, GC-free message bus designed for Unity 6 and XR
frame budgets. Drop it in, publish events, never allocate.

## Why

Unity's built-in messaging options (`UnityEvent`, C# `event`, `SendMessage`,
`ScriptableObject` events) all allocate, copy, or reflect in the hot path. On
a 90 Hz XR headset, a single GC spike drops a frame. **Messages dispatches
zero-allocation by design.**

## Quick start

```csharp
using Tutan.Messages;

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

That's it. Open **Project Settings → Tutan → Messages** once and flip
**Auto-Install Drainers** on — the bus is then drained for you every frame,
with no prefab to drag into a scene.

## Features

- **Zero GC in the dispatch hot path.** `ref`-passed handlers, no
  multicast delegates, no struct copies.
- **EventBus (N:M)** for notifications, **CommandBus (N:1)** for intents
  (CQRS-friendly).
- **Editor tooling** — a live **Messages Console** for bus traffic and a
  **Project Settings → Tutan → Messages** page that drives the three opt-in
  scripting defines and embeds a static command → handler audit (flagging any
  command with zero or multiple handlers).
- **Thread-safe `Enqueue`** for network/decode/async callbacks; deferred
  dispatch on the main thread via `DrainQueues()`.
- **Subscription tokens** — explicit lifecycle, no leaked lambdas, no
  `-=` bugs with closures.
- **Profiler markers** on every entry point. Visible in Unity Profiler timeline.
- **Opt-in bootstrap** via `RuntimeInitializeOnLoad`. Toggle
  **Auto-Install Drainers** and **Auto-Install Command Bus** in the settings
  page (or set `TUTAN_MESSAGES_AUTOINSTALL_DRAINERS` /
  `TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS` directly) to have the host and
  handlers wired up at startup; leave them off for manual control.
- **Unity 6.0 (6000.1) and newer.** Works on Windows, Mac, Linux, iOS,
  Android, WebGL, all XR platforms (Quest, PCVR, visionOS).

## Samples

One sample included, importable from the Package Manager:

- **Basic Publish / Subscribe** — a live score in a self-contained scene. A button
  publishes commands, a background thread enqueues commands off the main thread, and
  the score UI listens for events. Covers the CommandBus (N:1), the EventBus (N:M),
  the composition-root `TryInstall` pattern, and thread-safe `Enqueue`/drain in one
  place. Drop the `BasicPubSubSample` component on a GameObject and press Play.

## Full documentation

See [`docs/index.md`](docs/index.md) for the complete documentation site —
API reference, threading model, performance characteristics, edge-case
behavior, architectural guidance, and editor tooling.

## Support

- Issues and questions: <https://github.com/TutanDev>
- Contact: andrespino.95@gmail.com
