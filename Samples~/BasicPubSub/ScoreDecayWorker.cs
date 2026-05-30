using System.Threading;
using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Off-thread publisher ─────────────────────────────────────────────

    /// <summary>
    /// A background thread that drains the score by one point every second to
    /// simulate decay (a timer, network tick, simulation step — anything that does
    /// not live on the main thread).
    /// <para>
    /// It cannot call <see cref="CommandBus.Publish"/> — that is main-thread only.
    /// Instead it uses <see cref="CommandBus.Enqueue"/>, which is thread-safe. The
    /// queued command is dispatched on the main thread by <c>MessagesHost</c> in the
    /// next <c>LateUpdate</c>, so <see cref="ScoreModel"/> still runs where it is safe
    /// to publish events and touch Unity objects.
    /// </para>
    /// <para>
    /// Both this worker and the HUD button send the very same <see cref="AdjustScore"/>
    /// command to the very same handler — one sync, one async — which is exactly the
    /// N:1 guarantee the CommandBus exists to provide.
    /// </para>
    /// </summary>
    public sealed class ScoreDecayWorker : MonoBehaviour
    {
        int DecayPerSecond = -1;

        Thread _thread;
        volatile bool _running;

        void OnEnable()
        {
            _running = true;
            _thread = new Thread(DecayLoop) { IsBackground = true, Name = "ScoreDecayWorker" };
            _thread.Start();
        }

        void OnDisable()
        {
            // Signal the loop to stop and wait for it to unwind, so it cannot enqueue a
            // stray decay tick after the game has ended.
            _running = false;
            _thread?.Join();
            _thread = null;
        }

        void DecayLoop()
        {
            // Sleep in short slices instead of one 1000 ms block so the loop notices a
            // stop request quickly — that keeps OnDisable's Join() from stalling the main
            // thread for up to a second.
            const int SliceMs = 50;
            int elapsed = 0;

            while (_running)
            {
                Thread.Sleep(SliceMs);
                elapsed += SliceMs;
                if (elapsed < 1000) continue;
                elapsed = 0;

                // Thread-safe hand-off. Dispatched on the main thread next frame.
                CommandBus.Enqueue(new AdjustScore { Delta = DecayPerSecond-- });
            }
        }
    }
}
