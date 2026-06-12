# Changelog

All notable changes to `com.tutan.messages` will be documented in this file.

## [1.1.0] - 2026-06-12

### Changed
- **BREAKING — `CommandBus.TryInstall(out string error, configure)` is replaced
  by `CommandBus.Install(configure)`, which returns an `InstallResult`.** The
  out-parameter-before-lambda signature made call sites awkward; the result
  struct keeps the no-throw contract and reads top-to-bottom:

  ```csharp
  var result = CommandBus.Install(r => r
      .Handle<PlaceOrder>(orderHandler.Handle)
      .Handle<MovePlayer>(movement.Handle));

  if (!result.Ok) Debug.LogError(result.Error);
  ```

  **`InstallResult`** is a readonly struct: `Ok` (bindings validated and swapped
  in atomically), `Error` (names the offending command type(s); null on
  success), and `HandlerCount` (number of handlers bound; 0 on failure).
  Semantics are unchanged — validation failures are reported as values, never
  exceptions; a failed install leaves the live bus untouched; calling again
  rebuilds the bus wholesale.

  **Migration:** `bool ok = CommandBus.TryInstall(out var error, r => ...)`
  becomes `var result = CommandBus.Install(r => ...)`; replace `ok` with
  `result.Ok` and `error` with `result.Error`. The `configure` callback and
  `CommandRegistry.Handle<T>` are unchanged.

## [1.0.2] - 2026-06-12

### Fixed
- **`DrainQueues` no longer allocates every frame.** Draining enumerated the
  channel `ConcurrentDictionary` directly, which allocates a class enumerator
  per call — with the auto-host that was two small heap allocations every
  frame, contradicting the zero-GC contract. Channels are now drained from a
  cached list that is rebuilt only when the channel set changes.
- **Subscription-list compaction heuristic was inverted.** The intent was to
  compact when at least 4 entries are dead *and* they make up a quarter of the
  list; the condition used `&&` in the skip branch, so a large list compacted
  (an O(n) sweep) after every 4 unsubscribes regardless of size. Mass
  unsubscription on big channels no longer pays repeated full-list sweeps.
- **Dispatch depth is now restored in a `finally`.** An exception escaping
  `Channel<T>.Publish` outside the per-handler catch would have left the
  re-entrancy counter stuck above zero, permanently disabling compaction for
  that channel.

### Changed
- `EventBus.Publish<T>(T)` / `CommandBus.Publish<T>(T)` forward by `ref` to the
  underlying bus, saving one of the two struct copies the by-value convenience
  overload used to make.
- `Channel<T>.DrainQueue` checks `ConcurrentQueue.IsEmpty` (O(1)) before
  `Count` (a cross-segment snapshot), so idle channels pay almost nothing
  per frame.
- Instrumentation: the `TotalEver` counter is incremented inside the ring-buffer
  lock, so the Messages Console's incremental catch-up can no longer observe a
  total ahead of the buffer contents (which could skip or duplicate records in
  the log view).

## [1.0.1] - 2026-06-12

### Docs
- **Stopped prescribing `Reset()` for scene transitions.** Whether (and when) to
  reset the buses across scene loads depends on the app's scene architecture —
  additive scenes, persistent managers, and DontDestroyOnLoad roots all want
  different lifetimes. The docs now document `Reset()` as a test-teardown tool
  and leave scene-lifecycle policy to the user: removed the "Scene Transition
  Cleanup" example (`docs/Examples.md`), the scene-transition framing in
  `docs/Bootstrap.md`, `docs/Threading.md`, `docs/API-Reference.md`, and the
  `EventBus.Reset`/`CommandBus.Reset` doc comments. Behavior is unchanged.
- Removed a stale reference to `decisions/CommandBus.md` from
  `docs/API-Reference.md` — the decisions folder is not shipped with the package.

## [1.0.0] - 2026-06-12

First stable release. Hardening pass over the runtime, editor tooling, and docs
ahead of the Asset Store submission — no breaking API changes since 0.14.0.

