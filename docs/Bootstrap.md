[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · **Bootstrap** · [Editor](Editor)

---

# Bootstrap

Two pieces of startup work are usually needed before the bus is useful:

1. Something must call `CommandBus.DrainQueues()` and `EventBus.DrainQueues()`
   every frame so enqueued messages get dispatched.
2. Each command type needs its single handler bound through
   `CommandBus.TryInstall`.

Both are **opt-in** and controlled from **Project Settings → Tutan → Messages**.
The page writes the matching Scripting Define Symbols to the active build
target's player settings, so the same toggles apply in builds.

## Auto-Install Drainers

Toggle **Auto-Install Drainers** on (or define
`TUTAN_MESSAGES_AUTOINSTALL_DRAINERS`) and `MessagesBootstrap` spawns a hidden
persistent `[MessagesHost]` GameObject at
`RuntimeInitializeLoadType.BeforeSceneLoad`. The host survives scene loads and
calls `DrainQueues()` for both buses in `LateUpdate`.

With the toggle **off**, you are responsible for the equivalent — either
attach `MessagesHost` to a persistent GameObject yourself, or call
`CommandBus.DrainQueues()` / `EventBus.DrainQueues()` from your own update
logic (a PlayerLoop callback, a manager, etc.).

## Auto-Install Command Bus

Toggle **Auto-Install Command Bus** on (or define
`TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS`) and `MessagesBootstrap` runs at
`RuntimeInitializeLoadType.AfterAssembliesLoaded`:

1. Scans every loaded assembly for concrete, non-generic types implementing
   `ICommandHandler` with a parameterless constructor.
2. Instantiates each one.
3. For every closed `ICommandHandler<T>` interface they implement, binds
   `instance.Handle` through a single `CommandBus.TryInstall` call.

Skipped types: abstract, interface, open-generic, or no parameterless
constructor. If two handler types claim the same command, the registry's N:1
rule turns it into one consolidated error logged at startup — the bus is left
empty rather than partially installed.

With the toggle **off**, declare bindings at your composition root yourself:

```csharp
var movement = new MovementManager();
var pools    = new PoolsManager();

CommandBus.TryInstall(out var error, r => r
    .Handle<MovePlayer>(movement.Handle)
    .Handle<SpawnEnemy>(pools.Handle));
```

This is also the right path when handlers need dependencies the discovery scan
can't supply (database, services, scene refs, etc.).
