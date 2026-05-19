using UnityEngine;

namespace Tutan.MessageBus.Samples.XRHandGesture
{
    /// <summary>Subscriber 3: triggers haptic feedback above a confidence threshold.</summary>
    public sealed class GestureHaptics : MonoBehaviour
    {
        SubscriptionToken _token;
        void OnEnable()  => _token = EventBus.Subscribe<HandGesture>(OnGesture);
        void OnDisable() => EventBus.Unsubscribe(_token);

        void OnGesture(ref HandGesture g)
        {
            if (g.Confidence > 0.9f)
                Debug.Log($"[Haptics] Pulse on hand {g.HandIndex}");
        }
    }
}
