using System;
using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Composition root ─────────────────────────────────────────────────

    /// <summary>
    /// The one component you add to the scene. Drop it on an empty GameObject and
    /// press Play — it assembles the whole sample and wires the CommandBus.
    /// <para>
    /// This is the only class that knows about all the parts. Everything it builds
    /// (<see cref="ScoreModel"/>, <see cref="ScoreHud"/>, <see cref="ScoreDecayWorker"/>)
    /// talks exclusively through the bus, never to each other.
    /// </para>
    /// <para>
    /// The N:1 rule lives here: <see cref="AdjustScore"/> is bound to exactly one
    /// handler via <see cref="CommandBus.TryInstall"/>. A duplicate or null handler is
    /// reported as a return value (logged below), never thrown.
    /// </para>
    /// </summary>
    public sealed class BasicPubSubSample : MonoBehaviour
    {
        [SerializeField] MenuHud _menuHud;
        [SerializeField] ScoreHud _scoreHud;


        MenuModel _menuModel;
        ScoreModel _scoreModel;

        ScoreDecayWorker _enemy;

        SubscriptionToken _startToken;
        SubscriptionToken _gameOverToken;


        void Awake()
        {
            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

            // Make sure queued commands/events get drained on the main thread even if
            // auto-install of the host is turned off in the Messages settings.
            EnsureMessagesHost();

            // Build the models and declare each as the single handler for its command.
            // Both handlers MUST be installed in one TryInstall call — each call rebuilds
            // the bus from scratch, so a second call would discard the first handler.
            _menuModel = new();
            _scoreModel = new();
            bool ok = CommandBus.TryInstall(out string error, r =>
            {
                r.Handle<StartGame>(_menuModel.Handle);
                r.Handle<AdjustScore>(_scoreModel.Handle);
            });
            if (!ok)
            {
                Debug.LogError($"[BasicPubSubSample] Command install failed: {error}");
                return;
            }

            _startToken = EventBus.Subscribe<GameStarted>(OnGameStarted);
            _gameOverToken = EventBus.Subscribe<GameEnded>(OnGameEnded);
        }

        private void OnGameStarted(ref GameStarted message)
        {
            _scoreHud.gameObject.SetActive(true);
            _menuHud.gameObject.SetActive(false);

            // Reset to the starting score for a fresh game. This publishes ScoreChanged,
            // so it must run after the HUD is active and subscribed (above).
            _scoreModel.Reset();

            // Spawn the publishers/subscribers. They find each other through the bus.
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

        void EnsureMessagesHost()
        {
            if (FindAnyObjectByType<MessagesHost>() != null) return;
            var go = new GameObject("[MessagesHost]");
            DontDestroyOnLoad(go);
            go.AddComponent<MessagesHost>();
        }
    }
}
