# Changelog

All notable changes to `com.tutan.messagebus` will be documented in this file.

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
- The per-frame frame-counter update in `MessageBusHost` now goes through a
  new `[Conditional]` `MessageBusInstrumentation.SyncFrame(int)` instead of
  assigning the `CurrentFrame` field directly. This was the last
  instrumentation touchpoint that survived in release player builds; the
  direct assignment also forced the instrumentation's static constructor to
  run, eagerly allocating the ~256 KB record ring buffer even when it was
  never used. With the call stripped (no `UNITY_EDITOR` /
  `TUTAN_MESSAGEBUS_DEBUG`), the type is never touched in release, the buffer
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
- **`MessageBusInstrumentation`** public static surface — `Enabled`,
  `CapturePayloads`, `Snapshot()` — for wiring custom diagnostics or in-game
  overlays. All hooks decorated with
  `[Conditional("UNITY_EDITOR"), Conditional("TUTAN_MESSAGEBUS_DEBUG")]` so
  release player builds are unaffected (no branches, no allocations).
- **`TUTAN_MESSAGEBUS_DEBUG`** scripting define — opt-in flag that enables
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
  `MessageBusInstrumentation.Enabled = false`, so every hook short-circuits
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
  `CommandBus`, not the core `MessageBus<TBase>`.

## [0.2.0] - 2026-05-18

### Removed
- **Live Debugger Editor window** and all associated runtime diagnostics
  (`MessageBusDiagnostics`, `MessageEnvelope`, `ChannelStats`).
- `[CallerFilePath]` / `[CallerLineNumber]` / `[CallerMemberName]` parameters
  on `Publish<T>` / `Enqueue<T>`. These existed solely to power the debugger's
  click-to-source navigation.
- `MessageBus<TBase>(string busName)` constructor — the bus name was used
  only by the diagnostics layer.

The runtime assembly is now strictly the dispatcher: `MessageBus<TBase>`,
`EventBus`, `CommandBus`, `MessageBusHost`, `MessageBusBootstrap`, and the
message marker interfaces. No editor-only code paths remain in `Runtime/`.

## [0.1.0] - 2026-05-18

### Added
- Initial release.
- `MessageBus<TBase>` core with zero-allocation `ref`-based dispatch.
- `EventBus` (N:M fan-out) and `CommandBus` (N:1 — many publishers, single subscriber, duplicate-handler guard).
- `IMessage`, `IEvent`, `ICommand` marker interfaces. Messages are `unmanaged struct`.
- `SubscriptionToken` for deterministic unsubscription (no delegate-equality pitfalls).
- Thread-safe `Enqueue` + main-thread `DrainQueues` for cross-thread/cross-frame work.
- `MessageBusHost` MonoBehaviour with auto-bootstrap via `RuntimeInitializeOnLoad`.
  Opt out with the `TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP` scripting define.
- Profiler markers on `Publish` and `DrainQueues`.
- Three importable samples: BasicPubSub, ThreadedDispatch, XRHandGesture.
