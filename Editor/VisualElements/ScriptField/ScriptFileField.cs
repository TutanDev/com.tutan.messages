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
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.Messages.Editor
{
    /// <summary>
    /// UI Toolkit row showing the C# source file that declares a type: single click
    /// pings it in the Project window, double click opens it in the script editor.
    /// Used by the Messages Console; usable in your own editor tooling and UXML.
    /// </summary>
    [UxmlElement]
    public partial class ScriptFileField : VisualElement
    {
        /// <summary>USS class added to the root element.</summary>
        public static readonly string ussClassName = "tutan-script-field";
        /// <summary>USS class added to the script icon.</summary>
        public static readonly string iconUssClassName = ussClassName + "__icon";
        /// <summary>USS class added to the type-name label.</summary>
        public static readonly string labelUssClassName = ussClassName + "__label";
        /// <summary>USS class added to the root while no source file is found for the type.</summary>
        public static readonly string missingUssClassName = ussClassName + "--missing";

        // ScriptFileField.uss, loaded by GUID (from its .meta) because the package's
        // asset path depends on how it was installed; the GUID does not.
        const string UssGuid = "4b4dccc04c65413aa83d83b51a26ddd8";

        // Only a successful load is kept: Unity's == null also catches a sheet
        // destroyed by a reimport, so a miss is retried by the next row.
        static StyleSheet s_uss;

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

        /// <summary>
        /// Creates an empty row; call <see cref="SetType"/> or <see cref="SetTypeName"/>
        /// to point it at a type.
        /// </summary>
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

            // The control carries its own stylesheet so every consumer renders
            // identically without copying USS into each window.
            if (s_uss == null)
                s_uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(UssGuid));
            if (s_uss != null) styleSheets.Add(s_uss);

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
        /// first, then a source-text scan for the declaration as a fallback. For a
        /// compiler-generated type (lambda closure, iterator, async state machine)
        /// this returns the script of the type that encloses it.
        /// </summary>
        public static MonoScript FindScript(Type type)
        {
            if (type == null) return null;
            if (s_scriptCache.TryGetValue(type, out var cached)) return cached;

            // Lambda handlers report compiler-generated nested types ("Hud+<>c",
            // "Hud+<>c__DisplayClass3_0", "Hud+<Run>d__5"); their source is the
            // enclosing type's file.
            var declaring = type;
            while (declaring.DeclaringType != null && IsCompilerGenerated(declaring))
                declaring = declaring.DeclaringType;

            // A name that can't be declared in C# can't match by file name or by
            // the declaration regex, so skip the project-wide source scan.
            MonoScript found = IsIdentifier(StripGenericArity(declaring.Name))
                ? FindByFileName(declaring) ?? FindBySource(declaring)
                : null;
            s_scriptCache[type] = found;
            return found;
        }

        static bool IsCompilerGenerated(Type type) =>
            type.Name.StartsWith("<", StringComparison.Ordinal)
            || type.IsDefined(typeof(CompilerGeneratedAttribute), false);

        static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || char.IsDigit(name[0])) return false;
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
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

        /// <summary>
        /// Resolve a type's full or assembly-qualified name across loaded assemblies
        /// (drift-tolerant, see <see cref="MessageTypeResolver.Resolve"/>). Cached per
        /// input string for the lifetime of the domain — misses are cached too, which
        /// is correct because adding a type triggers a domain reload that clears it.
        /// </summary>
        public static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (s_typeCache.TryGetValue(fullName, out var cached)) return cached;

            Type found = MessageTypeResolver.Resolve(fullName);
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
