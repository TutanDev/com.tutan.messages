[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · **Threading** · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Threading Model

```
 Worker Thread              Main Thread
 ─────────────              ───────────
      │                          │
  Enqueue(msg)              LateUpdate()
      │                          │
 GetOrAdd channel           DrainQueues()
 (ConcurrentDictionary)          │
      │                          │
 CAS-init pending queue     channel.DrainQueue()
      │                          │
  ConcurrentQueue.Enqueue   ConcurrentQueue.TryDequeue
                                 │
                            Publish(ref msg)  ◄── synchronous dispatch
                                 │
                            handler1(ref msg)
                            handler2(ref msg)
                            ...
```

- `Publish`, `Subscribe`, `Subscription.Dispose`, `DrainQueues` — **main thread only**.
  Lock-free reads via `ConcurrentDictionary<Type, ChannelBase>`. In the editor
  and in development builds, calling one of these from a worker thread logs an
  error naming the offending call; in release builds the check is compiled out.
- `Enqueue` — **thread-safe**. Channel lookup/creation goes through
  `ConcurrentDictionary.GetOrAdd`, the per-channel pending queue is lazily
  created with an `Interlocked.CompareExchange`, and the message itself lands
  in a `ConcurrentQueue<T>`. Safe to race against any main-thread call and
  against concurrent `Enqueue` calls from other threads — including the very
  first `Enqueue` of a message type.
  - **Shallow copy across threads.** The message struct is copied into the
    queue by value, but the copy is shallow: any reference-type field still
    points at the same object the worker thread holds. When the main thread
    drains it, that object is shared across both threads. Treat reference-typed
    payloads as immutable handoffs, or keep cross-thread messages value-only.
- **One exception:** `Reset()` (and `CommandBus.Install`) replaces the bus
  instance wholesale. A worker-thread `Enqueue` racing that swap can land its
  message in the discarded bus, where it is dropped along with the rest of the
  cleared state. Quiesce worker threads before resetting or re-installing —
  both are composition-root operations, not steady-state ones.

If you need to react to a worker-thread event on the main thread, the rule is
simple: **`Enqueue` from the worker, subscribe normally on the main thread,
and the handler will run inside the next `DrainQueues()` call** — which the
default `MessagesHost` triggers in `LateUpdate`. See [Bootstrap](Bootstrap).
