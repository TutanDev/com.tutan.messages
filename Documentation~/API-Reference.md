[Home](index.md) · [Why](Messages.md) · **API Reference** · [Examples](Examples.md) · [Threading](Threading.md) · [Performance](Performance.md) · [Edge Cases](EdgeCases.md) · [Architecture](Architecture.md) · [Bootstrap](Bootstrap.md) · [Editor](Editor.md)

---

# API Reference

Every public type in the package. Runtime types live in the `Tutan.Messages`
namespace (assembly `Tutan.Messages`); the editor tooling lives in
`Tutan.Messages.Editor` (assembly `Tutan.Messages.Editor`, editor only).

The dispatch methods — `Publish`, `Enqueue`, `DrainQueues` — are identical
across `EventBus` and `CommandBus` (and the underlying `MessageBus<TBase>`
instance they wrap). The buses differ only in how handlers are registered:
`EventBus` is a mutable N:M bus you subscribe to at any time (and unsubscribe
by disposing the returned `Subscription`), while `CommandBus` handlers are
declared once at the composition root via `Install` — see
[CommandBus](#commandbus).

The shared signatures below are written against `TBase`, the bus's message base
type: it resolves to `IEvent` on `EventBus` and `ICommand` on `CommandBus`. So
`EventBus.Publish<T>` constrains `T` to `struct, IEvent`, and
`CommandBus.Publish<T>` to `struct, ICommand`.

## Messages and Handlers

```csharp
interface IMessage { }               // base marker for every message struct
interface IEvent   : IMessage { }    // notification: any number of handlers (EventBus)
interface ICommand : IMessage { }    // intent: at most one handler (CommandBus)

delegate void MessageHandler<T>(ref T message) where T : struct, IMessage;
```

A message is a `struct` implementing `IEvent` or `ICommand`. Handlers receive it
by `ref` (no copy per handler); a handler may mutate it, and later handlers in
the same dispatch see the change. See [Why this library](Messages.md#core-concepts).

## EventBus

```csharp
static class EventBus
{
    static Subscription Subscribe<T>(MessageHandler<T> handler) where T : struct, IEvent
    static void Publish<T>(ref T message)     where T : struct, IEvent
    static void Publish<T>(T message)         where T : struct, IEvent
    static void Enqueue<T>(in T message)      where T : struct, IEvent
    static void DrainQueues()
    static int  GetSubscriberCount<T>()       where T : struct, IEvent
    static int  ChannelCount { get; }
    static void Reset()
}
```

The members are described below, by operation.

## Subscribe (EventBus)

```csharp
Subscription Subscribe<T>(MessageHandler<T> handler) where T : struct, IEvent
```

Registers `handler` for messages of type `T`. Returns a disposable
[`Subscription`](#subscription-lifetime) — disposing it is how you
unsubscribe. Throws `ArgumentNullException` for a null handler. Main thread
only. `EventBus` only — for commands, see [CommandBus](#commandbus).

## Publish (Immediate)

```csharp
void Publish<T>(ref T message)  where T : struct, TBase
void Publish<T>(T message)      where T : struct, TBase  // convenience, one copy
```

Dispatches `message` synchronously to all active subscribers of type `T`, in
registration order. The `ref` overload is zero-copy. The value overload copies
once (caller → parameter) and is acceptable for small messages when the `ref`
call site is inconvenient.

Main thread only. If no channel exists for `T` (nothing has subscribed to or
enqueued `T` since the last reset), `Publish` returns after one dictionary
lookup. Otherwise it walks the channel's entry list, even if no handler is
active any more.

## Enqueue (Deferred)

```csharp
void Enqueue<T>(in T message) where T : struct, TBase
```

Copies `message` into the per-type queue for `T`. Thread-safe via
`ConcurrentDictionary` + `ConcurrentQueue`; safe to race with main-thread
`Subscribe`/`Publish`/`DrainQueues` *and* with concurrent `Enqueue` calls from
other worker threads (channel creation goes through `GetOrAdd`; the per-channel
queue's lazy creation is a compare-and-swap). Messages are dispatched on the
next `DrainQueues()` call. The first `Enqueue` of a type allocates its channel
and queue.

## DrainQueues

```csharp
void DrainQueues()
```

Dispatches the queued messages of every channel on the bus. Each channel's drain
is bounded by what was queued when it started: a handler that enqueues its own
type extends the next drain, not this one. Call once per frame. Main thread
only. By default `MessagesHost` does this for you — see
[Bootstrap](Bootstrap.md).

## Subscription Lifetime

```csharp
struct Subscription : IDisposable
{
    bool IsActive { get; }      // false once this handle has been disposed (or was never issued)
    void Dispose()              // unsubscribes; idempotent, safe during dispatch
}

sealed class SubscriptionBag : IDisposable
{
    int  Count { get; }                    // subscriptions currently held
    void Add(Subscription subscription)    // inactive handles are ignored
    void Clear()                           // disposes every held subscription and empties the bag
    void Dispose()                         // same as Clear(); the bag stays usable
}

static class SubscriptionExtensions
{
    static Subscription AddTo(this Subscription s, SubscriptionBag bag)
    static Subscription AddTo(this Subscription s, GameObject gameObject)  // dies with the GameObject
    static Subscription AddTo(this Subscription s, Component component)    // dies with its GameObject
}

sealed class SubscriptionAnchor : MonoBehaviour   // added by AddTo(GameObject); never add it by hand
```

`Subscribe` returns a `Subscription` — a disposable struct pairing the
subscription's identity with the bus instance that issued it. Disposing it
unsubscribes. Three ways to manage that:

- **Hold it and dispose explicitly** — for dynamic lifetimes (e.g. a view that
  subscribes in `OnEnable` and unsubscribes in `OnDisable`).
- **Collect several in a `SubscriptionBag`** — one `Dispose()` releases the
  group; the bag is reusable afterward.
- **Tie it to a GameObject** with `.AddTo(this)` — disposal happens in
  `OnDestroy` via one hidden `SubscriptionAnchor` component per GameObject, so
  the common MonoBehaviour pattern collapses to:

```csharp
void Awake()
{
    EventBus.Subscribe<PlayerMoved>(OnMoved).AddTo(this);
}
```

No handle field, no `OnDestroy`. Details and trade-offs in
[Examples](Examples.md#subscription-lifetimes). Notes:

- `Subscription` is a struct around an id plus an existing reference.
  `Subscribe` allocates nothing per call beyond occasional growth of the type's
  subscriber list; the first `Subscribe` of a type also allocates its channel.
  A `SubscriptionBag`'s list grows as you add to it; the anchor component is
  added once per GameObject.
- Because the handle captures the bus *instance*, disposing a `Subscription`
  taken before a `Reset()` is a no-op — it can never remove an unrelated
  subscription on the replacement bus.
- `Dispose` is main thread only, safe to call during dispatch (the handler is
  marked inactive and skipped for the remainder of the current dispatch
  cycle), and idempotent. Disposing a *copy* also unsubscribes, but only the
  copy you disposed through reports `IsActive == false`.
- `SubscriptionAnchor` is hidden in the Inspector, never saved into a scene or
  prefab, and `[ExecuteAlways]`, so destroying its GameObject disposes the
  subscriptions in edit mode too. A GameObject destroyed without ever having
  been active never runs `OnDestroy` — see
  [Edge Cases](EdgeCases.md#addtogameobject-on-a-never-activated-gameobject).

## Diagnostics

```csharp
int GetSubscriberCount<T>() where T : struct, TBase   // active subscriptions (handlers) for T
int ChannelCount { get; }                             // types subscribed to or enqueued since the last reset
```

## CommandBus

```csharp
static class CommandBus
{
    static InstallResult Install(Action<CommandRegistry> configure)
    static void Publish<T>(ref T message)     where T : struct, ICommand
    static void Publish<T>(T message)         where T : struct, ICommand
    static void Enqueue<T>(in T message)      where T : struct, ICommand
    static void DrainQueues()
    static int  GetSubscriberCount<T>()       where T : struct, ICommand  // 0 or 1
    static int  ChannelCount { get; }
    static void Reset()
}
```

`CommandBus` shares `Publish`, `Enqueue`, `DrainQueues`, `Reset`,
`GetSubscriberCount<T>`, and `ChannelCount` with `EventBus`. It has **no**
`Subscribe`. Each command type has at most one handler, declared once at the
composition root:

```csharp
InstallResult Install(Action<CommandRegistry> configure)
```

On success the new bindings are swapped in atomically. On a duplicate command
type, a null handler, or a null `configure` the live bus is left untouched and
the failure is reported in the result — never as an exception. Calling it again
rebuilds the bus from scratch (composition-root semantics).

Like `Reset`, a successful `Install` replaces the bus, so any commands still
queued for the next drain are discarded — including on the first install. The
editor and development builds log a warning naming the discarded types. Install
before anything enqueues: from a `BeforeSceneLoad`
`[RuntimeInitializeOnLoadMethod]`, or from a bootstrap object with an early
Script Execution Order (plain `Awake` order between objects is undefined).

Nothing requires every command type to be bound: publishing or enqueuing a
command with no handler is a silent no-op. For commands that must be handled,
assert `CommandBus.GetSubscriberCount<T>() == 1` once after `Install`.

```csharp
readonly struct InstallResult
{
    bool   Ok           { get; }  // true when the bindings were validated and swapped in
    string Error        { get; }  // names the offending command type(s); null when Ok
    int    HandlerCount { get; }  // number of handlers bound; 0 on failure
}
```

Inside the callback, bind each command with the `CommandRegistry` builder (it
cannot be constructed directly):

```csharp
sealed class CommandRegistry
{
    CommandRegistry Handle<T>(MessageHandler<T> handler) where T : struct, ICommand  // fluent
}
```

```csharp
var result = CommandBus.Install(r => r
    .Handle<PlaceOrder>(orderHandler.Handle)
    .Handle<MovePlayer>(movement.Handle));

if (!result.Ok) Debug.LogError(result.Error); // names the offending command type(s)
```

For multi-app builds that vary by feature set or backend, express that variation at the
composition root: select which handlers to bind and which backend to inject into them,
then funnel them all through one `Install`.

## Lifecycle

```csharp
void Reset()    // Clears all subscriptions and queues. Use on test teardown.
```

On the static `EventBus` / `CommandBus`, `Reset()` disposes the current bus
instance and swaps in a fresh one: outstanding `Subscription` handles become
no-ops, and queued messages are discarded, not dispatched. Both buses also reset
themselves on every Enter Play Mode — see
[Edge Cases](EdgeCases.md#bus-reset-on-enter-play-mode).
`MessageBus<TBase>.Reset()` clears an instance in place.

## MessageBus&lt;TBase&gt;

```csharp
class MessageBus<TBase> : IDisposable where TBase : IMessage
{
    MessageBus()                                       // standalone instance (tests, isolated subsystems)
    virtual Subscription Subscribe<T>(MessageHandler<T> handler) where T : struct, TBase
    void Publish<T>(ref T message)  where T : struct, TBase
    void Publish<T>(T message)      where T : struct, TBase
    void Enqueue<T>(in T message)   where T : struct, TBase
    void DrainQueues()
    int  GetSubscriberCount<T>()    where T : struct, TBase
    int  GetSubscriberCount(Type type)                 // non-generic counterpart for tooling
    int  ChannelCount { get; }
    void Reset()                                       // clears this instance in place; stays usable
    void Dispose()                                     // drops all state; do not reuse afterwards
    protected virtual void Dispose(bool disposing)     // standard dispose pattern for subclasses
}
```

The engine behind both facades. `Subscribe` is virtual so a subclass can add
pre-subscribe guards. A standalone instance is **not** drained by
`MessagesHost` — call its `DrainQueues()` yourself. `MessageBus<ICommand>` used
directly does not enforce the N:1 rule; only `CommandBus.Install` does. Its
instrumentation records are tagged `BusKind.Command` when `TBase` is or derives
from `ICommand`, and `BusKind.Event` otherwise.

## MessagesHost / MessagesBootstrap

```csharp
[AddComponentMenu("Tutan/Messages Host")]
sealed class MessagesHost : MonoBehaviour   // drains both buses in LateUpdate

static class MessagesBootstrap              // spawns the host at startup; no public members
```

Auto-spawned (hidden, `DontDestroyOnLoad`) at `BeforeSceneLoad` unless
`TUTAN_MESSAGES_NO_AUTO_HOST` is defined. A second active host destroys itself
with a warning. Each `LateUpdate` it calls `MessagesInstrumentation.SyncFrame`,
then `CommandBus.DrainQueues()`, then `EventBus.DrainQueues()`. See
[Bootstrap](Bootstrap.md).

## Serialized References

```csharp
[Serializable] abstract class MessageReference
{
    string TypeName { get; }        // stored AssemblyQualifiedName; empty when none is picked
    bool   IsValid  { get; }        // a type has been picked (it may still fail to resolve)
    Type   GetMessageType()         // drift-tolerant; null if unresolvable
    object CreateMessage()          // boxed struct from the stored JSON payload; defaults on bad JSON
    abstract void Publish()         // boxes + JSON; authoring/debug use, not hot paths
}

[Serializable] class EventReference   : MessageReference  // Publish() → EventBus
[Serializable] class CommandReference : MessageReference  // Publish() → CommandBus

class EventTypeAttribute   : PropertyAttribute { Type BaseType { get; } }  // [EventType]: dropdown of IEvent structs
class CommandTypeAttribute : PropertyAttribute { Type BaseType { get; } }  // [CommandType]: dropdown of ICommand structs

static class MessageTypeResolver
{
    static Type Resolve(string typeName)   // assembly-qualified or full name → Type; null if not found, never throws
}
```

`Publish()` on a reference whose type no longer resolves logs a warning and
publishes nothing. These are runtime types — they work in player builds — but
they box and parse JSON, so keep them off hot paths. See
[Editor Tooling](Editor.md#inspector-support-serialized-message-references).

## MessagesInstrumentation

```csharp
static class MessagesInstrumentation
{
    static bool Enabled;                         // master toggle (the Console sets it while open)
    static bool RecordDrains;                    // include DrainStart/DrainEnd records
    static void SyncFrame(int frame);            // stamp the frame on later records; [Conditional]
    static List<Record> Snapshot();              // copy of the ring buffer, oldest first
    static List<Record> Snapshot(out long totalEver);  // records + the TotalEver that pairs with them
    static long TotalEver { get; }               // monotonic record count; survives wraparound
    static int  Count { get; }                   // records currently in the buffer
    static int  Capacity { get; }                // configured ring-buffer capacity
    static void SetCapacity(int capacity);       // min 16; discards records
    static void Clear();                         // discards records; TotalEver unchanged

    enum BusKind : byte { Event, Command }
    enum Op : byte { Publish, Enqueue, Subscribe, Unsubscribe, DrainStart, DrainEnd }

    readonly struct Record
    {
        long         TimestampTicks;   // UTC DateTime.Ticks
        int          Frame;            // frame stamped by SyncFrame
        int          ThreadId;         // managed thread id
        BusKind      Bus;
        Op           Op;
        Type         MessageType;      // null for drain records
        int          TokenId;          // Subscribe/Unsubscribe records; 0 otherwise
        object       PayloadBox;       // boxed message for Publish/Enqueue; null otherwise
        string       HandlerTarget;    // Subscribe records: handler target's short type name, or null
        string       HandlerMethod;    // Subscribe records: handler method name
        Subscriber[] Subscribers;      // Publish/Enqueue records: subscribers at that instant
    }

    readonly struct Subscriber
    {
        int    TokenId;
        string Target;                 // full type name of the handler's target, or "(static)"
        string Method;
    }
}
```

The fields of `Record` and `Subscriber` are public `readonly` fields. Records
exist only where the hooks are compiled in: the editor, or a player built with
`TUTAN_MESSAGES_DEBUG`. A drained queued message is recorded again as a
`Publish` when it is dispatched. See
[Editor Tooling](Editor.md#programmatic-access).

## Editor API

Namespace `Tutan.Messages.Editor`, in the editor-only `Tutan.Messages.Editor`
assembly.

```csharp
sealed class MessagesConsoleWindow : EditorWindow
{
    [MenuItem("Window/Tutan/Messages Console")]
    static void Open()                       // open (or focus) the Messages Console
    void CreateGUI()                         // Unity callback; builds the window's UI
}

[CustomPropertyDrawer(typeof(EventTypeAttribute))]
[CustomPropertyDrawer(typeof(CommandTypeAttribute))]
class MessageTypeDrawer : PropertyDrawer      // [EventType] / [CommandType] dropdown

[CustomPropertyDrawer(typeof(MessageReference), true)]
class MessageReferenceDrawer : PropertyDrawer // EventReference / CommandReference inspector
// both: override VisualElement CreatePropertyGUI(SerializedProperty property)

[UxmlElement]
partial class ScriptFileField : VisualElement // row for a type's source file: click pings, double-click opens
{
    static readonly string ussClassName;          // "tutan-script-field"
    static readonly string iconUssClassName;      // "tutan-script-field__icon"
    static readonly string labelUssClassName;     // "tutan-script-field__label"
    static readonly string missingUssClassName;   // "tutan-script-field--missing"

    ScriptFileField()
    [UxmlAttribute] string text { get; set; }     // label text; defaults to the type name
    void SetType(Type type)                       // point the row at a type
    void SetTypeName(string fullName)             // same, from a full or assembly-qualified name
    static MonoScript FindScript(Type type)       // the declaring script; compiler-generated types map to their enclosing type
    static Type ResolveType(string fullName)      // cached, drift-tolerant name → Type
}
```

The drawers are UI Toolkit only. See [Editor Tooling](Editor.md).

---

See [Threading](Threading.md) for the full main-thread vs thread-safe contract,
and [Edge Cases](EdgeCases.md) for behaviour during reentrant dispatch.
