[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · **Edge Cases** · [Architecture](Architecture) · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Behavioral Edge Cases

## Reentrant Publish

A handler calling `Publish<T>` for the *same* message type `T` will re-enter
the dispatch loop. This is handled correctly via an integer depth counter
(`_dispatchDepth`):

- `_dispatchDepth` is incremented on entry and decremented on exit.
- Compaction runs only when `_dispatchDepth` returns to zero, ensuring list
  compaction never mutates the entry list while an outer dispatch is still
  iterating it.
- Each nested call captures its own `count` at entry, so entries appended
  during inner dispatch (past the captured count) won't fire for that level.
- Entries removed (marked inactive) during inner dispatch are skipped by both
  inner and outer iterations.

Avoid deep reentrant chains — they consume stack proportionally to depth.

## Subscribe During Dispatch

If a handler calls `Subscribe<T>` for the message type currently being
dispatched, the new handler is appended to the list. It will **not** receive
the current message (the iteration count was captured before the append). It
will receive subsequent messages.

## Unsubscribe During Dispatch

Safe. The entry is marked inactive immediately. The delegate reference is set
to `null` to release the GC root. The inactive entry is skipped for the
remainder of the current dispatch. Compaction occurs after dispatch completes.

## Handler Exceptions

Exceptions in a handler are caught and logged via `Debug.LogException`.
Dispatch continues to the next handler. A broken handler must never cascade
into a broken frame.

## Zero Subscribers

`Publish<T>` returns immediately if no channel exists for `T`. Cost: one
`Dictionary.TryGetValue` call.
