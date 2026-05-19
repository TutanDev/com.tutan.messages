# Basic Pub/Sub Sample

The smallest possible introduction to `Tutan.MessageBus`.

1. Create an empty GameObject in your scene.
2. Add the `ScoreBoard` component to it.
3. Add the `ScoreSimulator` component to the same (or another) GameObject.
4. Press Play.
5. Click anywhere in the Game view (or press Space) — a `PlayerScored` event
   fires, `ScoreBoard` increments the total and logs it.
6. Press R — a `ResetScore` command fires, the score resets.

`ScoreBoard` has no reference to `ScoreSimulator` and vice versa. They are
fully decoupled through the bus.
