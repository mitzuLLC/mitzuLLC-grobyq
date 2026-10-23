using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Grobyq.Pages
{
    // Threads > Reference (4.0.6): what the game threads optimizer sets for one game. Header: the game, a pill that says
    // whether this PC runs the built-in reference of this version or its own rules, Restore reference. Then one row per
    // thread rule - the thread, its caption and its priority right there in a dropdown, with the base priority each level
    // gives (Time critical = 15). Below: the process part in one line and Advanced settings, which fold out the process
    // combos and the full rule editor. Every edit is a draft: Save writes presets.json and re-applies, Cancel drops it.
    public sealed partial class ThreadsPage
    {
        private ComboBox _pick, _cls, _io, _mem, _eco, _pboost;
        private TextBlock _clsDesc, _ioDesc, _memDesc, _procLine, _stateText;
        private Ellipse _stateDot;
        private StackPanel _rulesHost, _sumHost, _editHost;
        private TextBlock _rulesEmpty;
        private Button _restore, _editBtn;
        private readonly Dictionary<string, Preset> _edit = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);  // draft copies
        private string _cur;
        private bool _pload;

        private static readonly string[] Levels = { "Idle", "Lowest", "BelowNormal", "Normal", "AboveNormal", "Highest", "TimeCritical" };

        private void BuildPreset()
        {
            Title(L.T("Reference"));
            var card = Card();

            // ---- header: the game on the left; the state pill and Restore reference on the right ----
            var head = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pick = Combo.New(260);
            _pick.HorizontalAlignment = HorizontalAlignment.Left;
            _pick.SelectionChanged += (s, e) => { if (!_pload) LoadPreset(Combo.Val(_pick)); };
            head.Children.Add(_pick);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _stateDot = new Ellipse { Width = 7, Height = 7, Fill = Ui.B("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
            _stateText = Ui.Text("", 9, "Dim", false);
            _stateText.VerticalAlignment = VerticalAlignment.Center;
            var pillRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pillRow.Children.Add(_stateDot);
            pillRow.Children.Add(_stateText);
            right.Children.Add(new Border { Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.B("Input"), BorderBrush = Ui.B("Edge"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 14, 0), Child = pillRow, SnapsToDevicePixels = true });
            _restore = Ui.Btn(L.T("Restore reference"), "BtnOutline");
            _restore.Height = 30;
            _restore.Margin = new Thickness(8, 0, 0, 0);
            _restore.Click += (s, e) => RestoreReference();
            right.Children.Add(_restore);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);
            card.Add(head);
            card.Add(new Border { Height = 1, Background = Ui.B("Line"), SnapsToDevicePixels = true });

            // ---- the thread rules: one row each ----
            _sumHost = card.Add(new StackPanel());

            // ---- the process part in one line, Advanced settings on the right ----
            var foot = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _procLine = Ui.Text("", 8.75, "Dim");
            _procLine.VerticalAlignment = VerticalAlignment.Center;
            _procLine.Margin = new Thickness(0, 0, 16, 0);
            foot.Children.Add(_procLine);
            _editBtn = Ui.Btn(L.T("Advanced settings"), "BtnOutline");
            _editBtn.Height = 30;
            _editBtn.VerticalAlignment = VerticalAlignment.Center;
            _editBtn.Click += (s, e) => ShowEditor(_editHost.Visibility != Visibility.Visible);
            Grid.SetColumn(_editBtn, 1);
            foot.Children.Add(_editBtn);
            card.Add(foot);

            // ---- Advanced settings, folded: the process part and the full rule editor ----
            _editHost = card.Add(new StackPanel { Visibility = Visibility.Collapsed });
            _editHost.Children.Add(new Border { Height = 1, Background = Ui.B("Line"), SnapsToDevicePixels = true });
            _cls = Combo.New(200, new[] { new Opt("High", L.T("High")), new Opt("AboveNormal", L.T("Above normal")), new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Idle", L.T("Idle")) });
            _editHost.Children.Add(ComboRow(L.T("Priority class"), Explain.Class("High"), _cls, out _clsDesc));
            _io = Combo.New(200, new[] { new Opt("High", L.T("High")), new Opt("Normal", L.T("Normal")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")) });
            _editHost.Children.Add(ComboRow(L.T("I/O priority"), Explain.Io("Normal"), _io, out _ioDesc));
            _mem = Combo.New(200, new[] { new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Medium", L.T("Medium")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")) });
            _editHost.Children.Add(ComboRow(L.T("Memory priority"), Explain.Mem("Normal"), _mem, out _memDesc));
            TextBlock dummy;
            _eco = Combo.New(200, new[] { new Opt("Off", L.T("Off (HighQoS)")), new Opt("Keep", L.T("Keep Windows default")) });
            _editHost.Children.Add(ComboRow("EcoQoS", Explain.Eco, _eco, out dummy));
            _pboost = Combo.New(200, new[] { new Opt("Keep", L.T("Keep")), new Opt("Off", L.T("Off")), new Opt("On", L.T("On")) });
            _editHost.Children.Add(ComboRow(L.T("Process priority boost"), Explain.ProcBoost, _pboost, out dummy));

            // the class moves every level's number: the rows and the editor show them again
            _cls.SelectionChanged += (s, e) => { _clsDesc.Text = Explain.Class(Combo.Val(_cls)); Edit(p => p.Class = Combo.Val(_cls)); if (!_pload) FillRules(); };
            _io.SelectionChanged += (s, e) => { _ioDesc.Text = Explain.Io(Combo.Val(_io)); Edit(p => p.Io = Combo.Val(_io)); };
            _mem.SelectionChanged += (s, e) => { _memDesc.Text = Explain.Mem(Combo.Val(_mem)); Edit(p => p.Mem = Combo.Val(_mem)); };
            _eco.SelectionChanged += (s, e) => Edit(p => p.Eco = Combo.Val(_eco) == "Keep" ? "Keep" : "Off");
            _pboost.SelectionChanged += (s, e) => Edit(p => p.Boost = Combo.Val(_pboost));

            var rdesc = Ui.Text(L.T("Thread rules, top to bottom: the first matching rule wins for a thread. A pattern with a file name (.dll, .exe) matches the module the thread starts in, any other pattern matches the thread description (GetThreadDescription). Known threads get a caption under the pattern."), 8.75, "Dim");
            rdesc.Margin = new Thickness(0, 12, 0, 10);
            _editHost.Children.Add(rdesc);
            _editHost.Children.Add(RulesHeader());
            _rulesHost = new StackPanel();
            _editHost.Children.Add(_rulesHost);
            _rulesEmpty = Ui.Text(L.T("No rules: no thread of the game is touched."), 9, "Faint");
            _rulesEmpty.Margin = new Thickness(2, 2, 0, 6);
            _editHost.Children.Add(_rulesEmpty);
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10) };
            var add = Ui.Btn("+  " + L.T("Add rule"), "BtnOutline");
            add.Height = 30;
            // a new rule starts empty (an empty pattern matches nothing): it acts only once a pattern is typed in
            add.Click += (s, e) => { Edit(p => p.Threads.Add(new ThreadRule { Match = "desc", Pattern = "" })); FillRules(); };
            strip.Children.Add(add);
            _editHost.Children.Add(strip);
        }

        private void ShowEditor(bool show)
        {
            _editHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _editBtn.Content = show ? L.T("Hide advanced settings") : L.T("Advanced settings");
        }

        private static Border ComboRow(string title, string desc, ComboBox c, out TextBlock d, bool divider = true)
        {
            var row = Ui.Row(title, desc, c, divider);
            d = null;
            var g = row.Child as Grid;
            if (g != null && g.Children.Count > 0 && g.Children[0] is StackPanel left && left.Children.Count > 1) d = left.Children[1] as TextBlock;
            if (d == null) d = new TextBlock();
            return row;
        }

        // ------------------------------------------------------------------ levels
        // The thread priority levels with the base priority each gives under the preset's class. "keep" (the rule leaves the
        // priority alone) is offered in the rule editor, and in a row only when the rule already has it.
        private static IEnumerable<Opt> LevelOpts(string current, int classBase, bool withKeep)
        {
            string cur = current ?? "";
            if (withKeep || cur.Length == 0) yield return new Opt("", L.T("keep"));
            foreach (var k in Levels) yield return new Opt(k, LevelText(k, classBase));
            if (cur.Length > 0 && !Levels.Any(k => string.Equals(k, cur, StringComparison.OrdinalIgnoreCase))) yield return new Opt(cur, cur);   // a stored value grobyq does not know stays visible
        }

        // "Above normal · 9": the level and its base priority (Idle = 1 and Time critical = 15 whatever the class)
        private static string LevelText(string key, int classBase)
        {
            string name;
            switch (key)
            {
                case "Idle": name = L.T("Idle"); break;
                case "Lowest": name = L.T("Lowest"); break;
                case "BelowNormal": name = L.T("Below normal"); break;
                case "Normal": name = L.T("Normal"); break;
                case "AboveNormal": name = L.T("Above normal"); break;
                case "Highest": name = L.T("High (Highest)"); break;
                case "TimeCritical": name = L.T("Time critical"); break;
                default: return key;
            }
            int off;
            if (!Engine.ThreadPrio(key, out off)) return name;
            int v = off == -15 ? 1 : off == Engine.TimeCriticalValue ? 15 : Math.Max(1, Math.Min(15, classBase + off));
            return name + " · " + v.ToString(CultureInfo.InvariantCulture);
        }

        private static int ClassBase(string cls)
        {
            switch ((cls ?? "").Trim().ToLowerInvariant())
            {
                case "idle": return 4;
                case "belownormal": return 6;
                case "abovenormal": return 10;
                case "high": case "realtime": return 13;       // never Realtime: the engine runs it as High
                default: return 8;
            }
        }

        private static string ComboText(ComboBox c, string value)
        {
            foreach (var it in c.Items)
            {
                var o = it as Opt;
                if (o != null && string.Equals(o.Value, value ?? "", StringComparison.OrdinalIgnoreCase)) return o.Text;
            }
            return value ?? "";
        }

        // ------------------------------------------------------------------ the rows
        // One row per enabled rule with a pattern; a disabled rule shows only in Advanced settings.
        private void FillSummary()
        {
            _sumHost.Children.Clear();
            if (_cur == null) { _procLine.Text = ""; _stateText.Text = ""; _stateDot.Fill = Ui.B("Faint"); _restore.Visibility = Visibility.Collapsed; return; }
            var p = Source(_cur);
            int cb = ClassBase(p.Class);
            var idx = new List<int>();
            for (int i = 0; i < p.Threads.Count; i++)
                if (p.Threads[i].Enabled && (p.Threads[i].Pattern ?? "").Trim().Length > 0) idx.Add(i);
            bool was = _pload;
            _pload = true;
            try
            {
                if (idx.Count == 0)
                {
                    var none = Ui.Text(L.T("No thread rules: only the process part below applies."), 9, "Faint");
                    none.Margin = new Thickness(0, 12, 0, 4);
                    _sumHost.Children.Add(none);
                }
                for (int k = 0; k < idx.Count; k++) _sumHost.Children.Add(RuleLine(p.Threads[idx[k]], idx[k], cb, k < idx.Count - 1));
            }
            finally { _pload = was; }
            UpdateRefState(p);
        }

        // the thread and its caption on the left, its priority on the right (a draft edit, Save applies it)
        private Border RuleLine(ThreadRule r, int index, int classBase, bool divider)
        {
            var c = Combo.New(240, LevelOpts(r.Prio, classBase, false));
            Combo.Select(c, r.Prio ?? "");
            c.SelectionChanged += (s, e) =>
            {
                if (_pload) return;
                string v = Combo.Val(c);
                Edit(p => { if (index < p.Threads.Count) p.Threads[index].Prio = v; }, false);
            };
            string pat = (r.Pattern ?? "").Trim();
            string desc = ThreadNotes.For(pat);
            if (desc.Length == 0) desc = string.Equals(r.Match, "module", StringComparison.OrdinalIgnoreCase) ? L.F("Threads that start in {0}.", pat) : L.F("Threads named {0}.", pat);
            var extra = new List<string>();
            if (string.Equals(r.Boost, "Off", StringComparison.OrdinalIgnoreCase)) extra.Add(L.T("boost off"));
            else if (string.Equals(r.Boost, "On", StringComparison.OrdinalIgnoreCase)) extra.Add(L.T("boost on"));
            if (!string.IsNullOrEmpty(r.Ideal)) extra.Add("Ideal CPU " + r.Ideal);
            if (!string.IsNullOrEmpty(r.Tid)) extra.Add("tid " + r.Tid);
            if (extra.Count > 0) desc += "  ·  " + string.Join("  ·  ", extra);
            return Ui.Row(pat.Trim('*'), desc, c, divider);
        }

        // the pill, Restore reference and the process line
        private void UpdateRefState(Preset p)
        {
            _procLine.Text = L.F("Process: priority class {0}  ·  I/O {1}  ·  memory {2}  ·  EcoQoS {3}  ·  priority boost {4}",
                ComboText(_cls, p.Class), ComboText(_io, p.Io), ComboText(_mem, p.Mem),
                ComboText(_eco, string.Equals(p.Eco, "Keep", StringComparison.OrdinalIgnoreCase) ? "Keep" : "Off"), ComboText(_pboost, string.IsNullOrEmpty(p.Boost) ? "Keep" : p.Boost));
            bool has = Store.HasBuiltin(_cur), same = has && Store.IsBuiltin(_cur, p);
            _stateText.Text = same ? L.T("built-in reference") : has ? L.T("your rules") : L.T("no built-in reference");
            _stateText.Foreground = Ui.B(same ? "Fg" : "Dim");
            _stateDot.Fill = Ui.B(same ? "Ok" : has ? "Warn" : "Faint");
            _restore.Visibility = has && !same ? Visibility.Visible : Visibility.Collapsed;
        }

        // Restore reference: the built-in preset replaces this game's saved one at once (asked first) and a running session
        // re-applies; other unsaved edits on the page stay a draft.
        private void RestoreReference()
        {
            string exe = _cur;
            Preset b;
            if (exe == null || !Store.Defaults().TryGetValue(exe, out b)) return;
            if (!Dlg.Ask(Window.GetWindow(this), L.F("Restore the built-in reference for {0}? Your rules for it are replaced.", GameLabel(exe)))) return;
            _edit.Remove(exe);
            var o = Optimizer.Instance;
            if (o != null)
            {
                o.Eng.UnderLock(() => { o.Eng.Presets[exe] = b; });
                string err = o.Eng.SavePresets();
                AppLog.Add(err.Length == 0 ? "[PS] " + exe + ": built-in reference restored" : "[!!] presets.json: " + err);
                o.ApplyNow();                                   // in a session: restore, then apply the reference afresh
            }
            LoadPreset(exe);
            UpdateSaveBar(L.T("Reference restored."));
        }

        // ------------------------------------------------------------------ picker
        private string GameLabel(string exe)
        {
            foreach (var g in KnownGames) if (string.Equals(g[0], exe, StringComparison.OrdinalIgnoreCase)) return g[1];
            string l; return _labels.TryGetValue(exe, out l) && !string.IsNullOrEmpty(l) ? l : exe;
        }

        // the watched games (draft list) in the picker; keeps the current pick when it is still there
        private void FillPick()
        {
            if (_pick == null) return;
            _pload = true;
            try
            {
                string keep = _cur;
                _pick.Items.Clear();
                foreach (var exe in _games) _pick.Items.Add(new Opt(exe, GameLabel(exe) + (GameLabel(exe) == exe ? "" : "   ·   " + exe)));
                if (keep == null || !_games.Any(x => string.Equals(x, keep, StringComparison.OrdinalIgnoreCase))) keep = _games.FirstOrDefault();
                if (keep != null) Combo.Select(_pick, keep);
            }
            finally { _pload = false; }
            LoadPreset(Combo.Val(_pick));
        }

        // the preset as it is now: the draft copy, else the saved one, else the built-in, else engine defaults
        private Preset Source(string exe)
        {
            Preset p;
            if (_edit.TryGetValue(exe, out p)) return p;
            var o = Optimizer.Instance;
            if (o != null && o.Eng.Presets != null && o.Eng.Presets.TryGetValue(exe, out p) && p != null) return p;
            if (Store.Defaults().TryGetValue(exe, out p)) return p;
            return new Preset { Exe = exe };
        }

        private static Preset Saved(string exe)
        {
            Preset p;
            var o = Optimizer.Instance;
            return o != null && o.Eng.Presets != null && o.Eng.Presets.TryGetValue(exe, out p) ? p : null;
        }

        private void LoadPreset(string exe)
        {
            _cur = string.IsNullOrEmpty(exe) ? null : exe;
            bool on = _cur != null;
            foreach (var c in new Control[] { _cls, _io, _mem, _eco, _pboost, _editBtn }) c.IsEnabled = on;
            if (!on) { _rulesHost.Children.Clear(); _rulesEmpty.Visibility = Visibility.Visible; FillSummary(); return; }
            var p = Source(_cur);
            _pload = true;
            try
            {
                Combo.Select(_cls, p.Class); Combo.Select(_io, p.Io); Combo.Select(_mem, p.Mem);
                Combo.Select(_eco, string.Equals(p.Eco, "Keep", StringComparison.OrdinalIgnoreCase) ? "Keep" : "Off");
                Combo.Select(_pboost, p.Boost ?? "Keep");
                _clsDesc.Text = Explain.Class(Combo.Val(_cls)); _ioDesc.Text = Explain.Io(Combo.Val(_io)); _memDesc.Text = Explain.Mem(Combo.Val(_mem));
            }
            finally { _pload = false; }
            FillRules();
            FillSummary();
        }

        // ------------------------------------------------------------------ draft
        // rows = rebuild the rule rows (an edit made in Advanced settings); a priority picked in a row leaves the rows as
        // they are and refreshes the open editor instead
        private void Edit(Action<Preset> change, bool rows = true)
        {
            if (_pload || _loading || _cur == null) return;
            Preset p;
            if (!_edit.TryGetValue(_cur, out p)) { p = Source(_cur).Clone(); p.Exe = _cur; _edit[_cur] = p; }
            change(p);
            if (rows) FillSummary();
            else
            {
                UpdateRefState(p);
                if (_editHost.Visibility == Visibility.Visible) FillRules();
            }
            UpdateSaveBar(null);
        }

        private static string Sig(Preset p)
        {
            if (p == null) return "";
            var parts = new List<string> { p.Class, p.Io, p.Mem, p.Eco, p.Boost, p.HotFirst, p.HotOthers, p.Enabled ? "1" : "0" };
            foreach (var r in p.Threads) parts.Add(string.Join("\u0001", r.Enabled ? "1" : "0", r.Match, r.Pattern, r.Prio, r.Ideal, r.Boost, r.Tid));
            return string.Join("\u0002", parts.Select(x => x ?? ""));
        }

        // edited presets that differ from what is saved
        private List<string> DirtyPresets()
        {
            var l = new List<string>();
            foreach (var kv in _edit)
            {
                var saved = Saved(kv.Key);
                string baseSig = saved != null ? Sig(saved) : Sig(Source0(kv.Key));
                if (Sig(kv.Value) != baseSig) l.Add(kv.Key);
            }
            return l;
        }

        // what the engine would use without a saved preset (built-in or defaults)
        private static Preset Source0(string exe)
        {
            Preset p;
            return Store.Defaults().TryGetValue(exe, out p) ? p : new Preset { Exe = exe };
        }

        // Save: the edited presets replace the saved ones (presets.json, under the engine lock); the caller re-applies.
        private void SavePresets()
        {
            var dirty = DirtyPresets();
            var copies = dirty.ToDictionary(x => x, x => _edit[x].Clone(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in copies) kv.Value.Builtin = Store.IsBuiltin(kv.Key, kv.Value);   // an edit back to the reference follows updates again
            _edit.Clear();
            var o = Optimizer.Instance;
            if (copies.Count == 0 || o == null) return;
            o.Eng.UnderLock(() => { foreach (var kv in copies) o.Eng.Presets[kv.Key] = kv.Value; });
            string err = o.Eng.SavePresets();
            AppLog.Add(err.Length == 0 ? "[PS] preset saved: " + string.Join(", ", copies.Keys.Select(k => k + (Store.IsBuiltin(k, copies[k]) ? " (built-in reference)" : Store.HasBuiltin(k) ? " (your rules)" : ""))) : "[!!] presets.json: " + err);
        }

        // ------------------------------------------------------------------ the rule editor (Advanced settings)
        private const double CMatch = 116, CPrio = 210, CIdeal = 92, CBoost = 92, COn = 36, CAct = 92;

        private Grid RuleGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(COn) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CMatch + 8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CPrio + 8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CIdeal + 8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CBoost + 8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CAct) });
            return g;
        }

        private static void Put(Grid g, FrameworkElement e, int col)
        {
            Grid.SetColumn(e, col);
            e.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(e);
        }

        private Grid RulesHeader()
        {
            var g = RuleGrid();
            g.Margin = new Thickness(0, 0, 0, 6);
            string[] heads = { L.T("On"), L.T("Match"), L.T("Pattern (wildcards * ?)"), L.T("Priority"), L.T("Ideal CPU"), L.T("Boost") };
            string[] tips = { null, Explain.MatchKind, null, Explain.ThreadPriority, Explain.IdealCpu, Explain.Boost };
            for (int i = 0; i < heads.Length; i++)
            {
                var t = Ui.Semi(heads[i], 8.25, "Faint");
                t.Margin = new Thickness(i == 0 ? 2 : 1, 0, 8, 0);
                if (tips[i] != null) { t.ToolTip = tips[i]; t.Cursor = System.Windows.Input.Cursors.Help; }
                Put(g, t, i);
            }
            return g;
        }

        private void FillRules()
        {
            _rulesHost.Children.Clear();
            if (_cur == null) { _rulesEmpty.Visibility = Visibility.Visible; return; }
            var p = Source(_cur);
            int cb = ClassBase(p.Class);
            bool was = _pload;
            _pload = true;
            try { for (int i = 0; i < p.Threads.Count; i++) _rulesHost.Children.Add(RuleRow(p.Threads[i], i, p.Threads.Count, cb)); }
            finally { _pload = was; }
            _rulesEmpty.Visibility = p.Threads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement RuleRow(ThreadRule r, int i, int count, int classBase)
        {
            var g = RuleGrid();
            g.Margin = new Thickness(0, 0, 0, 6);

            var on = new BoxCheck("") { Margin = new Thickness(-4, 0, 0, 0) };
            on.SetSilently(r.Enabled);
            on.CheckedChanged += () => Edit(p => p.Threads[i].Enabled = on.Checked);
            Put(g, on, 0);

            // 4.0.3: the match kind is not a choice any more - it follows the pattern (ThreadRule.MatchFor): a file name is
            // the module the thread starts in, anything else the thread description. An untouched row shows what it stores
            // (that is what the engine matches by); typing a pattern sets both. Known threads get a caption (ThreadNotes).
            var kindText = Ui.Text(KindText(r.Match), 8.75, "Dim", false);
            kindText.VerticalAlignment = VerticalAlignment.Center;
            var kind = new Border { Height = 30, Width = CMatch, CornerRadius = new CornerRadius(6), Background = Ui.B("Input"), Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = kindText, ToolTip = Explain.MatchKind, SnapsToDevicePixels = true };
            Put(g, kind, 1);

            var patCol = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var pat = Ui.Box(r.Pattern, double.NaN);
            pat.HorizontalAlignment = HorizontalAlignment.Stretch;
            patCol.Children.Add(pat);
            var note = Ui.Text("", 8.25, "Faint");
            note.Margin = new Thickness(2, 3, 0, 0);
            SetNote(note, r.Pattern);
            patCol.Children.Add(note);
            pat.TextChanged += (s, e) =>
            {
                string v = (pat.Text ?? "").Trim();
                string m = ThreadRule.MatchFor(v);
                Edit(p => { p.Threads[i].Pattern = v; p.Threads[i].Match = m; });
                kindText.Text = KindText(m);
                SetNote(note, v);
            };
            Put(g, patCol, 2);

            var prio = Combo.New(CPrio, LevelOpts(r.Prio, classBase, true));
            Combo.Select(prio, r.Prio ?? "");
            prio.SelectionChanged += (s, e) => Edit(p => p.Threads[i].Prio = Combo.Val(prio));
            Put(g, prio, 3);

            var ideal = Combo.New(CIdeal, IdealOpts(r.Ideal));
            Combo.Select(ideal, r.Ideal ?? "");
            ideal.SelectionChanged += (s, e) => Edit(p => p.Threads[i].Ideal = Combo.Val(ideal));
            Put(g, ideal, 4);

            var boost = Combo.New(CBoost, new[] { new Opt("", L.T("keep")), new Opt("Off", L.T("Off")), new Opt("On", L.T("On")) });
            Combo.Select(boost, r.Boost ?? "");
            boost.SelectionChanged += (s, e) => Edit(p => p.Threads[i].Boost = Combo.Val(boost));
            Put(g, boost, 5);

            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            acts.Children.Add(IconBtn("up", L.T("Up"), i > 0, () => Move(i, -1)));
            acts.Children.Add(IconBtn("down", L.T("Down"), i < count - 1, () => Move(i, 1)));
            acts.Children.Add(IconBtn("x", L.T("Remove"), true, () => { Edit(p => p.Threads.RemoveAt(i)); FillRules(); }));
            Put(g, acts, 6);
            foreach (FrameworkElement c in g.Children) c.VerticalAlignment = VerticalAlignment.Top;   // a caption grows the pattern cell downwards only
            on.Margin = new Thickness(-4, 1, 0, 0);                    // the 28 px controls sit centred on the 30 px line
            acts.Margin = new Thickness(0, 1, 0, 0);
            return g;
        }

        private static string KindText(string match)
        {
            return string.Equals(match, "module", StringComparison.OrdinalIgnoreCase) ? L.T("module") : L.T("description");
        }

        private static void SetNote(TextBlock note, string pattern)
        {
            string t = ThreadNotes.For(pattern);
            note.Text = t;
            note.Visibility = t.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private int _lps = -1;
        private IEnumerable<Opt> IdealOpts(string current)
        {
            yield return new Opt("", L.T("none"));
            yield return new Opt("auto", "auto");
            if (_lps < 0) { _lps = 0; try { _lps = Topo.Live().LogicalCount; } catch { } }
            int n = _lps;
            int cur;
            bool curListed = string.IsNullOrEmpty(current) || current == "auto" || (int.TryParse(current, out cur) && cur >= 0 && cur < n);
            for (int k = 0; k < n; k++) yield return new Opt(k.ToString(System.Globalization.CultureInfo.InvariantCulture), "CPU " + k);
            if (!curListed) yield return new Opt(current, current);     // a saved value this CPU does not have stays visible
        }

        private void Move(int i, int d)
        {
            int j = i + d;
            Edit(p =>
            {
                if (i < 0 || j < 0 || i >= p.Threads.Count || j >= p.Threads.Count) return;
                var t = p.Threads[i]; p.Threads[i] = p.Threads[j]; p.Threads[j] = t;
            });
            FillRules();
        }

        private static Border IconBtn(string kind, string tip, bool enabled, Action click)
        {
            var ic = new Ico { Kind = kind, Width = 11, Height = 11, Stroke = Ui.B(enabled ? "Dim" : "Line"), StrokeWidth = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, Child = ic, ToolTip = tip };
            if (!enabled) return b;
            b.Cursor = System.Windows.Input.Cursors.Hand;
            b.MouseEnter += (s, e) => { b.Background = Ui.B("Input"); ic.Stroke = Ui.B("Fg"); };
            b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; ic.Stroke = Ui.B("Dim"); };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }
    }
}
