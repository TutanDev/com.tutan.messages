using UnityEngine;
using UnityEngine.UI;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── View ─────────────────────────────────────────────────────────────

    /// <summary>
    /// On-screen UI built entirely in code so the sample needs no scene wiring.
    /// Demonstrates both directions of the bus from a single component:
    /// <list type="bullet">
    /// <item><b>Listens to events</b> — subscribes to <see cref="ScoreChanged"/> and
    /// refreshes the score label whenever the total moves, no matter who moved it.</item>
    /// <item><b>Triggers commands</b> — the button publishes an <see cref="AdjustScore"/>
    /// command. It has no reference to <see cref="ScoreModel"/>; it just states intent.</item>
    /// </list>
    /// </summary>
    public sealed class ScoreHud : MonoBehaviour
    {
        const int PointsPerClick = 1;

        [SerializeField] Text _scoreLabel;
        [SerializeField] Button _button;

        SubscriptionToken _token;


        void OnEnable()
        {
            // EventBus is N:M and subscribe-anytime — wire the view to the event here.
            _token = EventBus.Subscribe<ScoreChanged>(OnScoreChanged);
            _button.onClick.AddListener(OnIncreaseClicked);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(_token);
            _button.onClick.RemoveListener(OnIncreaseClicked);
        }

        void OnScoreChanged(ref ScoreChanged e)
        {
            // Runs on the main thread (events are published from the drained command
            // handler), so touching UI here is safe.
            _scoreLabel.text = $"Score: {e.Total}";
        }

        void OnIncreaseClicked()
        {
            // Fire-and-forget intent. Exactly one handler (ScoreModel) will receive it.
            CommandBus.Publish(new AdjustScore { Delta = PointsPerClick });
        }
    }
}
