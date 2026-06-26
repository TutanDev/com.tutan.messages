using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.Messages.Editor
{
    [CustomPropertyDrawer(typeof(EventTypeAttribute))]
    [CustomPropertyDrawer(typeof(CommandTypeAttribute))]
    public class MessageTypeDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var baseType = attribute switch
            {
                EventTypeAttribute e => e.BaseType,
                CommandTypeAttribute c => c.BaseType,
                _ => typeof(IMessage)
            };
            var container = new VisualElement();

            if (property.propertyType != SerializedPropertyType.String)
            {
                container.Add(new Label($"[{attribute.GetType().Name}] only works on string fields."));
                return container;
            }

            var types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.Name)
                .ToList();

            var labels = BuildTypeLabels(types);

            var values = types.Select(t => t.AssemblyQualifiedName).ToList();
            values.Insert(0, string.Empty);

            var currentIndex = ResolveSelection(property.stringValue, types, values, labels, out bool missing);

            // The popup is index-based: two types can share a short name (same
            // struct name, different namespace), so mapping the selection back
            // through the display string would resolve to the wrong type.
            var popup = new PopupField<int>(
                property.displayName, Enumerable.Range(0, values.Count).ToList(), currentIndex,
                i => labels[i], i => labels[i]);
            popup.RegisterValueChangedCallback(evt =>
            {
                property.stringValue = values[evt.newValue];
                property.serializedObject.ApplyModifiedProperties();
            });

            container.Add(popup);
            if (missing)
                container.Add(MissingTypeWarning(property.stringValue));
            return container;
        }

        // Display labels for a "(None)" + types popup. Types whose short name
        // collides with another entry are shown with their full name so the two
        // are distinguishable in the dropdown.
        internal static List<string> BuildTypeLabels(List<Type> types)
        {
            var duplicated = types.GroupBy(t => t.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            var labels = types.Select(t => duplicated.Contains(t.Name) ? t.FullName : t.Name).ToList();
            labels.Insert(0, "(None)");
            return labels;
        }

        // Resolve which popup entry a stored type string should select. Three cases:
        //   • empty                          → (None) at index 0.
        //   • matches a listed type — by exact string, or by resolved Type identity
        //     when the stored assembly-qualified name has drifted (assembly version
        //     changed) but still resolves → that type's entry.
        //   • unresolved or no longer listed → a trailing "(Missing) …" entry is
        //     appended to `labels`/`values` and selected, so the orphaned value stays
        //     visible and is preserved instead of snapping silently to (None).
        // Matching by Type identity (not raw string) mirrors the drift-tolerant
        // resolution in ScriptFileField/MessageReference, so a still-valid reference
        // never reads as missing just because its assembly identity moved. Appends at
        // most one entry to `labels`/`values`; sets `missing` for the third case so
        // callers can warn and gate actions.
        internal static int ResolveSelection(
            string stored, List<Type> types, List<string> values, List<string> labels, out bool missing)
        {
            missing = false;
            if (string.IsNullOrEmpty(stored)) return 0;

            int exact = values.IndexOf(stored);
            if (exact >= 0) return exact;

            var resolved = ScriptFileField.ResolveType(stored);
            if (resolved != null)
            {
                int t = types.IndexOf(resolved);
                if (t >= 0) return t + 1; // +1 for the "(None)" entry at index 0.
            }

            missing = true;
            string shown = resolved != null ? resolved.FullName : stored;
            labels.Add($"(Missing) {ShortName(shown)}");
            values.Add(stored);
            return values.Count - 1;
        }

        // A warning shown beneath the popup when the stored type can't be resolved,
        // so an orphaned reference is obvious instead of looking like an empty field.
        internal static HelpBox MissingTypeWarning(string stored) => new HelpBox(
            $"Stored message type '{ShortName(stored)}' could not be found — it may have been " +
            "renamed, moved, or deleted. The reference is preserved; pick a type to replace it.",
            HelpBoxMessageType.Warning);

        // Last path segment of a stored type string for compact display:
        // "Namespace.Type, Assembly, Version=…" → "Type".
        static string ShortName(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            int comma = stored.IndexOf(',');
            string full = comma >= 0 ? stored.Substring(0, comma).Trim() : stored;
            int dot = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
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

            var labels = MessageTypeDrawer.BuildTypeLabels(types);
            var values = types.Select(t => t.AssemblyQualifiedName).ToList();
            values.Insert(0, string.Empty);

            int currentIndex = MessageTypeDrawer.ResolveSelection(
                typeNameProp.stringValue, types, values, labels, out bool missing);

            // Index-based for the same reason as MessageTypeDrawer: duplicate
            // short names must not resolve to the first match.
            var typePopup = new PopupField<int>(
                property.displayName, Enumerable.Range(0, values.Count).ToList(), currentIndex,
                i => labels[i], i => labels[i]);
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

                // Build the reference matching the field category (baseType was
                // resolved above), copy the serialized values straight in — the
                // fields are internal and this Editor assembly has InternalsVisibleTo
                // access — and dispatch through the virtual Publish().
                MessageReference tempRef = baseType == typeof(IEvent) ? new EventReference()
                                         : baseType == typeof(ICommand) ? new CommandReference()
                                         : null;
                if (tempRef == null) return;

                tempRef.typeName = typeNameProp.stringValue;
                tempRef.dataJson = dataJsonProp.stringValue;
                tempRef.Publish();
            };

            headerRow.Add(publishBtn);
            root.Add(headerRow);

            // An unresolved stored type can't be published — gate the button and say why.
            publishBtn.SetEnabled(!missing && !string.IsNullOrEmpty(typeNameProp.stringValue));
            if (missing)
                root.Add(MessageTypeDrawer.MissingTypeWarning(typeNameProp.stringValue));

            var dataContainer = new VisualElement();
            dataContainer.style.marginLeft = 15;
            root.Add(dataContainer);

            Action RefreshDataUI = () =>
            {
                dataContainer.Clear();
                if (string.IsNullOrEmpty(typeNameProp.stringValue)) return;

                var type = ScriptFileField.ResolveType(typeNameProp.stringValue);
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

                // One native UI-Toolkit field per public field of the boxed struct.
                // Each field writes back into the boxed `instance` via reflection and
                // re-serializes it to the JSON property — the same data flow as before,
                // just per-field instead of one IMGUI pass.
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    dataContainer.Add(CreateFieldElement(f, instance, dataJsonProp));
                }
            };

            typePopup.RegisterValueChangedCallback(evt =>
            {
                typeNameProp.stringValue = values[evt.newValue];
                dataJsonProp.stringValue = string.Empty; // Reset data on type change
                typeNameProp.serializedObject.ApplyModifiedProperties();
                publishBtn.SetEnabled(!string.IsNullOrEmpty(typeNameProp.stringValue));
                RefreshDataUI();
            });

            RefreshDataUI();

            return root;
        }

        // Builds a native UI-Toolkit field bound to one public field of the boxed
        // struct `instance`. Edits write back through reflection and re-serialize the
        // whole struct to `dataJsonProp`. Unknown types render a disabled label so the
        // editor degrades gracefully instead of throwing.
        private VisualElement CreateFieldElement(System.Reflection.FieldInfo field, object instance, SerializedProperty dataJsonProp)
        {
            var label = ObjectNames.NicifyVariableName(field.Name);
            var type = field.FieldType;
            var val = field.GetValue(instance);

            void Persist()
            {
                dataJsonProp.stringValue = JsonUtility.ToJson(instance);
                dataJsonProp.serializedObject.ApplyModifiedProperties();
            }

            // Wires a field's value-changed callback to the reflection write + persist.
            VisualElement Bind<T>(BaseField<T> el, Action<T> write)
            {
                el.RegisterValueChangedCallback(evt =>
                {
                    write(evt.newValue);
                    Persist();
                });
                return el;
            }

            if (type == typeof(int))
                return Bind(new IntegerField(label) { value = (int)val }, v => field.SetValue(instance, v));
            if (type == typeof(long))
                return Bind(new LongField(label) { value = (long)val }, v => field.SetValue(instance, v));
            if (type == typeof(float))
                return Bind(new FloatField(label) { value = (float)val }, v => field.SetValue(instance, v));
            if (type == typeof(double))
                return Bind(new DoubleField(label) { value = (double)val }, v => field.SetValue(instance, v));
            if (type == typeof(bool))
                return Bind(new Toggle(label) { value = (bool)val }, v => field.SetValue(instance, v));
            if (type == typeof(string))
                return Bind(new TextField(label) { value = (string)val }, v => field.SetValue(instance, v));
            if (type.IsEnum)
                return Bind(new EnumField(label, (Enum)val), v => field.SetValue(instance, v));
            if (type == typeof(Vector2))
                return Bind(new Vector2Field(label) { value = (Vector2)val }, v => field.SetValue(instance, v));
            if (type == typeof(Vector3))
                return Bind(new Vector3Field(label) { value = (Vector3)val }, v => field.SetValue(instance, v));
            if (type == typeof(Vector4))
                return Bind(new Vector4Field(label) { value = (Vector4)val }, v => field.SetValue(instance, v));
            if (type == typeof(Vector2Int))
                return Bind(new Vector2IntField(label) { value = (Vector2Int)val }, v => field.SetValue(instance, v));
            if (type == typeof(Vector3Int))
                return Bind(new Vector3IntField(label) { value = (Vector3Int)val }, v => field.SetValue(instance, v));
            if (type == typeof(Color))
                return Bind(new ColorField(label) { value = (Color)val }, v => field.SetValue(instance, v));
            if (type == typeof(Quaternion))
                // Quaternion has no dedicated field — edit it as euler angles, same as
                // the Transform inspector does.
                return Bind(new Vector3Field(label) { value = ((Quaternion)val).eulerAngles },
                    v => field.SetValue(instance, Quaternion.Euler(v)));

            var unsupported = new Label($"{label}: unsupported type ({type.Name})");
            unsupported.SetEnabled(false);
            return unsupported;
        }
    }
}
