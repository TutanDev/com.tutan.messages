# Changelog

All notable changes to `com.tutan.messages` will be documented in this file.

## [1.0.0] - 2026-10-07

First public Asset Store release. Requires Unity 6000.3 LTS or newer. The public API is
unchanged from 0.19.0 apart from the additive `MessageTypeResolver`; the behavior
changes are listed below. Entries below 1.0.0 are the pre-release history.

### Added
- **`MessageTypeResolver.Resolve(string)`** — public, drift-tolerant resolution of a
  stored message type name (the `AssemblyQualifiedName` written by `[EventType]` /
  `[CommandType]` fields and `EventReference` / `CommandReference`). Use it instead of
  hand-rolling a `Type.GetType` fallback.
- **`CommandBus.Install` warns when it discards queued commands.** A successful install,
  like `Reset`, replaces the bus, so commands still waiting for the next drain are
  dropped — including on the first install. The editor and development builds now log
  a warning naming their types. Install before anything enqueues: from a
  `BeforeSceneLoad` `[RuntimeInitializeOnLoadMethod]`, or from a bootstrap object with
  an early Script Execution Order.

### Changed
- **The minimum Unity version is now 6000.3 LTS** (`"unity": "6000.3"`,
  `"unityRelease": "0f1"`). The manifest declared 6000.1 without the `unityRelease`
  field that Asset Store package validation requires, and 6000.1.0f1–6000.1.4f1 drop
  the hidden `SubscriptionAnchor`'s hide flags when `AddTo` runs in `Awake` /
  `OnEnable` (Unity issue UUM-96314).
- **`package.json` declares `com.unity.test-framework` 1.6.0 and `com.unity.ext.nunit`
  2.0.5**, the versions that ship with Unity 6000.3. The package ships its EditMode tests,
  and Asset Store validation requires every package their assembly references to be
  declared. The runtime itself still needs only the built-in JSONSerialize module
  (enabled by default). The description is shortened to the Asset Store's
  200-character limit.
- **The main-thread guard also covers edit mode.** It now captures the main thread
  after every editor domain reload, not only on entering Play Mode, so EditMode tests,
  `[ExecuteAlways]` scripts and editor tools that call `Publish`, `Subscribe`,
  `Subscription.Dispose` or `DrainQueues` from a worker thread get the same error.
- **`MessagesInstrumentation` allocates its ring buffer on the first recorded
  operation**, not in its type initializer, and `SetCapacity` no longer allocates. A
  release player strips every record hook, so it never allocates the ~256 KB buffer,
  even if your code reads `Enabled`, `Count` or `Snapshot()`. `Capacity` reports the
  configured value.
- Tests: the EditMode suite no longer needs the Physics module or a closed Messages
  Console, and now covers the allocation-free queued drain, the bounded drain,
  edit-mode anchor disposal, standalone-bus record tagging and the `Install` warning.
- Docs: requirements now state Unity 6000.3 LTS, the built-in JSONSerialize module and
  the Test Framework dependency, with uGUI needed by the sample only. Support is by
  email.
- Docs: cross-page links in `Documentation~` carry the `.md` extension, so they resolve
  on GitHub and in offline Markdown viewers. The API Reference lists the full public
  surface, editor API included.
- Docs: corrected stale or inaccurate claims. Commands have *at most* one handler,
  validated at install time, and an unbound command is a silent no-op. Subscriptions
  are disposable handles, not "integer tokens". Channels live in a
  `ConcurrentDictionary` and queues are `ConcurrentQueue`. `Enqueue` copies a message
  into the queue's reusable buffer, which the GC traces (it is not "never heap-stored",
  and not a "GC root"). `Publish` returns after one lookup only when the type has no
  channel. Pre-warming removes first-use allocation, not all allocation. The Messages
  Console's toolbar labels and per-record allocations are described as they are. The
  removed "scene cleanup" example is no longer advertised.
