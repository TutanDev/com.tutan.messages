# Changelog

All notable changes to `com.tutan.messages` will be documented in this file.

## [0.7.1] - 2026-05-25

### Changed
- **Messages Console** now captures payloads automatically while the window is
  open. The **Capture payloads** toolbar toggle (and its `EditorPrefs` entry)
  was removed — payload boxing is on whenever the window is open and incurs no
  cost once it is closed. `MessagesInstrumentation.CapturePayloads` remains
  available for programmatic use.
- **Messages Console** detail panel trimmed. The header now shows just the
  operation and bus (e.g. `Publish Event`) instead of repeating the full type
  name. The `Op` / `Bus` / `Frame` / `Thread` / `Time` field dump was removed,
  and the payload now renders in its own section directly below **Message
  Type** rather than at the top of the body.

## [0.7.0] - 2026-05-25

### Added
- **`ScriptFileField`** editor control (`[UxmlElement]`) — a clickable row that
  represents the C# source file backing a type. Single click pings/highlights
  the `.cs` asset in the Project window and selects it; double click opens it in
  the configured script editor. Resolves `Type` → `MonoScript` (and a type's
  full name → `Type`) with caching. Resolution works even when the file name
  differs from the type name — e.g. several message structs grouped in one
  file — by falling back to a source-text scan for the declaration when the
  fast file-name match misses. Shows a dimmed, non-interactive label only when
  no source file can be found at all.
- **Messages Console** detail panel now renders the message type and the
  captured subscribers as `ScriptFileField` rows instead of plain text, so you
  can jump straight from a record to the source of the message struct or any of
  its subscribers. The `Type:` text line and the textual subscriber dump were
  removed from the detail body in favor of these clickable rows.

### Changed
- **BREAKING — `[MessageType]` split into `[EventType]` and `[CommandType]`.**
  The single parameterized `[MessageType(typeof(...))]` attribute is replaced by
  two parameterless attributes that bake in the base type filter:
  - `[EventType]` — dropdown of all concrete `IEvent` types.
  - `[CommandType]` — dropdown of all concrete `ICommand` types.

  Migrate `[MessageType(typeof(IEvent))]` → `[EventType]` and
  `[MessageType(typeof(ICommand))]` → `[CommandType]`. The plain `[MessageType]`
  (defaulting to `IMessage`) no longer has a direct equivalent; pick the event or
  command variant. Field type, storage format (`AssemblyQualifiedName` in a
  `string`), and runtime resolution via `Type.GetType(...)` are unchanged.

## [0.5.0] - 2026-05-25

### Changed
- **BREAKING — "MessageBus" renamed to "Messages" throughout.** The package
  display name is `Messages`; everything that still carried the old `MessageBus`
  name has been brought in line:
  - Namespace `Tutan.MessageBus` → `Tutan.Messages` (and the matching sub-namespaces
    `Tutan.MessageBus.Editor` / `Tutan.MessageBus.Samples.*`).
  - Assemblies `Tutan.MessageBus`, `Tutan.MessageBus.Editor`, `Tutan.MessageBus.Tests`
    → `Tutan.Messages`, `Tutan.Messages.Editor`, `Tutan.Messages.Tests`.
  - Core type `MessageBus<TBase>` → `Messages<TBase>`; `MessageBusHost`,
    `MessageBusBootstrap`, `MessageBusInstrumentation`, and `MessageBusDebuggerWindow`
    → `MessagesHost`, `MessagesBootstrap`, `MessagesInstrumentation`, `MessagesDebuggerWindow`.
  - Scripting defines `TUTAN_MESSAGEBUS_DEBUG` and
    `TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP` → `TUTAN_MESSAGES_DEBUG` and
    `TUTAN_MESSAGES_DISABLE_AUTOBOOTSTRAP`.

  Update `using Tutan.MessageBus;` to `using Tutan.Messages;`, any references to
  the renamed types, and any project Scripting Define Symbols. The public
  `EventBus` / `CommandBus` facades and all method signatures are unchanged.

## [0.4.1] - 2026-05-25

### Fixed
- **Messages Console** subscriber list in the detail panel is now a snapshot
  captured at the instant a message was published or enqueued, instead of a
  live query of the bus performed when the row is selected. Previously,
  subscribing or unsubscribing after a message was sent would retroactively
  change the subscriber list shown for that past record. `Record` now carries
  a frozen `Subscriber[]` (token, target type, handler method), resolved to
  strings at capture time so the snapshot survives later unsubscription or GC.

### Changed
- The per-frame frame-counter update in `MessagesHost` now goes through a
  new `[Conditional]` `MessagesInstrumentation.SyncFrame(int)` instead of
  assigning the `CurrentFrame` field directly. This was the last
  instrumentation touchpoint that survived in release player builds; the
  direct assignment also forced the instrumentation's static constructor to
  run, eagerly allocating the ~256 KB record ring buffer even when it was
  never used. With the call stripped (no `UNITY_EDITOR` /
  `TUTAN_MESSAGES_DEBUG`), the type is never touched in release, the buffer
  is never allocated, and there is zero instrumentation cost in shipping
  builds.

## [0.4.0] - 2026-05-20

