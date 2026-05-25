using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Publisher ────────────────────────────────────────────────────────

    /// <summary>
    /// Click anywhere in the Game view (or press Space) to publish a
    /// PlayerScored event. Press R to send the ResetScore command.
    /// </summary>
    public sealed class ScoreSimulator : MonoBehaviour
    {
        [SerializeField] int _pointsPerHit = 10;

        void Update()
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                EventBus.Publish(new PlayerScored
                {
                    Points = _pointsPerHit,
                    Timestamp = Time.time
                });
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                CommandBus.Publish(new ResetScore());
            }
        }
    }
}
