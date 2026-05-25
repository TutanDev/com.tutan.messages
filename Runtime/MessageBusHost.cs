using UnityEngine;

namespace Tutan.MessageBus
{
    /// <summary>
    /// MonoBehaviour that drains <see cref="CommandBus"/> and <see cref="EventBus"/>
    /// queues every <c>LateUpdate</c>. Created automatically at startup by
    /// <see cref="MessageBusBootstrap"/> unless the
    /// <c>TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP</c> scripting define is set.
    /// You can also attach it manually to a persistent GameObject in your scene.
    /// </summary>
    [AddComponentMenu("Tutan/MessageBus Host")]
    public sealed class MessageBusHost : MonoBehaviour
    {
        void LateUpdate()
        {
            // Main-thread frame counter that worker threads can read without
            // touching UnityEngine.Time. The call is [Conditional], so in release
            // player builds it (and the Time.frameCount read) strip out entirely,
            // leaving no instrumentation touchpoint in this hot loop.
            MessageBusInstrumentation.SyncFrame(Time.frameCount);

            CommandBus.DrainQueues();
            EventBus.DrainQueues();
        }
    }
}
