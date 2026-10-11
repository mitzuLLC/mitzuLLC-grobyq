using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Grobyq.Pages
{
    // Processes tab: the per-process rule switch (draft), the mini Process Lasso (pick a process, tick its cores, save the
    // rule) and the list of saved rules. A rule is stored at once; it reaches the process only while the optimizer runs
    // with the switch on (a rule for the game replaces its core mask), and Remove rule gives the process its cores back.
    public sealed partial class AffinityPage
    {
        private CheckBox _swAff;
        private ComboBox _plProc;
        private CoreGrid _plGrid;
        private TextBox _plHex;
        private TextBlock _plInfo;
        private Button _plSave, _plRemove;
        private StackPanel _rules;
        private string _plSig = "", _plLoadedExe = "", _plLoadedRule = "";
        private bool _plDirty, _plBuilding, _plLoadingGrid, _plTexting;
        private string _plTypedSpec = "";
        private ulong _plTypedMask;

        private void BuildProcs(StackPanel p)
        {
            SubTitle(p, L.T("Per-process affinity rules"), true);
            var sc = SubCard(p, 20, 4, 4);
            _swAff = Ui.Switch(_pDoAff);
            _swAff.Click += (s, e) => { _pDoAff = _swAff.IsChecked == true; UpdateSaveBar(null); };
            sc.Add(Ui.Row(L.T("Apply per-process rules"), L.T("While the optimizer runs, every process in the list below gets the cores of its rule (voice, VPN, streaming, launchers). Off = the rules are kept but not applied. A rule for the game replaces its core mask on the Cores tab."), _swAff, false));

            // ---- the mini Process Lasso ----
            SubTitle(p, L.T("Process affinity"));
            var card = SubCard(p);
            var intro = card.Add(Ui.Text(L.T("Pick a process, tick the cores it may use and press Save rule. Remove rule gives the process its cores back. Anti-cheat and protected system processes are not listed."), 9, "Dim"));
            intro.Margin = new Thickness(0, 0, 0, 6);

            var pr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var pl = Ui.Semi(L.T("Process"), 10, "Fg"); pl.VerticalAlignment = VerticalAlignment.Center; pl.Width = 90;
            _plProc = Combo.New(320);
            _plProc.SelectionChanged += (s, e) => { if (!_plBuilding) PlLoad(); };
            var refresh = Ui.Btn(L.T("Refresh"), "BtnGhost");
            refresh.Margin = new Thickness(8, 0, 0, 0);
            refresh.Click += (s, e) => PlFillProcs(PlSel());
            pr.Children.Add(pl); pr.Children.Add(_plProc); pr.Children.Add(refresh);
            card.Add(pr);
            card.Add(Ui.Hairline());

            var ch = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            ch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ch.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            lg.Children.Add(Ui.Semi(L.T("Cores"), 10, "Fg"));
            var dot = Ui.Text("●", 9, "Acc", false); dot.Margin = new Thickness(10, 0, 4, 0); lg.Children.Add(dot);
            lg.Children.Add(Ui.Text(L.T("= a core of the current game mask"), 9, "Dim", false));
            ch.Children.Add(lg);
            var quick = new StackPanel { Orientation = Orientation.Horizontal };
            Func<string, string, Button> q = (text, style) => { var b = Ui.Btn(text, style); b.Height = 30; b.Margin = new Thickness(6, 0, 0, 0); quick.Children.Add(b); return b; };
            q(L.T("All cores"), "BtnOutline").Click += (s, e) => { var t = T; if (t != null) _plGrid.Mask = t.FullMask(); };
            q(L.T("Game cores"), "BtnOutline").Click += (s, e) => { var t = T; if (t != null) _plGrid.Mask = GameMask.Effective(t).Mask; };
            q(L.T("Off the game cores"), "BtnOutline").Click += (s, e) => { var t = T; if (t != null) { ulong g = GameMask.Effective(t).Mask; _plGrid.Mask = Masks.BgMask(t, g); } };
            q(L.T("Clear"), "BtnGhost").Click += (s, e) => _plGrid.Mask = 0;
            Grid.SetColumn(quick, 1);
            ch.Children.Add(quick);
            card.Add(ch);
            _plGrid = card.Add(new CoreGrid());
            _plGrid.Changed += () => { if (!_plLoadingGrid) _plDirty = true; PlShowInfo(); };
            card.Add(Ui.Hairline());

            var mr = new StackPanel { Orientation = Orientation.Horizontal };
            var ml = Ui.Semi(L.T("Mask"), 10, "Fg"); ml.VerticalAlignment = VerticalAlignment.Center; ml.Width = 90;
            _plHex = Ui.Box("", 180);
            _plHex.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; PlFromText(); } };
            _plHex.LostKeyboardFocus += (s, e) => PlFromText();
            _plInfo = Ui.Text("", 9.5, "Fg", false);
            _plInfo.VerticalAlignment = VerticalAlignment.Center;
            _plInfo.Margin = new Thickness(12, 0, 0, 0);
            mr.Children.Add(ml); mr.Children.Add(_plHex); mr.Children.Add(_plInfo);
            card.Add(mr);
            var hint = card.Add(Ui.Text(L.T("Hex (0xFC), LPs (2,4,6-9) or a rule (VCache+NoSMT, P+NoCore0); Enter ticks the grid. Stays in sync with the checkboxes."), 8.75, "Faint"));
            hint.Margin = new Thickness(90, 4, 0, 0);

            _plSave = Ui.Btn(L.T("Save rule"), "BtnAccent", 120); _plSave.Height = 36;
            _plSave.Click += (s, e) => PlApply();
            _plRemove = Ui.Btn(L.T("Remove rule"), "BtnOutline"); _plRemove.Height = 36;
            _plRemove.Click += (s, e) => PlRemove();
            var act = Ui.Strip(44, _plSave, _plRemove);
            act.Margin = new Thickness(0, 12, 0, 0);
            card.Add(act);

            // ---- the saved rules ----
            SubTitle(p, L.T("Per-process rules"));
            var rc = SubCard(p);
            var rnote = rc.Add(Ui.Text(L.T("A rule overrides the game mask for that process (targets), or pins any other process (voice, VPN, streaming). Click a row to edit it above."), 9, "Dim"));
            rnote.Margin = new Thickness(0, 0, 0, 8);
            _rules = rc.Add(new StackPanel());
        }

        private string PlSel() { var o = _plProc.SelectedItem; return o == null ? "" : o.ToString(); }
        private string SavedRule(string exe) { var eng = Eng; string r; return eng != null && exe.Length > 0 && eng.AffRules.TryGetValue(exe, out r) ? r : ""; }

        private bool PlIsTarget(string exe)
        {
            var eng = Eng;
            return eng != null && exe.Length > 0 && eng.Targets.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(x + ".exe", exe, StringComparison.OrdinalIgnoreCase));
        }

        private void PlRefresh()
        {
            var t = T;
            if (_plGrid == null) return;
            string sig = CoreGrid.Sig(t);
            bool rebuilt = false;
            if (t != null && sig != _plSig) { _plSig = sig; _plGrid.Build(t); rebuilt = true; }
            PlFillProcs(PlSel(), rebuilt);
            RulesRefresh();
        }

        // Refill the process list; the grid reloads from the saved rule only when the selection changed or was rebuilt.
        private void PlFillProcs(string keep, bool reload = false)
        {
            var eng = Eng;
            string before = PlSel();
            _plBuilding = true;
            try
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var pr in System.Diagnostics.Process.GetProcesses()) { using (pr) { if (pr.Id > 4) names.Add(pr.ProcessName + ".exe"); } } } catch { }
                if (eng != null)
                {
                    foreach (var k in eng.AffRules.Keys) names.Add(k);
                    foreach (var tg in eng.Targets) names.Add(tg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? tg : tg + ".exe");
                }
                // anti-cheat and protected system processes are never offered: grobyq does not change their cores
                names.RemoveWhere(n => Engine.Gate(n, Engine.Op.Affinity, Engine.ProtectedPriorityAllowed) != null);
                _plProc.Items.Clear();
                var first = names.Where(n => eng != null && (eng.AffRules.ContainsKey(n) || eng.Targets.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))).ToList();
                foreach (var n in first) _plProc.Items.Add(n);
                foreach (var n in names) if (!first.Contains(n)) _plProc.Items.Add(n);
                string pick = !string.IsNullOrEmpty(keep) ? keep : (first.Count > 0 ? first[0] : null);
                int idx = -1;
                if (pick != null) for (int i = 0; i < _plProc.Items.Count; i++) if (string.Equals(_plProc.Items[i].ToString(), pick, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                _plProc.SelectedIndex = idx >= 0 ? idx : (_plProc.Items.Count > 0 ? 0 : -1);
            }
            finally { _plBuilding = false; }
            if (reload || !string.Equals(before, PlSel(), StringComparison.OrdinalIgnoreCase)) PlLoad();
            else if (!_plDirty && SavedRule(PlSel()) != _plLoadedRule) PlLoad();
            else PlShowInfo();
        }

        private void PlLoad()
        {
            var t = T; string exe = PlSel();
            _plDirty = false; _plLoadedExe = exe; _plLoadedRule = SavedRule(exe); _plTypedSpec = ""; _plTypedMask = 0;
            if (t == null || _plGrid.IsEmpty || exe.Length == 0) { PlShowInfo(); return; }
            ulong m = PlIsTarget(exe) && Settings.GetBool("DoMask") ? GameMask.Effective(t).Mask : t.FullMask();   // no rule: what it really runs on
            string rule = SavedRule(exe);
            if (rule.Length > 0) { string err; var r = Masks.Resolve(t, rule, out err); if (r != null) m = r.Mask; }
            _plLoadingGrid = true;
            try
            {
                _plGrid.Mask = m;
                _plGrid.MarkMask = GameMask.Effective(t).Mask;
                if (PlIsTarget(exe)) _plGrid.MarkWith(GameMask.Info(t).Irq, Ui.B("Warn"));     // interrupt LPs, amber, for the game
            }
            finally { _plLoadingGrid = false; }
            PlShowInfo();
        }

        // Mask view -> Cores view: hex / LP list / portable rule, resolved on this CPU.
        private void PlFromText()
        {
            var t = T; string txt = (_plHex.Text ?? "").Trim();
            if (t == null || txt.Length == 0 || _plTexting) return;
            string err; var r = Masks.Resolve(t, txt, out err);
            if (r == null) { _plInfo.Foreground = Ui.B("Warn"); _plInfo.Text = L.F("Not a mask for this CPU: {0}", err ?? txt); return; }
            _plTexting = true;
            try { _plGrid.Mask = r.Mask; } finally { _plTexting = false; }
            _plTypedSpec = r.Portable ? r.Spec : ""; _plTypedMask = r.Mask;      // a portable rule is stored as the rule
            PlShowInfo();
        }

        private void PlShowInfo()
        {
            var t = T; string exe = PlSel(); var eng = Eng;
            bool target = PlIsTarget(exe), hasRule = SavedRule(exe).Length > 0;
            _plRemove.IsEnabled = hasRule;
            _plRemove.Content = target ? L.T("Use the game mask") : L.T("Remove rule");     // for the game, no rule = the game core mask
            _plSave.IsEnabled = exe.Length > 0;
            if (t == null || exe.Length == 0) { _plInfo.Text = ""; return; }
            ulong m = _plGrid.Mask, all = t.FullMask();
            if (m == 0) { _plInfo.Foreground = Ui.B("Warn"); _plInfo.Text = L.T("No core ticked - a process needs at least one."); return; }
            ulong off = all & ~m;
            if (!_plHex.IsKeyboardFocused && !_plTexting) _plHex.Text = _plTypedSpec.Length > 0 && _plTypedMask == m ? _plTypedSpec : Masks.Hex(m);
            _plInfo.Foreground = Ui.B("Fg");
            string state;
            if (target && !hasRule && !_plDirty) state = L.T("no own rule: runs on the game mask (adaptive)");
            else
            {
                state = hasRule && !_plDirty ? L.T("rule saved") : L.T("not saved yet");
                ulong idr = 0; if (target) GameMask.ForRule(t, m, out idr);
                if (idr != 0) state += "   ·   " + L.F("interrupts take LP {0} off it", Masks.Ranges(idr));
            }
            _plInfo.Text = L.F("{0}: LP {1}", exe, Masks.Ranges(m)) + (off != 0 ? "   ·   " + L.F("off: LP {0}", Masks.Ranges(off)) : "   ·   " + L.T("all cores")) + "   ·   " + state;
        }

        private void PlApply()
        {
            var t = T; string exe = PlSel(); var eng = Eng;
            if (t == null || exe.Length == 0 || eng == null) return;
            ulong m = _plGrid.Mask;
            if (m == 0) { Dlg.Info(null, L.T("Tick at least one core.")); return; }
            if (m == t.FullMask()) { PlRemove(); return; }        // all cores = no rule needed
            string stored = _plTypedSpec.Length > 0 && _plTypedMask == m ? _plTypedSpec : Masks.Hex(m);
            eng.AffRules[exe] = stored;
            Settings.SaveAffRules(eng.AffRules);
            _plDirty = false; _plLoadedExe = exe; _plLoadedRule = stored;
            AppLog.Add(string.Format("[AF] process affinity rule saved: {0} -> {1} = {2} LP {3}", exe, stored, Masks.Hex(m), Masks.Ranges(m)));
            var o = Optimizer.Instance;
            bool target = PlIsTarget(exe);
            if (o != null && o.Active && (target ? Settings.GetBool("DoMask") : Settings.GetBool("DoAffRules"))) o.ApplyNow();
            else AppLog.Add("[i] the rule is applied while the optimizer runs with " + (target ? "'Apply the game core mask'" : "'Apply per-process rules'") + " on");
            RulesRefresh(); PlShowInfo(); CoresShow(false);
        }

        private void PlRemove()
        {
            string exe = PlSel(); var eng = Eng;
            if (exe.Length == 0 || eng == null) return;
            if (eng.AffRules.Remove(exe))
            {
                Settings.SaveAffRules(eng.AffRules);
                AppLog.Add("[AF] process affinity rule removed: " + exe);
                AppLog.Add(eng.ReleaseAffinity(exe));            // the running process gets its original cores back now
                var o = Optimizer.Instance;
                if (o != null && o.Active) o.ApplyNow();         // ...then the live layer (game mask / background) again
            }
            RulesRefresh(); PlLoad(); CoresShow(false);
        }

        // ------------------------------------------------------------------ the rule list
        private void RulesRefresh()
        {
            var t = T; var eng = Eng;
            if (_rules == null) return;
            _rules.Children.Clear();
            var head = RuleRow(L.T("Process"), L.T("Rule"), L.T("On this PC"), L.T("Logical processors"), L.T("State"), "Faint", true);
            _rules.Children.Add(head);
            if (eng == null || eng.AffRules.Count == 0)
            {
                var none = Ui.Text(L.T("No rules yet: pick a process above, tick its cores and press Save rule."), 9, "Faint");
                none.Margin = new Thickness(0, 8, 0, 4);
                _rules.Children.Add(none);
                return;
            }
            foreach (var k in eng.AffRules.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList())
            {
                string rule = eng.AffRules[k], err = "";
                MaskResult mr = t != null ? Masks.Resolve(t, rule, out err) : null;
                int run = Optimizer.ByNameCount(k);
                string state = run > 0 ? L.T("running") : "-";
                string color = run > 0 ? "Ok" : "Fg";
                if (mr == null) { state = L.T("invalid"); color = "Warn"; }
                else if (!mr.Satisfied) { state += " / " + L.T("fallback"); color = "Warn"; }
                else if (!mr.Portable) state += " / " + L.T("fixed LPs");
                var row = RuleRow(k, rule, mr != null ? mr.Hex : "-", mr != null ? mr.LPs : err, state, color, false);
                string exe = k;
                row.MouseLeftButtonUp += (s, e) =>
                {
                    for (int i = 0; i < _plProc.Items.Count; i++)
                        if (string.Equals(_plProc.Items[i].ToString(), exe, StringComparison.OrdinalIgnoreCase)) { _plProc.SelectedIndex = i; break; }
                    Scroll.ScrollToTop();
                };
                _rules.Children.Add(row);
            }
        }

        private static Border RuleRow(string a, string b, string c, string d, string e, string color, bool head)
        {
            var g = new Grid { MinHeight = head ? 28 : 32 };
            foreach (var w in new[] { 26.0, 22.0, 14.0, 24.0, 14.0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
            var cells = new[] { a, b, c, d, e };
            for (int i = 0; i < cells.Length; i++)
            {
                var tb = head ? Ui.Semi(cells[i], 8.5, "Faint") : Ui.Text(cells[i], 9.5, i == 4 || i == 0 ? color : "Fg", false);
                tb.VerticalAlignment = VerticalAlignment.Center;
                tb.Margin = new Thickness(6, 0, 6, 0);
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
            }
            var row = new Border
            {
                Child = g,
                BorderBrush = Ui.B("Line"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = head ? Cursors.Arrow : Cursors.Hand,
                SnapsToDevicePixels = true
            };
            if (!head)
            {
                row.MouseEnter += (s, ev) => row.Background = Ui.B("CardHi");
                row.MouseLeave += (s, ev) => row.Background = System.Windows.Media.Brushes.Transparent;
            }
            return row;
        }
    }
}
