[Home](index.md) · [Why](Messages.md) · [API Reference](API-Reference.md) · [Examples](Examples.md) · [Threading](Threading.md) · [Performance](Performance.md) · [Edge Cases](EdgeCases.md) · [Architecture](Architecture.md) · **Bootstrap** · [Editor](Editor.md)

---

# Bootstrap

Two pieces of startup work are needed before the bus is useful:

1. Something must call `CommandBus.DrainQueues()` and `EventBus.DrainQueues()`
   every frame so enqueued messages get dispatched.
2. Each command type needs its handler bound through `CommandBus.Install`.

The first is automatic; the second is one explicit call at your composition root.

## Queue draining (automatic)

At startup `MessagesBootstrap` spawns a hidden, persistent `[MessagesHost]`
GameObject (`RuntimeInitializeLoadType.BeforeSceneLoad`). It survives scene
loads and calls `DrainQueues()` for both buses every `LateUpdate`. No prefab to
drag in, no setup.

If you would rather own the drain loop, define `TUTAN_MESSAGES_NO_AUTO_HOST` to
suppress the auto-spawned host, then either attach `MessagesHost` to a
persistent GameObject yourself, or call `CommandBus.DrainQueues()` /
`EventBus.DrainQueues()` from your own update logic (a PlayerLoop callback, a
manager, etc.). A custom loop should also call
`MessagesInstrumentation.SyncFrame(Time.frameCount)` once per frame on the main
thread, as the host does: it stamps the frame number onto instrumentation
records, which the Messages Console's frame column and `Record.Frame` rely on.
The call is `[Conditional]`, so it is stripped from release builds.

```csharp
void DrainMessages() // your own once-per-frame callback, on the main thread
{
    MessagesInstrumentation.SyncFrame(Time.frameCount);
    CommandBus.DrainQueues();
    EventBus.DrainQueues();
}
```

## Binding command handlers (explicit)

Command handlers are declared once, at your composition root, through
`CommandBus.Install`. The N:1 rule is validated there and reported in the
returned `InstallResult` — a duplicate command type or a null handler makes
`result.Ok` false with `result.Error` naming the offender, and leaves the live
bus untouched.

```csharp
var movement = new MovementManager();
var pools    = new PoolsManager();

var result = CommandBus.Install(r => r
    .Handle<MovePlayer>(movement.Handle)
    .Handle<SpawnEnemy>(pools.Handle));

if (!result.Ok)
    Debug.LogError(result.Error);
```

A handler is just a method matching `MessageHandler<T>` (`void Handle(ref T)`),
so any object — a `MonoBehaviour`, a plain C# manager, a service resolved from
your DI container — can supply one. Because you new them up yourself, handlers
are free to take whatever dependencies they need (database, services, scene
refs); there is no discovery scan imposing a parameterless-constructor rule.

`Install` does not require every command type to be bound: a command with no
handler is silently dropped. For commands that must be handled, assert
`CommandBus.GetSubscriberCount<T>() == 1` once after `Install`.

Call `Install` again to rebuild the bus from scratch (composition-root
semantics) — previously installed handlers are replaced wholesale.

### Install before anything enqueues

A successful `Install`, like `Reset`, replaces the bus, so commands still
queued for the next drain are discarded — including on the first install. The
editor and development builds log a warning naming the discarded command types.
Install from a `BeforeSceneLoad` `[RuntimeInitializeOnLoadMethod]`, or from a
bootstrap object with an early Script Execution Order: the `Awake` order of
objects in a scene is undefined, so another object's `Awake` may enqueue first.
If commands queued before a re-install matter, call `CommandBus.DrainQueues()`
first.

```csharp
static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InstallCommands()
    {
        var movement = new MovementManager();
        var result = CommandBus.Install(r => r
            .Handle<MovePlayer>(movement.Handle));
        if (!result.Ok) Debug.LogError(result.Error);
    }
}
```