### Added
- **Messages Console** editor window (`Window → Tutan → Messages Console`).
  Virtualized log of recent `Subscribe`/`Unsubscribe`/`Publish`/`Enqueue` (and
  optional drain) operations with timestamp, frame, bus (E/C), op, and type.
  Row selection pretty-prints the payload and lists current subscribers for
  the selected message type. Toolbar exposes Pause, Clear, Capture payloads,
  Events/Commands toggles, per-op toggles, Auto-scroll, and full-type-name
  search. Filter selections and payload capture persist across domain reloads
  (including entering Play mode) and window reopen via `EditorPrefs`; the log
  scroll position and the list/detail splitter restore via `viewDataKey`.
- **`MessagesInstrumentation`** public static surface — `Enabled`,
  `CapturePayloads`, `Snapshot()` — for wiring custom diagnostics or in-game
  overlays. All hooks decorated with
  `[Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGES_DEBUG")]` so
  release player builds are unaffected (no branches, no allocations).
- **`TUTAN_MESSAGES_DEBUG`** scripting define — opt-in flag that enables
  the instrumentation hooks in development player builds for on-device QA
  capture. Editor builds always have instrumentation available via
  `UNITY_EDITOR`.
- **`EventReference` / `CommandReference`** — serializable wrappers for
  inspector-driven message authoring. Type dropdown, reflection-based field
  editor (`int`, `float`, `bool`, `string`, `Vector3`, `Color`, `enum`), and
  an inline publish button. Intended for editor/authoring workflows only —
  `Publish()` boxes the struct and uses reflection, so it is not safe for
  per-frame gameplay dispatch.
- **`[MessageType]`** attribute — decorate a `string` field to render a
  dropdown of message types in the Inspector; stores the selected
  `AssemblyQualifiedName`. Optional `Type` filter narrows the dropdown to
  `IEvent` or `ICommand` assignability.
- Inline editor convention: a field literally named `Timestamp` is skipped
  by the reflection editor and auto-populated at publish time (`float` →
  `Time.time`, `double` → `(double)Time.time`, `long` →
  `DateTime.UtcNow.Ticks`).

### Notes
- Instrumentation is off by default; the closed-window state holds
  `MessagesInstrumentation.Enabled = false`, so every hook short-circuits
  on its first branch (~one `bool` check per `Publish`) even when the
  defines are present.

## [0.3.0] - 2026-05-19

### Fixed
- **Concurrency race in channel storage.** `Subscribe`/`Publish`/`Unsubscribe`/
  `DrainQueues` previously accessed `_channels` lock-free while `Enqueue` held
  a lock, allowing the dictionary to be corrupted when a worker-thread
  `Enqueue` introduced a new channel type concurrently with a main-thread
  call. `_channels` is now a `ConcurrentDictionary<Type, ChannelBase>`; reads
  on the dispatch hot path remain lock-free.
- **Static-bus state leaks across Play sessions with Domain Reload disabled.**
  `EventBus` and `CommandBus` now reset on `RuntimeInitializeLoadType.SubsystemRegistration`.
- **`SubscriptionToken` equality ignored `MessageType`.** Tokens with the
  same `Id` from different message types (or different bus instances) now
  compare unequal. `GetHashCode` updated accordingly.

### Changed
- Documentation: `CommandBus` consistently described as **N:1** (many publishers,
  single subscriber). `EventBus` consistently described as **N:M** (was
  inconsistently called `1:N` in `package.json`).
- `ICommand` doc clarifies that the single-handler rule is enforced by
  `CommandBus`, not the core `Messages<TBase>`.

## [0.2.0] - 2026-05-18

### Removed
- **Live Debugger Editor window** and all associated runtime diagnostics
  (`MessagesDiagnostics`, `MessageEnvelope`, `ChannelStats`).
- `[CallerFilePath]` / `[CallerLineNumber]` / `[CallerMemberName]` parameters
  on `Publish<T>` / `Enqueue<T>`. These existed solely to power the debugger's
  click-to-source navigation.
- `Messages<TBase>(string busName)` constructor — the bus name was used
  only by the diagnostics layer.

The runtime assembly is now strictly the dispatcher: `Messages<TBase>`,
`EventBus`, `CommandBus`, `MessagesHost`, `MessagesBootstrap`, and the
message marker interfaces. No editor-only code paths remain in `Runtime/`.

## [0.1.0] - 2026-05-18

### Added
- Initial release.
- `Messages<TBase>` core with zero-allocation `ref`-based dispatch.
- `EventBus` (N:M fan-out) and `CommandBus` (N:1 — many publishers, single subscriber, duplicate-handler guard).
- `IMessage`, `IEvent`, `ICommand` marker interfaces. Messages are `unmanaged struct`.
- `SubscriptionToken` for deterministic unsubscription (no delegate-equality pitfalls).
- Thread-safe `Enqueue` + main-thread `DrainQueues` for cross-thread/cross-frame work.
- `MessagesHost` MonoBehaviour with auto-bootstrap via `RuntimeInitializeOnLoad`.
  Opt out with the `TUTAN_MESSAGES_DISABLE_AUTOBOOTSTRAP` scripting define.
- Profiler markers on `Publish` and `DrainQueues`.
- Three importable samples: BasicPubSub, ThreadedDispatch, XRHandGesture.
