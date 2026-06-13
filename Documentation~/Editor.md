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

In the editor it is always available because `UNITY_EDITOR` is always
defined. To compile the hooks into a **development build** (so QA can capture
on-device), add `TUTAN_MESSAGES_DEBUG` to **Project Settings → Player →
Scripting Define Symbols** for the target platform.

When the window is closed, `MessagesInstrumentation.Enabled` is set back to
`false` and every hook short-circuits on the first branch — so even with the
defines present, an empty/closed window costs ~one `bool` check per
`Publish`.

### Programmatic access

`MessagesInstrumentation` is public and can be used to wire custom
diagnostics or in-game overlays:

```csharp
MessagesInstrumentation.Enabled = true;

// Snapshot the ring buffer (allocates a copy; do it off the hot path).
var records = MessagesInstrumentation.Snapshot();
foreach (var r in records)
    Debug.Log($"{r.Bus} {r.Op} {r.MessageType?.Name}");
```

Payload capture is not a separate switch: while `Enabled` is `true`, every
`Publish`/`Enqueue` record carries the boxed payload (one boxing allocation
per record); with `Enabled` false there is no cost at all.

---

## Inspector Support (Serialized Message References)

The package provides serializable wrappers — `EventReference` and
`CommandReference` — so designers can pick a message type and edit its
payload directly in the Inspector. Useful for hooking up `UnityEvent`s,
prototyping, or wiring data-driven triggers without writing publisher code.

> **Not recommended for runtime hot paths.** Publishing via a
> `MessageReference` involves JSON deserialization (`JsonUtility.FromJson`),
> `Activator.CreateInstance`, and **boxing the struct** before it is dispatched
> through the non-generic `PublishBoxed` seam (one unbox inside the channel).
> This is intentionally the opposite of the zero-alloc dispatch path the rest
> of the bus provides. Use it for editor/authoring workflows, debug buttons,
> and tooling — not for per-frame gameplay dispatch.

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

In the Inspector you get a type dropdown, a native UI-Toolkit field editor
for the struct's public fields, and a small **▶** button that synthesizes
and publishes the message immediately — handy for poking subscribers without
entering play mode logic. The struct's fields are enumerated by reflection,
but each one is rendered with its matching UI-Toolkit control (e.g.
`IntegerField`, `Vector3Field`, `EnumField`), consistent with the rest of the
editor tooling.

> **Mark referenced structs `[Serializable]`.** The payload round-trips
> through `JsonUtility`, which only serializes plain structs that carry the
> `[Serializable]` attribute. A message struct without it still works on the
> bus, but its payload cannot be edited through a reference — the values
> silently stay at their defaults.

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

The inline editor renders these field types with native UI-Toolkit controls:

| Field type | Control |
|------------|---------|
| `int` | `IntegerField` |
| `long` | `LongField` |
| `float` | `FloatField` |
| `double` | `DoubleField` |
| `bool` | `Toggle` |
| `string` | `TextField` |
| `enum` | `EnumField` |
| `Vector2` / `Vector3` / `Vector4` | `Vector2Field` / `Vector3Field` / `Vector4Field` |
| `Vector2Int` / `Vector3Int` | `Vector2IntField` / `Vector3IntField` |
| `Color` | `ColorField` |
| `Quaternion` | `Vector3Field` (edited as euler angles) |

Other types fall back to a disabled "unsupported type" label — if you need
them edited from the inspector, either add a case to
`MessageReferenceDrawer.CreateFieldElement` or expose the struct through a
`[Serializable]` wrapper instead.

Every field is authored explicitly — including any field named `Timestamp`.
The reference publishes exactly the values you enter; no field is populated
implicitly at publish time.
