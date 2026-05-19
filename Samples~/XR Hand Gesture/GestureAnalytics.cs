using UnityEngine;

namespace Tutan.MessageBus.Samples.XRHandGesture
{
    /// <summary>Subscriber 2: records analytics for every gesture.</summary>
    public sealed class GestureAnalytics : MonoBehaviour
    {
        SubscriptionToken _token;
        void OnEnable()  => _token = EventBus.Subscribe<HandGesture>(OnGesture);
        void OnDisable() => EventBus.Unsubscribe(_token);

        void OnGesture(ref HandGesture g)
        {
            Debug.Log($"[Analytics] hand={g.HandIndex} gesture={g.GestureId} conf={g.Confidence:F2}");
        }
    }
}
