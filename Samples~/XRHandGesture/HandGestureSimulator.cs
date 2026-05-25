using UnityEngine;

namespace Tutan.Messages.Samples.XRHandGesture
{
    /// <summary>
    /// Publishes synthetic hand gestures on a timer. In a real XR app this
    /// would be your hand-tracking pipeline.
    /// </summary>
    public sealed class HandGestureSimulator : MonoBehaviour
    {
        [SerializeField] float _intervalSeconds = 1.5f;
        float _elapsed;
        int _next;

        void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < _intervalSeconds) return;
            _elapsed = 0;

            EventBus.Publish(new HandGesture
            {
                HandIndex = _next % 2,
                GestureId = (_next++ % 3) + 1,
                Confidence = 0.9f
            });
        }
    }
}
