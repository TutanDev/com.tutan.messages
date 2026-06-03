using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Composition root ─────────────────────────────────────────────────

    /// <summary>
    /// The one component you add to the scene. Drop it on an empty GameObject and
    /// press Play — it switches between the menu and score HUDs and owns the
    /// game-lifecycle events.
    /// <para>
    /// It deliberately does <b>not</b> wire the CommandBus. The command handlers
    /// (<see cref="ScoreModel"/>, <see cref="MenuModel"/>) are discovered and bound
    /// automatically at startup by the auto-install bootstrap, which reflects over every
    /// <see cref="ICommandHandler{T}"/> and binds it through a single
    /// <see cref="CommandBus.TryInstall"/>. That requires the
    /// <c>TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS</c> and
    /// <c>TUTAN_MESSAGES_AUTOINSTALL_DRAINERS</c> scripting defines — enable them under
    /// <b>Project Settings ▸ Tutan ▸ Messages</b> (Auto-Install Command Bus /
    /// Auto-Install Drainers). Without them no handler is bound and nothing happens.
    /// </para>
    /// <para>
    /// Everything it builds (<see cref="ScoreHud"/>, <see cref="ScoreDecayWorker"/>, the
    /// models) talks exclusively through the bus, never to each other. The N:1 guarantee
    /// for <see cref="AdjustScore"/> still holds — it is just enforced by the auto-install
    /// path rather than a hand-written <see cref="CommandBus.TryInstall"/> call here.
    /// </para>
    /// </summary>
    public sealed class BasicPubSubSample : MonoBehaviour
    {
        [SerializeField] MenuHud _menuHud;
        [SerializeField] ScoreHud _scoreHud;



        ScoreModel _scoreModel;

        ScoreDecayWorker _enemy;

        SubscriptionToken _startToken;
        SubscriptionToken _gameOverToken;


        void Awake()
        {
            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

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
