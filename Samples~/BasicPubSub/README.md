# Basic Pub/Sub Sample

A tiny score-clicker built entirely on `Tutan.Messages` — it shows the two buses
working together with nothing wired directly between the parts.

## Before you run it

Nothing to configure. `BasicPubSubSample` is the composition root: in `Awake` it
builds `ScoreModel` and `MenuModel` and binds each command to its single handler
through one `CommandBus.Install` call. Queue draining is handled for free by the
auto-spawned `[MessagesHost]`. Just add the component and press Play.

## Run it

1. Open the sample scene (or add an empty GameObject and the `BasicPubSubSample`
   component, then assign its `MenuHud` and `ScoreHud` references).
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

Nothing holds a reference to anything else — every interaction goes through a bus:

- **`CommandBus` (N:1)** — `StartGame` → `MenuModel`; `AdjustScore` and `ResetScore`
  → `ScoreModel`. All three are bound at the composition root through a single
  `CommandBus.Install`, which enforces the N:1 rule (one handler per command type).
- **`EventBus` (N:M)** — `GameStarted` / `GameEnded` drive the HUD switching in
  `BasicPubSubSample`; `ScoreChanged` updates `ScoreHud`. Add another listener
  (logger, sound, analytics) and nothing else has to change.

`AdjustScore` is sent from two places — the button (main thread, `Publish`) and
the decay worker (background thread, `Enqueue`) — yet exactly one handler owns
it. That N:1 guarantee is the point of the `CommandBus`.
