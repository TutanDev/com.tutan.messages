[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · **Editor**

---

# Editor Tooling

Everything on this page is **editor-only / development-only**. The
instrumentation hooks are gated behind `[Conditional("UNITY_EDITOR")]` and
`[Conditional("TUTAN_MESSAGES_DEBUG")]`, so the C# compiler strips every
call site in release player builds. The inspector drawers live under
`Editor/` and are never compiled into a player.

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

To enable the window in **development builds** (so QA can capture on-device),
add the `TUTAN_MESSAGES_DEBUG` scripting define under **Project Settings →
Player → Scripting Define Symbols** for the target build. In the editor it is
always available because `UNITY_EDITOR` is always defined.

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
