using System;
using UnityEngine;

namespace Tutan.Messages
{
    /// <summary>
    /// Decorate a <c>string</c> field to show a dropdown of all concrete
    /// <see cref="IEvent"/> types in the inspector. The field stores the selected
    /// type's <c>AssemblyQualifiedName</c>; resolve it with
    /// <see cref="MessageTypeResolver.Resolve"/>. Prefer that over a bare
    /// <c>Type.GetType(field)</c>, which returns null once the type's assembly has
    /// been renamed or its identity has drifted since the name was serialized.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class EventTypeAttribute : PropertyAttribute
    {
        /// <summary>The interface the dropdown filters by (<see cref="IEvent"/>).</summary>
        public Type BaseType => typeof(IEvent);
    }

    /// <summary>
    /// Decorate a <c>string</c> field to show a dropdown of all concrete
    /// <see cref="ICommand"/> types in the inspector. The field stores the selected
    /// type's <c>AssemblyQualifiedName</c>; resolve it with
    /// <see cref="MessageTypeResolver.Resolve"/>. Prefer that over a bare
    /// <c>Type.GetType(field)</c>, which returns null once the type's assembly has
    /// been renamed or its identity has drifted since the name was serialized.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class CommandTypeAttribute : PropertyAttribute
    {
        /// <summary>The interface the dropdown filters by (<see cref="ICommand"/>).</summary>
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

        /// <summary>The stored <c>AssemblyQualifiedName</c> of the message type; empty when none is picked.</summary>
        public string TypeName => typeName;

        /// <summary>True when a type has been picked. Does not guarantee the type still resolves — see <see cref="GetMessageType"/>.</summary>
        public bool IsValid => !string.IsNullOrEmpty(typeName);

        /// <summary>
        /// Resolve the stored type through <see cref="MessageTypeResolver.Resolve"/>,
        /// which survives assembly renames. Null when nothing is picked or the type
        /// was deleted/renamed since serialization.
        /// </summary>
        public Type GetMessageType() => IsValid ? MessageTypeResolver.Resolve(typeName) : null;

        /// <summary>
        /// Publish the serialized message to its bus. Implemented per message
        /// category (event vs command).
        /// </summary>
        public abstract void Publish();

        /// <summary>
        /// Creates an instance of the message from serialized data. The returned
        /// message carries exactly the values authored in the inspector — no fields
        /// are populated implicitly. If the stored JSON cannot be parsed (e.g. it
        /// was mangled by a merge or edited by hand), a warning is logged and the
        /// type's default values are used - never an exception.
        /// Note: This boxes the struct.
        /// </summary>
        public object CreateMessage()
        {
            var type = GetMessageType();
            if (type == null) return null;

            if (string.IsNullOrEmpty(dataJson))
                return Activator.CreateInstance(type);

            try
            {
                return JsonUtility.FromJson(dataJson, type);
            }
            catch (Exception)
            {
                Debug.LogWarning(
                    $"Messages: stored payload for '{typeName}' is not valid JSON - " +
                    "publishing the type's default values. Re-edit the payload in the inspector.");
                return Activator.CreateInstance(type);
            }
        }
    }

    /// <summary>
    /// Serialized reference to an <see cref="IEvent"/>.
    /// </summary>
    [Serializable]
    public class EventReference : MessageReference
    {
        /// <summary>
        /// Publishes the serialized event to the <see cref="EventBus"/>. If the
        /// stored type cannot be resolved to an <see cref="IEvent"/> struct
        /// (renamed, moved, or deleted since it was serialized), a warning is
        /// logged and nothing is published.
        /// </summary>
        public override void Publish()
        {
            var msg = CreateMessage();
            if (msg is IEvent)
                EventBus.Bus.PublishBoxed(msg);
            else
                Debug.LogWarning(
                    $"Messages: EventReference could not publish '{typeName}' - the stored " +
                    "type could not be resolved to an IEvent struct. Re-pick the type in the inspector.");
        }
    }

    /// <summary>
    /// Serialized reference to an <see cref="ICommand"/>.
    /// </summary>
    [Serializable]
    public class CommandReference : MessageReference
    {
        /// <summary>
        /// Publishes the serialized command to the <see cref="CommandBus"/>. If the
        /// stored type cannot be resolved to an <see cref="ICommand"/> struct
        /// (renamed, moved, or deleted since it was serialized), a warning is
        /// logged and nothing is published.
        /// </summary>
        public override void Publish()
        {
            var msg = CreateMessage();
            if (msg is ICommand)
                CommandBus.Bus.PublishBoxed(msg);
            else
                Debug.LogWarning(
                    $"Messages: CommandReference could not publish '{typeName}' - the stored " +
                    "type could not be resolved to an ICommand struct. Re-pick the type in the inspector.");
        }
    }
}
