using UnityEngine;
using UnityEngine.UI;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── View ─────────────────────────────────────────────────────────────

    /// <summary>
    /// On-screen view — its label and button are serialized references wired in the
    /// sample scene. Demonstrates both directions of the bus from a single component:
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

        Subscription _subscription;


        void OnEnable()
        {
            // EventBus is N:M and subscribe-anytime — wire the view to the event here.
            // The subscription toggles with the component, so hold the disposable
            // handle and dispose it in OnDisable (rather than AddTo(this), which
            // would keep it live until the GameObject is destroyed).
            _subscription = EventBus.Subscribe<ScoreChanged>(OnScoreChanged);
            _button.onClick.AddListener(OnIncreaseClicked);
        }

        void OnDisable()
        {
            _subscription.Dispose();
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
