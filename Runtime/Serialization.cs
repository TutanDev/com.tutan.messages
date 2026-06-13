using System;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Decorate a <c>string</c> field to show a dropdown of all concrete
    /// <see cref="IEvent"/> types in the inspector. The field stores the selected
    /// type's <c>AssemblyQualifiedName</c>; resolve it with <c>Type.GetType(field)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class EventTypeAttribute : PropertyAttribute
    {
        public Type BaseType => typeof(IEvent);
    }

    /// <summary>
    /// Decorate a <c>string</c> field to show a dropdown of all concrete
    /// <see cref="ICommand"/> types in the inspector. The field stores the selected
    /// type's <c>AssemblyQualifiedName</c>; resolve it with <c>Type.GetType(field)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class CommandTypeAttribute : PropertyAttribute
    {
        public Type BaseType => typeof(ICommand);
    }

    /// <summary>
    /// Base class for serializing a message type and its data in the inspector.
    /// <para>
    /// The payload round-trips through <c>JsonUtility</c>, which only serializes
    /// plain structs marked <c>[Serializable]</c>. Message structs you want to
    /// author through an <see cref="EventReference"/>/<see cref="CommandReference"/>
    /// must carry that attribute — without it the payload silently stays at its
    /// default values. Bus dispatch itself does not need it.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class MessageReference
    {
        [SerializeField] internal string typeName;
        [SerializeField] internal string dataJson;

        public string TypeName => typeName;
        public bool IsValid => !string.IsNullOrEmpty(typeName);

        public Type GetMessageType() => IsValid ? ResolveType(typeName) : null;

        /// <summary>
        /// Resolve a stored (assembly-qualified) type name resiliently. Tries the
        /// direct <see cref="Type.GetType(string,bool)"/> first, then falls back to
        /// scanning loaded assemblies so resolution still succeeds if the
        /// assembly's version/identity has drifted since the name was serialized.
        /// </summary>
        static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var found = Type.GetType(name, false);
            if (found != null) return found;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = asm.GetType(name, false);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Publish the serialized message to its bus. Implemented per message
        /// category (event vs command).
        /// </summary>
        public abstract void Publish();

        /// <summary>
        /// Creates an instance of the message from serialized data. The returned
        /// message carries exactly the values authored in the inspector — no fields
        /// are populated implicitly.
        /// Note: This boxes the struct.
        /// </summary>
        public object CreateMessage()
        {
            var type = GetMessageType();
            if (type == null) return null;

            return string.IsNullOrEmpty(dataJson)
                ? Activator.CreateInstance(type)
                : JsonUtility.FromJson(dataJson, type);
        }
}

    /// <summary>
    /// Serialized reference to an <see cref="IEvent"/>.
    /// </summary>
    [Serializable]
    public class EventReference : MessageReference
    {
        /// <summary>
        /// Publishes the serialized event to the <see cref="EventBus"/>.
        /// </summary>
        public override void Publish()
        {
            var msg = CreateMessage();
            if (msg is IEvent)
                EventBus.Bus.PublishBoxed(msg);
        }
    }

    /// <summary>
    /// Serialized reference to an <see cref="ICommand"/>.
    /// </summary>
    [Serializable]
    public class CommandReference : MessageReference
    {
        /// <summary>
        /// Publishes the serialized command to the <see cref="CommandBus"/>.
        /// </summary>
        public override void Publish()
        {
            var msg = CreateMessage();
            if (msg is ICommand)
                CommandBus.Bus.PublishBoxed(msg);
        }
    }
}
