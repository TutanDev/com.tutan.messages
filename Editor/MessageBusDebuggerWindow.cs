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

        [MenuItem("Window/Tutan/Message Bus Debugger")]
        public static void Open()
        {
            var w = GetWindow<MessageBusDebuggerWindow>();
            w.titleContent = new GUIContent("Message Bus");
            w.minSize = new Vector2(640, 320);
            w.Show();
        }

        // ── State ────────────────────────────────────────────────────────
        readonly List<MessageBusInstrumentation.Record> _filtered = new();
        readonly Dictionary<(MessageBusInstrumentation.BusKind bus, Type type), StatRow> _stats = new();
        bool _paused;
        bool _showEvents = true;
        bool _showCommands = true;
        bool _showPublish = true;
        bool _showEnqueue = true;
        bool _showSubs = true;
        bool _showDrains; // recording is also gated by MessageBusInstrumentation.RecordDrains
        string _search = string.Empty;
        long _lastTotalEver;

        // Active tab
        enum Tab { Log, Subs, Stats }
        Tab _tab = Tab.Log;

        // Element refs
        ListView _logList;
        Label _detailHeader;
        Label _detailBody;
        VisualElement _logPanel, _subsPanel, _statsPanel;
        Button _tabLog, _tabSubs, _tabStats;
        VisualElement _subsContainer, _statsContainer;
        Label _statusLabel;

        // Stats book-keeping
        sealed class StatRow
        {
            public Type Type;
            public MessageBusInstrumentation.BusKind Bus;
            public long Total;
            public int LastFrame;
            public long LastTicks;
            public float Ema; // events/sec, smoothed
        }

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
            BindTabs();
            BindLog();

            _subsContainer = rootVisualElement.Q<VisualElement>("subs-container");
            _statsContainer = rootVisualElement.Q<VisualElement>("stats-container");
            _statusLabel = rootVisualElement.Q<Label>("status-label");

            MessageBusInstrumentation.Enabled = true;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            MessageBusInstrumentation.Enabled = false;
            MessageBusInstrumentation.CapturePayloads = false;
            MessageBusInstrumentation.RecordDrains = false;
        }

        // ── UI Wiring ────────────────────────────────────────────────────
        void BindToolbar()
        {
            var pause = rootVisualElement.Q<ToolbarToggle>("pause-toggle");
            pause.RegisterValueChangedCallback(e => _paused = e.newValue);

            rootVisualElement.Q<ToolbarButton>("clear-button").clicked += () =>
            {
                MessageBusInstrumentation.Clear();
                _stats.Clear();
                _filtered.Clear();
                _logList?.RefreshItems();
                _lastTotalEver = MessageBusInstrumentation.TotalEver;
            };

            var payloads = rootVisualElement.Q<ToolbarToggle>("payloads-toggle");
            payloads.SetValueWithoutNotify(MessageBusInstrumentation.CapturePayloads);
            payloads.RegisterValueChangedCallback(e => MessageBusInstrumentation.CapturePayloads = e.newValue);

            var events = rootVisualElement.Q<ToolbarToggle>("events-toggle");
            events.SetValueWithoutNotify(true);
            events.RegisterValueChangedCallback(e => _showEvents = e.newValue);

            var commands = rootVisualElement.Q<ToolbarToggle>("commands-toggle");
            commands.SetValueWithoutNotify(true);
            commands.RegisterValueChangedCallback(e => _showCommands = e.newValue);

            BindOpToggle("op-publish-toggle", true, v => _showPublish = v);
            BindOpToggle("op-enqueue-toggle", true, v => _showEnqueue = v);
            BindOpToggle("op-subs-toggle", true, v => _showSubs = v);
            var drainToggle = rootVisualElement.Q<ToolbarToggle>("op-drain-toggle");
            drainToggle.SetValueWithoutNotify(false);
            drainToggle.RegisterValueChangedCallback(e =>
            {
                _showDrains = e.newValue;
                MessageBusInstrumentation.RecordDrains = e.newValue;
            });

            var search = rootVisualElement.Q<ToolbarSearchField>("search-field");
            search.RegisterValueChangedCallback(e => _search = e.newValue ?? string.Empty);
        }

        void BindOpToggle(string name, bool initial, Action<bool> setter)
        {
            var t = rootVisualElement.Q<ToolbarToggle>(name);
            t.SetValueWithoutNotify(initial);
            t.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        void BindTabs()
        {
            _logPanel = rootVisualElement.Q<VisualElement>("log-panel");
            _subsPanel = rootVisualElement.Q<VisualElement>("subs-panel");
            _statsPanel = rootVisualElement.Q<VisualElement>("stats-panel");
            _tabLog = rootVisualElement.Q<Button>("tab-log");
            _tabSubs = rootVisualElement.Q<Button>("tab-subs");
            _tabStats = rootVisualElement.Q<Button>("tab-stats");
            _tabLog.clicked += () => SetTab(Tab.Log);
            _tabSubs.clicked += () => SetTab(Tab.Subs);
            _tabStats.clicked += () => SetTab(Tab.Stats);
            SetTab(Tab.Log);
        }

        void SetTab(Tab t)
        {
            _tab = t;
            _logPanel.style.display = t == Tab.Log ? DisplayStyle.Flex : DisplayStyle.None;
            _subsPanel.style.display = t == Tab.Subs ? DisplayStyle.Flex : DisplayStyle.None;
            _statsPanel.style.display = t == Tab.Stats ? DisplayStyle.Flex : DisplayStyle.None;
            SetActive(_tabLog, t == Tab.Log);
            SetActive(_tabSubs, t == Tab.Subs);
            SetActive(_tabStats, t == Tab.Stats);
        }

        static void SetActive(Button b, bool active)
        {
            if (active) b.AddToClassList("mb-tab-active");
            else b.RemoveFromClassList("mb-tab-active");
        }

        void BindLog()
        {
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
            row.Add(new Label { name = "time", text = "" }.WithClass("mb-log-col-time"));
            row.Add(new Label { name = "frame", text = "" }.WithClass("mb-log-col-frame"));
            row.Add(new Label { name = "bus", text = "" }.WithClass("mb-log-col-bus"));
            row.Add(new Label { name = "op", text = "" }.WithClass("mb-log-col-op"));
            row.Add(new Label { name = "type", text = "" }.WithClass("mb-log-col-type"));
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
            if (total == _lastTotalEver) { UpdateStatus(); return; }
            _lastTotalEver = total;

            RebuildFilteredAndStats();

            if (_tab == Tab.Log)
                _logList.RefreshItems();
            else if (_tab == Tab.Subs)
                RebuildSubscribersTab();
            else
                RebuildStatsTab();

            UpdateStatus();
            Repaint();
        }

        void RebuildFilteredAndStats()
        {
            _filtered.Clear();
            var all = MessageBusInstrumentation.Snapshot();
            long nowTicks = DateTime.UtcNow.Ticks;
            const float emaDecaySeconds = 1f;

            // Decay all EMA rows toward zero so stale types fade out.
            foreach (var s in _stats.Values)
            {
                float dt = (float)TimeSpan.FromTicks(Math.Max(0, nowTicks - s.LastTicks)).TotalSeconds;
                s.Ema *= Mathf.Exp(-dt / emaDecaySeconds);
            }

            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (!PassesFilter(r)) continue;
                _filtered.Add(r);

                if (r.Op == MessageBusInstrumentation.Op.Publish || r.Op == MessageBusInstrumentation.Op.Enqueue)
                {
                    if (r.MessageType == null) continue;
                    var key = (r.Bus, r.MessageType);
                    if (!_stats.TryGetValue(key, out var s))
                    {
                        s = new StatRow { Type = r.MessageType, Bus = r.Bus, LastTicks = r.TimestampTicks };
                        _stats[key] = s;
                    }
                    s.Total++;
                    s.LastFrame = r.Frame;
                    s.Ema += 1f; // each event contributes 1 to the smoothed rate
                    s.LastTicks = r.TimestampTicks;
                }
            }
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
            _statusLabel.text = $"{MessageBusInstrumentation.Count} records · total {MessageBusInstrumentation.TotalEver}";
        }

        // ── Subscribers tab ──────────────────────────────────────────────
        void RebuildSubscribersTab()
        {
            _subsContainer.Clear();
            AddSubscriberSection("Events (EventBus)", EventBus.Bus.EnumerateSubscriptions());
            AddSubscriberSection("Commands (CommandBus)", CommandBus.Bus.EnumerateSubscriptions());
        }

        void AddSubscriberSection(string header, IEnumerable<(Type MessageType, IEnumerable<(int TokenId, Delegate Handler)> Entries)> subs)
        {
            var headerLabel = new Label(header);
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.marginTop = 6;
            _subsContainer.Add(headerLabel);

            bool any = false;
            foreach (var (type, entries) in subs)
            {
                any = true;
                var typeLabel = new Label(type.FullName ?? type.Name);
                typeLabel.AddToClassList("mb-subs-type");
                _subsContainer.Add(typeLabel);

                foreach (var (tokenId, handler) in entries)
                {
                    string target = handler?.Target?.GetType().FullName ?? "(static)";
                    string method = handler?.Method?.Name ?? "?";
                    var line = new Label($"#{tokenId}  {target}.{method}");
                    line.AddToClassList("mb-subs-entry");
                    _subsContainer.Add(line);
                }
            }
            if (!any)
            {
                var empty = new Label("    (no active subscriptions)");
                empty.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                _subsContainer.Add(empty);
            }
        }

        // ── Stats tab ────────────────────────────────────────────────────
        void RebuildStatsTab()
        {
            _statsContainer.Clear();
            var sorted = new List<StatRow>(_stats.Values);
            sorted.Sort((a, b) => b.Total.CompareTo(a.Total));
            foreach (var s in sorted)
            {
                var row = new VisualElement();
                row.AddToClassList("mb-stats-row");
                row.Add(new Label(s.Type.Name).WithClass("mb-col-type"));
                row.Add(new Label(s.Bus.ToString()).WithClass("mb-col-bus"));
                int subs = SubscriberCountFor(s.Bus, s.Type);
                row.Add(new Label(subs.ToString()).WithClass("mb-col-num"));
                row.Add(new Label(s.Total.ToString()).WithClass("mb-col-num"));
                row.Add(new Label(s.Ema.ToString("F1", CultureInfo.InvariantCulture)).WithClass("mb-col-num"));
                row.Add(new Label("f" + s.LastFrame).WithClass("mb-col-num"));
                _statsContainer.Add(row);
            }
        }

        static int SubscriberCountFor(MessageBusInstrumentation.BusKind bus, Type type)
        {
            // Reflection: call MessageBus.GetSubscriberCount<T>() because we don't
            // know T at compile time here.
            object busInstance = bus == MessageBusInstrumentation.BusKind.Event
                ? (object)EventBus.Bus
                : (object)CommandBus.Bus;
            var mi = busInstance.GetType().GetMethod(nameof(MessageBus<IMessage>.GetSubscriberCount));
            var generic = mi.MakeGenericMethod(type);
            return (int)generic.Invoke(busInstance, null);
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
            return sb.ToString();
        }

        static void FormatPayload(StringBuilder sb, object payload)
        {
            var t = payload.GetType();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0)
            {
                sb.Append("  (no public fields)\n");
                return;
            }
            foreach (var f in fields)
            {
                object value;
                try { value = f.GetValue(payload); } catch { value = "<error>"; }
                sb.Append("  ").Append(f.Name).Append(" = ").Append(value).Append('\n');
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
