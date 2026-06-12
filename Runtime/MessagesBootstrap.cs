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
    /// at your composition root through <see cref="CommandBus.Install"/>.
    /// </summary>
    public static class MessagesBootstrap
    {
#if !TUTAN_MESSAGES_NO_AUTO_HOST
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallHost()
        {
            // HideInHierarchy only — NOT HideAndDontSave. DontSave-flagged objects
            // are excluded from the editor's play-mode cleanup, so they would leak
            // one hidden host per play session. DontDestroyOnLoad already provides
            // the scene-load persistence.
            var go = new GameObject("[MessagesHost]")
            {
                hideFlags = HideFlags.HideInHierarchy
            };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MessagesHost>();
        }
#endif
    }
}
