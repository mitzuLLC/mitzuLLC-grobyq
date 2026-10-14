using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Grobyq.Pages
{
    // Cores tab: the game core mask (switch, base rule, custom rule, per-LP grid, interrupt cores, result), this CPU with
    // the A/B steps for it, and the CPU map. Every edit here is part of the page draft (Save / Cancel).
    public sealed partial class AffinityPage
    {
        private CheckBox _swMask;
        private ComboBox _base, _irqMode;
        private TextBlock _baseDesc, _customInfo, _irqInfo, _sum, _detail, _ruleWarn, _mapInfo;
        private TextBox _custom;
        private CoreGrid _grid;
        private Button _reset, _ruleBtn;
        private readonly List<MaskOption> _baseOpts = new List<MaskOption>();
        private string _gridSig = "";
        private ulong _locked;
        private InfoGrid _cpuInfo;
        private StackPanel _advice;
        private CpuMap _map;

        private void BuildCores(StackPanel p)
        {
            // ---- the game core mask ----
            SubTitle(p, L.T("Game core mask"), true);
            var card = SubCard(p);
            _swMask = Ui.Switch(_pDoMask);
            _swMask.Click += (s, e) => { _pDoMask = _swMask.IsChecked == true; CoresShow(); UpdateSaveBar(null); };
            card.Add(Ui.Row(L.T("Apply the game core mask"), L.T("Off = Windows decides where the game runs (stock). On = while the game threads optimizer runs, the game gets the cores below; switching it off gives the game its original cores back."), _swMask));
            var intro = card.Add(Ui.Text(L.T("Pick a base rule for this CPU, then untick a core or a single thread the game should not use, or tick one back, and press Save. Cores that device interrupts are pinned to can leave the mask by themselves."), 9, "Dim"));
            intro.Margin = new Thickness(0, 10, 0, 4);

            // 1. base rule
            var br = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var bl = Ui.Semi(L.T("Base"), 10, "Fg"); bl.VerticalAlignment = VerticalAlignment.Center; bl.Width = 120;
            _base = Combo.New(320);
            _base.SelectionChanged += (s, e) =>
            {
                if (_loading || _base.SelectedIndex < 0 || _base.SelectedIndex >= _baseOpts.Count) return;
                SetPendingBase(_baseOpts[_base.SelectedIndex].Key);
            };
            br.Children.Add(bl); br.Children.Add(_base);
            card.Add(br);
            _baseDesc = card.Add(Ui.Text("", 8.75, "Faint"));
            _baseDesc.Margin = new Thickness(120, 4, 0, 0);

            // custom rule (VCache+NoSMT, P+NoCore0, 0x5554 ...)
            var cr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var cl = Ui.Semi(L.T("Custom rule"), 10, "Fg"); cl.VerticalAlignment = VerticalAlignment.Center; cl.Width = 120;
            _custom = Ui.Box("", 220);
            var use = Ui.Btn(L.T("Use"), "BtnOutline", 80);
            use.Margin = new Thickness(8, 0, 0, 0);
            use.Click += (s, e) => UseCustom();
            _custom.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; UseCustom(); } };
            cr.Children.Add(cl); cr.Children.Add(_custom); cr.Children.Add(use);
            card.Add(cr);
            _customInfo = card.Add(Ui.Text(Explain.MaskGrammar, 8.75, "Faint"));
            _customInfo.Margin = new Thickness(120, 4, 0, 0);
            card.Add(Ui.Hairline());

            // 2. the grid
            var gh = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            legend.Children.Add(Ui.Semi(L.T("Cores for the game"), 10, "Fg"));
            var dot = Ui.Text("●", 9, "Warn", false); dot.Margin = new Thickness(10, 0, 4, 0); legend.Children.Add(dot);
            legend.Children.Add(Ui.Text(L.T("= device interrupts are pinned here"), 9, "Dim", false));
            gh.Children.Add(legend);
            _reset = Ui.Btn(L.T("Back to the base"), "BtnOutline");
            _reset.Height = 30;
            _reset.Click += (s, e) =>
            {
                var t = T; if (t == null) return;
                _pManual = Masks.Build(t, _pBase).Mask;
                CoresShow(true);
                UpdateSaveBar(null);
            };
            Grid.SetColumn(_reset, 1);
            gh.Children.Add(_reset);
            card.Add(gh);
            _grid = card.Add(new CoreGrid());
            _grid.Changed += () =>
            {
                if (_loading) return;
                _pManual = _grid.Mask | (_pManual & _locked);
                CoresShow(false);
                UpdateSaveBar(null);
            };

            // 3. interrupt LPs
            var ir = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var il = Ui.Semi(L.T("Interrupt cores"), 10, "Fg"); il.VerticalAlignment = VerticalAlignment.Center; il.Width = 120;
            _irqMode = Combo.New(260, new[]
            {
                new Opt(GameMask.IrqThread, L.T("take that thread off")),
                new Opt(GameMask.IrqCore, L.T("take the whole core off")),
                new Opt(GameMask.IrqOff, L.T("keep them in the mask")),
            });
            _irqMode.SelectionChanged += (s, e) =>
            {
                if (_loading) return;
                _pMode = Combo.Val(_irqMode);
                CoresShow(true);
                UpdateSaveBar(null);
            };
            _irqInfo = Ui.Text("", 9, "Dim", false);
            _irqInfo.VerticalAlignment = VerticalAlignment.Center;
            _irqInfo.Margin = new Thickness(12, 0, 0, 0);
            ir.Children.Add(il); ir.Children.Add(_irqMode); ir.Children.Add(_irqInfo);
            card.Add(ir);
            var irqNote = card.Add(Ui.Text(L.T("Follows the interrupt settings saved on the Interrupts tab (Specified processors, one core per device). The device itself moves after Restart device or a reboot. Interrupt cores never take more than half of the game's cores."), 8.75, "Faint"));
            irqNote.Margin = new Thickness(120, 4, 0, 0);
            card.Add(Ui.Hairline());

            // 4. result
            _sum = card.Add(Ui.Text("", 10, "Fg"));
            _detail = card.Add(Ui.Text("", 9, "Dim"));
            _detail.Margin = new Thickness(0, 3, 0, 0);
            _ruleWarn = card.Add(Ui.Text("", 9, "Warn"));
            _ruleWarn.Margin = new Thickness(0, 6, 0, 0);
            _ruleBtn = Ui.Btn(L.T("Use the game mask for it"), "BtnOutline");
            _ruleBtn.Height = 30; _ruleBtn.HorizontalAlignment = HorizontalAlignment.Left; _ruleBtn.Margin = new Thickness(0, 6, 0, 0);
            _ruleBtn.Click += (s, e) => DropTargetRules();
            card.Add(_ruleBtn);

            // ---- this CPU ----
            SubTitle(p, L.T("Your processor"));
            var cc = SubCard(p);
            _cpuInfo = cc.Add(new InfoGrid { KeyWidth = 200 });
            var adv = cc.Add(Ui.Text(L.T("What to try on this CPU, in this order. Each step is an A/B test against All cores: keep a narrower mask only if frame-time (1% / 0.1% lows) and DPC/ISR per core get better in your own measurement."), 9, "Dim"));
            adv.Margin = new Thickness(0, 10, 0, 4);
            _advice = cc.Add(new StackPanel());

            // ---- the map ----
            SubTitle(p, L.T("CPU map"));
            var mc = SubCard(p);
            _mapInfo = mc.Add(Ui.Text("", 9.5, "Fg"));
            _mapInfo.Margin = new Thickness(0, 0, 0, 8);
            _map = mc.Add(new CpuMap());
        }

        // A new base (combo, advice, custom rule): the grid starts from that base again (nothing is saved yet).
        private void SetPendingBase(string key)
        {
            var t = T; if (t == null) return;
            _pBase = key ?? "";
            _pManual = Masks.Build(t, _pBase).Mask;
            CoresRefresh(false);
            UpdateSaveBar(null);
        }

        private void UseCustom()
        {
            var t = T; if (t == null) return;
            string txt = (_custom.Text ?? "").Trim();
            if (txt.Length == 0) return;
            string err; var r = Masks.Resolve(t, txt, out err);
            if (r == null) { _customInfo.Foreground = Ui.B("Warn"); _customInfo.Text = "'" + txt + "' " + err; return; }
            _customInfo.Foreground = r.Satisfied ? Ui.B("Faint") : Ui.B("Warn");
            _customInfo.Text = string.Format("{0} -> {1}  ({2} LP: {3}){4}{5}", r.Spec, r.Hex, r.Count, r.LPs,
                r.Portable ? "" : "  ·  " + L.T("fixed LPs (not portable)"), r.Notes.Count > 0 ? "\n" + string.Join("\n", r.Notes) : "");
            SetPendingBase(r.Portable ? r.Spec : r.Hex);
        }

        // Rebuilds the base list and the grid from the draft. reloadGrid = the ticks come from the draft again.
        private void CoresRefresh(bool reloadGrid)
        {
            var t = T;
            if (_base == null) return;
            _loading = true;
            try
            {
                _swMask.IsChecked = _pDoMask;
                _baseOpts.Clear();
                if (t != null) _baseOpts.AddRange(Masks.Options(t)); else foreach (var n in Masks.Names) _baseOpts.Add(new MaskOption { Key = n, Label = n });
                if (_pBase.Length > 0 && !_baseOpts.Any(o => string.Equals(o.Key, _pBase, StringComparison.OrdinalIgnoreCase)))
                    _baseOpts.Add(new MaskOption { Key = _pBase, Label = L.T("custom") + ": " + _pBase });
                _base.Items.Clear();
                foreach (var o in _baseOpts) _base.Items.Add(new Opt(o.Key, Explain.MaskLabel(o)));
                int idx = _baseOpts.FindIndex(o => string.Equals(o.Key, _pBase, StringComparison.OrdinalIgnoreCase));
                _base.SelectedIndex = idx < 0 ? 0 : idx;
                _baseDesc.Text = Explain.Mask(_pBase, t);
                Combo.Select(_irqMode, _pMode);
            }
            finally { _loading = false; }
            CoresShow(true);
            CpuRefresh();
        }

        // The grid, the locked interrupt LPs and the result line for the draft as it is now.
        private void CoresShow(bool reloadGrid = false)
        {
            var t = T;
            if (_grid == null) return;
            if (t == null) { _sum.Text = L.T("CPU topology unavailable (GetSystemCpuSetInformation)."); return; }
            string sig = CoreGrid.Sig(t);
            if (sig != _gridSig) { _gridSig = sig; _grid.Build(t); reloadGrid = true; }

            var si = GameMask.Info(t);                                    // interrupt pins (registry), saved state
            ulong baseM = Masks.Build(t, _pBase).Mask, add, drop, irqDrop = 0, fin = 0;
            bool ign = false;
            GameMask.EditsFrom(baseM, _pManual, out add, out drop);
            bool none = (_pManual & t.FullMask()) == 0;
            if (!none) fin = GameMask.Adjust(t, baseM, add, drop, si.Irq, _pMode, out irqDrop, out ign);
            _locked = irqDrop;

            _loading = true;
            try
            {
                if (reloadGrid) _grid.Load(_pManual & ~_locked);
                _grid.MarkMask = 0;
                _grid.MarkWith(si.Irq, Ui.B("Warn"));
                _grid.LockedMask = _locked;
                foreach (var s in t.Sets.Where(x => x.Group == 0))
                {
                    int lp = s.LP;
                    var who = si.IrqDevices.Where(d => ((d.Value >> lp) & 1) != 0).Select(d => d.Key).ToList();
                    string tip = who.Count == 0 ? "" : L.F("CPU {0} - device interrupts: {1}", lp, string.Join(", ", who)) +
                                 (((_locked >> lp) & 1) != 0 ? "\n" + L.T("Taken off the game mask automatically (Interrupt cores).") : "");
                    _grid.TipFor(lp, tip);
                }
            }
            finally { _loading = false; }

            ulong reserved = Reservation.Reserved(), kept = Reservation.Unmix(fin, reserved, false), all = t.FullMask();
            int n = CountBits(kept), total = CountBits(all);
            bool dirty = DirtyCount() > 0;
            if (none) { _sum.Foreground = Ui.B("Warn"); _sum.Text = L.T("No core ticked - the game needs at least one."); }
            else
            {
                _sum.Foreground = _pDoMask ? Ui.B("Fg") : Ui.B("Dim");
                _sum.Text = L.F("The game gets LP {0}  ({1} of {2})", Masks.Ranges(kept), n, total)
                          + (!_pDoMask ? "   ·   " + L.T("not applied: the switch above is off") : dirty ? "   ·   " + L.T("not saved yet") : "");
            }
            var parts = new List<string>();
            var opt = _baseOpts.FirstOrDefault(o => string.Equals(o.Key, _pBase, StringComparison.OrdinalIgnoreCase)) ?? new MaskOption { Key = _pBase, Label = _pBase };
            parts.Add(L.F("base {0}: LP {1}", Explain.MaskLabel(opt), Masks.Ranges(baseM)));
            parts.Add(drop != 0 ? L.F("you took off LP {0}", Masks.Ranges(drop)) : L.T("nothing taken off by hand"));
            if (add != 0) parts.Add(L.F("you added LP {0}", Masks.Ranges(add)));
            if (irqDrop != 0) parts.Add(L.F("interrupts took LP {0}", Masks.Ranges(irqDrop)));
            else if (ign) parts.Add(L.T("interrupt LPs kept: taking them would leave the game less than half of its cores"));
            if (fin != kept) parts.Add(L.F("reserved LP {0} left out (ReservedCpuSets)", Masks.Ranges(fin & ~kept)));
            if (si.EditsStale) parts.Add(L.T("saved grid edits were made on another CPU layout and are ignored"));
            _detail.Text = string.Join("   ·   ", parts);

            var irqParts = si.IrqDevices.Select(d => d.Key + " → LP " + Masks.Ranges(d.Value)).ToList();
            if (si.IrqWide.Count > 0) irqParts.Add(L.F("not counted (wider than one core): {0}", string.Join(", ", si.IrqWide.Select(d => d.Key + " → LP " + Masks.Ranges(d.Value)))));
            if (irqParts.Count == 0) { _irqInfo.Foreground = Ui.B("Faint"); _irqInfo.Text = L.T("no device has pinned interrupts"); }
            else { _irqInfo.Foreground = ign ? Ui.B("Warn") : Ui.B("Dim"); _irqInfo.Text = string.Join("   ·   ", irqParts); }

            // a per-process rule for a target wins over this mask: say so
            var eng = Eng;
            var ruled = eng == null ? new List<string>() : eng.Targets.Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x : x + ".exe")
                                                              .Where(x => eng.AffRules.ContainsKey(x)).ToList();
            _ruleWarn.Text = ruled.Count == 0 ? "" : L.F("{0} has its own rule on the Processes tab ({1}): for it the rule replaces the base and your ticks here; interrupt cores still leave it.",
                                                          string.Join(", ", ruled), string.Join(", ", ruled.Select(x => eng.AffRules[x])));
            _ruleWarn.Visibility = ruled.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _ruleBtn.Visibility = ruled.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _reset.IsEnabled = _pManual != baseM;
            _grid.SetEnabled(true);

            MapRefresh(fin, t);
        }

        // targets back on the game mask: their own rules go, the running game gets its captured cores back, then the live layer
        private void DropTargetRules()
        {
            var eng = Eng; if (eng == null) return;
            var ruled = eng.Targets.Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x : x + ".exe").Where(x => eng.AffRules.ContainsKey(x)).ToList();
            if (ruled.Count == 0) return;
            foreach (var exe in ruled)
            {
                eng.AffRules.Remove(exe);
                AppLog.Add("[AF] process affinity rule removed: " + exe + " (back on the game mask)");
                AppLog.Add(eng.ReleaseAffinity(exe));
            }
            Settings.SaveAffRules(eng.AffRules);
            var o = Optimizer.Instance;
            if (o != null && o.Active) o.ApplyNow();
            CoresShow(false);
            PlRefresh();
        }

        // ------------------------------------------------------------------ this CPU
        private static string CpuName()
        {
            var s = Shell.Sys;
            if (s != null && s.Cpu.Length > 0) return s.Cpu;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                    if (k != null) return Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
            }
            catch { }
            return "";
        }

        private void CpuRefresh()
        {
            var t = T;
            if (_cpuInfo == null) return;
            var rows = new List<KeyValuePair<string, string>>();
            rows.Add(KV(L.T("Model"), CpuName()));
            if (t != null)
            {
                rows.Add(KV(L.T("Cores / threads"), L.F("{0} physical cores · {1} logical processors · SMT {2}", t.PhysicalCount, t.LogicalCount, t.SMT ? L.T("on") : L.T("off"))));
                if (t.Hybrid)
                {
                    byte top = t.Sets.Max(s => s.Eff);
                    int pc = t.Sets.Where(s => s.Eff == top).Select(s => s.Core).Distinct().Count();
                    int ec = t.Sets.Where(s => s.Eff != top).Select(s => s.Core).Distinct().Count();
                    rows.Add(KV(L.T("Core types"), L.F("hybrid: {0} P-cores + {1} E-cores", pc, ec)));
                }
                rows.Add(KV(L.T("L3 cache"), string.Join("   ·   ", t.LLCGroups.Select((l, i) => (t.LLCGroups.Count > 1 ? L.F("domain {0}", i) + ": " : "") + t.L3Text(l) + (t.VCacheLlc.HasValue && t.VCacheLlc.Value == l ? " (3D V-Cache)" : "")))));
                rows.Add(KV(L.T("Class"), CpuClass(t)));
                var so = Masks.Options(t).FirstOrDefault(o => string.Equals(o.Key, Settings.Get("Mask"), StringComparison.OrdinalIgnoreCase)) ?? new MaskOption { Label = Settings.Get("Mask") };
                var gi = GameMask.Info(t);
                rows.Add(KV(L.T("Mask in use"), (Settings.GetBool("DoMask") ? Explain.MaskLabel(so) + (gi.Adjusted ? "   ·   " + L.F("adapted: LP {0}", Masks.Ranges(gi.Final)) : "") : L.T("not applied (Windows decides)"))));
            }
            _cpuInfo.Set(rows);

            _advice.Children.Clear();
            if (t == null) return;
            int n = 1;
            foreach (var a in Advice(t))
            {
                var r = Masks.Build(t, a.Key);
                bool inUse = string.Equals(a.Key, _pBase, StringComparison.OrdinalIgnoreCase) || string.Equals(Masks.ToSpec(a.Key), Masks.ToSpec(_pBase), StringComparison.OrdinalIgnoreCase);
                var b = Ui.Btn(inUse ? L.T("In use") : L.T("Use"), inUse ? "BtnAccent" : "BtnNormal", 90);
                string key = a.Key;
                b.Click += (s, e) => Dispatcher.BeginInvoke((Action)(() => SetPendingBase(key)));   // rebuilds this list: never inside the click
                string label = Explain.MaskLabel(Masks.Options(t).FirstOrDefault(o => o.Key == a.Key) ?? new MaskOption { Label = a.Key });
                _advice.Children.Add(Ui.Row(L.F("Step {0}:", n) + "  " + label + "      " + r.Count + " LP  ·  " + Masks.LpsShort(r.Mask), a.Value, b));
                n++;
            }
        }

        private static string CpuClass(Topo t)
        {
            if (t.VCacheLlc.HasValue && t.LLCGroups.Count > 1) return L.T("dual-CCD with 3D V-Cache on one CCD (e.g. 7950X3D / 9950X3D)");
            if (t.VCacheLlc.HasValue) return L.T("3D V-Cache");
            if (t.LLCGroups.Count > 1) return L.T("several L3 domains (multi-CCD / CCX)");
            if (t.Hybrid) return L.T("Intel hybrid (P-cores + E-cores)");
            if (t.PhysicalCount >= 8) return L.T("single L3, 8+ cores");
            if (t.PhysicalCount >= 6) return L.T("single L3, 6-7 cores");
            return L.T("4 cores or fewer");
        }

        private static List<KeyValuePair<string, string>> Advice(Topo t)
        {
            var have = new HashSet<string>(Masks.Options(t).Select(o => o.Key), StringComparer.OrdinalIgnoreCase);
            var list = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, why) => { if (have.Contains(k) && !list.Any(x => x.Key == k || Masks.Build(t, x.Key).Mask == Masks.Build(t, k).Mask)) list.Add(new KeyValuePair<string, string>(k, why)); };
            if (t.Split.CanSplit)
                add("GameSet", t.Split.Family == "A"
                    ? L.T("The whole V-Cache CCD with both SMT threads of every core; Windows and background keep the other CCD. The grobyq default on a verified dual-CCD X3D.")
                    : L.T("Every P-core with both SMT threads; E-cores stay with Windows and background."));
            if (t.VCacheLlc.HasValue && t.LLCGroups.Count > 1)
            {
                add("VCache", L.T("CS2 is cache-sensitive: keep it on the CCD with the big L3 so its threads never cross to the other CCD."));
                add("VCache+NoSMT", L.T("Same CCD, one thread per core: less sharing inside the core, fewer logical processors."));
                add("AllCores", L.T("Baseline: Windows / the AMD driver decide. Compare every step with this."));
                add("Freq", L.T("The other CCD: higher clocks, smaller cache. Rarely better for CS2, worth one test."));
            }
            else if (t.LLCGroups.Count > 1)
            {
                add("CCD0", L.T("One L3 domain: the game's threads share one cache and avoid cross-CCD latency."));
                add("CCD0+NoSMT", L.T("Same domain, one thread per core."));
                add("AllCores", L.T("Baseline: compare every step with this."));
            }
            else if (t.Hybrid)
            {
                add("P", L.T("Only P-cores: the game never lands on the slower E-cores; background uses the E-cores."));
                add("P+NoCore0", L.T("P-cores without the first core, where Windows puts many interrupts."));
                add("AllCores", L.T("Baseline: Thread Director decides. Compare with it."));
            }
            else if (t.PhysicalCount >= 8)
            {
                // baseline first (v4.0): CPU 0 stays with the game; taking it away is an A/B step, not the default
                add("AllCores", L.T("Baseline: compare every step with this."));
                add("NoCpu0", L.T("Leaves the first core to interrupts and Windows; the game keeps 14+ logical processors."));
                add("SmtOffNoCpu0", L.T("Also one thread per core: most isolation. Only if it measurably helps on your PC."));
            }
            else if (t.PhysicalCount >= 6)
            {
                add("AllCores", L.T("Baseline. With 6 cores the difference is small; keep whichever measures better."));
                add("NoCpu0", L.T("6-7 cores: leave the first core to interrupts and Windows, keep SMT for the game's worker threads."));
            }
            else add("AllCores", L.T("4 cores or fewer: every narrower mask takes more than it gives. Keep all cores."));
            return list;
        }

        // ------------------------------------------------------------------ map
        private void MapRefresh(ulong game, Topo t)
        {
            if (_map == null) return;
            if (t == null) { _mapInfo.Text = L.T("CPU topology unavailable (GetSystemCpuSetInformation)."); return; }
            ulong manualMask = (_pManual & t.FullMask()) != 0 ? _pManual : Masks.Build(t, _pBase).Mask;
            ulong bg = Masks.BgMask(t, manualMask);
            _map.Set(t, game, bg);
            _map.Overlay(t.Split.CanSplit ? t.Split.Game : 0, t.Split.CanSplit ? t.Split.NonGame : 0, Reservation.Reserved());
            _mapInfo.Text = L.F("Game mask {0} = {1}  ({2} LP: {3})   ·   background {4}", _pBase, Masks.Hex(game), CountBits(game), Masks.Ranges(game), Masks.Hex(bg))
                          + (_pDoMask ? "" : "   ·   " + L.T("not applied: the switch above is off"));
        }
    }
}
