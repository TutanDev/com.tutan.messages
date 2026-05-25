[Home](index) · [Why](Messages) · [API Reference](API-Reference) · [Examples](Examples) · [Threading](Threading) · [Performance](Performance) · [Edge Cases](EdgeCases) · [Architecture](Architecture) · **Bootstrap** · [Editor](Editor)

---

# Auto-bootstrap and Opting Out

By default, the package creates a hidden persistent `[MessagesHost]`
GameObject at startup via `RuntimeInitializeOnLoad`. This GameObject lives
across scene loads and calls `DrainQueues()` for both buses in `LateUpdate`.

**To opt out** (e.g., to drain from a custom PlayerLoop callback, or to host
the drainer under your own scene root), add this to your Player settings:

```
Scripting Define Symbols:  TUTAN_MESSAGES_DISABLE_AUTOBOOTSTRAP
```

When disabled, attach the `MessagesHost` component to a persistent
GameObject yourself, or call `CommandBus.DrainQueues()` and
`EventBus.DrainQueues()` from your own update logic.