### Fixed
- **The auto-spawned `[MessagesHost]` no longer leaks across editor play
  sessions.** It was created with `HideFlags.HideAndDontSave`, which excludes an
  object from the editor's play-mode cleanup — each play session left another
  hidden host behind. It now uses `HideFlags.HideInHierarchy` only
  (`DontDestroyOnLoad` already provides scene persistence).
- **Duplicate `MessagesHost` instances are rejected.** If a host is already
  active (e.g. a manually placed one coexisting with the auto-spawned one), the
  newcomer logs a warning and destroys itself, so the buses are drained exactly
  once per frame.
- **Message-type dropdowns (`EventReference`/`CommandReference`,
  `[EventType]`/`[CommandType]`) mis-mapped duplicate type names.** Two message
  structs with the same name in different namespaces rendered as identical
  entries, and picking the second silently stored the first. The popups are now
  index-based, and colliding names are shown fully qualified.

### Added
- **Main-thread guard in editor and development builds.** `Publish`,
  `Subscribe`, `Subscription.Dispose`, and `DrainQueues` now log an error when
  called off the main thread (the classic `async` continuation mistake) instead
  of silently corrupting the subscription list. The check is `[Conditional]` —
  release player builds strip it entirely.
- **`DrainQueues` is bounded per frame.** A drain processes at most the
  messages that were pending when it started; a handler that enqueues the same
  message type during dispatch extends the next frame's drain instead of the
  current one, so a self-perpetuating handler can no longer hang the frame.
  Documented in `docs/EdgeCases.md`.

### Changed
- `EventBus`/`CommandBus` hold their bus instance in a `volatile` field so a
  bus swapped in by `Reset()`/`TryInstall` is promptly visible to worker
  threads using `Enqueue`. `docs/Threading.md` now also documents the one
  thread-safety carve-out: an `Enqueue` racing a `Reset()`/`TryInstall` swap
  can land in the discarded bus — quiesce workers before resetting.

### Docs
- Removed the stale `MessagesInstrumentation.CapturePayloads` reference from
  `docs/Editor.md` — the field no longer exists; payload capture is automatic
  whenever instrumentation is enabled.
- Documented that structs authored through `EventReference`/`CommandReference`
  must be `[Serializable]` (the payload round-trips through `JsonUtility`).
- Sample: clarified the intended scoring — the final score is the fatal decay
  tick, which grows the longer you survive (`ScoreModel`/`MenuHud` comments,
  sample `README`); renamed the misleading `DecayPerSecond` field to
  `_nextDecayDelta`; corrected the stale "UI built entirely in code" comment
  and the package `README`'s "drop the component on a GameObject" instruction
  (the sample is scene-based).
- `package.json` description updated — it still advertised the
  pre-0.14.0 "integer subscription tokens".
- Fixed `Runtime/IMessage.cs` source encoding (Windows-1252 em-dashes rendered
  as `�`); the file is now UTF-8.

## [0.14.0] - 2026-06-12

### Fixed
- **Worker threads racing on the first `Enqueue` of a message type could lose a
  message.** The per-channel pending queue was lazily created with a non-atomic
  `??=`; two threads observing it as null would each create a queue, and the
  loser's message landed in a queue that was immediately overwritten. The lazy
  init is now an `Interlocked.CompareExchange`, and `DrainQueue` reads the queue
  field with `Volatile.Read` so a queue created on a worker thread is visible to
  the main thread no later than the next frame's drain. Covered by a new
  multi-thread first-enqueue stress test.

