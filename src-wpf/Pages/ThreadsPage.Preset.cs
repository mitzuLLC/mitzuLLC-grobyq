using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    // Threads > Game preset + Thread rules: what the game optimizer writes for one game (pick it in the list): process
    // class, I/O and memory priority, EcoQoS, process boost, the hot-thread priorities, and the thread rules (match by
    // thread description or start module; the first matching rule wins). Edits go into the page draft like every other
    // setting: Save writes presets.json and re-applies, Cancel drops them.
    public sealed partial class ThreadsPage
    {
        private ComboBox _pick, _cls, _io, _mem, _eco, _pboost, _hot1, _hotN;
        private TextBlock _clsDesc, _ioDesc, _memDesc;
        private StackPanel _rulesHost;
        private TextBlock _rulesEmpty;
        private readonly Dictionary<string, Preset> _edit = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);  // draft copies
        private string _cur;
        private bool _pload;
        private string[] _prioKeys, _prioText;

        private void BuildPreset()
        {
            BuildPrioChoices();

            Title(L.T("Game preset"));
            var card = Card();
            _pick = Combo.New(240);
            _pick.SelectionChanged += (s, e) => { if (!_pload) LoadPreset(Combo.Val(_pick)); };
            var builtin = Ui.Btn(L.T("Built-in"), "BtnOutline");
            builtin.Height = 30;
            builtin.Margin = new Thickness(8, 0, 0, 0);
            builtin.Click += (s, e) => ResetToBuiltin();
            var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
            pickRow.Children.Add(_pick);
            pickRow.Children.Add(builtin);
            pickRow.Height = 30;
            card.Add(Ui.Row(L.T("Game"), L.T("Pick the game to tune. While the game threads optimizer runs, the process part below (class, I/O, memory, EcoQoS, boost) always applies - Normal means unchanged; the thread priorities and rules apply when Optimize threads is on."), pickRow));

            _cls = Combo.New(200, new[] { new Opt("High", L.T("High")), new Opt("AboveNormal", L.T("Above normal")), new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Idle", L.T("Idle")) });
            card.Add(ComboRow(L.T("Priority class"), Explain.Class("High"), _cls, out _clsDesc));
            _io = Combo.New(200, new[] { new Opt("High", L.T("High")), new Opt("Normal", L.T("Normal")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")) });
            card.Add(ComboRow(L.T("I/O priority"), Explain.Io("Normal"), _io, out _ioDesc));
            _mem = Combo.New(200, new[] { new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Medium", L.T("Medium")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")) });
            card.Add(ComboRow(L.T("Memory priority"), Explain.Mem("Normal"), _mem, out _memDesc));
            TextBlock dummy;
            _eco = Combo.New(200, new[] { new Opt("Off", L.T("Off (HighQoS)")), new Opt("Keep", L.T("Keep Windows default")) });
            card.Add(ComboRow("EcoQoS", Explain.Eco, _eco, out dummy));
            _pboost = Combo.New(200, new[] { new Opt("Keep", L.T("Keep")), new Opt("Off", L.T("Off")), new Opt("On", L.T("On")) });
            card.Add(ComboRow(L.T("Process priority boost"), Explain.ProcBoost, _pboost, out dummy));
            _hot1 = Combo.New(200, PrioOpts());
            card.Add(ComboRow(L.T("Hottest thread priority"), L.T("Priority for the busiest unnamed thread when no named rule matched (usually the main thread)."), _hot1, out dummy));
            _hotN = Combo.New(200, PrioOpts());
            card.Add(ComboRow(L.T("Other hot threads priority"), L.T("Priority for the remaining hot threads. keep = only the Ideal CPU is set."), _hotN, out dummy, false));

            _cls.SelectionChanged += (s, e) => { _clsDesc.Text = Explain.Class(Combo.Val(_cls)); Edit(p => p.Class = Combo.Val(_cls)); };
            _io.SelectionChanged += (s, e) => { _ioDesc.Text = Explain.Io(Combo.Val(_io)); Edit(p => p.Io = Combo.Val(_io)); };
            _mem.SelectionChanged += (s, e) => { _memDesc.Text = Explain.Mem(Combo.Val(_mem)); Edit(p => p.Mem = Combo.Val(_mem)); };
            _eco.SelectionChanged += (s, e) => Edit(p => p.Eco = Combo.Val(_eco) == "Keep" ? "Keep" : "Off");
            _pboost.SelectionChanged += (s, e) => Edit(p => p.Boost = Combo.Val(_pboost));
            _hot1.SelectionChanged += (s, e) => Edit(p => p.HotFirst = Combo.Val(_hot1));
            _hotN.SelectionChanged += (s, e) => Edit(p => p.HotOthers = Combo.Val(_hotN));

            // ---- thread rules of the picked game ----
            Title(L.T("Thread rules"));
            var rc = Card();
            var rdesc = rc.Add(Ui.Text(L.T("Top to bottom; the first matching rule wins for a thread. Match by thread description (GetThreadDescription) or by the module its start address is in. Applied when Optimize threads is on."), 8.75, "Dim"));
            rdesc.Margin = new Thickness(0, 8, 0, 10);
            rc.Add(RulesHeader());
            _rulesHost = rc.Add(new StackPanel());
            _rulesEmpty = rc.Add(Ui.Text(L.T("No rules: only the hot threads are tuned."), 9, "Faint"));
            _rulesEmpty.Margin = new Thickness(2, 2, 0, 6);
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10) };
            var add = Ui.Btn("+  " + L.T("Add rule"), "BtnOutline");
            add.Height = 30;
            // a new rule starts empty (an empty pattern matches nothing): it acts only once a pattern is typed in
            add.Click += (s, e) => { Edit(p => p.Threads.Add(new ThreadRule { Match = "desc", Pattern = "" })); FillRules(); };
            strip.Children.Add(add);
            rc.Add(strip);
        }

        private void BuildPrioChoices()
        {
            var k = new List<string> { "", "Idle", "Lowest", "BelowNormal", "Normal", "AboveNormal", "Highest" };
            var t = new List<string> { L.T("keep"), L.T("Idle"), L.T("Lowest"), L.T("Below normal"), L.T("Normal"), L.T("Above normal"), L.T("High (Highest)") };
            var o = Optimizer.Instance;
            bool used = o != null && o.Eng.Presets != null && o.Eng.Presets.Values.Any(p => p.Threads.Any(r => string.Equals(r.Prio, "TimeCritical", StringComparison.OrdinalIgnoreCase))
                || string.Equals(p.HotFirst, "TimeCritical", StringComparison.OrdinalIgnoreCase) || string.Equals(p.HotOthers, "TimeCritical", StringComparison.OrdinalIgnoreCase));
            if (Engine.AdvancedThreadMode || used) { k.Add("TimeCritical"); t.Add(L.T("Time critical (15) - Advanced")); }
            _prioKeys = k.ToArray(); _prioText = t.ToArray();
        }

        private IEnumerable<Opt> PrioOpts()
        {
            for (int i = 0; i < _prioKeys.Length; i++) yield return new Opt(_prioKeys[i], _prioText[i]);
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
            foreach (var c in new Control[] { _cls, _io, _mem, _eco, _pboost, _hot1, _hotN }) c.IsEnabled = on;
            if (!on) { _rulesHost.Children.Clear(); _rulesEmpty.Visibility = Visibility.Visible; return; }
            var p = Source(_cur);
            _pload = true;
            try
            {
                Combo.Select(_cls, p.Class); Combo.Select(_io, p.Io); Combo.Select(_mem, p.Mem);
                Combo.Select(_eco, string.Equals(p.Eco, "Keep", StringComparison.OrdinalIgnoreCase) ? "Keep" : "Off");
                Combo.Select(_pboost, p.Boost ?? "Keep");
                Combo.Select(_hot1, p.HotFirst ?? ""); Combo.Select(_hotN, p.HotOthers ?? "");
                _clsDesc.Text = Explain.Class(Combo.Val(_cls)); _ioDesc.Text = Explain.Io(Combo.Val(_io)); _memDesc.Text = Explain.Mem(Combo.Val(_mem));
            }
            finally { _pload = false; }
            FillRules();
        }

        private void ResetToBuiltin()
        {
            if (_cur == null) return;
            Preset b;
            if (!Store.Defaults().TryGetValue(_cur, out b))
            {
                Dlg.Info(Window.GetWindow(this), L.F("{0} has no built-in preset. Built-in: {1}", _cur, string.Join(", ", Store.Defaults().Keys)));
                return;
            }
            if (!Dlg.Ask(Window.GetWindow(this), L.F("Reset {0} to the built-in preset? Your rules for it are replaced.", _cur))) return;
            _edit[_cur] = b.Clone();
            LoadPreset(_cur);
            UpdateSaveBar(null);
        }

        // ------------------------------------------------------------------ draft
        private void Edit(Action<Preset> change)
        {
            if (_pload || _loading || _cur == null) return;
            Preset p;
            if (!_edit.TryGetValue(_cur, out p)) { p = Source(_cur).Clone(); p.Exe = _cur; _edit[_cur] = p; }
            change(p);
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
            _edit.Clear();
            var o = Optimizer.Instance;
            if (copies.Count == 0 || o == null) return;
            o.Eng.UnderLock(() => { foreach (var kv in copies) o.Eng.Presets[kv.Key] = kv.Value; });
            string err = o.Eng.SavePresets();
            AppLog.Add(err.Length == 0 ? "[PS] preset saved: " + string.Join(", ", copies.Keys) : "[!!] presets.json: " + err);
        }

        // ------------------------------------------------------------------ rules editor
        private const double CMatch = 116, CPrio = 150, CIdeal = 92, CBoost = 92, COn = 36, CAct = 92;

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
            string[] tips = { null, null, null, Explain.ThreadPriority, Explain.IdealCpu, Explain.Boost };
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
            _pload = true;
            try { for (int i = 0; i < p.Threads.Count; i++) _rulesHost.Children.Add(RuleRow(p.Threads[i], i, p.Threads.Count)); }
            finally { _pload = false; }
            _rulesEmpty.Visibility = p.Threads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement RuleRow(ThreadRule r, int i, int count)
        {
            var g = RuleGrid();
            g.Margin = new Thickness(0, 0, 0, 6);

            var on = new BoxCheck("") { Margin = new Thickness(-4, 0, 0, 0) };
            on.SetSilently(r.Enabled);
            on.CheckedChanged += () => Edit(p => p.Threads[i].Enabled = on.Checked);
            Put(g, on, 0);

            var match = Combo.New(CMatch, new[] { new Opt("desc", L.T("description")), new Opt("module", L.T("module")) });
            Combo.Select(match, string.Equals(r.Match, "module", StringComparison.OrdinalIgnoreCase) ? "module" : "desc");
            match.SelectionChanged += (s, e) => Edit(p => p.Threads[i].Match = Combo.Val(match));
            Put(g, match, 1);

            var pat = Ui.Box(r.Pattern, double.NaN);
            pat.HorizontalAlignment = HorizontalAlignment.Stretch;
            pat.Margin = new Thickness(0, 0, 8, 0);
            pat.TextChanged += (s, e) => Edit(p => p.Threads[i].Pattern = (pat.Text ?? "").Trim());
            Put(g, pat, 2);

            var prio = Combo.New(CPrio, PrioOpts());
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
            return g;
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
