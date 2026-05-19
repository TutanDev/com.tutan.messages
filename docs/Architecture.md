[Home](index) · [Why](MessageBus) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · **Architecture** · [Bootstrap](Bootstrap) · [Editor](Editor)

---

# Architectural Guidance

The bus is at its best when systems treat events as *system-state notifications*
and commands as *intents to act*. A few rules of thumb that scale well:

1. **Reactors do not publish events.** Pure consumers (UI, audio, haptics)
   subscribe to events and react. They should not in turn publish events,
   which leads to dependency cycles.
2. **Activity classes do not publish or subscribe.** A class that performs a
   step inside a larger workflow has no knowledge of the broader system state
   needed to decide whether something is event-worthy. Let the owner decide.
3. **Cross-system cycles are usually a smell.** If A publishes to B and B
   publishes back to A in the same frame, that's a direct method call dressed
   up — use a method.
4. **Use commands when intent must reach exactly one handler.** This is
   common for "do this work" requests with no fan-out.
5. **Use events for fan-out notifications.** Many subscribers, none of whom
   must run; any of whom may stop existing without breaking the publisher.

---

## When NOT to Use the Bus

The bus is for decoupled N:M communication. Do not use it for:

- **Direct 1:1 calls within the same subsystem.** A controller calling its own
  helper is a direct method call, not a message. Using the bus adds latency
  and indirection with no decoupling benefit.
- **High-frequency per-bone / per-vertex data.** If you need to broadcast 26
  hand joint positions at 90 Hz, the per-message overhead (dictionary lookup
  + iteration) is unnecessary. Use a shared `NativeArray` or ring buffer and
  publish a single `HandTrackingFrameReady` event as a notification that new
  data is available.
- **Ordered pipelines with strict sequencing guarantees.** The bus dispatches
  in registration order, but this is an implementation detail, not a contract.
  If your pipeline requires A→B→C ordering, model it as explicit method calls
  or a dedicated pipeline abstraction.
