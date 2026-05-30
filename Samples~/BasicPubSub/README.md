# Basic Pub/Sub Sample

A tiny score-clicker built entirely on `Tutan.Messages` — it shows the two buses
working together with nothing wired directly between the parts.

## Run it

1. Open the sample scene (or add an empty GameObject and the `BasicPubSubSample`
   component, then assign its `MenuHud` and `ScoreHud` references).
2. Press Play. The menu appears.
3. Click **Start** — the menu publishes a `StartGame` command. `MenuModel`
   handles it and raises `GameStarted`; the score HUD takes over.
4. Click the score button to add points (`AdjustScore +10`). Meanwhile
   `ScoreDecayWorker` drains one point per second from a background thread via
   `CommandBus.Enqueue`.
5. Let the score fall below zero — `ScoreModel` raises `GameEnded`, the menu
   returns and shows your final score.
6. Click **Start** again to play another round; the score resets.

## How the pieces talk

Nothing holds a reference to anything else — every interaction goes through a bus:

- **`CommandBus` (N:1)** — `StartGame` → `MenuModel`, `AdjustScore` → `ScoreModel`.
  Both handlers are installed in a single `TryInstall` call (each call rebuilds
  the bus, so they must be registered together).
- **`EventBus` (N:M)** — `GameStarted` / `GameEnded` drive the HUD switching in
  `BasicPubSubSample`; `ScoreChanged` updates `ScoreHud`. Add another listener
  (logger, sound, analytics) and nothing else has to change.

`AdjustScore` is sent from two places — the button (main thread, `Publish`) and
the decay worker (background thread, `Enqueue`) — yet exactly one handler owns
it. That N:1 guarantee is the point of the `CommandBus`.
