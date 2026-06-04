using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Spawns a persistent <see cref="MessagesHost"/> at startup so both buses
    /// drain their queued messages every <c>LateUpdate</c> with zero configuration.
    ///
    /// On by default. Define <c>TUTAN_MESSAGES_NO_AUTO_HOST</c> to opt out when you
    /// want to own the drain loop yourself — attach <see cref="MessagesHost"/> to a
    /// persistent GameObject, or call <see cref="CommandBus.DrainQueues"/> /
    /// <see cref="EventBus.DrainQueues"/> from your own update logic (a PlayerLoop
    /// callback, a manager, etc.).
    ///
    /// Command handlers are <b>not</b> wired here. Bind each command's single handler
    /// at your composition root through <see cref="CommandBus.TryInstall"/>.
    /// </summary>
    public static class MessagesBootstrap
    {
#if !TUTAN_MESSAGES_NO_AUTO_HOST
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallHost()
        {
            var go = new GameObject("[MessagesHost]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MessagesHost>();
        }
#endif
    }
}
