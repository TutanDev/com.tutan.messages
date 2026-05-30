---
title: com.tutan.messages
---

# 📨 Messages

> Stop wiring `UnityEvent`s, abandoning `SendMessage()`, and worrying about
> a stray `Action<T>` boxing your struct on a 90 Hz frame.

A zero-allocation pub/sub message bus for Unity, designed for XR and other
hot-path workloads where a single GC spike means a dropped frame.

---

## ✨ Features

| | |
|---|---|
| 🎯 **Zero-alloc dispatch** | Messages are `unmanaged struct`s, delivered to handlers as `ref T` — no boxing, no copies |
| 🧵 **Thread-safe queueing** | `Enqueue` from any thread; the main thread drains in `LateUpdate` |
| 🪙 **Token subscriptions** | `Subscribe` returns a token — no `-=`, no delegate-equality footguns with lambdas |
| 🧭 **Events vs Commands** | `IEvent` for N:M notifications, `ICommand` for 1:1 intent — CQRS enforced at runtime |
| 🎮 **XR-aware** | No GC roots on the hot path, profiler markers on every public entry point |
| 🛠️ **Editor tooling** | Live Messages Console, a `Project Settings ▸ Tutan ▸ Messages` page with an embedded command → handler audit, and serialized `EventReference` / `CommandReference` for inspector wiring |

---

## 📚 Documentation

| | Guide | What it covers |
|---|---|---|
| 📖 | [Why this library](Messages) | The problem, the approach, message and handler basics |
| 📋 | [API Reference](API-Reference) | Every public member — signature and one-line description |
| 🧪 | [Examples](Examples) | Basic pub/sub, queued worker dispatch, commands, scene cleanup — plus the runnable **Basic Publish / Subscribe** sample (Package Manager ▸ Samples) |
| 🧵 | [Threading](Threading) | Which calls are main-thread-only, which are thread-safe, and why |
| ⚡ | [Performance](Performance) | Cost table, allocation contract, pre-warming recipe |
| ⚠️ | [Edge Cases](EdgeCases) | Reentrant publish, subscribe/unsubscribe during dispatch, exceptions |
| 🏛️ | [Architecture](Architecture) | When to reach for the bus and when not to |
| 🚀 | [Bootstrap](Bootstrap) | Auto-install drainers, auto-install command bus, the settings page that drives them |
| 🛠️ | [Editor Tooling](Editor) | Project Settings page, Messages Console window, commands authoring view, inspector-serializable references |
