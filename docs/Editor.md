[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · **Editor**

---

# Editor Tooling

Everything on this page is **editor-only / development-only**. The
instrumentation hooks are gated behind `[Conditional("UNITY_EDITOR")]` and
`[Conditional("TUTAN_MESSAGES_DEBUG")]`, so the C# compiler strips every
call site in release player builds. The inspector drawers live under
`Editor/` and are never compiled into a player.

---

## Project Settings

The package adds a settings page at **Project Settings → Tutan → Messages**.
It owns three toggles, each backed by a Scripting Define Symbol that is
written to the active build target's player settings on toggle and re-synced
on every editor load (so the project state matches the asset even after a
VCS pull or hand-edit):

| Toggle | Define | Effect |
|---|---|---|
| **Enable Instrumentation** | `TUTAN_MESSAGES_DEBUG` | Activates the Messages Console hooks. With this off, every `[Conditional]` call site is stripped at compile time — no branches, no buffer, no cost. |
| **Auto-Install Drainers** | `TUTAN_MESSAGES_AUTOINSTALL_DRAINERS` | Spawns the hidden `[MessagesHost]` GameObject at startup so `CommandBus.DrainQueues()` / `EventBus.DrainQueues()` are called every `LateUpdate`. |
| **Auto-Install Command Bus** | `TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS` | Reflects over loaded assemblies, instantiates every concrete `ICommandHandler` with a parameterless constructor, and binds each closed `ICommandHandler<T>` it implements through one `CommandBus.TryInstall` at `AfterAssembliesLoaded`. |

Settings are stored in `ProjectSettings/Packages/com.tutan.messages/MessagesProjectSettings.asset`
(text format) so they diff cleanly in version control. The page also embeds
the **Commands authoring view** described below in a foldout — there is no
separate window.

### Commands authoring view

A static, edit-time audit of the command → handler routing table. Unlike the
Messages Console (which shows *observed* runtime traffic), this view shows
what is *declared* in your code, with no play mode required.

It lists every `ICommand` struct found via `TypeCache` as a card and resolves
its handler(s) through the `ICommandHandler<T>` interface. The command and each
handler render as clickable `ScriptFileField` rows — single-click pings the
`.cs` file in the Project window, double-click opens it.

Because commands are **N:1** (exactly one handler each), a card is flagged when
a command has:

- **no handler** — an *orphan*; nothing handles it, or
- **more than one** — an N:1 violation; only one handler may be bound.

Toolbar: a name **search**, an **Only warnings** toggle (persisted in
`EditorPrefs`) to hide healthy commands, and **Refresh** to re-scan on demand
(the view already re-scans after every recompile / domain reload).

```csharp
using Tutan.Messages;

// Implementing ICommandHandler<T> makes a handler discoverable by the view.
public sealed class MovementManager : ICommandHandler<MovePlayer>
{
    public void Handle(ref MovePlayer cmd) { /* ... */ }
}
```

`ICommandHandler<T>` is **declarative only**. With **Auto-Install Command Bus**
off, `Handle(ref T)` matches the bus's `MessageHandler<T>` delegate, so you
bind it at the composition root yourself:

```csharp
var movement = new MovementManager();
CommandBus.TryInstall(out var error, r => r.Handle<MovePlayer>(movement.Handle));
```

With the toggle on, the bootstrap discovers and binds every declared
`ICommandHandler<T>` automatically; the view then doubles as a sanity check
for what *will be* installed.

---

## Messages Console

The package ships with an editor window for live introspection of bus traffic:
**Window → Tutan → Messages Console**.

It is a single virtualized log of recent `Subscribe`, `Unsubscribe`, `Publish`,
`Enqueue`, and (optionally) drain operations with timestamp, frame, bus (E/C),
op, and type. Selecting a row pretty-prints the payload,
handler details, and — for `Publish`/`Enqueue` rows — the subscribers as they
were **at the moment the message was sent** in the right pane. This subscriber
list is a snapshot frozen into the record at fire time, not a live query, so
subscribing or unsubscribing afterwards does not change what a past record
shows.

Toolbar: **Pause** (freeze the view), **Clear** (empty the ring buffer),
**Events / Commands** toggles, per-op toggles (**Publish / Enqueue /
Subscribe·Unsubscribe / Drain**), and a search field that filters by full
type name. Drain records are off by default because they are noisy.

While the window is open, struct payloads are boxed into records so they can
be inspected — this adds one boxing allocation per `Publish`/`Enqueue`. It is
on automatically whenever the window is open and incurs no cost once the
window is closed.

### Runtime cost

The instrumentation hooks on `Messages.Publish` / `Enqueue` /
`Subscribe` / `Unsubscribe` / `DrainQueues` are decorated with:

```csharp
[Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
```

so the C# compiler strips every call site at compile time when neither
define is set. The per-frame frame-counter sync in `MessagesHost` goes
through the same kind of `[Conditional]` method (`SyncFrame`), so it strips
too. With every touchpoint gone, `MessagesInstrumentation`'s static
constructor never runs in a release player, so its ~256 KB record ring
buffer is never even allocated. **In release player builds the bus runs
exactly as before — no branches, no allocations, no buffer.**

In the editor it is always available because `UNITY_EDITOR` is always
defined. To enable the window in **development builds** (so QA can capture
on-device), turn on **Enable Instrumentation** in the Messages settings page
— it writes the `TUTAN_MESSAGES_DEBUG` define to the active build target.

When the window is closed, `MessagesInstrumentation.Enabled` is set back to
`false` and every hook short-circuits on the first branch — so even with the
defines present, an empty/closed window costs ~one `bool` check per
`Publish`.

### Programmatic access

`MessagesInstrumentation` is public and can be used to wire custom
diagnostics or in-game overlays:

```csharp
MessagesInstrumentation.Enabled = true;
MessagesInstrumentation.CapturePayloads = true;

// Snapshot the ring buffer (allocates a copy; do it off the hot path).
var records = MessagesInstrumentation.Snapshot();
foreach (var r in records)
    Debug.Log($"{r.Bus} {r.Op} {r.MessageType?.Name}");
```

---

## Inspector Support (Serialized Message References)

The package provides serializable wrappers — `EventReference` and
`CommandReference` — so designers can pick a message type and edit its
payload directly in the Inspector. Useful for hooking up `UnityEvent`s,
prototyping, or wiring data-driven triggers without writing publisher code.

> **Not recommended for runtime hot paths.** Publishing via a
> `MessageReference` involves JSON deserialization (`JsonUtility.FromJson`),
> `Activator.CreateInstance`, **boxing the struct**, and a reflected generic
> `Publish` invocation. This is intentionally the opposite of the zero-alloc
> dispatch path the rest of the bus provides. Use it for editor/authoring
> workflows, debug buttons, and tooling — not for per-frame gameplay
> dispatch.

### Usage

```csharp
using Tutan.Messages;
using UnityEngine;

public class TriggerZone : MonoBehaviour
{
    [SerializeField] EventReference   onEntered;   // dropdown of all IEvent structs
    [SerializeField] CommandReference onActivate;  // dropdown of all ICommand structs

    void OnTriggerEnter(Collider other)
    {
        onEntered.Publish();   // boxes + reflection — fine for a one-shot trigger
        onActivate.Publish();
    }
}
```

In the Inspector you get a type dropdown, a reflection-based field editor
for the struct's public fields, and a small **▶** button that synthesizes
and publishes the message immediately — handy for poking subscribers without
entering play mode logic.

### `[EventType]` / `[CommandType]` attributes

If you only need the *type* (not a payload), decorate a `string` field with
`[EventType]` or `[CommandType]` to get a dropdown that stores the
`AssemblyQualifiedName`:

```csharp
[EventType]   public string eventType;    // dropdown of all IEvent types
[CommandType] public string commandType;  // dropdown of all ICommand types
```

Resolve it at runtime with `Type.GetType(eventType)`.

### Supported field types in the inline editor

The inline reflection editor renders `int`, `float`, `bool`, `string`,
`Vector3`, `Color`, and any `enum`. Other types fall back to an
"Unsupported type" label — if you need them edited from the inspector,
either add support to `MessageReferenceDrawer.DrawField` or expose the
struct through a `[Serializable]` wrapper instead.

A field literally named `Timestamp` is skipped by the editor and is
auto-populated at publish time (`float` → `Time.time`, `double` →
`(double)Time.time`, `long` → `DateTime.UtcNow.Ticks`).
