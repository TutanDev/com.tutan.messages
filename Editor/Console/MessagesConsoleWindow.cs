// ============================================================================
// MessagesConsoleWindow.cs — Live view of EventBus / CommandBus traffic.
//
// Opens via "Window → Tutan → Messages Console". While open it sets
// MessagesInstrumentation.Enabled = true; while closed it disables it again
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

namespace Tutan.Messages.Editor
{
    /// <summary>
    /// Editor window (<b>Window ▸ Tutan ▸ Messages Console</b>) that shows a live,
    /// filterable log of <see cref="EventBus"/> / <see cref="CommandBus"/> traffic
    /// recorded by <see cref="MessagesInstrumentation"/>. Instrumentation is enabled
    /// only while the window is open.
    /// </summary>
    public sealed class MessagesConsoleWindow : EditorWindow
    {
        // Loaded by GUID (from the .meta files next to them), not by path: the
        // package's asset path depends on how it was installed (embedded,
        // Library/PackageCache, a local file: path), its GUIDs do not.
        const string UxmlGuid = "e9d14c8ade845e848aa5e9881cb7cbfb"; // MessagesConsoleWindow.uxml
        const string UssGuid = "a4a10763f8ecdcf4c816abe8f713ba24";  // MessagesConsoleWindow.uss

        // Persisted filter preferences. Kept in EditorPrefs so the user's filter setup
        // survives domain reloads (notably entering Play mode) and window reopen.
        const string KeyEvents = "Tutan.Messages.Debugger.Events";
        const string KeyCommands = "Tutan.Messages.Debugger.Commands";
        const string KeyPublish = "Tutan.Messages.Debugger.Publish";
        const string KeyEnqueue = "Tutan.Messages.Debugger.Enqueue";
        const string KeySubs = "Tutan.Messages.Debugger.Subs";
        const string KeyDrains = "Tutan.Messages.Debugger.Drains";
        const string KeyAutoScroll = "Tutan.Messages.Debugger.AutoScroll";

        /// <summary>Open (or focus) the Messages Console window.</summary>
        [MenuItem("Window/Tutan/Messages Console")]
        public static void Open()
        {
            var w = GetWindow<MessagesConsoleWindow>();
            w.titleContent = new GUIContent("Messages Console");
            w.minSize = new Vector2(640, 320);
            w.Show();
        }

        // ── State ────────────────────────────────────────────────────────
        readonly List<MessagesInstrumentation.Record> _filtered = new();
        bool _paused;
        bool _showEvents = true;
        bool _showCommands = true;
        bool _showPublish = true;
        bool _showEnqueue = true;
        bool _showSubs = true;
        bool _showDrains;
        bool _autoScroll = true;
        string _search = string.Empty;
        long _lastTotalProcessed;

        // Element refs
        ListView _logList;
        Label _detailHeader;
        Label _detailBody;
        VisualElement _detailTypeSection;
        VisualElement _detailPayloadSection;
        VisualElement _detailSubsSection;
        VisualElement _logPanel;
        Label _statusLabel;

        // ── Lifecycle ────────────────────────────────────────────────────
        // Instrumentation is toggled with the window's visibility so the bus pays
        // no cost when nobody is looking. UI construction lives in CreateGUI().
        void OnEnable()
        {
            MessagesInstrumentation.Enabled = true;
            EditorApplication.update += Tick;

            // Sync initial state
            _lastTotalProcessed = MessagesInstrumentation.TotalEver;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            MessagesInstrumentation.Enabled = false;
        }

        /// <summary>
        /// Unity callback that builds the window's UI; runs on open and after every
        /// domain reload.
        /// </summary>
        public void CreateGUI()
        {
            // Runs again after every reload, so keep it idempotent: rebuild from
            // scratch and restore state from EditorPrefs.
            rootVisualElement.Clear();

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(UxmlGuid));
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(UssGuid));
            if (uxml == null)
            {
                rootVisualElement.Add(new HelpBox(
                    $"Messages Console could not load its layout (MessagesConsoleWindow.uxml, GUID {UxmlGuid}). " +
                    "Reimport the Tutan Messages package, then reopen this window.",
                    HelpBoxMessageType.Error));
                return;
            }
            uxml.CloneTree(rootVisualElement);
            if (uss != null) rootVisualElement.styleSheets.Add(uss);

            LoadPrefs(); // restore filter setup before binding so toggles reflect it

            BindToolbar();
            BindLog();

            _statusLabel = rootVisualElement.Q<Label>("status-label");

