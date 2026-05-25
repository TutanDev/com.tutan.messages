[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · **Threading** · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Threading Model

```
 Worker Thread              Main Thread
 ─────────────              ───────────
      │                          │
  Enqueue(msg)              LateUpdate()
      │                          │
 lock(_queueLock)           DrainQueues()
  GetOrCreate channel            │
 release lock                    │
      │                     channel.DrainQueue()
  ConcurrentQueue.Enqueue        │
                            ConcurrentQueue.TryDequeue
                                 │
                            Publish(ref msg)  ◄── synchronous dispatch
                                 │
                            handler1(ref msg)
                            handler2(ref msg)
                            ...
```

- `Publish`, `Subscribe`, `Unsubscribe`, `DrainQueues` — **main thread only**.
  Lock-free reads via `ConcurrentDictionary<Type, ChannelBase>`.
- `Enqueue` — **thread-safe**. Channel lookup/creation goes through
  `ConcurrentDictionary.GetOrAdd`; the message itself lands in a per-channel
  `ConcurrentQueue<T>`. Safe to race against any main-thread call.

If you need to react to a worker-thread event on the main thread, the rule is
simple: **`Enqueue` from the worker, subscribe normally on the main thread,
and the handler will run inside the next `DrainQueues()` call** — which the
default `MessagesHost` triggers in `LateUpdate`. See [Bootstrap](Bootstrap).
