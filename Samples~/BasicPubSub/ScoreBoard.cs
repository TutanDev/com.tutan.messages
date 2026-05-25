using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Subscriber ───────────────────────────────────────────────────────

    /// <summary>
    /// Listens for score events and command resets. Attach to any GameObject
    /// in a scene and press Play. See Console for output.
    /// </summary>
    public sealed class ScoreBoard : MonoBehaviour
    {
        SubscriptionToken _eventToken;
        SubscriptionToken _commandToken;
        int _total;

        void OnEnable()
        {
            _eventToken   = EventBus.Subscribe<PlayerScored>(OnScored);
            _commandToken = CommandBus.Subscribe<ResetScore>(OnReset);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(_eventToken);
            CommandBus.Unsubscribe(_commandToken);
        }

        void OnScored(ref PlayerScored e)
        {
            _total += e.Points;
            Debug.Log($"[ScoreBoard] +{e.Points} → total {_total} (t={e.Timestamp:F2})");
        }

        void OnReset(ref ResetScore _)
        {
            Debug.Log($"[ScoreBoard] Reset (was {_total})");
            _total = 0;
        }
    }
}
