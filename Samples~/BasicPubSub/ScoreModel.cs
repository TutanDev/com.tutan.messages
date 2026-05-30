using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Domain ───────────────────────────────────────────────────────────

    /// <summary>
    /// The single owner of the score. It is the one handler for the
    /// <see cref="AdjustScore"/> command (N:1) and the only publisher of the
    /// <see cref="ScoreChanged"/> event (N:M).
    /// <para>
    /// Note what it does <b>not</b> have: a reference to the button, the HUD, or
    /// the decay worker. Every input arrives as a command; every output leaves as
    /// an event. That is the whole point of the bus.
    /// </para>
    /// <para>
    /// It implements <see cref="ICommandHandler{AdjustScore}"/> as a self-documenting
    /// marker, but the binding still happens explicitly at the composition root —
    /// see <see cref="BasicPubSubSample"/>.
    /// </para>
    /// </summary>
    public sealed class ScoreModel : ICommandHandler<AdjustScore>
    {
        const int StartingScore = 10;

        int _total = StartingScore;

        /// <summary>Reset to the starting score for a new game and announce it.</summary>
        public void Reset()
        {
            _total = StartingScore;
            EventBus.Publish(new ScoreChanged { Total = _total, Delta = 0 });
        }

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
    }
}
