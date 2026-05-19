using System;
using System.Linq;
using UnityEngine;

namespace Tutan.MessageBus
{
    /// <summary>
    /// Attribute to show a type selection dropdown in the inspector.
    /// Works on string fields (storing AssemblyQualifiedName) or MessageReference fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class MessageTypeAttribute : PropertyAttribute
    {
        public Type BaseType { get; }
        public MessageTypeAttribute(Type baseType = null) => BaseType = baseType ?? typeof(IMessage);
    }

    /// <summary>
    /// Base class for serializing a message type and its data in the inspector.
    /// </summary>
    [Serializable]
    public abstract class MessageReference
    {
        [SerializeField] internal string typeName;
        [SerializeField] internal string dataJson;

        public string TypeName => typeName;
        public bool IsValid => !string.IsNullOrEmpty(typeName);

        public Type GetMessageType() => IsValid ? Type.GetType(typeName) : null;

        /// <summary>
        /// Creates an instance of the message from serialized data.
        /// Note: This boxes the struct.
        /// </summary>
        public object CreateMessage()
        {
            var type = GetMessageType();
            if (type == null) return null;

            object msg = string.IsNullOrEmpty(dataJson)
                ? Activator.CreateInstance(type)
                : JsonUtility.FromJson(dataJson, type);

            // Fill Timestamp if the field exists
            var timestampField = type.GetField("Timestamp", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (timestampField != null)
            {
                if (timestampField.FieldType == typeof(float))
                    timestampField.SetValue(msg, Time.time);
                else if (timestampField.FieldType == typeof(double))
                    timestampField.SetValue(msg, (double)Time.time);
                else if (timestampField.FieldType == typeof(long))
                    timestampField.SetValue(msg, DateTime.UtcNow.Ticks);
            }

            return msg;
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
        public void Publish()
        {
            var msg = CreateMessage();
            if (msg is IEvent ev)
            {
                // EventBus.Publish<T>(T message)
                var method = typeof(EventBus).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethod && m.GetParameters().Length == 1 && !m.GetParameters()[0].ParameterType.IsByRef);
                
                method?.MakeGenericMethod(ev.GetType()).Invoke(null, new[] { msg });
            }
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
        public void Publish()
        {
            var msg = CreateMessage();
            if (msg is ICommand cmd)
            {
                // CommandBus.Publish<T>(T message)
                var method = typeof(CommandBus).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethod && m.GetParameters().Length == 1 && !m.GetParameters()[0].ParameterType.IsByRef);

                method?.MakeGenericMethod(cmd.GetType()).Invoke(null, new[] { msg });
            }
        }
    }
}
