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
    /// It implements <see cref="ICommandHandler{T}"/> once per command it owns. Those
    /// interfaces are how the auto-install bootstrap finds it: at startup the bootstrap
    /// reflects over every <see cref="ICommandHandler{T}"/>, instantiates it via its
    /// parameterless constructor, and binds it through one <see cref="CommandBus.TryInstall"/> —
    /// there is no composition-root wiring code in this sample. (Requires the
    /// <c>TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS</c> define; see <see cref="BasicPubSubSample"/>.)
    /// </para>
    /// </summary>
    public sealed class ScoreModel : ICommandHandler<AdjustScore>, ICommandHandler<ResetScore>
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
                // Game over. Report the score the player finished with (before the fatal
                // tick), not the delta that pushed it under.
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
