using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.Messages.Editor
{
    /// <summary>
    /// Inspector dropdown for <c>string</c> fields decorated with
    /// <see cref="EventTypeAttribute"/> / <see cref="CommandTypeAttribute"/>. Lists
    /// every concrete message struct of the matching category and stores the pick's
    /// <c>AssemblyQualifiedName</c>. UI Toolkit only (no IMGUI fallback).
    /// </summary>
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

            var types = ListMessageTypes(baseType);

            // What the popup was last built from. Rebuilding (not just re-selecting)
            // keeps the "(Missing)" entry and its warning in step with the value.
            string shownValue = null;
            bool shownMixed = false;

            void Rebuild()
            {
                container.Clear();
                shownValue = property.stringValue;
                shownMixed = property.hasMultipleDifferentValues;

                var labels = BuildTypeLabels(types);
                var values = types.Select(t => t.AssemblyQualifiedName).ToList();
                values.Insert(0, string.Empty);

                var currentIndex = ResolveSelection(shownValue, types, values, labels, out bool missing);

                // The popup is index-based: two types can share a short name (same
                // struct name, different namespace), so mapping the selection back
                // through the display string would resolve to the wrong type.
                var popup = new PopupField<int>(
                    property.displayName, Enumerable.Range(0, values.Count).ToList(), currentIndex,
                    i => labels[i], i => labels[i]);
                // Differing values across a multi-selection show as mixed; a pick
                // writes the one type to every selected object.
                popup.showMixedValue = shownMixed;
                popup.RegisterValueChangedCallback(evt =>
                {
                    property.stringValue = values[evt.newValue];
                    property.serializedObject.ApplyModifiedProperties();
                    Rebuild();
                });

                container.Add(popup);
                if (missing && !shownMixed)
                    container.Add(MissingTypeWarning(shownValue));
            }

            // Undo/redo, prefab revert and other inspectors change the string without
            // going through the popup. The drawer's own writes arrive here too and
            // are skipped because Rebuild already shows them.
            container.TrackPropertyValue(property, p =>
            {
                if (p.stringValue != shownValue || p.hasMultipleDifferentValues != shownMixed)
                    Rebuild();
            });

            Rebuild();
            return container;
        }

        // The structs a dropdown for `baseType` offers. Structs only: the bus API is
        // constrained to `where T : struct`, so a class implementing IEvent/ICommand
        // could never be published. Open generics (EntityChanged<T>) are excluded
        // too: without type arguments they can't be instantiated or published.
        internal static List<Type> ListMessageTypes(Type baseType) =>
            TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.IsInterface && t.IsValueType && !t.ContainsGenericParameters)
                .OrderBy(t => t.Name)
                .ToList();

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

    /// <summary>
    /// Inspector for <see cref="EventReference"/> / <see cref="CommandReference"/>
    /// (and subclasses): a type dropdown, an inline editor for the struct's
    /// serialized public fields (stored as JSON), and a ▶ button that publishes the
    /// message immediately through the reference's own <c>Publish()</c>. Works for
    /// plain fields as well as arrays and lists of references. With several objects
    /// selected, the payload is editable only while they all hold the same message.
    /// UI Toolkit only (no IMGUI fallback).
    /// </summary>
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

            // For array/List<T> fields Unity invokes the drawer per element, but
            // fieldInfo still describes the collection field — classify by the
            // element type, or the category (and the publish button) is lost.
            var referenceType = ElementType(fieldInfo.FieldType);
            Type baseType = typeof(IMessage);
            if (typeof(EventReference).IsAssignableFrom(referenceType))
                baseType = typeof(IEvent);
            else if (typeof(CommandReference).IsAssignableFrom(referenceType))
                baseType = typeof(ICommand);

            var types = MessageTypeDrawer.ListMessageTypes(baseType);

            // The serialized values the UI was last built from or last wrote. The
            // trackers at the bottom rebuild when the stored values stop matching
            // (undo/redo, prefab revert, another inspector); the drawer's own writes
            // update these first, so their echoes are skipped and a field being
            // typed into keeps its focus.
            string shownType = null;
            string shownJson = null;
            bool shownMixed = false;

            bool IsMixed() => typeNameProp.hasMultipleDifferentValues || dataJsonProp.hasMultipleDifferentValues;

            void Rebuild()
            {
                root.Clear();
                shownType = typeNameProp.stringValue;
                shownJson = dataJsonProp.stringValue;
                shownMixed = IsMixed();

                var labels = MessageTypeDrawer.BuildTypeLabels(types);
                var values = types.Select(t => t.AssemblyQualifiedName).ToList();
                values.Insert(0, string.Empty);

                int currentIndex = MessageTypeDrawer.ResolveSelection(
                    shownType, types, values, labels, out bool missing);

                // Index-based for the same reason as MessageTypeDrawer: duplicate
                // short names must not resolve to the first match.
                var typePopup = new PopupField<int>(
                    property.displayName, Enumerable.Range(0, values.Count).ToList(), currentIndex,
                    i => labels[i], i => labels[i]);
                typePopup.style.flexGrow = 1;
                typePopup.showMixedValue = typeNameProp.hasMultipleDifferentValues;
                typePopup.RegisterValueChangedCallback(evt =>
                {
                    typeNameProp.stringValue = values[evt.newValue];
                    dataJsonProp.stringValue = string.Empty; // Reset data on type change
                    typeNameProp.serializedObject.ApplyModifiedProperties();
                    Rebuild();
                });

                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.Add(typePopup);

                var publishBtn = new Button();
                publishBtn.style.width = 20;
                publishBtn.style.height = 18;
                publishBtn.style.marginLeft = 2;
                publishBtn.style.paddingLeft = 0;
                publishBtn.style.paddingRight = 0;
                publishBtn.style.paddingTop = 0;
                publishBtn.style.paddingBottom = 0;

                // The un-prefixed name resolves to the current skin's variant
                // ("d_PlayButton" is the dark-skin icon only).
                var icon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
                publishBtn.style.backgroundImage = icon;

                publishBtn.clicked += () =>
                {
                    if (string.IsNullOrEmpty(typeNameProp.stringValue)) return;

                    // boxedValue deserializes a fresh copy of the stored reference,
                    // with every serialized field a subclass adds, so its Publish()
                    // override sees the inspector's values and nothing it mutates
                    // carries over to the next click.
                    (property.boxedValue as MessageReference)?.Publish();
                };

                // An unresolved stored type can't be published, and differing values
                // across a multi-selection have no single message to publish.
                publishBtn.SetEnabled(!missing && !shownMixed && !string.IsNullOrEmpty(shownType));
                publishBtn.tooltip = shownMixed
                    ? "The selected objects hold different messages; select one to publish it."
                    : "Publish Message (Synthetic)";

                headerRow.Add(publishBtn);
                root.Add(headerRow);

                if (missing && !typeNameProp.hasMultipleDifferentValues)
                    root.Add(MessageTypeDrawer.MissingTypeWarning(shownType));

                var dataContainer = new VisualElement();
                dataContainer.style.marginLeft = 15;
                root.Add(dataContainer);
                BuildPayloadEditor(dataContainer);
            }

            void BuildPayloadEditor(VisualElement dataContainer)
            {
                // Each edit re-serializes the whole payload into every selected
                // object, which would overwrite the others' fields with the first
                // object's values.
                if (shownMixed)
                {
                    dataContainer.Add(new HelpBox(
                        "The selected objects have different message types or payloads. Editing the " +
                        "payload of several objects at once is not supported; select one object to edit it.",
                        HelpBoxMessageType.Info));
                    return;
                }

                if (string.IsNullOrEmpty(shownType)) return;

                var type = ScriptFileField.ResolveType(shownType);
                if (type == null) return;

                var instance = CreatePayload(type, shownJson);
                if (instance == null)
                {
                    dataContainer.Add(new HelpBox(
                        $"Cannot create an instance of '{type.Name}', so its payload can't be edited here.",
                        HelpBoxMessageType.Error));
                    return;
                }

                // One native UI-Toolkit field per public field of the boxed struct.
                // `instance` only seeds the displayed values; each edit goes through
                // Persist against the JSON as currently stored.
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    dataContainer.Add(IsJsonSerialized(f)
                        ? CreateFieldElement(f, instance, v => Persist(type, f, v))
                        : NotSerializedLabel(f));
                }
            }

            // Apply one edited field to a fresh copy of the stored payload, never to
            // a box captured when the UI was built: after an undo or an external
            // edit that box is stale, and writing it back would silently re-apply
            // the values the user just undid.
            void Persist(Type type, FieldInfo field, object value)
            {
                var box = CreatePayload(type, dataJsonProp.stringValue);
                if (box == null) return;

                field.SetValue(box, value);
                shownJson = JsonUtility.ToJson(box);
                dataJsonProp.stringValue = shownJson;
                dataJsonProp.serializedObject.ApplyModifiedProperties();
            }

            void OnTrackedChange(SerializedProperty _)
            {
                if (typeNameProp.stringValue != shownType
                    || dataJsonProp.stringValue != shownJson
                    || IsMixed() != shownMixed)
                    Rebuild();
            }

            root.TrackPropertyValue(typeNameProp, OnTrackedChange);
            root.TrackPropertyValue(dataJsonProp, OnTrackedChange);

            Rebuild();
            return root;
        }

        // T[] → T, List<T> → T; anything else is returned unchanged.
        static Type ElementType(Type fieldType)
        {
            if (fieldType.IsArray) return fieldType.GetElementType();
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return fieldType.GetGenericArguments()[0];
            return fieldType;
        }

        // The stored payload, or the type's defaults when nothing is stored or the
        // JSON doesn't parse (matching MessageReference.CreateMessage). Null when
        // the type can't be instantiated at all.
        static object CreatePayload(Type type, string json)
        {
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var parsed = JsonUtility.FromJson(json, type);
                    if (parsed != null) return parsed;
                }
                catch (Exception)
                {
                    // Mangled JSON (merge conflict, hand edit): fall back to defaults.
                }
            }

            try { return Activator.CreateInstance(type); }
            catch (Exception) { return null; }
        }

        // JsonUtility skips readonly and [NonSerialized] fields, so an edit to one
        // would look applied but never reach the stored JSON.
        static bool IsJsonSerialized(FieldInfo field) => !field.IsInitOnly && !field.IsNotSerialized;

        static VisualElement NotSerializedLabel(FieldInfo field)
        {
            string reason = field.IsInitOnly ? "readonly" : "[NonSerialized]";
            var label = new Label($"{ObjectNames.NicifyVariableName(field.Name)}: not serialized ({reason})");
            label.SetEnabled(false);
            return label;
        }

        // Builds a native UI-Toolkit field showing one field of the boxed struct
        // `instance`; each edit hands the new value to `persist`. Unknown types
        // render a disabled label so the editor degrades gracefully instead of
        // throwing.
        private static VisualElement CreateFieldElement(FieldInfo field, object instance, Action<object> persist)
        {
            var label = ObjectNames.NicifyVariableName(field.Name);
            var type = field.FieldType;
            var val = field.GetValue(instance);

            VisualElement Bind<T>(BaseField<T> el)
            {
                el.RegisterValueChangedCallback(evt => persist(evt.newValue));
                return el;
            }

            if (type == typeof(int))
                return Bind(new IntegerField(label) { value = (int)val });
            if (type == typeof(long))
                return Bind(new LongField(label) { value = (long)val });
            if (type == typeof(float))
                return Bind(new FloatField(label) { value = (float)val });
            if (type == typeof(double))
                return Bind(new DoubleField(label) { value = (double)val });
            if (type == typeof(bool))
                return Bind(new Toggle(label) { value = (bool)val });
            if (type == typeof(string))
                return Bind(new TextField(label) { value = (string)val });
            if (type.IsEnum)
                return Bind(new EnumField(label, (Enum)val));
            if (type == typeof(Vector2))
                return Bind(new Vector2Field(label) { value = (Vector2)val });
            if (type == typeof(Vector3))
                return Bind(new Vector3Field(label) { value = (Vector3)val });
            if (type == typeof(Vector4))
                return Bind(new Vector4Field(label) { value = (Vector4)val });
            if (type == typeof(Vector2Int))
                return Bind(new Vector2IntField(label) { value = (Vector2Int)val });
            if (type == typeof(Vector3Int))
                return Bind(new Vector3IntField(label) { value = (Vector3Int)val });
            if (type == typeof(Color))
                return Bind(new ColorField(label) { value = (Color)val });
            if (type == typeof(Quaternion))
            {
                // Quaternion has no dedicated field — edit it as euler angles, same as
                // the Transform inspector does.
                var euler = new Vector3Field(label) { value = ((Quaternion)val).eulerAngles };
                euler.RegisterValueChangedCallback(evt => persist(Quaternion.Euler(evt.newValue)));
                return euler;
            }

            var unsupported = new Label($"{label}: unsupported type ({type.Name})");
            unsupported.SetEnabled(false);
            return unsupported;
        }
    }
}
