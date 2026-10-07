# 📨 Tutan Messages

> Stop wiring `UnityEvent`s, abandoning `SendMessage()`, and worrying about
> a stray `Action<T>` boxing your struct on a 90 Hz frame.

A zero-allocation pub/sub message bus for Unity, designed for XR and other
hot-path workloads where a single GC spike means a dropped frame.

---

## ✨ Features

| | |
|---|---|
| 🎯 **Zero-alloc dispatch** | Messages are `struct`s, delivered to handlers as `ref T` — no boxing, no copies |
| 🧵 **Thread-safe queueing** | `Enqueue` from any thread; the main thread drains in `LateUpdate` |
| 🪙 **Disposable subscriptions** | `Subscribe` returns a `Subscription` — dispose it, bag it, or `.AddTo(this)`; no `-=`, no delegate-equality footguns |
| 🧭 **Events vs Commands** | `IEvent` for N:M notifications, `ICommand` for N:1 intent — at most one handler per command, validated once at install |
| 🎮 **XR-aware** | Zero-allocation dispatch on the hot path, profiler markers on the dispatch path (`Publish`, `Enqueue`, `DrainQueues`) |
| 🛠️ **Editor tooling** | Live Messages Console and serialized `EventReference` / `CommandReference` for inspector wiring |

---

## 📚 Documentation

| | Guide | What it covers |
|---|---|---|
| 📖 | [Why this library](Messages.md) | The problem, the approach, message and handler basics |
| 📋 | [API Reference](API-Reference.md) | Every public type and member — signature and one-line description |
| 🧪 | [Examples](Examples.md) | Basic pub/sub, subscription lifetimes, queued worker dispatch, commands — plus the runnable **Basic Publish / Subscribe** sample (Package Manager ▸ Tutan Messages ▸ Samples) |
| 🧵 | [Threading](Threading.md) | Which calls are main-thread-only, which are thread-safe, and why |
| ⚡ | [Performance](Performance.md) | Cost table, allocation contract, pre-warming recipe |
| ⚠️ | [Edge Cases](EdgeCases.md) | Reentrant publish, subscribe/unsubscribe during dispatch, exceptions, lifetime and reset caveats |
| 🏛️ | [Architecture](Architecture.md) | When to reach for the bus and when not to |
| 🚀 | [Bootstrap](Bootstrap.md) | Automatic queue draining and explicit command-handler binding |
| 🛠️ | [Editor Tooling](Editor.md) | Messages Console window and inspector-serializable references |

---

## ✅ Requirements

- Unity 6000.3 LTS or newer. Pure C# — no native plugins.
- The runtime uses the built-in **JSONSerialize** module
  (`com.unity.modules.jsonserialize`), which is enabled by default; the Test
  Framework dependency below keeps it enabled.
- Declared package dependencies: the **Test Framework** (`com.unity.test-framework`,
  part of Unity's default project templates) and its NUnit package
  (`com.unity.ext.nunit`). The package ships its EditMode tests, which reference
  them; the tests compile only when the package is listed in your project's
  `testables`.
- Any render pipeline (Built-in, URP, HDRP) and any platform Unity targets.
- The **Basic Publish / Subscribe** sample additionally uses uGUI (`com.unity.ugui`).
