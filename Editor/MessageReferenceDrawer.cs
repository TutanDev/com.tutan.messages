using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.MessageBus.Editor
{
    [CustomPropertyDrawer(typeof(MessageTypeAttribute))]
    public class MessageTypeDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var attr = (MessageTypeAttribute)attribute;
            var container = new VisualElement();

            if (property.propertyType != SerializedPropertyType.String)
            {
                container.Add(new Label("MessageTypeAttribute only works on string fields."));
                return container;
            }

            var types = TypeCache.GetTypesDerivedFrom(attr.BaseType)
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.Name)
                .ToList();

            var choices = types.Select(t => t.Name).ToList();
            choices.Insert(0, "(None)");
            
            var values = types.Select(t => t.AssemblyQualifiedName).ToList();
            values.Insert(0, string.Empty);

            var currentIndex = Math.Max(0, values.IndexOf(property.stringValue));

            var popup = new PopupField<string>(property.displayName, choices, currentIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int idx = choices.IndexOf(evt.newValue);
                property.stringValue = values[idx];
                property.serializedObject.ApplyModifiedProperties();
            });

            container.Add(popup);
            return container;
        }
    }

    [CustomPropertyDrawer(typeof(MessageReference), true)]
    public class MessageReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.style.marginBottom = 2;
            root.style.marginTop = 2;
            root.style.borderBottomWidth = 1;
            root.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            root.style.paddingBottom = 4;

            var typeNameProp = property.FindPropertyRelative("typeName");
            var dataJsonProp = property.FindPropertyRelative("dataJson");

            Type baseType = typeof(IMessage);
            if (fieldInfo.FieldType == typeof(EventReference) || fieldInfo.FieldType.IsSubclassOf(typeof(EventReference)))
                baseType = typeof(IEvent);
            else if (fieldInfo.FieldType == typeof(CommandReference) || fieldInfo.FieldType.IsSubclassOf(typeof(CommandReference)))
                baseType = typeof(ICommand);

            var types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.IsInterface && t.IsValueType) // Bus wants structs
                .OrderBy(t => t.Name)
                .ToList();

            var choices = types.Select(t => t.Name).ToList();
            choices.Insert(0, "(None)");
            var values = types.Select(t => t.AssemblyQualifiedName).ToList();
            values.Insert(0, string.Empty);

            int currentIndex = Math.Max(0, values.IndexOf(typeNameProp.stringValue));

            var typePopup = new PopupField<string>(property.displayName, choices, currentIndex);
            typePopup.style.flexGrow = 1;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.Add(typePopup);

            var publishBtn = new Button();
            publishBtn.tooltip = "Publish Message (Synthetic)";
            publishBtn.style.width = 20;
            publishBtn.style.height = 18;
            publishBtn.style.marginLeft = 2;
            publishBtn.style.paddingLeft = 0;
            publishBtn.style.paddingRight = 0;
            publishBtn.style.paddingTop = 0;
            publishBtn.style.paddingBottom = 0;

            // Use a built-in Unity icon
            var icon = EditorGUIUtility.IconContent("d_PlayButton").image as Texture2D;
            publishBtn.style.backgroundImage = icon;

            publishBtn.clicked += () =>
            {
                if (string.IsNullOrEmpty(typeNameProp.stringValue)) return;

                // Create a temporary instance to call Publish
                MessageReference tempRef = null;
                if (fieldInfo.FieldType == typeof(EventReference) || fieldInfo.FieldType.IsSubclassOf(typeof(EventReference)))
                    tempRef = new EventReference();
                else if (fieldInfo.FieldType == typeof(CommandReference) || fieldInfo.FieldType.IsSubclassOf(typeof(CommandReference)))
                    tempRef = new CommandReference();

                if (tempRef != null)
                {
                    // Copy values
                    var typeField = typeof(MessageReference).GetField("typeName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var dataField = typeof(MessageReference).GetField("dataJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    typeField.SetValue(tempRef, typeNameProp.stringValue);
                    dataField.SetValue(tempRef, dataJsonProp.stringValue);

                    // Call Publish via reflection to be safe with the specific type
                    var publishMethod = tempRef.GetType().GetMethod("Publish");
                    publishMethod?.Invoke(tempRef, null);
                }
            };

            headerRow.Add(publishBtn);
            root.Add(headerRow);

            var dataContainer = new VisualElement();
            dataContainer.style.marginLeft = 15;
            root.Add(dataContainer);

            Action RefreshDataUI = () =>
            {
                dataContainer.Clear();
                if (string.IsNullOrEmpty(typeNameProp.stringValue)) return;

                var type = Type.GetType(typeNameProp.stringValue);
                if (type == null) return;

                // Create a temporary object to hold the data for editing
                // We use JsonUtility to sync between the string and this object
                object instance;
                try
                {
                    instance = string.IsNullOrEmpty(dataJsonProp.stringValue) 
                        ? Activator.CreateInstance(type) 
                        : JsonUtility.FromJson(dataJsonProp.stringValue, type);
                }
                catch
                {
                    instance = Activator.CreateInstance(type);
                }

                // Since we can't easily use PropertyField on a non-serialized object,
                // we'll use IMGUIContainer to draw the fields of the struct.
                var imgui = new IMGUIContainer(() =>
                {
                    EditorGUI.BeginChangeCheck();
                    
                    // Simple reflection-based inspector for the struct
                    var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        DrawField(f, instance);
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        dataJsonProp.stringValue = JsonUtility.ToJson(instance);
                        dataJsonProp.serializedObject.ApplyModifiedProperties();
                    }
                });
                dataContainer.Add(imgui);
            };

            typePopup.RegisterValueChangedCallback(evt =>
            {
                int idx = choices.IndexOf(evt.newValue);
                typeNameProp.stringValue = values[idx];
                dataJsonProp.stringValue = string.Empty; // Reset data on type change
                typeNameProp.serializedObject.ApplyModifiedProperties();
                publishBtn.SetEnabled(!string.IsNullOrEmpty(typeNameProp.stringValue));
                RefreshDataUI();
            });

            RefreshDataUI();

            return root;
        }

        private void DrawField(System.Reflection.FieldInfo field, object target)
        {
            if (field.Name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
                return;

            var val = field.GetValue(target);
var type = field.FieldType;

            if (type == typeof(int))
                field.SetValue(target, EditorGUILayout.IntField(field.Name, (int)val));
            else if (type == typeof(float))
                field.SetValue(target, EditorGUILayout.FloatField(field.Name, (float)val));
            else if (type == typeof(bool))
                field.SetValue(target, EditorGUILayout.Toggle(field.Name, (bool)val));
            else if (type == typeof(string))
                field.SetValue(target, EditorGUILayout.TextField(field.Name, (string)val));
            else if (type == typeof(Vector3))
                field.SetValue(target, EditorGUILayout.Vector3Field(field.Name, (Vector3)val));
            else if (type == typeof(Color))
                field.SetValue(target, EditorGUILayout.ColorField(field.Name, (Color)val));
            else if (type.IsEnum)
                field.SetValue(target, EditorGUILayout.EnumPopup(field.Name, (Enum)val));
            else
            {
                EditorGUILayout.LabelField(field.Name, $"Unsupported type: {type.Name}");
            }
        }
    }
}
