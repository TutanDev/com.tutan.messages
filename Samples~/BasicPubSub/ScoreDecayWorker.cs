using System.Collections;
using System.Threading;
using UnityEngine;

namespace Tutan.Messages.Samples.BasicPubSub
{
    // ── Off-thread publisher ─────────────────────────────────────────────

    /// <summary>
    /// A background thread that drains the score every second, one point more each
    /// tick (-1, -2, -3, …) so the pressure ramps up the longer the game runs. It
    /// stands in for anything that does not live on the main thread — a timer,
    /// network tick, simulation step.
    /// <para>
    /// It cannot call <see cref="CommandBus.Publish{T}(T)"/> — that is main-thread only.
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
    /// <para>
    /// Web players have no managed threads, so there the same ticks are enqueued from
    /// a coroutine on the main thread instead; everything downstream is unchanged.
    /// </para>
    /// </summary>
    public sealed class ScoreDecayWorker : MonoBehaviour
    {
        const int TickMs = 1000;

        // Grows (more negative) by one each tick — the decay accelerates over time.
        int _nextDecayDelta = -1;

        void EnqueueDecayTick()
        {
            // Thread-safe hand-off. Dispatched on the main thread at the next drain.
            CommandBus.Enqueue(new AdjustScore { Delta = _nextDecayDelta-- });
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Thread.Start() throws on Web players, which would leave the game unable to
        // end. A coroutine keeps the same one-second cadence on the main thread.
        void OnEnable() => StartCoroutine(DecayRoutine());

        void OnDisable() => StopAllCoroutines();

        IEnumerator DecayRoutine()
        {
            var tick = new WaitForSeconds(TickMs / 1000f);
            while (true)
            {
                yield return tick;
                EnqueueDecayTick();
            }
        }
#else
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
            // Signal the loop to stop and wait for it to unwind, so the thread is gone
            // once OnDisable returns. A tick it already queued — or queued between game
            // over and this OnDisable, which Destroy() defers to the end of the frame —
            // still reaches ScoreModel, whose game-over guard ignores it.
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
                if (elapsed < TickMs) continue;
                elapsed = 0;

                EnqueueDecayTick();
            }
        }
#endif
    }
}
