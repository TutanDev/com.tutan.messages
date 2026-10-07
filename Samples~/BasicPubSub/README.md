# Basic Pub/Sub Sample

A tiny score-clicker built entirely on `Tutan.Messages` — it shows the two buses
working together: the models, the score HUD and the decay worker never reference
each other.

## Before you run it

Nothing to configure. `BasicPubSubSample` is the composition root: in `Awake` it
builds `ScoreModel` and `MenuModel` and binds each command to its single handler
through one `CommandBus.Install` call. Queue draining is handled for free by the
auto-spawned `[MessagesHost]`.

**Requirements:** the sample UI uses uGUI (`com.unity.ugui`, installed by default
in Unity 6 templates). It is render-pipeline agnostic (Built-in, URP, HDRP) and
works with either input backend: at startup it adds a UI input module to match —
`InputSystemUIInputModule` when **Player ▸ Active Input Handling** includes the
Input System and the Input System package (`com.unity.inputsystem`) is installed,
otherwise `StandaloneInputModule` for the legacy Input Manager. With **Input System
Package (New)** selected but the package not installed, the buttons cannot respond
and a warning in the Console says how to fix it.

**Web players:** Web builds have no managed threads, so there `ScoreDecayWorker`
enqueues its decay ticks from a coroutine on the main thread instead of a
background thread. The game plays the same; `CommandBus.Enqueue` simply defers each
tick to the next drain.

## Run it

1. Open `BasicPubSubSample.unity` from the imported sample folder.
2. Press Play. The menu appears.
3. Click **Start** — the menu publishes a `StartGame` command. `MenuModel`
   handles it and raises `GameStarted`; the score HUD takes over and the score is
   reset to its starting value (a `ResetScore` command).
4. Click the score button to add points (`AdjustScore +1`). Meanwhile
   `ScoreDecayWorker` drains the score from a background thread via
   `CommandBus.Enqueue` — a little more each second.
5. Let the score fall below zero — `ScoreModel` raises `GameEnded`, the menu
   returns and shows your final score. The final score is the size of the decay
   tick that ended the run — it grows every second, so surviving longer means a
   bigger score.
6. Click **Start** again to play another round; the score resets.

## How the pieces talk

The models, the score HUD and the decay worker never reference each other — every
interaction between them goes through a bus. Only the composition root holds direct
references: to what it builds (the models, and the decay worker it starts and stops)
and to the two HUDs, to switch them and to hand the final score to the menu.

- **`CommandBus` (N:1)** — `StartGame` → `MenuModel`; `AdjustScore` and `ResetScore`
  → `ScoreModel`. All three are bound at the composition root through a single
  `CommandBus.Install`, which enforces the N:1 rule (one handler per command type).
- **`EventBus` (N:M)** — `GameStarted` / `GameEnded` drive the HUD switching in
  `BasicPubSubSample`; `ScoreChanged` updates `ScoreHud`. Add another listener
  (logger, sound, analytics) and nothing else has to change.

`AdjustScore` is sent from two places — the button (main thread, `Publish`) and
the decay worker (background thread, `Enqueue`) — yet exactly one handler owns
it. That N:1 guarantee is the point of the `CommandBus`.
