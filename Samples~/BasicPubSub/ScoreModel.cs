namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Domain ───────────────────────────────────────────────────────────

    /// <summary>
    /// The single owner of the score. It is the one handler for the
    /// <see cref="AdjustScore"/> and <see cref="ResetScore"/> commands (N:1) and the
    /// only publisher of the <see cref="ScoreChanged"/> event (N:M).
    /// <para>
    /// Note what it does <b>not</b> have: a reference to the button, the HUD, or
    /// the decay worker. Every input arrives as a command; every output leaves as
    /// an event. That is the whole point of the bus.
    /// </para>
    /// <para>
    /// Its <c>Handle(ref T)</c> methods match the bus's <c>MessageHandler&lt;T&gt;</c>
    /// delegate, so <see cref="BasicPubSubSample"/> binds them as this command's single
    /// handler at the composition root through <see cref="CommandBus.Install"/>.
    /// </para>
    /// </summary>
    public sealed class ScoreModel
    {
        const int StartingScore = 10;

        int _total = StartingScore;

        /// <summary>Handles <see cref="AdjustScore"/>. Always invoked on the main thread
        /// (the bus drains queued commands in <c>LateUpdate</c>), so publishing an event
        /// from here is safe.</summary>
        public void Handle(ref AdjustScore cmd)
        {
            int newTotal = _total + cmd.Delta;

            if (newTotal < 0)
            {
                // Game over. The final score is the fatal decay tick itself: the decay
                // grows every second, so the longer the player survived, the bigger the
                // delta that ended the run — survival time is the score.
                EventBus.Publish(new GameEnded { FinalScore = cmd.Delta });
            }
            else
            {
                _total = newTotal;
                // Broadcast the result. The model neither knows nor cares who is listening.
                EventBus.Publish(new ScoreChanged { Total = _total, Delta = cmd.Delta });
            }
        }

        public void Handle(ref ResetScore command)
        {
            _total = StartingScore;
            EventBus.Publish(new ScoreChanged { Total = _total, Delta = 0 });
        }
    }
}
