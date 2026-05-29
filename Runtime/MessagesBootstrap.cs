using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Creates a persistent <see cref="MessagesHost"/> at startup so the bus
    /// drains queued messages every <c>LateUpdate</c> with zero configuration,
    /// and optionally auto-installs every declared <see cref="ICommandHandler{T}"/>
    /// into the <see cref="CommandBus"/>.
    ///
    /// Behavior is controlled by Scripting Define Symbols (set via the Messages
    /// project settings page):
    /// <list type="bullet">
    /// <item><c>TUTAN_MESSAGES_AUTOINSTALL_DRAINERS</c> — spawns the host that
    /// calls <see cref="CommandBus.DrainQueues"/> / <see cref="EventBus.DrainQueues"/>
    /// each frame.</item>
    /// <item><c>TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS</c> — reflects over loaded
    /// assemblies, instantiates every concrete <see cref="ICommandHandler"/> with a
    /// parameterless constructor, and binds each closed <see cref="ICommandHandler{T}"/>
    /// it implements through a single <see cref="CommandBus.TryInstall"/>.</item>
    /// </list>
    /// When the symbols are absent, you are responsible for the equivalent setup
    /// at your composition root.
    /// </summary>
    public static class MessagesBootstrap
    {
#if TUTAN_MESSAGES_AUTOINSTALL_DRAINERS
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallHost()
        {
            var go = new GameObject("[MessagesHost]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MessagesHost>();
        }
#endif

#if true
        // Runs at AfterAssembliesLoaded — late enough that user assemblies are
        // available for reflection, early enough that handlers are bound before
        // any BeforeSceneLoad code can publish a command.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void InstallCommandHandlers()
        {
            var handlerTypes = DiscoverHandlerTypes();
            if (handlerTypes.Count == 0) return;

            var handleMethod = typeof(CommandRegistry).GetMethod(nameof(CommandRegistry.Handle));

            bool installed = CommandBus.TryInstall(out var error, registry =>
            {
                foreach (var type in handlerTypes)
                {
                    object instance;
                    try
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Messages] Auto-install: failed to construct {type.FullName}: {ex.Message}");
                        continue;
                    }

                    foreach (var iface in type.GetInterfaces())
                    {
                        if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(ICommandHandler<>))
                            continue;

                        var commandType = iface.GetGenericArguments()[0];
                        var delegateType = typeof(MessageHandler<>).MakeGenericType(commandType);
                        var ifaceHandle = iface.GetMethod(nameof(ICommandHandler<DummyCommand>.Handle));
                        var del = Delegate.CreateDelegate(delegateType, instance, ifaceHandle);
                        handleMethod.MakeGenericMethod(commandType).Invoke(registry, new object[] { del });
                    }
                }
            });

            if (!installed)
                Debug.LogError(error);
        }

        static List<Type> DiscoverHandlerTypes()
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract || t.IsInterface || t.ContainsGenericParameters) continue;
                    if (!typeof(ICommandHandler).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    result.Add(t);
                }
            }
            return result;
        }

        // Only used to give nameof() something concrete to chew on for the
        // ICommandHandler<T>.Handle lookup. Never instantiated.
        struct DummyCommand : ICommand { }
#endif
    }
}
