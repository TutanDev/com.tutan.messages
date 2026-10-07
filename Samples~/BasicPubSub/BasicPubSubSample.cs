using UnityEngine;
using UnityEngine.EventSystems;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Composition root ─────────────────────────────────────────────────

    /// <summary>
    /// The sample's composition root, already placed in <c>BasicPubSubSample.unity</c>
    /// with its <see cref="MenuHud"/> / <see cref="ScoreHud"/> references wired —
    /// open the scene and press Play. It switches between the menu and score HUDs
    /// and owns the game-lifecycle events.
    /// <para>
    /// This is the composition root: in <see cref="Awake"/> it builds the command
    /// handlers (<see cref="ScoreModel"/>, <see cref="MenuModel"/>) and binds each
    /// command to its single handler through one <see cref="CommandBus.Install"/>
    /// call. Queue draining is handled for free by the auto-spawned
    /// <c>[MessagesHost]</c>, so there is nothing else to wire — just press Play.
    /// </para>
    /// <para>
    /// Everything it builds (the models and <see cref="ScoreDecayWorker"/>) and
    /// <see cref="ScoreHud"/> talk exclusively through the bus, never to each other.
    /// Only this root holds direct references: to what it builds (the models, and
    /// the worker it starts and stops) and to the two HUDs, to switch them and to
    /// hand the final score to <see cref="MenuHud"/>. The N:1 guarantee for
    /// <see cref="AdjustScore"/> is enforced by <see cref="CommandBus.Install"/>:
    /// exactly one handler owns the command no matter who publishes it.
    /// </para>
    /// </summary>
    public sealed class BasicPubSubSample : MonoBehaviour
    {
        [SerializeField] MenuHud _menuHud;
        [SerializeField] ScoreHud _scoreHud;

        ScoreModel _scoreModel;
        MenuModel _menuModel;

        // Background decay driver for the current run; null between runs.
        ScoreDecayWorker _enemy;

        void Awake()
        {
            EnsureUIInputModule();

            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

            // Composition root: bind each command to its one handler. The models are
            // plain C# objects — their Handle(ref T) methods match MessageHandler<T>.
            _scoreModel = new ScoreModel();
            _menuModel = new MenuModel();

            var install = CommandBus.Install(r => r
                .Handle<AdjustScore>(_scoreModel.Handle)
                .Handle<ResetScore>(_scoreModel.Handle)
                .Handle<StartGame>(_menuModel.Handle));

            if (!install.Ok)
                Debug.LogError($"[BasicPubSub] Command install failed: {install.Error}");

            // Scoped to this component's GameObject: both subscriptions are
            // disposed automatically when it is destroyed — no OnDestroy needed.
            EventBus.Subscribe<GameStarted>(OnGameStarted).AddTo(this);
            EventBus.Subscribe<GameEnded>(OnGameEnded).AddTo(this);
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
            if (_enemy != null)
            {
                Destroy(_enemy);
                _enemy = null;
            }

            _menuHud.gameObject.SetActive(true);
            _scoreHud.gameObject.SetActive(false);

            // MenuHud is a passive view that does not subscribe to GameEnded (it was
            // inactive when the event fired anyway), so push the final score to it.
            _menuHud.SetFinalScore(message.FinalScore);
        }

        // The scene ships an EventSystem without an input module so it works with
        // either input backend: the Input System package (Unity 6 default) or the
        // legacy Input Manager. Pick the module that matches the project's Active
        // Input Handling setting — the wrong one throws or ignores clicks.
        void EnsureUIInputModule()
        {
            var eventSystem = EventSystem.current != null
                ? EventSystem.current
                : FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<BaseInputModule>() != null) return;

#if ENABLE_INPUT_SYSTEM
            // ENABLE_INPUT_SYSTEM follows Active Input Handling, not whether the
            // com.unity.inputsystem package is installed. A direct type reference would
            // then break all of Assembly-CSharp, so look the module up by name; the
            // constant string also lets the linker keep it in stripped player builds.
            // With no actions asset assigned, the module binds its default UI actions.
            var inputSystemModule = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null)
            {
                eventSystem.gameObject.AddComponent(inputSystemModule);
                return;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
#else
            Debug.LogWarning("[BasicPubSub] No UI input module available, so the buttons "
                + "will not respond. Install the Input System package (com.unity.inputsystem), "
                + "or set Player Settings > Active Input Handling to Both or Input Manager (Old).");
#endif
        }
    }
}