- Docs: `MessagesInstrumentation.Enabled` is a no-op in a release player (the hooks are
  `[Conditional]` on `UNITY_EDITOR` / `TUTAN_MESSAGES_DEBUG`), on-device capture needs
  `TUTAN_MESSAGES_DEBUG`, and a custom drain loop should call `SyncFrame` once per frame.
- Docs: Edge Cases documents the never-activated-GameObject `AddTo` caveat, the
  `SubsystemRegistration` reset ordering, and that `Install` and `Reset` discard queued
  commands. Performance documents queue pre-warming.
- XML docs added to previously undocumented public members (`MessageReference`,
  attribute `BaseType`, editor drawers, `ScriptFileField`, `MessagesConsoleWindow`).

### Fixed
- **Queued dispatch allocated on every drain under a steady stream.** `DrainQueues`
  sized each drain with `ConcurrentQueue.Count`, which in the `ConcurrentQueue` Unity's
  Mono uses freezes the queue's segments once it holds more than two, so the next
  `Enqueue` started a new segment. The budget now comes from enqueue/dequeue counters,
  and a steady or bursty queued stream settles to zero allocation after warm-up. A
  handler that enqueues its own type is still drained on the next frame.
- **A handler that enqueued its own type and then called `DrainQueues` overflowed the
  stack.** Each nested drain started with a fresh budget of one and recursed once per
  message. A `DrainQueues` call made from inside a handler now skips the channel that
  is already draining (other message types still drain), so the per-drain bound holds
  under re-entrancy.
- **`new MessageBus<ICommand>()` recorded its traffic as `Event`.** A standalone bus now
  tags its instrumentation records `Command` when `TBase` is or derives from
  `ICommand`, `Event` otherwise.
- **`SubscriptionAnchor` is now actually hidden.** It was documented as hidden but
  only hidden from the Add Component menu; it showed in the Inspector, and an
  edit-mode `AddTo(gameObject)` (e.g. from an `[ExecuteAlways]` script) saved an empty
  anchor into the scene. It now carries `HideInInspector | DontSaveInEditor`.
- **Edit-mode `AddTo(gameObject)` subscriptions survived the GameObject.**
  `SubscriptionAnchor` is now `[ExecuteAlways]`, so destroying its GameObject in edit
  mode disposes the subscriptions tied to it.
- **Assembly-drift fallback for stored message types never matched.**
  `MessageReference.GetMessageType()` and the editor's `ScriptFileField.ResolveType`
  retried a failed `Type.GetType` by passing the *assembly-qualified* name to
  `Assembly.GetType`, which rejects assembly-qualified input and returns null. A
  message struct moved into another assembly (e.g. into or out of an `.asmdef`) after
  being serialized therefore showed as `(Missing)` in the inspector and
  `EventReference.Publish()` / `CommandReference.Publish()` logged a warning instead of
  publishing. Both now go through `MessageTypeResolver`, which strips the assembly
  part (generic-argument aware) before scanning loaded assemblies.
- **The Messages Console failed to load when installed from the Asset Store, a
  registry, a tarball or git.** The 0.19.0 fix still built the UXML/USS paths from
  the compile-time source path, which Unity 6 records relative to the project
  (`./Library/PackageCache/...`) for packages inside it. Every PackageCache install,
  and any embedded copy whose folder name differed from the package name, showed
  "Could not load UXML at ...", and `ScriptFileField` rows were unstyled. The console
  and `ScriptFileField` now load their UXML/USS by asset GUID, and the internal
  `PathUtils` helper is removed.
- **Reference drawers now follow undo/redo, prefab revert and edits made in other
  inspectors.** The `EventReference` / `CommandReference` and `[EventType]` /
  `[CommandType]` drawers kept showing pre-undo values, and the next field edit wrote
  the stale payload back. They now track the serialized values and rebuild when those
  change from outside the drawer; a field edit applies to the payload as currently
  stored.
- **Multi-object editing no longer overwrites other objects' payloads.** When the
  selected objects have different types, the type dropdown shows a mixed value; when
  they differ in type or payload, the payload editor is replaced by a note and ▶ is
  disabled.
