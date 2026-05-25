using UnityEngine;

namespace Tutan.Messages.Samples.XRHandGesture
{
    /// <summary>Subscriber 1: triggers UI actions on Pinch.</summary>
    public sealed class GestureMenu : MonoBehaviour
    {
        SubscriptionToken _token;
        void OnEnable()  => _token = EventBus.Subscribe<HandGesture>(OnGesture);
        void OnDisable() => EventBus.Unsubscribe(_token);

        void OnGesture(ref HandGesture g)
        {
            if (g.GestureId == Gestures.Pinch && g.Confidence > 0.85f)
                Debug.Log($"[GestureMenu] Pinch activated by hand {g.HandIndex}");
        }
    }
}