            // Fill the log from what the ring buffer already holds: Tick only picks
            // up records newer than the cursor, so a reopened window would otherwise
            // list nothing until a filter was toggled.
            FullRebuild();
        }

        // Restore persisted filter preferences into fields + instrumentation statics.
        void LoadPrefs()
        {
            _showEvents = EditorPrefs.GetBool(KeyEvents, true);
            _showCommands = EditorPrefs.GetBool(KeyCommands, true);
            _showPublish = EditorPrefs.GetBool(KeyPublish, true);
            _showEnqueue = EditorPrefs.GetBool(KeyEnqueue, true);
            _showSubs = EditorPrefs.GetBool(KeySubs, true);
            _showDrains = EditorPrefs.GetBool(KeyDrains, false);
            _autoScroll = EditorPrefs.GetBool(KeyAutoScroll, true);
            MessagesInstrumentation.RecordDrains = _showDrains;
        }

        // ── UI Wiring ────────────────────────────────────────────────────
        void BindToolbar()
        {
            var pause = rootVisualElement.Q<ToolbarToggle>("pause-toggle");
            pause.SetValueWithoutNotify(_paused);
            pause.RegisterValueChangedCallback(e => _paused = e.newValue);

            rootVisualElement.Q<ToolbarButton>("clear-button").clicked += () =>
            {
                MessagesInstrumentation.Clear();
                _filtered.Clear();
                ResetSelection();
                _logList?.RefreshItems();
                _lastTotalProcessed = MessagesInstrumentation.TotalEver;
                UpdateStatus();
            };

            var autoScroll = rootVisualElement.Q<ToolbarToggle>("autoscroll-toggle");
            autoScroll.SetValueWithoutNotify(_autoScroll);
            autoScroll.RegisterValueChangedCallback(e => { _autoScroll = e.newValue; EditorPrefs.SetBool(KeyAutoScroll, e.newValue); });

            var events = rootVisualElement.Q<ToolbarToggle>("events-toggle");
            events.SetValueWithoutNotify(_showEvents);
            events.RegisterValueChangedCallback(e => { _showEvents = e.newValue; EditorPrefs.SetBool(KeyEvents, e.newValue); FullRebuild(); });

            var commands = rootVisualElement.Q<ToolbarToggle>("commands-toggle");
            commands.SetValueWithoutNotify(_showCommands);
            commands.RegisterValueChangedCallback(e => { _showCommands = e.newValue; EditorPrefs.SetBool(KeyCommands, e.newValue); FullRebuild(); });

            BindOpToggle("op-publish-toggle", _showPublish, KeyPublish, v => { _showPublish = v; FullRebuild(); });
            BindOpToggle("op-enqueue-toggle", _showEnqueue, KeyEnqueue, v => { _showEnqueue = v; FullRebuild(); });
            BindOpToggle("op-subs-toggle", _showSubs, KeySubs, v => { _showSubs = v; FullRebuild(); });

            var drainToggle = rootVisualElement.Q<ToolbarToggle>("op-drain-toggle");
            drainToggle.SetValueWithoutNotify(_showDrains);
            drainToggle.RegisterValueChangedCallback(e =>
            {
                _showDrains = e.newValue;
                MessagesInstrumentation.RecordDrains = e.newValue;
                EditorPrefs.SetBool(KeyDrains, e.newValue);
                FullRebuild();
            });

            var search = rootVisualElement.Q<ToolbarSearchField>("search-field");
            search.RegisterValueChangedCallback(e => { _search = e.newValue ?? string.Empty; FullRebuild(); });
        }

        void FullRebuild()
        {
            // Snapshot(out totalEver) pairs the records and the total under one
            // lock — a separate TotalEver/Count read pair can tear against
            // worker-thread appends and desync the incremental catch-up.
            _filtered.Clear();
            ResetSelection();
            var snapshot = MessagesInstrumentation.Snapshot(out long totalEver);
            int count = snapshot.Count;

            // While paused, the view must stay frozen at the pause point: drop
            // the trailing records that arrived after it, and leave
            // _lastTotalProcessed untouched so unpausing catches up normally.
            // (Records the ring buffer has already overwritten are gone either
            // way; the pause freezes the view, not the buffer.)
            if (_paused)
                count -= (int)Math.Min(count, totalEver - _lastTotalProcessed);
            else
                _lastTotalProcessed = totalEver;

            for (int i = 0; i < count; i++)
            {
                if (PassesFilter(snapshot[i]))
                    _filtered.Add(snapshot[i]);
            }
            _logList?.RefreshItems();
            UpdateStatus();
        }

        void BindOpToggle(string name, bool initial, string prefKey, Action<bool> setter)
        {
            var t = rootVisualElement.Q<ToolbarToggle>(name);
            t.SetValueWithoutNotify(initial);
            t.RegisterValueChangedCallback(e => { EditorPrefs.SetBool(prefKey, e.newValue); setter(e.newValue); });
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
            _detailTypeSection = rootVisualElement.Q<VisualElement>("detail-type-section");
            _detailPayloadSection = rootVisualElement.Q<VisualElement>("detail-payload-section");
            _detailSubsSection = rootVisualElement.Q<VisualElement>("detail-subs-section");
        }

        static VisualElement MakeLogRow()
        {
            var row = new VisualElement();
            row.AddToClassList("mb-log-row");
            var time = new Label { name = "time" }.WithClass("mb-log-col-time");
            var frame = new Label { name = "frame" }.WithClass("mb-log-col-frame");
            var bus = new Label { name = "bus" }.WithClass("mb-log-col-bus");
            var op = new Label { name = "op" }.WithClass("mb-log-col-op");
            var type = new Label { name = "type" }.WithClass("mb-log-col-type");
            row.Add(time);
            row.Add(frame);
            row.Add(bus);
            row.Add(op);
            row.Add(type);
            // Cache child refs so bindItem (called on every scroll tick) doesn't re-query.
            row.userData = new[] { time, frame, bus, op, type };
            return row;
        }

        void BindLogRow(VisualElement row, int i)
        {
            if (i < 0 || i >= _filtered.Count) return;
            if (row.userData is not Label[] cols || cols.Length < 5) return;
            var r = _filtered[i];
            var time = cols[0];
            var frame = cols[1];
            var bus = cols[2];
            var op = cols[3];
            var type = cols[4];

            time.text = new DateTime(r.TimestampTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            frame.text = "f" + r.Frame.ToString(CultureInfo.InvariantCulture);
            bus.text = r.Bus == MessagesInstrumentation.BusKind.Event ? "E" : "C";
            bus.RemoveFromClassList("mb-log-bus-event");
            bus.RemoveFromClassList("mb-log-bus-command");
            bus.AddToClassList(r.Bus == MessagesInstrumentation.BusKind.Event ? "mb-log-bus-event" : "mb-log-bus-command");
            op.text = r.Op.ToString();
            type.text = r.MessageType?.Name ?? "(drain)";
        }

        void OnRowSelected(IEnumerable<object> selection)
        {
            foreach (var o in selection)
            {
                if (o is MessagesInstrumentation.Record r)
                {
                    _detailHeader.text = $"{r.Op} {r.Bus}";
                    var body = FormatRecord(r);
                    _detailBody.text = body;
                    _detailBody.style.display = body.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                    BuildDetailSections(r);
                    return;
                }
            }
            ClearDetail(); // deselected: don't leave the last record's details up
        }

        // ListView selection is index-based, and every rebuild, Clear, or trim
        // re-indexes _filtered: a kept selection would highlight whichever record
        // now sits at that index while the detail pane still shows the old one.
        void ResetSelection()
        {
            _logList?.ClearSelection();
            ClearDetail();
        }

        void ClearDetail()
        {
            if (_detailHeader == null) return; // UI not built
            _detailHeader.text = "Select a row to inspect";
            _detailBody.text = string.Empty;
            _detailBody.style.display = DisplayStyle.None;
            _detailTypeSection.Clear();
            _detailPayloadSection.Clear();
            _detailSubsSection.Clear();
        }

        // Render the message type and the captured subscribers as clickable
        // ScriptFileFields: single-click pings the .cs file, double-click opens it.
        void BuildDetailSections(MessagesInstrumentation.Record r)
        {
            _detailTypeSection.Clear();
            _detailPayloadSection.Clear();
            _detailSubsSection.Clear();

            if (r.MessageType != null)
            {
                _detailTypeSection.Add(SectionHeader("Message Type"));
                var field = new ScriptFileField();
                field.SetType(r.MessageType);
                field.text = r.MessageType.FullName;
                _detailTypeSection.Add(field);
            }

            bool isFire = r.Op == MessagesInstrumentation.Op.Publish || r.Op == MessagesInstrumentation.Op.Enqueue;

            // Payload sits right after the message type so the struct contents
            // read next to the type that defines them.
            if (r.PayloadBox != null)
            {
                _detailPayloadSection.Add(SectionHeader("Payload"));
                var sb = new StringBuilder();
                FormatPayload(sb, r.PayloadBox);
                var payloadLabel = new Label(sb.ToString());
                payloadLabel.AddToClassList("mb-detail-body");
                _detailPayloadSection.Add(payloadLabel);
            }

            if (!isFire) return;

            string when = r.Op == MessagesInstrumentation.Op.Publish ? "publish" : "enqueue";
            _detailSubsSection.Add(SectionHeader($"Subscribers (at {when} time)"));

            var subs = r.Subscribers;
            if (subs == null || subs.Length == 0)
            {
                _detailSubsSection.Add(DimNote("(none)"));
            }
            else
            {
                foreach (var s in subs)
                {
                    var field = new ScriptFileField();
                    field.SetTypeName(s.Target);
                    field.text = $"#{s.TokenId}  {s.Target}.{s.Method}";
                    _detailSubsSection.Add(field);
                }
            }
        }

        static Label SectionHeader(string text)
        {
            var l = new Label(text);
            l.AddToClassList("mb-detail-section-header");
            return l;
        }

        static Label DimNote(string text)
        {
            var l = new Label(text);
            l.AddToClassList("mb-detail-section-note");
            return l;
        }

        // ── Tick (per editor frame) ──────────────────────────────────────
        void Tick()
        {
            if (_logList == null || _paused) return; // UI may not be built yet (OnEnable runs before CreateGUI)

            // Lock-free fast path: a stale read here only delays processing to the
            // next tick. ProcessIncremental re-reads the total paired with its
            // snapshot, so correctness never rests on this value.
            long total = MessagesInstrumentation.TotalEver;
            if (total == _lastTotalProcessed)
            {
                UpdateStatus();
                return;
            }

            ProcessIncremental();

            _logList.RefreshItems();

            // Keep the newest record in view as traffic arrives.
            if (_autoScroll && _filtered.Count > 0)
                _logList.ScrollToItem(_filtered.Count - 1);

            UpdateStatus();
            Repaint();
        }

        void ProcessIncremental()
        {
            // The paired overload: totalEver is read under the same lock as the
            // records, so "the last N records are new" is exact. Reading TotalEver
            // before snapshotting let worker-thread records land in between — those
            // were displayed twice and pushed an equal number of older records out
            // of the catch-up window entirely.
            var snapshot = MessagesInstrumentation.Snapshot(out long totalEver);
            int currentCount = snapshot.Count;

            int capacity = MessagesInstrumentation.Capacity;
            int newCount = (int)Math.Min(currentCount, totalEver - _lastTotalProcessed);

            if (newCount <= 0) return;

            int startIdx = currentCount - newCount;
            for (int i = startIdx; i < currentCount; i++)
            {
                var r = snapshot[i];

                // Filtering for Log
                if (PassesFilter(r))
                    _filtered.Add(r);
            }

            // Batch-trim rather than RemoveAt(0) per overflowing record: each
            // RemoveAt(0) shifts the whole list (O(n)); doing it per item makes a
            // steady-state overflow O(n²). Let the list run a margin past capacity,
            // then drop the entire overflow in one RemoveRange (a single shift).
            const int TrimMargin = 128;
            if (_filtered.Count > capacity + TrimMargin)
            {
                int trimmed = _filtered.Count - capacity;
                _filtered.RemoveRange(0, trimmed);

                // Selection is by index: move it with its record, or drop it if
                // the record was trimmed away.
                int selected = _logList.selectedIndex;
                if (selected >= trimmed)
                    _logList.SetSelectionWithoutNotify(new[] { selected - trimmed });
                else if (selected >= 0)
                    ResetSelection();
            }

            _lastTotalProcessed = totalEver;
        }

        bool PassesFilter(MessagesInstrumentation.Record r)
        {
            if (r.Bus == MessagesInstrumentation.BusKind.Event && !_showEvents) return false;
            if (r.Bus == MessagesInstrumentation.BusKind.Command && !_showCommands) return false;

            switch (r.Op)
            {
                case MessagesInstrumentation.Op.Publish:    if (!_showPublish) return false; break;
                case MessagesInstrumentation.Op.Enqueue:    if (!_showEnqueue) return false; break;
                case MessagesInstrumentation.Op.Subscribe:
                case MessagesInstrumentation.Op.Unsubscribe: if (!_showSubs) return false; break;
                case MessagesInstrumentation.Op.DrainStart:
                case MessagesInstrumentation.Op.DrainEnd:   if (!_showDrains) return false; break;
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
            _statusLabel.text = $"{_filtered.Count} visible · {MessagesInstrumentation.Count} total records";
        }

        // ── Payload formatting ───────────────────────────────────────────
        // Body shows only the token/handler for subscription records; the op
        // and bus live in the header, the payload in its own section.
        static string FormatRecord(MessagesInstrumentation.Record r)
        {
            var sb = new StringBuilder();
            if (r.TokenId != 0) sb.Append("Token:   #").Append(r.TokenId);
            if (r.HandlerTarget != null || r.HandlerMethod != null)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("Handler: ").Append(r.HandlerTarget).Append('.').Append(r.HandlerMethod);
            }
            return sb.ToString();
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
                // Indexers need arguments - GetValue(payload) would always throw.
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
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
