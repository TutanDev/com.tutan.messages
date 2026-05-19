# Changelog

All notable changes to `com.tutan.messagebus` will be documented in this file.

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
