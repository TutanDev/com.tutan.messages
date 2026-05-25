// ============================================================================
// ScriptFileField.cs — Clickable row that represents the C# source file
// backing a type.
//
//   • Single click  → pings (highlights) the .cs asset in the Project window
//                      and selects it.
//   • Double click  → opens the file in the configured script editor.
//
// Resolution of Type → MonoScript and of a type's full name → Type are both
// cached (AssetDatabase searches and assembly scans are not cheap, and the
// Messages Console rebuilds these rows on every row selection).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.Messages.Editor
{
    [UxmlElement]
    public partial class ScriptFileField : VisualElement
    {
        public static readonly string ussClassName = "tutan-script-field";
        public static readonly string iconUssClassName = ussClassName + "__icon";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string missingUssClassName = ussClassName + "--missing";

        readonly Image _icon;
        readonly Label _label;
        MonoScript _script;

        /// <summary>The text shown next to the icon. Defaults to the type name.</summary>
        [UxmlAttribute]
        public string text
        {
            get => _label.text;
            set => _label.text = value;
        }

        public ScriptFileField()
        {
            AddToClassList(ussClassName);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            focusable = true;

            _icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            _icon.AddToClassList(iconUssClassName);
            _icon.style.width = 16;
            _icon.style.height = 16;
            _icon.style.marginRight = 4;
            _icon.style.flexShrink = 0;
            Add(_icon);

            _label = new Label();
            _label.AddToClassList(labelUssClassName);
            _label.style.overflow = Overflow.Hidden;
            _label.style.textOverflow = TextOverflow.Ellipsis;
            Add(_label);

            RegisterCallback<ClickEvent>(OnClick);
        }

        /// <summary>Point this row at <paramref name="type"/>, resolving its source file.</summary>
        public void SetType(Type type)
        {
            _script = FindScript(type);
            _label.text = type != null ? NiceTypeName(type) : "(unknown)";
            Refresh();
        }

        /// <summary>
        /// Point this row at a type identified by full (or assembly-qualified) name —
        /// used for subscriber snapshots, which carry the target type as a string.
        /// </summary>
        public void SetTypeName(string fullName)
        {
            SetType(ResolveType(fullName));
        }

        void Refresh()
        {
            if (_script != null)
            {
                _icon.image = AssetPreview.GetMiniThumbnail(_script);
                _icon.style.display = DisplayStyle.Flex;
                RemoveFromClassList(missingUssClassName);
                tooltip = AssetDatabase.GetAssetPath(_script) + "\nClick to highlight · double-click to open";
                SetEnabled(true);
            }
            else
            {
                _icon.style.display = DisplayStyle.None;
                AddToClassList(missingUssClassName);
                tooltip = "No source file found for this type.";
                // Leave enabled=true so the (dimmed) label still renders normally,
                // but clicks are no-ops because _script is null.
            }
        }

        void OnClick(ClickEvent evt)
        {
            if (_script == null) return;

            if (evt.clickCount >= 2)
            {
                AssetDatabase.OpenAsset(_script);
            }
            else
            {
                EditorGUIUtility.PingObject(_script);
                Selection.activeObject = _script;
            }
        }

        // ── Type / MonoScript resolution (cached) ────────────────────────
        static readonly Dictionary<Type, MonoScript> s_scriptCache = new();
        static readonly Dictionary<string, Type> s_typeCache = new();

        /// <summary>
        /// Find the <see cref="MonoScript"/> asset that declares <paramref name="type"/>.
        /// Works even when the file name differs from the type name (e.g. several
        /// message structs grouped in one file): a fast name-based match is tried
        /// first, then a source-text scan for the declaration as a fallback.
        /// </summary>
        public static MonoScript FindScript(Type type)
        {
            if (type == null) return null;
            if (s_scriptCache.TryGetValue(type, out var cached)) return cached;

            MonoScript found = FindByFileName(type) ?? FindBySource(type);
            s_scriptCache[type] = found;
            return found;
        }

        // Fast path: a file named after the type. GetClass() returns the type only
        // when the file name matches it, so this confirms the match cheaply.
        static MonoScript FindByFileName(Type type)
        {
            string simpleName = StripGenericArity(type.Name);
            foreach (var guid in AssetDatabase.FindAssets($"{simpleName} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == type)
                    return ms;
            }
            return null;
        }

        // Fallback: scan every MonoScript's source for the type declaration. This
        // catches types whose file name does not match (GetClass() returns null for
        // those, so FindByFileName misses them). When the type has a namespace and
        // the same simple name appears in several files, prefer the file that also
        // mentions the namespace; otherwise take the first declaration found.
        static MonoScript FindBySource(Type type)
        {
            string simpleName = StripGenericArity(type.Name);
            var decl = new Regex($@"\b(class|struct|interface|enum|record)\s+{Regex.Escape(simpleName)}\b");
            string ns = type.Namespace;

            MonoScript firstMatch = null;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null) continue;

                string text = ms.text;
                // Cheap reject before the regex: the name must appear at all.
                if (string.IsNullOrEmpty(text) || text.IndexOf(simpleName, StringComparison.Ordinal) < 0)
                    continue;
                if (!decl.IsMatch(text)) continue;

                // Declaration + namespace both present → confident match, take it.
                if (string.IsNullOrEmpty(ns) || text.Contains(ns))
                    return ms;

                firstMatch ??= ms;
            }
            return firstMatch;
        }

        /// <summary>Resolve a type's full or assembly-qualified name across loaded assemblies.</summary>
        public static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (s_typeCache.TryGetValue(fullName, out var cached)) return cached;

            Type found = Type.GetType(fullName, false);
            if (found == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    found = asm.GetType(fullName, false);
                    if (found != null) break;
                }
            }

            s_typeCache[fullName] = found;
            return found;
        }

        static string StripGenericArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        static string NiceTypeName(Type type)
        {
            return type.IsGenericType ? StripGenericArity(type.Name) : type.Name;
        }
    }
}
