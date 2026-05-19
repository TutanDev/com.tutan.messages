[Home](index) · [Why](MessageBus) · [API Reference](API-Reference) · **Examples** · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Examples

## Basic Publish / Subscribe

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

## Commands (1:1 Dispatch)

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

## Scene Transition Cleanup

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
