using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Subscriber ───────────────────────────────────────────────────────

    /// <summary>
    /// Listens for score events and handles the reset command. Attach to a GameObject
    /// in a scene and press Play. See Console for output.
    /// <para>
    /// Events are subscribed here in <c>OnEnable</c> (EventBus is N:M, subscribe-anytime).
    /// The <see cref="ResetScore"/> command handler, by contrast, is <b>not</b> wired here —
    /// commands are N:1 and must be bound once at the composition root. See
    /// <see cref="BasicPubSubInstaller"/>, which binds <see cref="Handle"/> via
    /// <see cref="CommandBus.TryInstall"/>.
    /// </para>
    /// </summary>
    public sealed class ScoreBoard : MonoBehaviour, ICommandHandler<ResetScore>
    {
        SubscriptionToken _eventToken;
        int _total;

        void OnEnable()
        {
            _eventToken = EventBus.Subscribe<PlayerScored>(OnScored);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(_eventToken);
        }

        void OnScored(ref PlayerScored e)
        {
            _total += e.Points;
            Debug.Log($"[ScoreBoard] +{e.Points} → total {_total} (t={e.Timestamp:F2})");
        }

        /// <summary>Command handler for <see cref="ResetScore"/>. Bound at the composition root.</summary>
        public void Handle(ref ResetScore _)
        {
            Debug.Log($"[ScoreBoard] Reset (was {_total})");
            _total = 0;
        }
    }
}
