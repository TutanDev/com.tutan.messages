[Home](index.md) · [Why](Messages.md) · [API Reference](API-Reference.md) · [Examples](Examples.md) · [Threading](Threading.md) · [Performance](Performance.md) · [Edge Cases](EdgeCases.md) · [Architecture](Architecture.md) · [Bootstrap](Bootstrap.md) · **Editor**

---

# Editor Tooling

This page covers two kinds of tooling:

- **Editor-only:** the Messages Console window and the inspector drawers live
  in the `Tutan.Messages.Editor` assembly under `Editor/` and are never
  compiled into a player. The instrumentation hooks the Console reads are
  gated behind `[Conditional("UNITY_EDITOR")]` and
  `[Conditional("TUTAN_MESSAGES_DEBUG")]`, so the C# compiler strips every call
  site in release player builds.
- **Runtime types:** `EventReference` / `CommandReference`, the
  `[EventType]` / `[CommandType]` attributes and `MessageTypeResolver` live in
  the runtime assembly and work in player builds. They are meant for authoring
  and tooling, not hot paths. The `MessagesInstrumentation` API is runtime too,
  but it only records where the hooks are compiled in — see
  [Programmatic access](#programmatic-access).

---

## Messages Console

The package ships with an editor window for live introspection of bus traffic:
**Window → Tutan → Messages Console**.

It is a single virtualized log of recent `Subscribe`, `Unsubscribe`, `Publish`,
`Enqueue`, and (optionally) drain operations with timestamp, frame, bus (E/C),
op, and type. A queued message appears twice: as an `Enqueue` row when it is
sent, and as a `Publish` row when the drain dispatches it. Selecting a row
pretty-prints the payload, handler details, and — for `Publish`/`Enqueue`
rows — the subscribers as they were **at the moment the message was sent** in
the right pane. This subscriber list is a snapshot frozen into the record at
fire time, not a live query, so subscribing or unsubscribing afterwards does
not change what a past record shows. The message type and each subscriber are
clickable rows: click to ping the script, double-click to open it (a lambda
handler resolves to the script of the class that declares it).

Toolbar: **Pause** (freeze the view; records keep arriving in the buffer),
**Clear** (empty the ring buffer), **Auto-scroll** (keep the newest record in
view), a search field that filters by full type name, and the filter toggles
**Events** / **Commands** and **Publish** / **Enqueue** / **Subs**
(Subscribe·Unsubscribe) / **Drains**. Drain records are off by default because
they are noisy. Filter choices persist across domain reloads, and reopening the
window lists the records still in the ring buffer.

While the window is open, every recorded `Publish`/`Enqueue` makes a few small
allocations: the boxed payload, so it can be inspected, plus a snapshot of the
subscribers. A drained queued message is recorded (and boxed) again as a
`Publish`. Recording is on automatically whenever the window is open and costs
nothing beyond a `bool` check once the window is closed.

### Runtime cost

The instrumentation hooks on `Messages.Publish` / `Enqueue` /
`Subscribe` / `Unsubscribe` / `DrainQueues` are decorated with:

```csharp
[Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]
```

so the C# compiler strips every call site at compile time when neither
define is set. The per-frame frame-counter sync in `MessagesHost` goes
through the same kind of `[Conditional]` method (`SyncFrame`), so it strips
too. The ~256 KB record ring buffer is only allocated by the first recorded
operation, so it is never allocated in a release player. **In release player
builds the bus runs exactly as before — no branches, no allocations, no
buffer.**

In the editor it is always available because `UNITY_EDITOR` is always
defined. To compile the hooks into a **development build** (so QA can capture
on-device), add `TUTAN_MESSAGES_DEBUG` to **Project Settings → Player →
Scripting Define Symbols** for the target platform.

When the window is closed, `MessagesInstrumentation.Enabled` is set back to
`false` and every hook short-circuits on the first branch — so even with the
defines present, an empty/closed window costs ~one `bool` check per
`Publish`.

### Programmatic access

`MessagesInstrumentation` is public runtime API — the Console window is just
one consumer of it. You can read the ring buffer yourself to drive an in-game
debug overlay, dump a trace to a log, or ship records to telemetry. That is
the whole point of using it **outside the editor**: to observe bus traffic
on a standalone / mobile / XR device build, where the editor Console can't
reach.

```csharp
MessagesInstrumentation.Enabled = true;

// Snapshot the ring buffer (allocates a copy; do it off the hot path).
var records = MessagesInstrumentation.Snapshot();
foreach (var r in records)
    Debug.Log($"{r.Bus} {r.Op} {r.MessageType?.Name}");
```

> **`Enabled` only produces records where the hooks are compiled in.** It is a
> plain toggle over the same `[Conditional]`-gated hooks described in **Runtime
> cost**, above — so in a normal release player build, where neither
> `UNITY_EDITOR` nor `TUTAN_MESSAGES_DEBUG` is defined, every `Record*` call
> site is stripped, so nothing is recorded and the ring buffer (allocated only
> by the first recorded operation) is never allocated. There, setting
> `Enabled = true` is a **silent no-op** and `Snapshot()` stays empty. To use
> this API outside the editor you must build with `TUTAN_MESSAGES_DEBUG` in the
> target platform's Scripting Define Symbols. It is a development / QA
> capability — not something to flip on in a shipping build.

Payload capture is not a separate switch: while `Enabled` is `true`, every
`Publish`/`Enqueue` record carries the boxed payload and a subscriber snapshot
(a few small allocations per record); with `Enabled` false the hooks return
after one `bool` check.

If you drain the queues from your own loop instead of `MessagesHost`, call
`MessagesInstrumentation.SyncFrame(Time.frameCount)` once per frame so records
carry the right frame number — see [Bootstrap](Bootstrap.md#queue-draining-automatic).

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

Arrays and lists (`EventReference[]`, `List<CommandReference>`) are supported;
each element gets the same drawer. The drawers are UI Toolkit only — they render
in the default Inspector (UI Toolkit since Unity 2022.2) but not inside a custom
IMGUI editor that draws the field with `EditorGUILayout.PropertyField`.

In the Inspector you get a type dropdown, a native UI-Toolkit field editor
for the struct's public fields, and a small **▶** button that synthesizes
and publishes the message immediately — handy for poking subscribers without
entering play mode logic. ▶ publishes a fresh copy of the stored reference,
so a `Publish()` override on an `EventReference` / `CommandReference` subclass
runs with that subclass's serialized fields, as at runtime. The struct's
fields are enumerated by reflection, but each one is rendered with its matching
UI-Toolkit control (e.g. `IntegerField`, `Vector3Field`, `EnumField`),
consistent with the rest of the editor tooling. The drawers follow undo/redo,
prefab reverts and edits made in other inspectors.

With several objects selected, the type dropdown writes to all of them. The
payload can be edited, and ▶ used, only while every selected object holds the
same type and payload; otherwise a note replaces the payload editor.

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
[EventType]   public string eventType;    // dropdown of all IEvent structs
[CommandType] public string commandType;  // dropdown of all ICommand structs
```

Resolve it at runtime with `MessageTypeResolver.Resolve(eventType)`:

```csharp
Type type = MessageTypeResolver.Resolve(eventType); // null if deleted/renamed
```

Prefer it over a bare `Type.GetType(eventType)`, which returns `null` once the
type's assembly has been renamed (e.g. the script moved into or out of an
`.asmdef`) since the name was serialized. The resolver falls back to looking up
the namespace-qualified name in every loaded assembly — the same resolution
`MessageReference.GetMessageType()` and the inspector drawers use. Resolve once
(at load time), not per frame: the fallback path scans loaded assemblies.

Only structs appear in the dropdown — the bus is constrained to
`where T : struct`, so a class implementing `IEvent` / `ICommand` could never
be published. Open generic structs (e.g. `EntityChanged<T>`) are not listed
either: without type arguments they can't be instantiated or published.

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

Only fields `JsonUtility` serializes can be authored. `readonly` and
`[NonSerialized]` public fields are listed as disabled "not serialized" rows,
because edits to them would never reach the stored JSON.

Other types render as a disabled "unsupported type" label and keep their
default/serialized value. To edit them, serialize the message struct as an
ordinary field on your own MonoBehaviour (drawn by Unity's default inspector)
and publish it from code, or embed the package (copy it into `Packages/`) to
extend `MessageReferenceDrawer`.

Every field is authored explicitly — including any field named `Timestamp`.
The reference publishes exactly the values you enter; no field is populated
implicitly at publish time.
