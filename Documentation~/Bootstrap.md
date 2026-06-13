[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · **Bootstrap** · [Editor](Editor)

---

# Bootstrap

Two pieces of startup work are needed before the bus is useful:

1. Something must call `CommandBus.DrainQueues()` and `EventBus.DrainQueues()`
   every frame so enqueued messages get dispatched.
2. Each command type needs its single handler bound through
   `CommandBus.Install`.

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

Call `Install` again to rebuild the bus from scratch (composition-root
semantics) — previously installed handlers are replaced wholesale.
