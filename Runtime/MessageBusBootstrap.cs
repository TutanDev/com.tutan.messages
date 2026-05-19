using UnityEngine;

namespace Tutan.MessageBus
{
    /// <summary>
    /// Creates a persistent <see cref="MessageBusHost"/> at startup so the bus
    /// drains queued messages every <c>LateUpdate</c> with zero configuration.
    ///
    /// Opt out by adding <c>TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP</c> to your
    /// project's Scripting Define Symbols (Project Settings > Player). When
    /// disabled, you are responsible for calling
    /// <see cref="CommandBus.DrainQueues"/> and <see cref="EventBus.DrainQueues"/>
    /// each frame — typically by attaching <see cref="MessageBusHost"/> to a
    /// persistent GameObject or via a custom PlayerLoop injection.
    /// </summary>
    public static class MessageBusBootstrap
    {
#if !TUTAN_MESSAGEBUS_DISABLE_AUTOBOOTSTRAP
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var go = new GameObject("[MessageBusHost]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MessageBusHost>();
        }
#endif
    }
}
