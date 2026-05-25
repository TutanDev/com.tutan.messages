using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// MonoBehaviour that drains <see cref="CommandBus"/> and <see cref="EventBus"/>
    /// queues every <c>LateUpdate</c>. Created automatically at startup by
    /// <see cref="MessagesBootstrap"/> unless the
    /// <c>TUTAN_MESSAGES_DISABLE_AUTOBOOTSTRAP</c> scripting define is set.
    /// You can also attach it manually to a persistent GameObject in your scene.
    /// </summary>
    [AddComponentMenu("Tutan/Messages Host")]
    public sealed class MessagesHost : MonoBehaviour
    {
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
