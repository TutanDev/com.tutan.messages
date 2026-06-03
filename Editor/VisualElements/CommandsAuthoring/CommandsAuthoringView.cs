using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Tutan.Messages.Editor
{
    public sealed class CommandsAuthoringView : VisualElement
    {
        static readonly string UxmlPath = PathUtils.RelativePath(".uxml");
        static readonly string UssPath = PathUtils.RelativePath(".uss");

        const string KeyOnlyWarnings = "Tutan.Messages.Commands.OnlyWarnings";

        readonly List<CommandRecord> _records = new();
        bool _onlyWarnings;
        string _search = string.Empty;

        ScrollView _cards;
        Label _statusLabel;

        public CommandsAuthoringView()
        {
            style.flexGrow = 1;

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uxml == null)
            {
                Add(new Label($"Could not load UXML at {UxmlPath}"));
                return;
            }
            uxml.CloneTree(this);
            if (uss != null) styleSheets.Add(uss);

            _onlyWarnings = EditorPrefs.GetBool(KeyOnlyWarnings, false);

            BindToolbar();
            _cards = this.Q<ScrollView>("cards");
            _statusLabel = this.Q<Label>("status-label");

            Rescan();
        }

        void BindToolbar()
        {
            this.Q<ToolbarButton>("refresh-button").clicked += Rescan;

            var warnings = this.Q<ToolbarToggle>("warnings-toggle");
            warnings.SetValueWithoutNotify(_onlyWarnings);
            warnings.RegisterValueChangedCallback(e =>
            {
                _onlyWarnings = e.newValue;
                EditorPrefs.SetBool(KeyOnlyWarnings, e.newValue);
                RebuildCards();
            });

            var search = this.Q<ToolbarSearchField>("search-field");
            search.RegisterValueChangedCallback(e =>
            {
                _search = e.newValue ?? string.Empty;
                RebuildCards();
            });
        }

        void Rescan()
        {
            _records.Clear();
            _records.AddRange(BuildRecords());
            RebuildCards();
        }

        static IEnumerable<CommandRecord> BuildRecords()
        {
            var map = new Dictionary<Type, List<Type>>();
            foreach (var cmd in TypeCache.GetTypesDerivedFrom<ICommand>())
                if (!cmd.IsAbstract && !cmd.IsInterface && cmd.IsValueType && !cmd.ContainsGenericParameters)
                    map[cmd] = new List<Type>();

            foreach (var h in TypeCache.GetTypesDerivedFrom<ICommandHandler>())
            {
                if (h.IsAbstract || h.IsInterface || h.ContainsGenericParameters) continue;
                foreach (var i in h.GetInterfaces())
                {
                    if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(ICommandHandler<>))
                        continue;
                    var cmd = i.GetGenericArguments()[0];
                    if (!map.TryGetValue(cmd, out var list))
                        map[cmd] = list = new List<Type>();
                    list.Add(h);
                }
            }

            return map
                .Select(kv => new CommandRecord(kv.Key, kv.Value))
                .OrderBy(r => r.CommandType.FullName, StringComparer.Ordinal);
        }

        void RebuildCards()
        {
            if (_cards == null) return;
            _cards.Clear();

            int warningCount = _records.Count(r => r.HasWarning);
            int shown = 0;

            foreach (var r in _records)
            {
                if (_onlyWarnings && !r.HasWarning) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    (r.CommandType.FullName?.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                    continue;

                _cards.Add(MakeCard(r));
                shown++;
            }

            if (shown == 0)
            {
                var msg = _records.Count == 0
                    ? "No ICommand types found."
                    : "No commands match the current filter.";
                var empty = new Label(msg) { name = "empty" };
                empty.AddToClassList("ca-empty");
                _cards.Add(empty);
            }

            if (_statusLabel != null)
                _statusLabel.text = $"{_records.Count} commands · {warningCount} warning{(warningCount == 1 ? "" : "s")}";
        }

        static VisualElement MakeCard(CommandRecord r)
        {
            var card = new VisualElement();
            card.AddToClassList("ca-card");
            if (r.HasWarning) card.AddToClassList("ca-card--warning");

            var command = new ScriptFileField();
            command.AddToClassList("ca-card__command");
            command.SetType(r.CommandType);
            command.text = r.CommandType.FullName;
            card.Add(command);

            var handlers = new VisualElement();
            handlers.AddToClassList("ca-card__handlers");
            card.Add(handlers);

            int n = r.Handlers.Count;
            if (n == 0)
            {
                handlers.Add(Note("No handler — this command is never handled (orphan).", "ca-card__note--error"));
            }
            else
            {
                handlers.Add(MakeLabel(n == 1 ? "Handler" : $"Handlers ({n})", "ca-card__label"));
                foreach (var h in r.Handlers)
                {
                    var field = new ScriptFileField();
                    field.SetType(h);
                    field.text = h.FullName;
                    handlers.Add(field);
                }
                if (n > 1)
                    handlers.Add(Note("Commands are N:1 — only one handler may be bound.", "ca-card__note--warning"));
            }

            return card;
        }

        static Label MakeLabel(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        static Label Note(string text, string modifier)
        {
            var l = new Label(text);
            l.AddToClassList("ca-card__note");
            l.AddToClassList(modifier);
            return l;
        }

        sealed class CommandRecord
        {
            public readonly Type CommandType;
            public readonly List<Type> Handlers;
            public bool HasWarning => Handlers.Count != 1;

            public CommandRecord(Type commandType, List<Type> handlers)
            {
                CommandType = commandType;
                Handlers = handlers;
            }
        }
    }
}