### Changed
- **BREAKING — `Subscribe` now returns a disposable `Subscription` instead of a
  `SubscriptionToken`, and disposal is the one way to unsubscribe.**
  `EventBus.Unsubscribe(token)` / `MessageBus<TBase>.Unsubscribe(token)` are no
  longer public, and `SubscriptionToken` is now internal (it survives as the
  handle's identity). One subscribe method, one handle type — no parallel
  token/scoped APIs. `CommandBus` is unaffected (its handlers are bound via
  `TryInstall`).

  **`Subscription`** is an `IDisposable` struct pairing the subscription's
  identity with the bus instance that issued it. `Dispose()` unsubscribes; it
  is idempotent, safe during dispatch, and a harmless no-op after the issuing
  bus was `Reset()` (it can never remove an unrelated subscription from the
  replacement bus). Zero allocation beyond `Subscribe` itself.

  **Migration:** `SubscriptionToken _token` fields become `Subscription
  _subscription`; `EventBus.Unsubscribe(_token)` becomes
  `_subscription.Dispose()`. Subscriptions that live as long as their component
  can drop the field entirely — see `AddTo` below.

### Added
- **Subscription lifetime helpers** for the new handle:
  - **`SubscriptionBag`** — collects subscriptions and disposes them as a group
    (`Dispose()`/`Clear()`); reusable after disposal. One bag per system instead
    of one handle field per subscription.
  - **`AddTo(...)`** fluent extensions — `AddTo(bag)`, `AddTo(gameObject)`, and
    `AddTo(component)` tie a subscription to a bag or to a GameObject's
    lifetime. The GameObject overloads attach one hidden **`SubscriptionAnchor`**
    component that disposes its bag in `OnDestroy`, so a MonoBehaviour
    subscription becomes one line:
    `EventBus.Subscribe<PlayerMoved>(OnMoved).AddTo(this);` — no handle field,
    no `OnDestroy` override.
- Docs: `README` quick start and features, `docs/API-Reference.md` (new
  **Subscription Lifetime** section), and `docs/Examples.md` (new
  **Subscription Lifetimes** example) cover the new surface; the **Basic
  Publish / Subscribe** sample now uses `.AddTo(this)` (composition root) and an
  explicit `Dispose` in `OnDisable` (`ScoreHud`). `docs/Threading.md` diagram
  updated to the actual `GetOrAdd` + CAS enqueue path (it still showed the
  pre-0.3.0 `lock(_queueLock)` design).

## [0.13.0] - 2026-06-05

### Removed
- **`ICommandHandler` / `ICommandHandler<T>` declarative interfaces.** They only
  fed the reflection auto-install path and the editor audit view, both removed
  below. Command handlers are now plain methods matching `MessageHandler<T>`
  (`void Handle(ref T)`), bound at the composition root.
- **Reflection-based command auto-install** (`TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS`).
  Scanning every assembly and `Activator.CreateInstance`-ing handlers only worked
  for parameterless handlers and fought DI containers. Bind handlers explicitly
  through `CommandBus.TryInstall` instead.
- **The Messages project settings page** (**Project Settings → Tutan → Messages**)
  and its `MessagesProjectSettings` asset. It mutated `PlayerSettings` scripting
  defines, which caused recompile churn and VCS noise. No more define-juggling UI.
- **The Commands authoring view** (the edit-time orphan / N:1 audit foldout). It
  depended on `ICommandHandler<T>`; the N:1 rule is still enforced at install time
  by `CommandBus.TryInstall`.

### Changed
- **Queue draining is now on by default with zero configuration.** `MessagesBootstrap`
  always spawns the persistent `[MessagesHost]` at startup; define
  `TUTAN_MESSAGES_NO_AUTO_HOST` to opt out and own the drain loop yourself. (Previously
  gated behind the **Auto-Install Drainers** toggle / `TUTAN_MESSAGES_AUTOINSTALL_DRAINERS`.)
- **Instrumentation in development builds** is enabled by adding the
  `TUTAN_MESSAGES_DEBUG` define in **Player → Scripting Define Symbols** directly,
  rather than via the removed settings page. In the editor it remains always available.
- **Basic Publish / Subscribe sample** now binds its command handlers at its own
  composition root: `BasicPubSubSample.Awake` news up `ScoreModel`/`MenuModel` and
  calls one `CommandBus.TryInstall`. No defines or settings to enable — drop the
  component on a GameObject and press Play.
- Docs (`README`, `docs/Bootstrap.md`, `docs/Editor.md`, `docs/Examples.md`, sample
  `README`) updated to match the trimmed surface.

### Migration
- Replace `class Foo : ICommandHandler<MyCommand>` with a plain `class Foo` exposing
  `void Handle(ref MyCommand cmd)`; the method signature is unchanged.
- If you relied on auto-install, add an explicit composition-root call:
  `CommandBus.TryInstall(out var error, r => r.Handle<MyCommand>(foo.Handle));`.
- If you had **Auto-Install Drainers** off and drained manually, define
  `TUTAN_MESSAGES_NO_AUTO_HOST` to keep that behavior.

## [0.12.2] - 2026-06-03

### Changed
- **Basic Publish / Subscribe sample now binds its command handlers through the
  auto-install bootstrap instead of an explicit composition-root `TryInstall`.**
  `ScoreModel` and `MenuModel` are discovered and bound from their
  `ICommandHandler<T>` interfaces when **Auto-Install Command Bus** and
  **Auto-Install Drainers** are enabled (**Project Settings → Tutan → Messages**), so
  the sample carries no hand-written wiring code. Inline doc-comments, the sample
  `README`, the package `README`, `docs/Examples.md`, and `docs/Bootstrap.md` were
  updated to match, and the sample `README` now calls out the two required defines up
  front — without them no handler is bound and the buttons do nothing.

### Fixed
- Sample docs: the score button is `AdjustScore +1` (the `README` previously said +10).

## [0.12.1] - 2026-05-30

### Changed
- **Docs now cover the Basic Publish / Subscribe sample.** `docs/Examples.md` gained
  a callout mapping each sample file to the section that explains it (and the Package
  Manager import path), and the queued-dispatch example now shows `CommandBus.Enqueue`
  — the same `AdjustScore` command arriving from both the button (`Publish`, main
  thread) and `ScoreDecayWorker` (`Enqueue`, background thread) reaching the one
  handler. `docs/index.md` points to the runnable sample.

## [0.12.0] - 2026-05-29

### Changed
- **Consolidated the package to a single sample.** The **Basic Publish / Subscribe**
  sample was rebuilt into one self-contained demo that exercises the whole library:
  a code-built UI with a score label and a button, a `ScoreModel` that is the single
  `AdjustScore` command handler and the publisher of `ScoreChanged` events, and a
  `ScoreDecayWorker` background thread that `Enqueue`s commands off the main thread.
  Drop the `BasicPubSubSample` component on a GameObject and press Play — no scene
  wiring. It now covers the CommandBus (N:1), the EventBus (N:M), the composition-root
  `TryInstall` pattern, and thread-safe `Enqueue`/drain in one place.

### Removed
- The **Threaded Dispatch** and **XR Hand Gesture** samples. Their concepts
  (off-thread `Enqueue` + main-thread drain, and one publisher fanning out to many
  decoupled subscribers) are now folded into the single Basic Publish / Subscribe
  sample.

## [0.11.0] - 2026-05-27

### Added
- **`ICommandHandler<T>`** (`Tutan.Messages`) — a declarative contract a class
  implements to state that it handles command `T`:

  ```csharp
  public sealed class MovementManager : ICommandHandler<MovePlayer>
  {
      public void Handle(ref MovePlayer cmd) { /* ... */ }
  }

  // Composition root — Handle matches MessageHandler<T>, so it binds directly:
  CommandBus.TryInstall(out var error, r => r.Handle<MovePlayer>(movement.Handle));
  ```

  The interface is **declarative only** — it does *not* auto-register. Handlers are
  still bound at the composition root via `CommandBus.TryInstall` exactly as before;
  `ICommandHandler<T>` adds a discoverable, self-documenting marker on top. A
  non-generic `ICommandHandler` base is included as the tooling discovery seam.
  The **BasicPubSub** sample's `ScoreBoard` now implements `ICommandHandler<ResetScore>`
  to demonstrate the pattern.
- **Commands** editor window (`Window → Tutan → Commands`). A static, edit-time
  audit of the command → handler routing table: it lists every `ICommand` type
  found via `TypeCache` as a card, resolves its handler(s) through
  `ICommandHandler<T>`, and renders the command and each handler as clickable
  `ScriptFileField` rows (single-click pings the `.cs`, double-click opens it).
  Each card is flagged when a command has **no handler** (orphan) or **more than
  one** (an N:1 violation). Toolbar offers a name search, an **Only warnings**
  filter (persisted in `EditorPrefs`), and a **Refresh** re-scan. This restores
  the orphan/duplicate routing audit that the removed `CommandBusProfile`
  inspector provided in 0.9.0, now driven by the handler interface rather than a
  module-discovery layer.

### Changed
- **`ScriptFileField` now carries its own stylesheet** (`Editor/ScriptFileField.uss`),
  loaded by the control itself, so every window that uses it renders identically
  without copying the `.tutan-script-field*` rules into each window's USS. The
  Messages Console's stylesheet no longer defines those rules.

## [0.10.0] - 2026-05-27

### Removed
- **Data-driven `CommandBus` composition (the `ICommandModule` / `CommandBusProfile`
  layer added in 0.9.0).** The module-discovery subsystem is gone; compose the bus by
  calling `CommandBus.TryInstall` directly at the composition root and binding instance
  handlers there. Removed types, all from `Tutan.Messages`:
  - `ICommandModule`
  - `CommandBusComposer`
  - `CommandBusProfile` (and its `Create ▸ Tutan ▸ Command Bus Profile` asset menu)
  - `CommandBusInstaller`
  - `IServiceProvider.GetService<T>()` / `GetRequiredService<T>()` extensions
    (`ServiceProviderExtensions`)

  **Migration:** replace `CommandBusComposer.Compose(profile, services, ...)` (and any
  `CommandBusInstaller` on a boot object) with a single `CommandBus.TryInstall(out err,
  r => r.Handle<T>(handler) ...)` call. Move each module's `Register` body into that
  callback, resolving collaborators however your app already does. `CommandBus`,
  `CommandRegistry`, and `CommandBus.TryInstall` are unchanged — this only removes the
  discovery layer that sat on top of them. Delete any `CommandBusProfile` assets.

## [0.9.0] - 2026-05-26

### Added
- **Data-driven `CommandBus` composition via a `CommandBusProfile` asset.** Instead of
  hand-listing module registrations in code, you now declare command handlers in
  `ICommandModule` implementations and pick which assemblies to scan from a profile asset
  edited in the Project window (`Create ▸ Tutan ▸ Command Bus Profile`).

  ```csharp
  public sealed class PoolsModule : ICommandModule
  {
      public void Register(CommandRegistry registry, IServiceProvider services)
      {
          var pools = services.GetRequiredService<PoolsManager>();
          registry.Handle<JoinPool>(pools.OnJoin)
                  .Handle<LeavePool>(pools.OnLeave);
      }
  }

  // Composition root — once at boot, before the first publish:
  if (!CommandBusComposer.Compose(profile, services, out var error))
      Debug.LogError(error);
  ```

  New types, all in `Tutan.Messages`:
  - **`ICommandModule`** — `Register(CommandRegistry, IServiceProvider)`; the unit of
    declared command→handler binding. Discovered by interface, instantiated via a public
    parameterless constructor.
  - **`CommandBusProfile`** — `ScriptableObject` listing the assemblies to scan. Its custom
    inspector lists every `ICommand` type found in those assemblies and flags any **orphan**
    (no handler) or **duplicate** (two modules) — an edit-time audit of the routing table.
  - **`CommandBusComposer.Compose(profile, services, out error)`** — discovers all modules
    in the profile's assemblies and runs them into one `CommandRegistry` through a single
    `CommandBus.TryInstall`, so N:1 is still validated across the union and the install stays
    atomic.
  - **`CommandBusInstaller`** — optional `MonoBehaviour` holding an explicit profile
    reference; composes once in `Awake` (or call `Compose(services)` yourself when handlers
    need injected dependencies). One per app.
  - **`IServiceProvider.GetService<T>()` / `GetRequiredService<T>()`** extensions — the seam
    for the implementation axis (inject the selected backend into a module). The package
    takes no DI dependency; supply any `IServiceProvider`.

  This is additive — manual `CommandBus.TryInstall` is unchanged and still works. See
  `decisions/CommandBus.md` (Amendment, 2026-05-26) for how this reconciles with the
  boot-once-frozen model.

## [0.8.0] - 2026-05-26

### Changed
- **BREAKING — `CommandBus` handlers are now bound at the composition root, and
  the N:1 rule no longer throws.** `CommandBus.Subscribe<T>` and
  `CommandBus.Unsubscribe` are removed. Instead, declare every command handler in
  one place via the new:

  ```csharp
  bool ok = CommandBus.TryInstall(out string error, r => r
      .Handle<PlaceOrder>(orderHandler.Handle)
      .Handle<MovePlayer>(movement.Handle));

  if (!ok) Debug.LogError(error); // names the offending command type(s)
  ```

  A duplicate command type or a null handler is reported as `false` + an `error`
  string (the previous `InvalidOperationException` on a second `Subscribe` is
  gone). `TryInstall` is atomic — a failed install leaves the currently installed
  handlers untouched — and calling it again rebuilds the command bus wholesale.
  `Publish` / `Enqueue` / `DrainQueues` / `Reset` are unchanged, so publishers do
  not change.

  **Migration:** move each `CommandBus.Subscribe<T>(handler)` out of `OnEnable`
  (or wherever it lived) into a single startup composition root that calls
  `CommandBus.TryInstall(... r.Handle<T>(handler) ...)`. Drop the matching
  `CommandBus.Unsubscribe` calls; use `CommandBus.Reset()` or re-`TryInstall` to
  rebind. **`EventBus` is unchanged** — it remains a mutable, subscribe-anytime,
  N:M bus with `Subscribe`/`Unsubscribe`.

### Added
- **`CommandRegistry`** — the fluent builder passed to `CommandBus.TryInstall`.
  `Handle<T>(MessageHandler<T>)` binds the single handler for command type `T`.

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
  - `MessageBusHost`, `MessageBusBootstrap`, `MessageBusInstrumentation`, and
    `MessageBusDebuggerWindow` → `MessagesHost`, `MessagesBootstrap`,
    `MessagesInstrumentation`, `MessagesDebuggerWindow`. The core generic type
    `MessageBus<TBase>` keeps its name.
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
  `CommandBus`, not the core `MessageBus<TBase>`.

## [0.2.0] - 2026-05-18

### Removed
- **Live Debugger Editor window** and all associated runtime diagnostics
  (`MessagesDiagnostics`, `MessageEnvelope`, `ChannelStats`).
- `[CallerFilePath]` / `[CallerLineNumber]` / `[CallerMemberName]` parameters
  on `Publish<T>` / `Enqueue<T>`. These existed solely to power the debugger's
  click-to-source navigation.
- `MessageBus<TBase>(string busName)` constructor — the bus name was used
  only by the diagnostics layer.

The runtime assembly is now strictly the dispatcher: `MessageBus<TBase>`,
`EventBus`, `CommandBus`, `MessagesHost`, `MessagesBootstrap`, and the
message marker interfaces. No editor-only code paths remain in `Runtime/`.

## [0.1.0] - 2026-05-18

### Added
- Initial release.
- `MessageBus<TBase>` core with zero-allocation `ref`-based dispatch.
- `EventBus` (N:M fan-out) and `CommandBus` (N:1 — many publishers, single subscriber, duplicate-handler guard).
- `IMessage`, `IEvent`, `ICommand` marker interfaces. Messages are `unmanaged struct`.
- `SubscriptionToken` for deterministic unsubscription (no delegate-equality pitfalls).
- Thread-safe `Enqueue` + main-thread `DrainQueues` for cross-thread/cross-frame work.
- `MessagesHost` MonoBehaviour with auto-bootstrap via `RuntimeInitializeOnLoad`.
  Opt out with the `TUTAN_MESSAGES_DISABLE_AUTOBOOTSTRAP` scripting define.
- Profiler markers on `Publish` and `DrainQueues`.
- Three importable samples: BasicPubSub, ThreadedDispatch, XRHandGesture.
