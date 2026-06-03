# Basic Pub/Sub Sample

A tiny score-clicker built entirely on `Tutan.Messages` — it shows the two buses
working together with nothing wired directly between the parts.

## Before you run it

This sample binds its command handlers and drains the buses through the package's
**auto-install bootstrap**, so it needs two scripting defines turned on:

- **Auto-Install Command Bus** (`TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS`) — discovers
  every `ICommandHandler<T>` (here `ScoreModel` and `MenuModel`) and binds it.
- **Auto-Install Drainers** (`TUTAN_MESSAGES_AUTOINSTALL_DRAINERS`) — spawns the host
  that drains queued commands/events every frame.

Enable both under **Project Settings ▸ Tutan ▸ Messages**. Without them no handler is
bound and the buttons do nothing.

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
   returns and shows your final score.
6. Click **Start** again to play another round; the score resets.

## How the pieces talk

Nothing holds a reference to anything else — every interaction goes through a bus:

- **`CommandBus` (N:1)** — `StartGame` → `MenuModel`; `AdjustScore` and `ResetScore`
  → `ScoreModel`. None of these handlers are wired by hand: the auto-install bootstrap
  reflects over every `ICommandHandler<T>` and binds them all through a single
  `CommandBus.TryInstall`, enforcing the N:1 rule (one handler per command type).
- **`EventBus` (N:M)** — `GameStarted` / `GameEnded` drive the HUD switching in
  `BasicPubSubSample`; `ScoreChanged` updates `ScoreHud`. Add another listener
  (logger, sound, analytics) and nothing else has to change.

`AdjustScore` is sent from two places — the button (main thread, `Publish`) and
the decay worker (background thread, `Enqueue`) — yet exactly one handler owns
it. That N:1 guarantee is the point of the `CommandBus`.
