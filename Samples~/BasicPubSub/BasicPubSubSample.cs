using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Composition root ─────────────────────────────────────────────────

    /// <summary>
    /// The one component you add to the scene. Drop it on an empty GameObject and
    /// press Play — it switches between the menu and score HUDs and owns the
    /// game-lifecycle events.
    /// <para>
    /// This is the composition root: in <see cref="Awake"/> it builds the command
    /// handlers (<see cref="ScoreModel"/>, <see cref="MenuModel"/>) and binds each
    /// command to its single handler through one <see cref="CommandBus.TryInstall"/>
    /// call. Queue draining is handled for free by the auto-spawned
    /// <c>[MessagesHost]</c>, so there is nothing else to wire — just press Play.
    /// </para>
    /// <para>
    /// Everything it builds (<see cref="ScoreHud"/>, <see cref="ScoreDecayWorker"/>, the
    /// models) talks exclusively through the bus, never to each other. The N:1 guarantee
    /// for <see cref="AdjustScore"/> is enforced by <see cref="CommandBus.TryInstall"/>:
    /// exactly one handler owns the command no matter who publishes it.
    /// </para>
    /// </summary>
    public sealed class BasicPubSubSample : MonoBehaviour
    {
        [SerializeField] MenuHud _menuHud;
        [SerializeField] ScoreHud _scoreHud;

        ScoreModel _scoreModel;
        MenuModel _menuModel;

        ScoreDecayWorker _enemy;

        SubscriptionToken _startToken;
        SubscriptionToken _gameOverToken;


        void Awake()
        {
            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

            // Composition root: bind each command to its one handler. The models are
            // plain C# objects — their Handle(ref T) methods match MessageHandler<T>.
            _scoreModel = new ScoreModel();
            _menuModel = new MenuModel();

            bool ok = CommandBus.TryInstall(out string error, r => r
                .Handle<AdjustScore>(_scoreModel.Handle)
                .Handle<ResetScore>(_scoreModel.Handle)
                .Handle<StartGame>(_menuModel.Handle));

            if (!ok)
                Debug.LogError($"[BasicPubSub] Command install failed: {error}");

            _startToken = EventBus.Subscribe<GameStarted>(OnGameStarted);
            _gameOverToken = EventBus.Subscribe<GameEnded>(OnGameEnded);
        }

        private void OnGameStarted(ref GameStarted message)
        {
            _scoreHud.gameObject.SetActive(true);
            _menuHud.gameObject.SetActive(false);

            CommandBus.Publish(new ResetScore());
            _enemy = gameObject.AddComponent<ScoreDecayWorker>();
        }

        private void OnGameEnded(ref GameEnded message)
        {
            Destroy(_enemy);

            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

            // The menu was inactive (and unsubscribed) when GameEnded fired, so push the
            // final score directly rather than relying on the event it missed.
            _menuHud.SetFinalScore(message.FinalScore);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe(_startToken);
            EventBus.Unsubscribe(_gameOverToken);
        }
    }
}