- **`EventReference` / `CommandReference` arrays and lists lost their category in the
  inspector.** The drawer classified the field by `fieldInfo.FieldType`, which for
  `EventReference[]` / `List<CommandReference>` is the collection type: the dropdown
  listed events *and* commands, and the ▶ publish button silently did nothing. The
  drawer now classifies by the element type.
- **The type dropdowns listed classes and open generic structs.** Neither can be
  published (the bus is `where T : struct`, and an open generic has no type
  arguments), and picking a generic struct threw and left the inspector unusable. The
  dropdowns now list closed structs only, and a payload type that can't be
  instantiated shows an error box instead of throwing.
- **`readonly` / `[NonSerialized]` payload fields are shown as disabled "not
  serialized" rows.** They were editable fields whose edits `JsonUtility` silently
  dropped.
- **▶ publishes the stored reference itself**, so a `Publish()` override on an
  `EventReference` / `CommandReference` subclass runs, with the subclass's own
  serialized fields as set in the inspector. Its icon now follows the editor skin (it
  used the dark-skin icon in the light skin).
- **Messages Console:**
  - the highlighted row and the detail pane no longer drift apart after a filter
    change, Clear, log trim, or domain reload (the log list no longer restores a stale
    selection from view data);
  - after `MessagesInstrumentation.Clear()` (e.g. from the package's own tests) the
    window no longer re-snapshots and repaints on every editor tick until the next
    record arrives;
  - reopening the window lists the records already in the buffer;
  - subscriber rows for lambda/closure handlers resolve to the enclosing script
    (`ScriptFileField.FindScript` maps compiler-generated types to their declaring
    type) instead of scanning every script in the project and finding nothing;
  - payloads with an indexer property printed `<error>` for it; indexers are skipped;
  - removed misleading "(not captured)" notes.
- **Sample: a decay tick racing game over could overwrite the final score.** The
  worker is destroyed at end of frame, so a tick enqueued in that window raised a
  second `GameEnded` on the next drain. `ScoreModel` now ignores `AdjustScore` between
  game over and `ResetScore`.
- **Sample scene no longer depends on URP, and compiles without the Input System
  package.** It carried a URP Global Volume (referencing a `VolumeProfile` that was not
  shipped), URP camera/light data, and an `InputSystemUIInputModule`: missing scripts
  in Built-in / HDRP projects, and unresponsive buttons with the legacy Input Manager.
  Those components are removed. At startup `BasicPubSubSample` adds
  `InputSystemUIInputModule` (looked up by name, so the sample compiles even when
  Active Input Handling is New or Both but `com.unity.inputsystem` is not installed) or
  `StandaloneInputModule`, and otherwise logs a warning saying how to fix it. The
  sample still requires uGUI.
- **Sample: the score and game-over labels were not drawn in Game views shorter than
  ~600 px** (stretch anchors with large negative size deltas). They now use centre
  anchors with fixed sizes and vertical overflow; the `GameOVer` object is renamed
  `GameOver`.
- **Sample on Web players:** `ScoreDecayWorker` used a managed thread, which Web
  players do not support, so the game could never end there. On Web it now enqueues
  the same ticks from a coroutine.

## [0.19.0] - 2026-07-02

### Added
- **`Messages.Enqueue` profiler marker.**
  `Enqueue` is now visible in the Unity Profiler timeline alongside the existing `Messages.Publish` and `Messages.DrainQueues` markers, including when called from worker threads.
- **XML documentation for the remaining public API.**
  `MessageBus<TBase>` (constructor, `GetSubscriberCount` overloads, `ChannelCount`, `Dispose`), `MessagesInstrumentation` (class, `BusKind`, `Op`, `Record` and `Subscriber` members, `Capacity`, `Count`, `TotalEver`, `SetCapacity`, `Snapshot`, `Clear`), `SubscriptionBag.Dispose`, and `MessagesConsoleWindow.Open` now carry doc comments.

### Changed
- Docs: **Corrected the minimum Unity version wording.**
  `package.json` declares `"unity": "6000.1"`, which is Unity 6.1; the `README` and `Documentation~/Messages.md` labeled it "Unity 6.0 (6000.1)".
  Both now say Unity 6.1 (6000.1) and newer.
- Docs: **Corrected the profiler-marker claim.**
  The `README`, `Documentation~/index.md`, and `Documentation~/Messages.md` claimed markers on "every (public) entry point"; markers exist on the dispatch path (`Publish`, `Enqueue`, `DrainQueues`), and with the new `Enqueue` marker the docs now say exactly that.
- Docs: **XML-doc corrections.**
  `MessageBus<TBase>.Subscribe`'s dispose-after-reset note pointed its `cref` at the instance `Reset()` (in-place clear, no replacement bus) while describing the static facade swap; it now names the facades.
  `CommandBus` claimed `Publish`/`Enqueue`/`DrainQueues` were "the only operations" after install (`Reset`, `GetSubscriberCount`, and `ChannelCount` also exist).
  `MessagesInstrumentation` claimed the type is "compiled into editor and `TUTAN_MESSAGES_DEBUG` builds only" - the type is always compiled; it is the hook call sites that strip, and the never-initialized guarantee holds only while nothing calls its public surface.
  `[EventType]`/`[CommandType]` (and `Documentation~/Editor.md`) advised resolving the stored name with a bare `Type.GetType`, which fails under the assembly-identity drift that 0.17.x fixed inside the package; both now describe the drift-tolerant fallback.
  Stale file headers fixed: `MessageBus.cs` still called itself `Messages.cs`, and `MessagesConsoleWindow.cs` called itself `MessagesDebuggerWindow.cs` with the menu path misnamed "Message Console".

### Fixed
- **Editor assets now load when the package is not embedded.**
  `PathUtils.RelativePath` derived the Messages Console UXML/USS paths from the source file's compile-time location and only recognized `/Packages/` and `/Assets/` roots, so a package consumed from `Library/PackageCache` (registry or tarball install) or mounted via "Add package from disk" failed to load the console's UXML ("Could not load UXML at ...") and left `ScriptFileField` rows unstyled.
  Resolution now falls back to mapping the physical path onto the package's virtual `Packages/<id>/` mount via `PackageInfo.FindForAssembly`.
  `PathUtils` is now `internal` (it was never meant as public API).
  *Correction: this fix was incomplete. Unity 6 records project-relative source paths for packages inside the project, so PackageCache installs still failed to load the console; 1.0.0 loads these assets by GUID and removes `PathUtils`.*
- **Messages Console no longer duplicates and skips records under worker-thread traffic.**
  The incremental catch-up read `TotalEver` and then took a `Snapshot()` as two separate operations; records appended in between (worker-thread `Enqueue`) were displayed twice while an equal number of older records fell out of the catch-up window and were never shown.
  A new `MessagesInstrumentation.Snapshot(out long totalEver)` overload returns the records and the paired total under one lock, and the console's tick and filter-toggle rebuild both consume it.
  Filter toggles now also rebuild the visible log immediately instead of one editor tick later.
- **Re-selecting the "(Missing)" dropdown entry no longer re-enables the synthetic Publish button.**
  The `EventReference`/`CommandReference` drawer gated the button correctly on first draw but its type-changed callback only checked for a non-empty stored value, so picking a valid type and then re-selecting the trailing `(Missing)` entry enabled a button that publishes nothing.
  The callback now resolves the stored type before enabling.
- **`EventReference.Publish()` / `CommandReference.Publish()` no longer fail silently.**
  When the stored type cannot be resolved (renamed, moved, or deleted since serialization), a warning naming the stored type is logged instead of a silent no-op.
- **`MessageReference.CreateMessage()` no longer throws on corrupt stored JSON.**
  `JsonUtility.FromJson` throws on malformed input, so a payload mangled by a merge or edited by hand made `EventReference.Publish()` / `CommandReference.Publish()` throw - against the package's failures-as-values convention (the inspector drawer already guarded this exact call).
  A parse failure now logs a warning naming the stored type and publishes the type's default values.
- **Messages Console: filter and search changes no longer break Pause.**
  Toggling a filter (or editing the search) while paused rebuilt the visible log from the live ring buffer, pulling in records newer than the pause point and advancing the incremental-catch-up cursor past them.
  The rebuild now drops records that arrived after the pause point and leaves the cursor untouched, so the view stays frozen and unpausing catches up normally.
- **Editor asset paths no longer mis-resolve when the project lives under a folder named `Packages/` or `Assets/`.**
  `PathUtils.RelativePath` scanned the physical path for a `/Packages/` / `/Assets/` marker before trying the exact package-mount mapping, so a project physically located under such a parent directory (e.g. `D:/Assets/MyProject/…`) false-matched and produced a broken asset path.
  The `PackageInfo` mapping is now tried first; the marker scan remains only as the fallback for classic non-package installs.
- **Deleted the stray `docs/` folder.**
  It was a leftover from before the move to `Documentation~` and contained an outdated `Editor.md` (it still described the reflection-based publish path and the `Timestamp` auto-fill removed in 0.17.0).
  Because the folder lacked the `~` suffix, Unity imported it and shipped the stale page into consuming projects; `Documentation~` is the one documentation source.

## [0.18.0] - 2026-06-26

### Changed
- **Message types now only require `where T : struct` (was `unmanaged`).** This
  relaxes the constraint across `EventBus`, `CommandBus`, `MessageBus<TBase>`,
  `CommandRegistry`, and `MessageHandler<T>`. The `IEvent`/`ICommand`/`IMessage`
  marker requirement is unchanged, so all existing messages keep compiling
  (`unmanaged` is a strict subset of `struct`) — this is a source-compatible,
  additive change.
- **Messages may now carry reference-type fields** (`string`, arrays,
  collections, class payloads), which the old constraint forbade. This unblocks
  real-world payloads that previously needed a `FixedString` or an int handle.
- Performance: **Dispatch remains zero-allocation.** The guarantee was always delivered by
  generic specialization plus `ref`-passing, not by blittability — the message
  is never boxed or heap-stored on either the immediate or deferred path,
  regardless of its fields.
- Note: A message that carries reference-type fields is **not** free: while it sits in
  the deferred queue it is a GC root the collector must scan during the mark
  phase (a scan cost, not an allocation), and `Enqueue` copies the struct
  *shallowly* — a worker thread shares any referenced object with the main
  thread that drains it. Prefer value-only messages on hot or cross-thread
  paths. See `Documentation~/Performance.md` and `Documentation~/Threading.md`.

## [0.17.1] - 2026-06-13

### Changed
- Docs: **Corrected the minimum Unity version in `Documentation~/Messages.md`.** It
  still claimed "Unity 2021.3 LTS and newer", contradicting `package.json`
  (`"unity": "6000.1"`) and the `README` ("Unity 6.0 (6000.1) and newer"). The
  doc now states Unity 6.0 (6000.1) and newer. No code change.
- Docs: **Tightened the generic constraints in `Documentation~/API-Reference.md` to
  match the code.** `Subscribe<T>` now shows `where T : unmanaged, IEvent`
  (it is `EventBus`-only), and the shared `Publish`/`Enqueue`/`GetSubscriberCount`
  signatures use `where T : unmanaged, TBase` (resolving to `IEvent` on
  `EventBus`, `ICommand` on `CommandBus`) instead of a blanket `IMessage`. No
  code change.

### Fixed
- **`EventReference`/`CommandReference` and `[EventType]`/`[CommandType]` no longer
  silently blank a stored type that can't be matched.** The drawers selected the
  current entry with `values.IndexOf(storedAssemblyQualifiedName)`, a raw-string
  match against the live type list. That missed two cases and snapped the popup to
  `(None)` in both: a type whose assembly identity had *drifted* since serialization
  (still resolvable, but its `AssemblyQualifiedName` string no longer matched), and a
  type that had been renamed/moved/deleted. The first is now matched by resolved
  `Type` identity — consistent with the drift-tolerant resolution already in
  `ScriptFileField`/`MessageReference` — so a still-valid reference selects correctly.
  The second is surfaced: the orphaned value is kept, shown as a trailing
  `(Missing) <Type>` entry, and called out with a warning `HelpBox`, and the
  reference's synthetic Publish button is disabled until a resolvable type is picked.
  Previously the stored value looked like an empty field and was overwritten the
  moment the dropdown was touched.

## [0.17.0] - 2026-06-13

### Added
- **The inline `EventReference`/`CommandReference` payload editor now supports
  many more field types.** Alongside the previous `int`/`float`/`bool`/`string`/
  `Vector3`/`Color`/`enum`, it now renders `long`, `double`, `Vector2`, `Vector4`,
  `Vector2Int`, `Vector3Int`, and `Quaternion` (edited as euler angles). Unknown
  types still fall back to a disabled "unsupported type" label.

### Changed
- **Editor synthetic-publish path no longer uses reflection.** The inspector
  "publish" button (and `EventReference.Publish()` / `CommandReference.Publish()`)
  previously found the generic `Publish<T>` via a `GetMethods()` scan and invoked
  it with `MakeGenericMethod`, and the drawer set the reference's `typeName` /
  `dataJson` through `NonPublic` reflection. Dispatch now goes through a new
  non-generic `MessageBus.PublishBoxed` seam (one unbox inside `Channel<T>`,
  editor-only — not on the hot path), and the drawer assigns the internal fields
  directly via the existing `InternalsVisibleTo` access. No public API change;
  `MessageReference.Publish()` is now declared `abstract` on the base.
- **`MessageReference.CreateMessage()` no longer auto-fills a `Timestamp` field.**
  It previously reflected for a field named `Timestamp` (case-insensitive) and
  stamped `Time.time` / `DateTime.UtcNow.Ticks` into it — an invisible convention
  that coupled serialization to a field name. A synthetic message now carries
  exactly the values authored in the inspector, and the `Timestamp` field is
  editable like any other (the drawer no longer hides it). Set timestamps
  explicitly at publish time if you need them.
- **The `EventReference`/`CommandReference` payload editor is now built in UI
  Toolkit** instead of an `IMGUIContainer`. Each public field of the message
  struct renders with its matching native control (`IntegerField`, `Vector3Field`,
  `EnumField`, …), consistent with the rest of the editor tooling. The data flow
  is unchanged — edits write back into the boxed struct and re-serialize to the
  stored JSON.
- **`MessageReference` type resolution is now resilient to assembly-identity
  drift.** `GetMessageType()` / `CreateMessage()` resolved the stored
  `AssemblyQualifiedName` with a bare `Type.GetType`, which returns null if the
  assembly's version/identity has changed since the name was serialized.
  Resolution now falls back to scanning loaded assemblies. The stored format is
  unchanged.
- Performance: **Messages Console no longer trims its visible log one row at a time.** When
  the filtered log overran capacity it called `List.RemoveAt(0)` per overflowing
  record — an O(n) shift each, O(n²) under steady traffic. It now lets the list
  run a small margin past capacity and drops the whole overflow in one
  `RemoveRange`. Editor-only; no behavior change.

### Fixed
- **`ScriptFileField` rows render styled again.** The control loaded its
  stylesheet from a hardcoded `Packages/com.tutan.messages/Editor/ScriptFileField.uss`
  path that no longer matched the file's actual location, so `LoadAssetAtPath`
  returned null and the row (icon + label) drew unstyled. It now resolves the
  `.uss` relative to its own source file via `PathUtils.RelativePath`, so it
  survives the package being embedded or renamed.

## [0.16.0] - 2026-06-12

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

## [0.15.2] - 2026-06-12

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

## [0.15.1] - 2026-06-12

### Changed
- Docs: **Stopped prescribing `Reset()` for scene transitions.** Whether (and when) to
  reset the buses across scene loads depends on the app's scene architecture —
  additive scenes, persistent managers, and DontDestroyOnLoad roots all want
  different lifetimes. The docs now document `Reset()` as a test-teardown tool
  and leave scene-lifecycle policy to the user: removed the "Scene Transition
  Cleanup" example (`docs/Examples.md`), the scene-transition framing in
  `docs/Bootstrap.md`, `docs/Threading.md`, `docs/API-Reference.md`, and the
  `EventBus.Reset`/`CommandBus.Reset` doc comments. Behavior is unchanged.
- Docs: Removed a stale reference to `decisions/CommandBus.md` from
  `docs/API-Reference.md` — the decisions folder is not shipped with the package.

## [0.15.0] - 2026-06-12

API-stable pre-release. Hardening pass over the runtime, editor tooling, and docs
ahead of the Asset Store submission — no breaking API changes since 0.14.0.

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
- Docs: Removed the stale `MessagesInstrumentation.CapturePayloads` reference from
  `docs/Editor.md` — the field no longer exists; payload capture is automatic
  whenever instrumentation is enabled.
- Docs: Documented that structs authored through `EventReference`/`CommandReference`
  must be `[Serializable]` (the payload round-trips through `JsonUtility`).
- Docs: Sample: clarified the intended scoring — the final score is the fatal decay
  tick, which grows the longer you survive (`ScoreModel`/`MenuHud` comments,
  sample `README`); renamed the misleading `DecayPerSecond` field to
  `_nextDecayDelta`; corrected the stale "UI built entirely in code" comment
  and the package `README`'s "drop the component on a GameObject" instruction
  (the sample is scene-based).
- Docs: `package.json` description updated — it still advertised the
  pre-0.14.0 "integer subscription tokens".
- Docs: Fixed `Runtime/IMessage.cs` source encoding (Windows-1252 em-dashes rendered
  as `�`); the file is now UTF-8.

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

## [0.14.0] - 2026-06-12

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

### Fixed
- **Worker threads racing on the first `Enqueue` of a message type could lose a
  message.** The per-channel pending queue was lazily created with a non-atomic
  `??=`; two threads observing it as null would each create a queue, and the
  loser's message landed in a queue that was immediately overwritten. The lazy
  init is now an `Interlocked.CompareExchange`, and `DrainQueue` reads the queue
  field with `Volatile.Read` so a queue created on a worker thread is visible to
  the main thread no later than the next frame's drain. Covered by a new
  multi-thread first-enqueue stress test.

## [0.13.0] - 2026-06-05

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
- Migration: Replace `class Foo : ICommandHandler<MyCommand>` with a plain `class Foo` exposing
  `void Handle(ref MyCommand cmd)`; the method signature is unchanged.
- Migration: If you relied on auto-install, add an explicit composition-root call:
  `CommandBus.TryInstall(out var error, r => r.Handle<MyCommand>(foo.Handle));`.
- Migration: If you had **Auto-Install Drainers** off and drained manually, define
  `TUTAN_MESSAGES_NO_AUTO_HOST` to keep that behavior.

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

### Added
- **`CommandRegistry`** — the fluent builder passed to `CommandBus.TryInstall`.
  `Handle<T>(MessageHandler<T>)` binds the single handler for command type `T`.

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

### Fixed
- **Messages Console** subscriber list in the detail panel is now a snapshot
  captured at the instant a message was published or enqueued, instead of a
  live query of the bus performed when the row is selected. Previously,
  subscribing or unsubscribing after a message was sent would retroactively
  change the subscriber list shown for that past record. `Record` now carries
  a frozen `Subscriber[]` (token, target type, handler method), resolved to
  strings at capture time so the snapshot survives later unsubscription or GC.

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
- Note: Instrumentation is off by default; the closed-window state holds
  `MessagesInstrumentation.Enabled = false`, so every hook short-circuits
  on its first branch (~one `bool` check per `Publish`) even when the
  defines are present.

## [0.3.0] - 2026-05-19

### Changed
- Documentation: `CommandBus` consistently described as **N:1** (many publishers,
  single subscriber). `EventBus` consistently described as **N:M** (was
  inconsistently called `1:N` in `package.json`).
- `ICommand` doc clarifies that the single-handler rule is enforced by
  `CommandBus`, not the core `MessageBus<TBase>`.

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
