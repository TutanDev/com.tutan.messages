using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Composition root ─────────────────────────────────────────────────

    /// <summary>
    /// Declares every command handler in one place. Attach to a GameObject in the
    /// scene and assign the <see cref="ScoreBoard"/> reference.
    /// <para>
    /// This is where the N:1 rule lives: each command type is bound exactly once via
    /// <see cref="CommandBus.TryInstall"/>. A duplicate or null handler is reported as
    /// a return value (logged below), never thrown. Publishers — see
    /// <c>ScoreSimulator</c> — just call <c>CommandBus.Publish</c>.
    /// </para>
    /// </summary>
    public sealed class BasicPubSubInstaller : MonoBehaviour
    {
        [SerializeField] ScoreBoard _scoreBoard;

        void Awake()
        {
            bool ok = CommandBus.TryInstall(out string error, r => r
                .Handle<ResetScore>(_scoreBoard.Handle));

            if (!ok)
                Debug.LogError($"[BasicPubSubInstaller] Command install failed: {error}");
        }
    }
}
