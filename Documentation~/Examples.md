[Home](index) · [Why](Messages) · [API Reference](API-Reference) · **Examples** · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Examples

> **Runnable sample.** Everything below is exercised by the **Basic Publish /
> Subscribe** sample shipped with the package — import it from **Package Manager
> ▸ Messages ▸ Samples ▸ Import**. It is a self-contained score clicker (one
> scene, no inspector wiring) where a button and a background thread both drive a
> single score model through the buses. `BasicPubSubSample` is the composition
> root: it builds the handlers and binds them with one `CommandBus.Install`
> call, while queue draining is handled for free by the auto-spawned
> `[MessagesHost]`:
>
> | Sample file | Shows |
> |---|---|
> | `ScoreHud` / `MenuHud` | `EventBus.Subscribe` / `Subscription.Dispose` and publishing commands from a view — [Basic Publish / Subscribe](#basic-publish--subscribe) |
> | `BasicPubSubSample` | The composition root: binds command handlers via `CommandBus.Install` and switches HUDs on `GameStarted`/`GameEnded` events — [Bootstrap](Bootstrap) |
> | `ScoreModel` / `MenuModel` | A single command handler that re-broadcasts results as events |
> | `ScoreDecayWorker` | `CommandBus.Enqueue` from a background thread — [Queued Dispatch](#queued-dispatch-from-a-worker-thread) |

## Basic Publish / Subscribe

```csharp
using Tutan.Messages;
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
    Subscription _subscription;

    void OnEnable()
    {
        _subscription = EventBus.Subscribe<PlayerTeleported>(OnTeleported);
    }

    void OnDisable()
    {
        _subscription.Dispose();
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

## Subscription Lifetimes

The handle field + `OnDisable` pair above is the explicit form — right when the
subscription toggles with the component. When a subscription should simply live
and die with the component, skip the bookkeeping entirely: `.AddTo(this)` ties
the `Subscription` to the GameObject, and it is disposed automatically in
`OnDestroy`.

```csharp
public class FadeController : MonoBehaviour
{
    void Awake()
    {
        // Auto-unsubscribed when this GameObject is destroyed.
        EventBus.Subscribe<PlayerTeleported>(OnTeleported).AddTo(this);
    }

    void OnTeleported(ref PlayerTeleported msg) => StartFade(msg.Duration);

    void StartFade(float duration) { /* ... */ }
}
```

For non-MonoBehaviour systems (or several subscriptions with one shared
lifetime), collect them in a `SubscriptionBag` and dispose it once:

```csharp
public class AnalyticsSystem : IDisposable
{
    readonly SubscriptionBag _subscriptions = new();

    public AnalyticsSystem()
    {
        EventBus.Subscribe<PlayerTeleported>(OnTeleport).AddTo(_subscriptions);
        EventBus.Subscribe<PlayerScored>(OnScore).AddTo(_subscriptions);
    }

    public void Dispose() => _subscriptions.Dispose(); // unsubscribes both

    void OnTeleport(ref PlayerTeleported msg) { /* ... */ }
    void OnScore(ref PlayerScored msg) { /* ... */ }
}
```

Prefer `.AddTo(this)` when the subscription lives as long as the component, the
bag for plain-C# systems, and an explicit handle field when the lifetime is
genuinely dynamic (e.g. toggled in `OnEnable`/`OnDisable`). All three are the
same `Subscription` underneath. See
[API Reference](API-Reference#subscription-lifetime).

## Queued Dispatch from a Worker Thread

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
    Subscription _subscription;

    void OnEnable()  => _subscription = EventBus.Subscribe<FrameDecoded>(OnFrameDecoded);
    void OnDisable() => _subscription.Dispose();

    // Guaranteed to run on the main thread (dispatched via DrainQueues)
    void OnFrameDecoded(ref FrameDecoded msg)
    {
        // Safe to touch Unity API here
        UpdateTexture(msg.FrameIndex, msg.Width, msg.Height);
    }

    void UpdateTexture(int i, int w, int h) { /* ... */ }
}
```

`Enqueue` is shared by both buses, so the same pattern works for commands:
`CommandBus.Enqueue(cmd)` is the thread-safe way to drive a command's single
handler from off the main thread. The bundled sample uses exactly this — its
`ScoreDecayWorker` runs a background thread that calls
`CommandBus.Enqueue(new AdjustScore { ... })` once a second, while the on-screen
button calls `CommandBus.Publish(new AdjustScore { ... })` on the main thread.
Both routes reach the one `AdjustScore` handler, which is the whole point of the
N:1 guarantee: it does not matter who sends a command, or from which thread —
exactly one handler owns it.

## Commands (N:1 Dispatch)

Unlike events, command handlers are not subscribed ad-hoc. A command has exactly
one handler, and that handler is declared **once at the composition root** via
`CommandBus.Install`. The N:1 rule is validated there and reported in the
returned `InstallResult` — a duplicate or null handler never throws.

```csharp
public struct PlaceOrder : ICommand
{
    public int ItemId;
    public int Quantity;
}

// The handler is just a method — it does NOT subscribe itself.
public class OrderHandler : MonoBehaviour
{
    public void Handle(ref PlaceOrder cmd)
    {
        // Single handler — guaranteed by CommandBus
        ProcessOrder(cmd.ItemId, cmd.Quantity);
    }

    void ProcessOrder(int itemId, int qty) { /* ... */ }
}

// ── Composition root — runs once at startup ───────────────────────
public class GameInstaller : MonoBehaviour
{
    [SerializeField] OrderHandler _orderHandler;

    void Awake()
    {
        var result = CommandBus.Install(r => r
            .Handle<PlaceOrder>(_orderHandler.Handle));
        //  .Handle<NextCommand>(...)   // one Handle per command type

        if (!result.Ok)
            Debug.LogError($"Command install failed: {result.Error}");
    }
}

// Caller — same publish API as before
CommandBus.Publish(new PlaceOrder { ItemId = 7, Quantity = 3 });
```

Registering a second handler for the same command type — e.g. two
`.Handle<PlaceOrder>(...)` calls — makes `Install` return a result whose
`Error` names `PlaceOrder`. The install is atomic, so the previously
installed handlers are left untouched.

> The bundled sample does exactly this: `BasicPubSubSample.Awake` news up
> `ScoreModel` and `MenuModel` and binds `AdjustScore`, `ResetScore`, and
> `StartGame` through one `Install` call — see [Bootstrap](Bootstrap).
