using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// MonoBehaviour that drains <see cref="CommandBus"/> and <see cref="EventBus"/>
    /// queues every <c>LateUpdate</c>. Spawned automatically at startup by
    /// <see cref="MessagesBootstrap"/> (define <c>TUTAN_MESSAGES_NO_AUTO_HOST</c> to
    /// opt out). You can also attach it manually to a persistent GameObject in your scene.
    /// </summary>
    [AddComponentMenu("Tutan/Messages Host")]
    public sealed class MessagesHost : MonoBehaviour
    {
        // One host must drain per frame, not two — guards against a manually
        // placed host coexisting with the auto-spawned one (or two manual ones).
        static MessagesHost s_active;

        void Awake()
        {
            if (s_active != null && s_active != this)
            {
                Debug.LogWarning(
                    "[Messages] A MessagesHost is already active — destroying the duplicate component. " +
                    "Define TUTAN_MESSAGES_NO_AUTO_HOST if you want to own the drain loop yourself.",
                    s_active);
                Destroy(this);
                return;
            }
            s_active = this;
        }

        void OnDestroy()
        {
            if (s_active == this) s_active = null;
        }

        void LateUpdate()
        {
            // Main-thread frame counter that worker threads can read without
            // touching UnityEngine.Time. The call is [Conditional], so in release
            // player builds it (and the Time.frameCount read) strip out entirely,
            // leaving no instrumentation touchpoint in this hot loop.
            MessagesInstrumentation.SyncFrame(Time.frameCount);

            CommandBus.DrainQueues();
            EventBus.DrainQueues();
        }
    }
}
