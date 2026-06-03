namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Messages ─────────────────────────────────────────────────────────
    //
    // Two message types carry the core score loop (the lifecycle messages further
    // down — StartGame / GameStarted / ResetScore / GameEnded — drive menu↔game
    // switching):
    //
    //   AdjustScore  (ICommand) — N:1. "Change the score by Delta." Sent by the
    //                 button (main thread) AND the decay worker (off thread).
    //                 Exactly one handler owns it: ScoreModel.
    //
    //   ScoreChanged (IEvent)   — N:M. "The score is now Total." Published by
    //                 ScoreModel after every change; any number of views can
    //                 listen. Here the HUD is the only subscriber, but adding a
    //                 second (logger, sound, analytics) needs no other change.
    //
    // Both are unmanaged structs — the bus never boxes or allocates for them.



    /// <summary>Command: change the score by <see cref="Delta"/> (may be negative).</summary>
    public struct AdjustScore : ICommand
    {
        public int Delta;
    }

    /// <summary>Event: the score changed. Carries the new total and the delta that produced it.</summary>
    public struct ScoreChanged : IEvent
    {
        public int Total;
        public int Delta;
    }

    public struct ResetScore : ICommand { }

    public struct StartGame : ICommand { }
    public struct GameStarted : IEvent { }

    public struct GameEnded : IEvent
    {
        public int FinalScore;
    }
}
