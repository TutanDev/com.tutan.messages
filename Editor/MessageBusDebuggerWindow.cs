// ============================================================================
// MessageBusDebuggerWindow.cs — Live view of EventBus / CommandBus traffic.
//
// Opens via "Window → Tutan → Message Bus Debugger". While open it sets
// MessageBusInstrumentation.Enabled = true; while closed it disables it again
// so the bus pays no instrumentation cost when nobody is looking.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tutan.MessageBus.Editor
{
    public sealed class MessageBusDebuggerWindow : EditorWindow
    {
        const string UxmlPath = "Packages/com.tutan.messages/Editor/MessageBusDebuggerWindow.uxml";
        const string UssPath = "Packages/com.tutan.messages/Editor/MessageBusDebuggerWindow.uss";

        [MenuItem("Window/Tutan/Messages Console")]
        public static void Open()
        {
            var w = GetWindow<MessageBusDebuggerWindow>();
            w.titleContent = new GUIContent("Messages Console");
            w.minSize = new Vector2(640, 320);
            w.Show();
        }

        // ── State ────────────────────────────────────────────────────────
        readonly List<MessageBusInstrumentation.Record> _filtered = new();
        bool _paused;
        bool _showEvents = true;
        bool _showCommands = true;
        bool _showPublish = true;
        bool _showEnqueue = true;
        bool _showSubs = true;
        bool _showDrains;
        string _search = string.Empty;
        long _lastTotalProcessed;

        // Element refs
        ListView _logList;
        Label _detailHeader;
        Label _detailBody;
        VisualElement _logPanel;
        Label _statusLabel;

        // ── Lifecycle ────────────────────────────────────────────────────
        void OnEnable()
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uxml == null)
            {
                rootVisualElement.Add(new Label($"Could not load UXML at {UxmlPath}"));
                return;
            }
            uxml.CloneTree(rootVisualElement);
            if (uss != null) rootVisualElement.styleSheets.Add(uss);

            BindToolbar();
            BindLog();

            _statusLabel = rootVisualElement.Q<Label>("status-label");

            MessageBusInstrumentation.Enabled = true;
            EditorApplication.update += Tick;
            
            // Sync initial state
            _lastTotalProcessed = MessageBusInstrumentation.TotalEver;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            MessageBusInstrumentation.Enabled = false;
        }

        // ── UI Wiring ────────────────────────────────────────────────────
        void BindToolbar()
        {
            var pause = rootVisualElement.Q<ToolbarToggle>("pause-toggle");
            pause.RegisterValueChangedCallback(e => _paused = e.newValue);

            rootVisualElement.Q<ToolbarButton>("clear-button").clicked += () =>
            {
                MessageBusInstrumentation.Clear();
                _filtered.Clear();
                _logList?.RefreshItems();
                _lastTotalProcessed = MessageBusInstrumentation.TotalEver;
            };

            var payloads = rootVisualElement.Q<ToolbarToggle>("payloads-toggle");
            payloads.SetValueWithoutNotify(MessageBusInstrumentation.CapturePayloads);
            payloads.RegisterValueChangedCallback(e => MessageBusInstrumentation.CapturePayloads = e.newValue);

            var events = rootVisualElement.Q<ToolbarToggle>("events-toggle");
            events.SetValueWithoutNotify(true);
            events.RegisterValueChangedCallback(e => { _showEvents = e.newValue; FullRebuild(); });

            var commands = rootVisualElement.Q<ToolbarToggle>("commands-toggle");
            commands.SetValueWithoutNotify(true);
            commands.RegisterValueChangedCallback(e => { _showCommands = e.newValue; FullRebuild(); });

            BindOpToggle("op-publish-toggle", true, v => { _showPublish = v; FullRebuild(); });
            BindOpToggle("op-enqueue-toggle", true, v => { _showEnqueue = v; FullRebuild(); });
            BindOpToggle("op-subs-toggle", true, v => { _showSubs = v; FullRebuild(); });
            
            var drainToggle = rootVisualElement.Q<ToolbarToggle>("op-drain-toggle");
            drainToggle.SetValueWithoutNotify(false);
            drainToggle.RegisterValueChangedCallback(e =>
            {
                _showDrains = e.newValue;
                MessageBusInstrumentation.RecordDrains = e.newValue;
                FullRebuild();
            });

            var search = rootVisualElement.Q<ToolbarSearchField>("search-field");
            search.RegisterValueChangedCallback(e => { _search = e.newValue ?? string.Empty; FullRebuild(); });
        }

        void FullRebuild()
        {
            _filtered.Clear();
            _lastTotalProcessed = MessageBusInstrumentation.TotalEver - MessageBusInstrumentation.Count;
            // Next Tick will catch up from the start of the buffer
        }

        void BindOpToggle(string name, bool initial, Action<bool> setter)
        {
            var t = rootVisualElement.Q<ToolbarToggle>(name);
            t.SetValueWithoutNotify(initial);
            t.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        void BindLog()
        {
            _logPanel = rootVisualElement.Q<VisualElement>("log-panel");
            _logList = rootVisualElement.Q<ListView>("log-list");
            _logList.itemsSource = _filtered;
            _logList.makeItem = MakeLogRow;
            _logList.bindItem = BindLogRow;
            _logList.selectionChanged += OnRowSelected;
            _detailHeader = rootVisualElement.Q<Label>("detail-header");
            _detailBody = rootVisualElement.Q<Label>("detail-body");
        }

        static VisualElement MakeLogRow()
        {
            var row = new VisualElement();
            row.AddToClassList("mb-log-row");
            row.Add(new Label { name = "time" }.WithClass("mb-log-col-time"));
            row.Add(new Label { name = "frame" }.WithClass("mb-log-col-frame"));
            row.Add(new Label { name = "bus" }.WithClass("mb-log-col-bus"));
            row.Add(new Label { name = "op" }.WithClass("mb-log-col-op"));
            row.Add(new Label { name = "type" }.WithClass("mb-log-col-type"));
            return row;
        }

        void BindLogRow(VisualElement row, int i)
        {
            if (i < 0 || i >= _filtered.Count) return;
            var r = _filtered[i];
            var time = (Label)row.ElementAt(0);
            var frame = (Label)row.ElementAt(1);
            var bus = (Label)row.ElementAt(2);
            var op = (Label)row.ElementAt(3);
            var type = (Label)row.ElementAt(4);

            time.text = new DateTime(r.TimestampTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            frame.text = "f" + r.Frame.ToString(CultureInfo.InvariantCulture);
            bus.text = r.Bus == MessageBusInstrumentation.BusKind.Event ? "E" : "C";
            bus.RemoveFromClassList("mb-log-bus-event");
            bus.RemoveFromClassList("mb-log-bus-command");
            bus.AddToClassList(r.Bus == MessageBusInstrumentation.BusKind.Event ? "mb-log-bus-event" : "mb-log-bus-command");
            op.text = r.Op.ToString();
            type.text = r.MessageType?.Name ?? "(drain)";
        }

        void OnRowSelected(IEnumerable<object> selection)
        {
            foreach (var o in selection)
            {
                if (o is MessageBusInstrumentation.Record r)
                {
                    _detailHeader.text = $"{r.Op} {r.Bus} :: {r.MessageType?.FullName ?? "-"}";
                    _detailBody.text = FormatRecord(r);
                    return;
                }
            }
        }

        // ── Tick (per editor frame) ──────────────────────────────────────
        void Tick()
        {
            if (_paused) return;

            long total = MessageBusInstrumentation.TotalEver;
            if (total == _lastTotalProcessed) 
            { 
                UpdateStatus(); 
                return; 
            }

            ProcessIncremental(total);

            _logList.RefreshItems();

            UpdateStatus();
            Repaint();
        }

        void ProcessIncremental(long totalEver)
        {
            var snapshot = MessageBusInstrumentation.Snapshot();
            int currentCount = snapshot.Count;
            
            int capacity = MessageBusInstrumentation.Capacity;
            int newCount = (int)Math.Min(currentCount, totalEver - _lastTotalProcessed);
            
            if (newCount <= 0) return;

            int startIdx = currentCount - newCount;
            for (int i = startIdx; i < currentCount; i++)
            {
                var r = snapshot[i];
                
                // Filtering for Log
                if (PassesFilter(r))
                {
                    _filtered.Add(r);
                    if (_filtered.Count > capacity)
                        _filtered.RemoveAt(0);
                }
            }

            _lastTotalProcessed = totalEver;
        }

        bool PassesFilter(MessageBusInstrumentation.Record r)
        {
            if (r.Bus == MessageBusInstrumentation.BusKind.Event && !_showEvents) return false;
            if (r.Bus == MessageBusInstrumentation.BusKind.Command && !_showCommands) return false;

            switch (r.Op)
            {
                case MessageBusInstrumentation.Op.Publish:    if (!_showPublish) return false; break;
                case MessageBusInstrumentation.Op.Enqueue:    if (!_showEnqueue) return false; break;
                case MessageBusInstrumentation.Op.Subscribe:
                case MessageBusInstrumentation.Op.Unsubscribe: if (!_showSubs) return false; break;
                case MessageBusInstrumentation.Op.DrainStart:
                case MessageBusInstrumentation.Op.DrainEnd:   if (!_showDrains) return false; break;
            }

            if (!string.IsNullOrEmpty(_search))
            {
                string name = r.MessageType?.FullName ?? "";
                if (name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        void UpdateStatus()
        {
            if (_statusLabel == null) return;
            _statusLabel.text = $"{_filtered.Count} visible · {MessageBusInstrumentation.Count} total records";
        }

        // ── Payload formatting ───────────────────────────────────────────
        static string FormatRecord(MessageBusInstrumentation.Record r)
        {
            var sb = new StringBuilder();
            sb.Append("Op:     ").Append(r.Op).Append('\n');
            sb.Append("Bus:    ").Append(r.Bus).Append('\n');
            sb.Append("Type:   ").Append(r.MessageType?.FullName ?? "-").Append('\n');
            sb.Append("Frame:  ").Append(r.Frame).Append('\n');
            sb.Append("Thread: ").Append(r.ThreadId).Append('\n');
            sb.Append("Time:   ").Append(new DateTime(r.TimestampTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fffffff")).Append('\n');
            if (r.TokenId != 0) sb.Append("Token:  #").Append(r.TokenId).Append('\n');
            if (r.HandlerTarget != null || r.HandlerMethod != null)
                sb.Append("Handler:").Append(r.HandlerTarget).Append('.').Append(r.HandlerMethod).Append('\n');

            sb.Append('\n');
            if (r.PayloadBox != null)
            {
                sb.Append("Payload:\n");
                FormatPayload(sb, r.PayloadBox);
            }
            else if (r.Op == MessageBusInstrumentation.Op.Publish || r.Op == MessageBusInstrumentation.Op.Enqueue)
            {
                sb.Append("Payload: (capture disabled — toggle \"Capture payloads\" to inspect)\n");
            }

            if (r.MessageType != null && (r.Op == MessageBusInstrumentation.Op.Publish || r.Op == MessageBusInstrumentation.Op.Enqueue))
            {
                sb.Append("\nSubscribers (Current):\n");
                AppendSubscribers(sb, r.Bus, r.MessageType);
            }

            return sb.ToString();
        }

        static void AppendSubscribers(StringBuilder sb, MessageBusInstrumentation.BusKind bus, Type type)
        {
            IEnumerable<(Type MessageType, IEnumerable<(int TokenId, Delegate Handler)> Entries)> allSubs;
            if (bus == MessageBusInstrumentation.BusKind.Event)
                allSubs = EventBus.Bus.EnumerateSubscriptions();
            else
                allSubs = CommandBus.Bus.EnumerateSubscriptions();

            bool found = false;
            foreach (var group in allSubs)
            {
                if (group.MessageType == type)
                {
                    foreach (var entry in group.Entries)
                    {
                        found = true;
                        string target = entry.Handler?.Target?.GetType().FullName ?? "(static)";
                        string method = entry.Handler?.Method?.Name ?? "?";
                        sb.Append("  #").Append(entry.TokenId).Append(" ").Append(target).Append('.').Append(method).Append('\n');
                    }
                    break;
                }
            }

            if (!found)
                sb.Append("  (none)\n");
        }

        static void FormatPayload(StringBuilder sb, object payload)
        {
            var t = payload.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            
            var fields = t.GetFields(flags);
            var props = t.GetProperties(flags);

            if (fields.Length == 0 && props.Length == 0)
            {
                sb.Append("  (no public fields or properties)\n");
                return;
            }

            foreach (var f in fields)
            {
                object value;
                try { value = f.GetValue(payload); } catch { value = "<error>"; }
                sb.Append("  ").Append(f.Name).Append(" = ").Append(value ?? "null").Append('\n');
            }

            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                object value;
                try { value = p.GetValue(payload); } catch { value = "<error>"; }
                sb.Append("  ").Append(p.Name).Append(" = ").Append(value ?? "null").Append('\n');
            }
        }
    }

    static class VisualElementExt
    {
        public static T WithClass<T>(this T ve, string cls) where T : VisualElement
        {
            ve.AddToClassList(cls);
            return ve;
        }
    }
}
