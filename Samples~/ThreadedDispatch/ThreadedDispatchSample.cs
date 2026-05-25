using System.Threading;
using System.Threading.Tasks;
using Tutan.Messages;
using UnityEngine;

namespace Tutan.Messages.Samples.ThreadedDispatch
{
    /// <summary>
    /// Demonstrates publishing from a worker thread via Enqueue. The bus drains
    /// queued messages on the main thread in LateUpdate (via MessagesHost),
    /// so the handler is guaranteed to run on the main thread — safe to touch
    /// Unity APIs.
    /// </summary>
    public sealed class ThreadedDispatchSample : MonoBehaviour
    {
        SubscriptionToken _token;
        int _nextJobId;

        void OnEnable() => _token = EventBus.Subscribe<WorkCompleted>(OnWorkCompleted);
        void OnDisable() => EventBus.Unsubscribe(_token);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                LaunchJob();
        }

        void LaunchJob()
        {
            int id = ++_nextJobId;
            Debug.Log($"[Main thread {Thread.CurrentThread.ManagedThreadId}] Dispatching job {id}");

            Task.Run(() =>
            {
                // Simulate work on a worker thread
                Thread.Sleep(50);
                int result = id * 42;

                // Thread-safe — dispatched on main thread next frame
                EventBus.Enqueue(new WorkCompleted
                {
                    JobId = id,
                    Result = result,
                    ThreadId = Thread.CurrentThread.ManagedThreadId
                });
            });
        }

        // Runs on the main thread thanks to DrainQueues in LateUpdate
        void OnWorkCompleted(ref WorkCompleted e)
        {
            Debug.Log($"[Main thread {Thread.CurrentThread.ManagedThreadId}] " +
                      $"Job {e.JobId} (worker tid {e.ThreadId}) → {e.Result}. " +
                      $"Touching Unity: {transform.position}");
        }
    }

    public struct WorkCompleted : IEvent
    {
        public int JobId;
        public int Result;
        public long ThreadId;
    }
}
