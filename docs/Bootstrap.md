[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · **Bootstrap** · [Editor](Editor)

---

# Bootstrap

Two pieces of startup work are needed before the bus is useful:

1. Something must call `CommandBus.DrainQueues()` and `EventBus.DrainQueues()`
   every frame so enqueued messages get dispatched.
2. Each command type needs its single handler bound through
   `CommandBus.TryInstall`.

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
manager, etc.).

## Binding command handlers (explicit)

Command handlers are declared once, at your composition root, through
`CommandBus.TryInstall`. The N:1 rule is validated there and reported as a return
value — a duplicate command type or a null handler makes `TryInstall` return
`false` with an `error` naming the offender, and leaves the live bus untouched.

```csharp
var movement = new MovementManager();
var pools    = new PoolsManager();

bool ok = CommandBus.TryInstall(out var error, r => r
    .Handle<MovePlayer>(movement.Handle)
    .Handle<SpawnEnemy>(pools.Handle));

if (!ok)
    Debug.LogError(error);
```

A handler is just a method matching `MessageHandler<T>` (`void Handle(ref T)`),
so any object — a `MonoBehaviour`, a plain C# manager, a service resolved from
your DI container — can supply one. Because you new them up yourself, handlers
are free to take whatever dependencies they need (database, services, scene
refs); there is no discovery scan imposing a parameterless-constructor rule.

Call `TryInstall` again to rebuild the bus from scratch (composition-root
semantics) — for example after a scene transition that calls `CommandBus.Reset()`.
