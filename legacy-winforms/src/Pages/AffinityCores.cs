using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobyq
{
    // Core / affinity sections of the Affinity page: the active game mask picker, the CCD split, this CPU +
    // A/B advice, the CPU map, all masks for this CPU, a custom rule and per-process affinity rules.
    // Moved here from the Threads page (v3.2 tab split): Threads owns thread priority, Affinity owns cores.
    internal sealed partial class AffinityPage
    {
        private DropBox _mask;
        private readonly List<MaskOption> _maskOpts = new List<MaskOption>();
        private bool _syncing;

        private StackBox _split;
        private InfoGrid _cpuInfo;
        private StackBox _advice, _opts;
        private CpuMap _map;
        private Label _mapInfo;
        private TextBox _custom;
        private Label _customInfo;
        private DataGridView _affRules;
        private TextBox _rName, _rRule;
        private DropBox _rQuick;
        private bool _affBuilding;

        private static Topo T { get { return ThreadsPage.SafeTopo(); } }

        // Cores tab: the game mask, layout templates, the CCD split, this CPU + map, all masks, a custom rule.
        private void BuildCoresTab()
        {
            BuildMaskPicker();
            BuildTemplates();
            BuildSplit();
            BuildCpu();
            BuildMaskList();
            BuildCustom();
        }

        // Processes tab: the mini Process Lasso and the per-process rule table.
        private void BuildProcessesTab()
        {
            BuildProcessLasso();
            BuildAffRules();
        }

        private void RefreshCoreAffinity()
        {
            FillMask();
            FillTemplates();
            PlRefresh();
            RefreshSplit();
            RefreshCpu();
            RefreshMap();
            FillMaskOptions();
            RefreshAffRules();
        }

        // ------------------------------------------------------------------ active game mask (3.6.5: adaptive)
        // One card: the base rule, a grid of every LP (ticked = the game may run there) where a core or a single thread
        // is taken off or added back, what to do with interrupt LPs, and the result line. Grid ticks are a pending edit
        // until Apply (like the Processes tab); the base and the interrupt mode apply at once.
        private DropBox _gmIrq;
        private TextBlock _maskDesc, _gmSum, _gmDetail, _gmRuleWarn;
        private CoreGrid _gmGrid;
        private WrapRow _gmRuleRow;
        private Label _gmIrqInfo;
        private Btn _gmApply, _gmCancel, _gmReset;
        private bool _gmLoading, _gmDirty;
        private string _gmSig = "", _gmBaseRule = "";
        // the manual layer the grid stands for (ticks + the locked interrupt LPs as the user left them) and the LPs locked
        // when the grid was loaded: pending ticks survive a pin change or a mode switch without inventing drops
        private ulong _gmPending, _gmLocked;

        private void BuildMaskPicker()
        {
            Title(L.T("Game core mask"));
            var card = NewCard();
            card.Padding = new Padding(Theme.D(20), Theme.D(10), Theme.D(20), Theme.D(14));
            card.Stack(new TextBlock(L.T("The cores cs2 (and every target) runs on. Pick a base rule for this CPU, then untick a core or a single thread the game should not use, or tick one back, and press Apply. Cores that device interrupts are pinned to can leave the mask by themselves."), Theme.Dim, 9f));

            // 1. base rule
            var br = card.Stack(new LineRow(Theme.D(48)));
            br.L(Theme.Lbl(L.T("Base"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            _mask = br.L(Theme.Combo(Theme.D(300)));
            _maskDesc = card.Stack(new TextBlock("", Theme.Faint, 8.75f));
            card.Stack(new Hairline());

            // 2. the grid
            var ch = card.Stack(new LineRow(Theme.D(40)));
            ch.Gap = 6;
            ch.L(Theme.Lbl(L.T("Cores for the game"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            ch.L(Theme.Lbl("●", Theme.F(9f), Theme.Warn)).BackColor = Theme.Card;
            ch.L(Theme.Lbl(L.T("= device interrupts are pinned here"), Theme.F(9f), Theme.Dim)).BackColor = Theme.Card;
            _gmReset = ch.R(new Btn(L.T("Back to the base"), Btn.Style.Outline) { Height = Theme.D(30), DisabledOutline = true });
            card.Stack(new Spacer(2));
            _gmGrid = card.Stack(new CoreGrid());
            card.Stack(new Spacer(4));

            // 3. interrupt LPs
            var ir = card.Stack(new LineRow(Theme.D(44)));
            ir.L(Theme.Lbl(L.T("Interrupt cores"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            _gmIrq = ir.L(Theme.Combo(Theme.D(250), new Opt(GameMask.IrqThread, L.T("take that thread off")),
                                                    new Opt(GameMask.IrqCore, L.T("take the whole core off")),
                                                    new Opt(GameMask.IrqOff, L.T("keep them in the mask"))));
            _gmIrqInfo = ir.L(new Label { AutoSize = false, Height = Theme.D(24), Font = Theme.F(9f), ForeColor = Theme.Dim, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true });
            ir.Fill = _gmIrqInfo;
            card.Stack(new TextBlock(L.T("Follows the interrupt settings saved on the Interrupts tab (Specified processors, one core per device). The device itself moves after Restart device or a reboot. Interrupt cores never take more than half of the game's cores."), Theme.Faint, 8.75f));
            card.Stack(new Hairline());

            // 4. result + actions
            _gmSum = card.Stack(new TextBlock("", Theme.Fg, 10f));
            _gmDetail = card.Stack(new TextBlock("", Theme.Dim, 9f));
            _gmRuleWarn = card.Stack(new TextBlock("", Theme.Warn, 9f));
            _gmRuleRow = card.Stack(new WrapRow());
            _gmRuleRow.Add(new Btn(L.T("Use the game mask for it"), Btn.Style.Outline) { Height = Theme.D(30) }).Click += (s, e) => GmDropTargetRules();
            card.Stack(new Spacer(8));
            var act = card.Stack(new WrapRow());
            _gmApply = act.Add(new Btn(L.T("Apply"), Btn.Style.Accent, Theme.D(120)) { Height = Theme.D(36), DisabledOutline = true });
            _gmCancel = act.Add(new Btn(L.T("Cancel"), Btn.Style.Outline) { Height = Theme.D(36), DisabledOutline = true });

            FillMask();

            _mask.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || _mask.SelectedIndex < 0) return;
                string key = _maskOpts[_mask.SelectedIndex].Key;
                if (string.Equals(key, Settings.Get("Mask"), StringComparison.OrdinalIgnoreCase)) return;
                ulong add = GameMask.SavedAdd, drop = GameMask.SavedDrop;
                if ((add != 0 || drop != 0 || _gmDirty) &&
                    !Dlg.Ask(this, L.T("Change the base rule? Your ticks in the grid were made against the current base and are cleared.")))
                { FillMask(); return; }
                GameMask.SetBase(key);
                _gmDirty = false;
                M.Log("[AF] game core mask base = " + key + (add != 0 || drop != 0 ? "  (grid edits cleared)" : ""));
                _syncing = true; try { M.NotifyMask(); } finally { _syncing = false; }
                if (M.Active) M.ApplyNow();
                FillMask();
            };
            _gmIrq.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing) return;
                string v = Theme.Val(_gmIrq);
                if (v == GameMask.Mode) return;
                GameMask.SetMode(v);
                M.Log("[AF] interrupt cores in the game mask: " + v);
                _syncing = true; try { M.NotifyMask(); } finally { _syncing = false; }
                if (M.Active) M.ApplyNow();
                FillMask();
            };
            _gmGrid.Changed += () =>
            {
                if (_gmLoading) return;
                _gmPending = _gmGrid.Mask | (_gmPending & _gmLocked);
                var t0 = T; _gmDirty = t0 == null || _gmPending != GameMask.Info(t0, false, null).Manual;   // back to the saved ticks = nothing pending
                GmShow();
            };
            _gmApply.Click += (s, e) => GmApply();
            _gmCancel.Click += (s, e) => { _gmDirty = false; FillMask(); };
            _gmReset.Click += (s, e) =>
            {
                var t = T; if (t == null) return;
                var i = GameMask.Info(t, false, null);
                _gmPending = i.Base;                                   // every edit gone, locked LPs included
                _gmLoading = true;
                try { _gmGrid.Mask = _gmPending & ~_gmLocked; } finally { _gmLoading = false; }
                _gmDirty = _gmPending != i.Manual;
                GmShow();
            };
        }

        private void FillMask()
        {
            if (_mask == null) return;
            _syncing = true;
            try
            {
                string saved = Settings.Get("Mask");
                _maskOpts.Clear();
                var t = T;
                if (t != null) _maskOpts.AddRange(Masks.Options(t)); else foreach (var n in Masks.Names) _maskOpts.Add(new MaskOption { Key = n, Label = n });
                if (!_maskOpts.Any(o => string.Equals(o.Key, saved, StringComparison.OrdinalIgnoreCase)) && saved.Length > 0)
                    _maskOpts.Add(new MaskOption { Key = saved, Label = L.T("custom") + ": " + saved });
                _mask.Items.Clear();
                foreach (var o in _maskOpts) _mask.Items.Add(new Opt(o.Key, Explain.MaskLabel(o)));
                int idx = _maskOpts.FindIndex(o => string.Equals(o.Key, saved, StringComparison.OrdinalIgnoreCase));
                _mask.SelectedIndex = idx < 0 ? 0 : idx;
                if (_maskDesc != null) _maskDesc.Text = Explain.Mask(saved, t);
                if (_gmIrq != null) Theme.SelectVal(_gmIrq, GameMask.Mode);
            }
            finally { _syncing = false; }
            GmLoad();
        }

        // grid <- saved state (unless the user has unsaved ticks: those stay, only the marks and the summary follow)
        private void GmLoad()
        {
            var t = T;
            if (_gmGrid == null) return;
            if (t == null) { _gmSum.Text = L.T("CPU topology unavailable (GetSystemCpuSetInformation)."); return; }
            string sig = TopoSig(t);
            if (sig != _gmSig) { _gmSig = sig; _gmGrid.Build(t); _gmDirty = false; }
            var i = GameMask.Info(t);
            // pending ticks belong to the base they were made against: a base changed elsewhere (template, Use, custom
            // rule, a profile) drops them
            if (_gmDirty && !string.Equals(_gmBaseRule, i.BaseRule, StringComparison.OrdinalIgnoreCase)) _gmDirty = false;
            if (!_gmDirty) { _gmPending = i.Manual; _gmBaseRule = i.BaseRule; }
            _gmLocked = i.IrqDrop;
            _gmLoading = true;
            try
            {
                _gmGrid.Mask = _gmPending & ~_gmLocked;
                _gmGrid.MarkMask = 0;
                _gmGrid.MarkWith(i.Irq, Theme.Warn);
                _gmGrid.LockedMask = _gmLocked;
                foreach (var s in t.Sets.Where(x => x.Group == 0))
                {
                    int lp = s.LP;
                    var who = i.IrqDevices.Where(d => ((d.Value >> lp) & 1) != 0).Select(d => d.Key).ToList();
                    string tip = who.Count == 0 ? "" : L.F("CPU {0} - device interrupts: {1}", lp, string.Join(", ", who)) +
                                 (((i.IrqDrop >> lp) & 1) != 0 ? "\n" + L.T("Taken off the game mask automatically (Interrupt cores).") : "");
                    _gmGrid.TipFor(lp, tip);
                }
            }
            finally { _gmLoading = false; }
            GmShow();
        }

        // result line for the grid as it is now (pending ticks included)
        private void GmShow()
        {
            var t = T;
            if (t == null || _gmGrid == null) return;
            var i = GameMask.Info(t);
            ulong manual = _gmPending, add, drop, irqDrop = 0; bool ign = false;
            bool none = (manual & ~_gmLocked) == 0;                 // nothing the user can see is ticked
            GameMask.EditsFrom(i.Base, manual, out add, out drop);
            ulong fin = 0;
            if (!none) fin = GameMask.Adjust(t, i.Base, add, drop, i.Irq, i.Mode, out irqDrop, out ign);
            ulong reserved = Reservation.Reserved(), kept = Reservation.Unmix(fin, reserved, false), all = t.FullMask();
            int n = CountBits(kept), total = CountBits(all);

            if (none) { _gmSum.ForeColor = Theme.Warn; _gmSum.Text = L.T("No core ticked - the game needs at least one."); }
            else
            {
                _gmSum.ForeColor = Theme.Fg;
                _gmSum.Text = L.F("The game gets LP {0}  ({1} of {2})", Masks.Ranges(kept), n, total) + (_gmDirty ? "   ·   " + L.T("not applied yet") : "");
            }
            var parts = new List<string>();
            string saved = Settings.Get("Mask");
            var opt = _maskOpts.FirstOrDefault(o => string.Equals(o.Key, saved, StringComparison.OrdinalIgnoreCase)) ?? new MaskOption { Key = saved, Label = saved };
            parts.Add(L.F("base {0}: LP {1}", Explain.MaskLabel(opt), Masks.Ranges(i.Base)));
            parts.Add(drop != 0 ? L.F("you took off LP {0}", Masks.Ranges(drop)) : L.T("nothing taken off by hand"));
            if (add != 0) parts.Add(L.F("you added LP {0}", Masks.Ranges(add)));
            if (irqDrop != 0) parts.Add(L.F("interrupts took LP {0}", Masks.Ranges(irqDrop)));
            else if (ign) parts.Add(L.T("interrupt LPs kept: taking them would leave the game less than half of its cores"));
            if (fin != kept) parts.Add(L.F("reserved LP {0} left out (ReservedCpuSets)", Masks.Ranges(fin & ~kept)));
            if (i.EditsStale) parts.Add(L.T("saved grid edits were made on another CPU layout and are ignored"));
            _gmDetail.Text = string.Join("   ·   ", parts);

            // interrupt info next to the mode
            var irqParts = i.IrqDevices.Select(d => d.Key + " → LP " + Masks.Ranges(d.Value)).ToList();
            if (i.IrqWide.Count > 0) irqParts.Add(L.F("not counted (wider than one core): {0}", string.Join(", ", i.IrqWide.Select(d => d.Key + " → LP " + Masks.Ranges(d.Value)))));
            if (irqParts.Count == 0) { _gmIrqInfo.ForeColor = Theme.Faint; _gmIrqInfo.Text = L.T("no device has pinned interrupts"); }
            else { _gmIrqInfo.ForeColor = ign ? Theme.Warn : Theme.Dim; _gmIrqInfo.Text = string.Join("   ·   ", irqParts); }

            // a per-process rule for a target wins over this mask: say so
            var ruled = M.Eng.Targets.Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x : x + ".exe")
                                     .Where(x => M.Eng.AffRules.ContainsKey(x)).ToList();
            _gmRuleWarn.Text = ruled.Count == 0 ? "" : L.F("{0} has its own rule on the Processes tab ({1}): for it the rule replaces the base and your ticks here; interrupt cores still leave it.",
                                                          string.Join(", ", ruled), string.Join(", ", ruled.Select(x => M.Eng.AffRules[x])));
            _gmRuleWarn.Visible = ruled.Count > 0;
            _gmRuleRow.Visible = ruled.Count > 0;

            _gmApply.Enabled = _gmDirty && !none;
            _gmCancel.Enabled = _gmDirty;
            _gmReset.Enabled = manual != i.Base;
        }

        // targets back on the game mask: their own rules go, the running game gets its captured cores back, then the live layer
        private void GmDropTargetRules()
        {
            var ruled = M.Eng.Targets.Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x : x + ".exe")
                                     .Where(x => M.Eng.AffRules.ContainsKey(x)).ToList();
            if (ruled.Count == 0) return;
            foreach (var exe in ruled)
            {
                M.Eng.AffRules.Remove(exe);
                M.Log("[AF] process affinity rule removed: " + exe + " (back on the game mask)");
                M.Log(M.Eng.ReleaseAffinity(exe));
            }
            Settings.SaveAffRules(M.Eng.AffRules);
            if (M.Active) M.ApplyNow();
            RefreshAffRules();
            GmShow();
        }

        private static int CountBits(ulong m) { int n = 0; while (m != 0) { m &= m - 1; n++; } return n; }

        private void GmApply()
        {
            var t = T; if (t == null) return;
            var i = GameMask.Info(t, false, null);
            ulong manual = _gmPending, add, drop;
            if ((manual & ~_gmLocked) == 0) { Dlg.Info(this, L.T("Tick at least one core.")); return; }
            GameMask.EditsFrom(i.Base, manual, out add, out drop);
            GameMask.SetEdits(add, drop);
            _gmDirty = false;
            M.Log(string.Format("[AF] game mask edits: base {0}{1}{2}", Settings.Get("Mask"),
                add != 0 ? ", +LP " + Masks.Ranges(add) : "", drop != 0 ? ", -LP " + Masks.Ranges(drop) : (add == 0 ? ", none (the base as is)" : "")));
            _syncing = true; try { M.NotifyMask(); } finally { _syncing = false; }
            if (M.Active) M.ApplyNow();
            FillMask();
        }

        private void SyncMask() { if (!_syncing) FillMask(); }

        // ------------------------------------------------------------------ process affinity (mini Process Lasso)
        private DropBox _plProc;
        private CoreGrid _plGrid;
        private Label _plInfo;
        private Btn _plRemove;
        private bool _plBuilding;

        // Layout, top to bottom: short note - Process (picker + Refresh) - Cores (legend | quick picks) - the grid with
        // "Core N" headers - Mask entry + summary - Apply / Remove rule.
        private void BuildProcessLasso()
        {
            Title(L.T("Process affinity"));
            var card = NewCard();
            card.Padding = new Padding(Theme.D(20), Theme.D(10), Theme.D(20), Theme.D(14));
            card.Stack(new TextBlock(L.T("Pick a process, tick the cores it may use and press Apply. The rule works now and on every Apply; Remove rule gives the process its cores back. Anti-cheat and protected system processes are not listed."), Theme.Dim, 9f));

            // 1. process
            var pr = card.Stack(new LineRow(Theme.D(48)));
            pr.L(Theme.Lbl(L.T("Process"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            _plProc = pr.L(Theme.Combo(Theme.D(300)));
            var refresh = pr.L(new Btn(L.T("Refresh"), Btn.Style.Ghost));
            card.Stack(new Hairline());

            // 2. cores: legend on the left, quick picks on the right
            var ch = card.Stack(new LineRow(Theme.D(40)));
            ch.L(Theme.Lbl(L.T("Cores"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            var dot = ch.L(Theme.Lbl("●", Theme.F(9f), Theme.Acc)); dot.BackColor = Theme.Card;
            ch.Gap = 6;
            ch.L(Theme.Lbl(L.T("= a core of the current game mask"), Theme.F(9f), Theme.Dim)).BackColor = Theme.Card;
            int qh = Theme.D(30);
            ch.R(new Btn(L.T("All cores"), Btn.Style.Outline) { Height = qh }).Click += (s, e) => { var t = T; if (t != null) _plGrid.Mask = t.FullMask(); };
            ch.R(new Btn(L.T("Game cores"), Btn.Style.Outline) { Height = qh }).Click += (s, e) => { var t = T; if (t != null) _plGrid.Mask = GameMask.Effective(t).Mask; };
            ch.R(new Btn(L.T("Off the game cores"), Btn.Style.Outline) { Height = qh }).Click += (s, e) => { var t = T; if (t != null) { ulong g = GameMask.Effective(t).Mask; _plGrid.Mask = Masks.BgMask(t, g); } };
            ch.R(new Btn(L.T("Clear"), Btn.Style.Ghost) { Height = qh }).Click += (s, e) => _plGrid.Mask = 0;
            card.Stack(new Spacer(2));

            // 3. the grid
            _plGrid = card.Stack(new CoreGrid());
            card.Stack(new Spacer(6));
            card.Stack(new Hairline());

            // 4. mask entry + what the grid means
            var mr = card.Stack(new LineRow(Theme.D(44)));
            mr.L(Theme.Lbl(L.T("Mask"), Theme.Semi(10f), Theme.Fg)).BackColor = Theme.Card;
            _plHex = mr.L(Theme.Box(Theme.D(170)));
            _plInfo = mr.L(new Label { AutoSize = false, Height = Theme.D(24), Font = Theme.F(9.5f), ForeColor = Theme.Fg, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true });
            mr.Fill = _plInfo;
            _plHex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PlFromText(); } };
            _plHex.Leave += (s, e) => PlFromText();
            card.Stack(new TextBlock(L.T("Hex (0xFC), LPs (2,4,6-9) or a rule (VCache+NoSMT, P+NoCore0); Enter ticks the grid. Stays in sync with the checkboxes."), Theme.Faint, 8.75f));
            card.Stack(new Spacer(8));

            // 5. actions
            var act = card.Stack(new WrapRow());
            act.Add(new Btn(L.T("Apply"), Btn.Style.Accent, Theme.D(120)) { Height = Theme.D(36) }).Click += (s, e) => PlApply();
            _plRemove = act.Add(new Btn(L.T("Remove rule"), Btn.Style.Outline) { Height = Theme.D(36), DisabledOutline = true });
            _plRemove.Click += (s, e) => PlRemove();

            _plProc.SelectedIndexChanged += (s, e) => { if (!_plBuilding) PlLoad(); };
            refresh.Click += (s, e) => PlFillProcs(PlSel());
            _plGrid.Changed += () => { if (!_plLoadingGrid) _plDirty = true; PlShowInfo(); };   // user ticks = unsaved edit
        }

        private TextBox _plHex;
        // what the grid was loaded from, and whether the user changed it since: a rule changed elsewhere (Full reset,
        // a profile, the rule table) reloads a clean grid; unsaved ticks are kept
        private string _plLoadedExe = "", _plLoadedRule = "";
        private bool _plDirty, _plLoadingGrid;
        private string SavedRule(string exe) { string r; return exe.Length > 0 && M.Eng.AffRules.TryGetValue(exe, out r) ? r : ""; }

        // called after anything that may have changed the saved rules
        private void PlSync()
        {
            if (_plGrid == null || _plDirty) return;
            string exe = PlSel();
            if (!string.Equals(exe, _plLoadedExe, StringComparison.OrdinalIgnoreCase) || SavedRule(exe) != _plLoadedRule) PlLoad();
        }
        private string _plSig = "";
        internal static string TopoSig(Topo t) { return t == null ? "" : string.Join(",", t.Sets.Select(x => x.Group + ":" + x.LP + ":" + x.Core + ":" + x.LLC + ":" + x.Eff)); }
        private bool _plTexting;
        private string PlSel() { var o = _plProc.SelectedItem; return o == null ? "" : o.ToString(); }

        // Mask view -> Cores view: hex / LP list / portable rule, resolved on this CPU.
        private void PlFromText()
        {
            var t = T; string txt = _plHex.Text.Trim();
            if (t == null || txt.Length == 0 || _plTexting) return;
            string err; var r = Masks.Resolve(t, txt, out err);
            if (r == null) { _plInfo.ForeColor = Theme.Warn; _plInfo.Text = L.F("Not a mask for this CPU: {0}", err ?? txt); return; }
            _plTexting = true;
            try { _plGrid.Mask = r.Mask; } finally { _plTexting = false; }
            PlShowInfo();
        }

        private void PlRefresh()
        {
            var t = T;
            // rebuild only when the topology really changed (SafeTopo hands out a fresh object every 30 s): unsaved ticks stay
            string sig = TopoSig(t);
            bool rebuilt = false;
            if (t != null && sig != _plSig) { _plSig = sig; _plGrid.Build(t); rebuilt = true; }
            try { if (t != null) { _plGrid.MarkMask = GameMask.Effective(t).Mask; _plGrid.MarkWith(PlIsTarget(PlSel()) ? GameMask.Info(t).Irq : 0, Theme.Warn); } } catch { }
            PlFillProcs(PlSel(), rebuilt);
        }

        // Refill the process list. The grid is reloaded from the saved rule only when the selection changed or the grid
        // was rebuilt: unsaved ticks survive a page switch or a Refresh.
        private void PlFillProcs(string keep, bool reload = false)
        {
            string before = PlSel();
            _plBuilding = true;
            try
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var p in System.Diagnostics.Process.GetProcesses()) { if (p.Id > 4) names.Add(p.ProcessName + ".exe"); } } catch { }
                foreach (var k in M.Eng.AffRules.Keys) names.Add(k);
                foreach (var tg in M.Eng.Targets) names.Add(tg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? tg : tg + ".exe");
                // anti-cheat and protected system processes are never offered: grobyq does not change their cores
                names.RemoveWhere(n => Engine.Gate(n, Engine.Op.Affinity, Engine.ProtectedPriorityAllowed) != null);
                _plProc.Items.Clear();
                // targets and processes with a rule first, then the rest
                var first = names.Where(n => M.Eng.AffRules.ContainsKey(n) || M.Eng.Targets.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))).ToList();
                foreach (var n in first) _plProc.Items.Add(n);
                foreach (var n in names) if (!first.Contains(n)) _plProc.Items.Add(n);
                string pick = !string.IsNullOrEmpty(keep) ? keep : (first.Count > 0 ? first[0] : null);
                if (pick != null) Theme.Select(_plProc, pick);
                else if (_plProc.Items.Count > 0) _plProc.SelectedIndex = 0;
            }
            finally { _plBuilding = false; }
            if (reload || !string.Equals(before, PlSel(), StringComparison.OrdinalIgnoreCase)) PlLoad();
            else if (!_plDirty && SavedRule(PlSel()) != _plLoadedRule) PlLoad();     // the saved rule changed elsewhere
            else PlShowInfo();
        }

        private void PlLoad()
        {
            var t = T; string exe = PlSel();
            _plDirty = false; _plLoadedExe = exe; _plLoadedRule = SavedRule(exe);
            if (t == null || _plGrid.IsEmpty || exe.Length == 0) { PlShowInfo(); return; }
            string rule; ulong m = PlIsTarget(exe) ? GameMask.Effective(t).Mask : t.FullMask();     // no rule: what it really runs on
            if (M.Eng.AffRules.TryGetValue(exe, out rule))
            {
                string err; var r = Masks.Resolve(t, rule, out err);
                if (r != null) m = r.Mask;
            }
            _plLoadingGrid = true;
            try
            {
                _plGrid.Mask = m;
                _plGrid.MarkMask = GameMask.Effective(t).Mask;
                _plGrid.MarkWith(PlIsTarget(exe) ? GameMask.Info(t).Irq : 0, Theme.Warn);     // interrupt LPs, amber, for the game
            }
            finally { _plLoadingGrid = false; }
        }

        private bool PlIsTarget(string exe)
        {
            return exe.Length > 0 && M.Eng.Targets.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(x + ".exe", exe, StringComparison.OrdinalIgnoreCase));
        }

        private void PlShowInfo()
        {
            var t = T; string exe = PlSel();
            bool target = PlIsTarget(exe);
            if (_plRemove != null)
            {
                _plRemove.Enabled = exe.Length > 0 && M.Eng.AffRules.ContainsKey(exe);
                string rt = target ? L.T("Use the game mask") : L.T("Remove rule");     // for the game, removing the rule = the adaptive game mask
                if (_plRemove.Text != rt) { _plRemove.Text = rt; _plRemove.Fit(); if (_plRemove.Parent != null) _plRemove.Parent.PerformLayout(); }
            }
            if (t == null || exe.Length == 0) { _plInfo.Text = ""; return; }
            ulong m = _plGrid.Mask, all = t.FullMask();
            bool hasRule = M.Eng.AffRules.ContainsKey(exe);
            if (m == 0) { _plInfo.ForeColor = Theme.Warn; _plInfo.Text = L.T("No core ticked - a process needs at least one."); return; }
            ulong off = all & ~m;
            if (_plHex != null && !_plHex.Focused && !_plTexting) _plHex.Text = Masks.Hex(m);
            _plInfo.ForeColor = Theme.Fg;
            string state;
            if (target && !hasRule && !_plDirty) state = L.T("no own rule: runs on the game mask (adaptive)");
            else
            {
                state = hasRule && !_plDirty ? L.T("rule saved") : L.T("not saved yet");
                ulong idr; if (target) GameMask.ForRule(t, m, out idr); else idr = 0;
                if (idr != 0) state += "   ·   " + L.F("interrupts take LP {0} off it", Masks.Ranges(idr));
            }
            _plInfo.Text = L.F("{0}: LP {1}", exe, Masks.Ranges(m)) + (off != 0 ? "   ·   " + L.F("off: LP {0}", Masks.Ranges(off)) : "   ·   " + L.T("all cores")) + "   ·   " + state;
        }

        private void PlApply()
        {
            var t = T; string exe = PlSel();
            if (t == null || exe.Length == 0) return;
            ulong m = _plGrid.Mask;
            if (m == 0) { Dlg.Info(this, L.T("Tick at least one core.")); return; }
            if (m == t.FullMask()) { PlRemove(); return; }        // all cores = no rule needed
            if (PlIsTarget(exe) && !M.Eng.AffRules.ContainsKey(exe) && m == GameMask.Effective(t).Mask)
            { _plDirty = false; PlShowInfo(); return; }           // the game already runs on exactly this: a fixed copy would stop it adapting
            M.Eng.AffRules[exe] = Masks.Hex(m);
            Settings.SaveAffRules(M.Eng.AffRules);
            _plDirty = false; _plLoadedExe = exe; _plLoadedRule = Masks.Hex(m);      // the grid now IS the saved rule
            M.Log(string.Format("[AF] process affinity: {0} -> {1} LP {2}", exe, Masks.Hex(m), Masks.Ranges(m)));
            M.Log(M.Eng.ApplyAffRules());
            RefreshAffRules(); PlShowInfo();
        }

        private void PlRemove()
        {
            string exe = PlSel();
            if (exe.Length == 0) return;
            if (M.Eng.AffRules.Remove(exe))
            {
                Settings.SaveAffRules(M.Eng.AffRules);
                M.Log("[AF] process affinity rule removed: " + exe);
                M.Log(M.Eng.ReleaseAffinity(exe));            // the running process gets its original cores back now
                if (M.Active) M.ApplyNow();                   // ...then the live layer (game mask / background) again
            }
            RefreshAffRules(); PlLoad();
        }

        // ------------------------------------------------------------------ core layout templates
        private Card _tplCard;
        private void BuildTemplates()
        {
            Title(L.T("Core layout templates"));
            _tplCard = NewCard();
            FillTemplates();
        }

        private void FillTemplates()
        {
            if (_tplCard == null) return;
            var t = T;
            _tplCard.SuspendLayout();
            try
            {
                _tplCard.ClearStack();
                _tplCard.Stack(new TextBlock(L.T("Orientation, not magic presets: each template resolves on this CPU and stays a hypothesis until a trace confirms it. Apply sets the game mask, background pinning and the CPU-0 rule; interrupts stay a manual step on the Interrupts tab."), Theme.Dim, 9f));
                string activeKey = Settings.Get("LayoutTemplate"), mask = Settings.Get("Mask");
                foreach (var tp in Layouts.For(t))
                {
                    bool active = tp.Key == activeKey && string.Equals(tp.Mask, mask, StringComparison.OrdinalIgnoreCase);
                    string desc = L.T(tp.Summary);
                    if (tp.Available)
                        desc += "\n" + L.F("Game LP {0}  ·  background LP {1}", Masks.Ranges(tp.Game), Masks.Ranges(tp.Background)) + "\n" + L.T("Interrupts:") + " " + L.T(tp.Irq)
                              + (tp.Reserve ? "\n" + L.T("Companion: the Reservation tab can reserve the non-game set (ReservedCpuSets, restart).") : "");
                    else desc += "\n" + L.T("Not available here:") + " " + (tp.WhyArg != null ? L.F(tp.Why, tp.WhyArg) : L.T(tp.Why));
                    var b = new Btn(active ? L.T("Active") : L.T("Apply"), active ? Btn.Style.Outline : Btn.Style.Accent, Theme.D(96));
                    b.Enabled = tp.Available && !active;
                    var key = tp.Key;
                    // deferred: applying rebuilds this card, and a button must not be disposed inside its own Click
                    b.Click += (s, e) => BeginInvoke((Action)(() => ApplyTemplate(key)));
                    _tplCard.Stack(new SettingRow(L.T(tp.Name), desc, b) { AlignTop = true });
                }
                _tplCard.Stack(new TextBlock(L.T("Verify: Contour A (WPR/WPA CPU Usage + DPC/ISR) - background and system threads left the game cores, no new DPC on the game LPs. Frame-time A/B (1% / 0.1% low, PresentMon / CapFrameX), same map, WHEA = 0. Never judge by average or menu FPS."), Theme.Faint, 9f));
            }
            finally { _tplCard.ResumeLayout(true); }
        }

        private void ApplyTemplate(string key)
        {
            var t = T; var tp = Layouts.Find(t, key);
            if (tp == null || !tp.Available) return;
            if (!Dlg.Ask(this, L.F("Apply the {0} layout?", L.T(tp.Name)) + "\n\n" + L.F("Game LP {0}  ·  background LP {1}", Masks.Ranges(tp.Game), Masks.Ranges(tp.Background))
                              + "\n\n" + L.T("Capture Contour A and a frame-time log before and after; keep it only if the trace improves."))) return;
            if (GameMask.SetBase(tp.Mask)) M.Log("[AF] game mask grid edits cleared (new base " + tp.Mask + ")");
            Settings.Set("LayoutTemplate", tp.Key);
            Settings.SetBool("SkipCpu0", tp.SkipCpu0);
            Settings.SetBool("BgAffinity", tp.Key != "baseline");
            M.Log(string.Format("[AF] layout template {0}: game {1} LP {2}, background LP {3}, SkipCpu0 {4}", tp.Key, Masks.Hex(tp.Game), Masks.Ranges(tp.Game), Masks.Ranges(tp.Background), tp.SkipCpu0));
            M.NotifyMask();
            M.NotifySettings();                           // Threads page switches (SkipCpu0, background pinning) follow
            FillMask();
            FillTemplates();
            if (M.Active) M.ApplyNow();
        }

        // ------------------------------------------------------------------ CCD split (game set / non-game set)
        private void BuildSplit()
        {
            Title(L.T("CCD split"));
            var card = NewCard();
            _split = card.Stack(new StackBox());
        }

        internal static bool GameSetInUse(Topo t)
        {
            return t != null && string.Equals(Masks.ToSpec(Settings.Get("Mask")) ?? "", "GameSet", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshSplit()
        {
            var t = T;
            _split.SuspendLayout();
            _split.ClearStack();
            try
            {
                if (t == null) { _split.Stack(new TextBlock(L.T("CPU topology unavailable (GetSystemCpuSetInformation)."), Theme.Dim, 9f)); return; }
                var p = t.Split;
                if (p.Family == "B" || p.Family == "D")
                {
                    _split.Stack(new TextBlock(Explain.SplitFamily(p) + "   ·   " + Explain.SplitWhy(p), Theme.Dim, 9f));
                    return;
                }
                var info = _split.Stack(new InfoGrid { KeyWidth = 150 });
                var rows = new List<KeyValuePair<string, string>>();
                rows.Add(KV(L.T("Family"), Explain.SplitFamily(p)));
                rows.Add(KV(L.T("Processor"), p.Name.Length > 0 ? p.Name : CpuName()));
                int gameRow = -1, nonRow = -1, stateRow = -1;
                if (p.IsSplitFamily)
                {
                    if (p.Family == "A") rows.Add(KV(L.T("L3 cache"), L.F("{0} MB game set (3D V-Cache)   ·   {1} MB non-game set", p.GameL3 >> 20, p.NonGameL3 >> 20)));
                    else rows.Add(KV(L.T("Core types"), L.F("{0} P-cores (EfficiencyClass {1})   ·   {2} E / LP-E cores", p.PCores, p.TopEff, p.ECores)));
                    gameRow = rows.Count; rows.Add(KV(L.T("GAME"), Masks.Hex(p.Game) + "   ·   LP " + Masks.Ranges(p.Game)));
                    nonRow = rows.Count; rows.Add(KV(L.T("NON-GAME"), Masks.Hex(p.NonGame) + "   ·   LP " + Masks.Ranges(p.NonGame)));
                }
                bool inUse = GameSetInUse(t);
                string state;
                Color sc;
                if (!p.CanSplit) { state = L.T("not available"); sc = Theme.Warn; }
                else if (inUse) { state = L.T("in use - cs2 on the game set, background on the non-game set"); sc = Theme.Ok; }
                else
                {
                    ulong cur = GameMask.Effective(t).Mask;
                    state = cur == p.Game ? L.F("available - the current mask ({0}) already covers the same LPs", Settings.Get("Mask")) : L.F("available - the game mask now is {0}", Settings.Get("Mask"));
                    sc = Theme.Fg;
                }
                stateRow = rows.Count; rows.Add(KV(L.T("State"), state));
                info.Set(rows);
                if (gameRow >= 0) info.Tint(gameRow, Theme.Mix(Theme.Fg, Theme.Acc, 0.55f));
                if (nonRow >= 0) info.Tint(nonRow, Theme.Mix(Theme.Fg, CpuMap.NonGameC, 0.55f));
                info.Tint(stateRow, sc);

                if (!p.CanSplit) _split.Stack(new TextBlock(Explain.SplitWhy(p), Theme.Warn, 9f));
                if (p.UpperHalf) _split.Stack(new TextBlock(L.F("On this PC the 3D V-Cache CCD is the upper half (LP {0}). The sets follow the topology probe, not the usual layout.", Masks.Ranges(p.Game)), Theme.Warn, 9f));

                if (p.CanSplit)
                {
                    var b = new Btn(inUse ? L.T("In use") : L.T("Use game set"), Btn.Style.Accent, Theme.D(130));
                    b.Click += (s, e) => BeginInvoke((Action)(() => { if (!GameSetInUse(T)) UseMask("GameSet"); }));   // rebuilds this card
                    var row = _split.Stack(new SettingRow(L.T("Game set for the optimizer"),
                        L.T("Affinity + default CPU Sets of cs2 = the game set; hot-thread Ideal CPUs one per core inside it; background pinned to the non-game set. SMT pairs stay whole. T1 APIs, T2 effect: judge by frame-time and WPA."), b));
                    row.Divider = false;
                }
                _split.Stack(new TextBlock(L.T("If you run the external ReservedCpuSets tool (ARM), turn the grobyq optimizer off: one owner of the cs2 mask."), Theme.Faint, 9f));
            }
            finally { _split.ResumeLayout(true); }
        }

        // ------------------------------------------------------------------ this CPU
        private void BuildCpu()
        {
            Title(L.T("Your processor"));
            var card = NewCard();
            _cpuInfo = card.Stack(new InfoGrid { KeyWidth = 200 });
            card.Stack(new TextBlock(L.T("What to try on this CPU, in this order. Each step is an A/B test against All cores: keep a narrower mask only if frame-time (1% / 0.1% lows) and DPC/ISR per core get better in your own measurement."), Theme.Dim, 9f));
            _advice = card.Stack(new StackBox());

            Title(L.T("CPU map"));
            var mc = NewCard();
            _mapInfo = mc.Stack(new TextBlock("", Theme.Fg, 9.5f));
            _map = mc.Stack(new CpuMap());
        }

        private string CpuName()
        {
            if (M.Sys != null && M.Sys.Cpu.Length > 0) return M.Sys.Cpu;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                    if (k != null) return Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
            }
            catch { }
            return "";
        }

        private void RefreshCpu()
        {
            var t = T;
            var rows = new List<KeyValuePair<string, string>>();
            rows.Add(KV(L.T("Model"), CpuName()));
            if (t != null)
            {
                rows.Add(KV(L.T("Cores / threads"), L.F("{0} physical cores · {1} logical processors · SMT {2}", t.PhysicalCount, t.LogicalCount, t.SMT ? L.T("on") : L.T("off"))));
                if (t.Hybrid)
                {
                    byte top = t.Sets.Max(s => s.Eff);
                    int p = t.Sets.Where(s => s.Eff == top).Select(s => s.Core).Distinct().Count();
                    int e = t.Sets.Where(s => s.Eff != top).Select(s => s.Core).Distinct().Count();
                    rows.Add(KV(L.T("Core types"), L.F("hybrid: {0} P-cores + {1} E-cores", p, e)));
                }
                rows.Add(KV(L.T("L3 cache"), string.Join("   ·   ", t.LLCGroups.Select((l, i) => (t.LLCGroups.Count > 1 ? L.F("domain {0}", i) + ": " : "") + t.L3Text(l) + (t.VCacheLlc.HasValue && t.VCacheLlc.Value == l ? " (3D V-Cache)" : "")))));
                rows.Add(KV(L.T("Class"), CpuClass(t)));
                var gi = GameMask.Info(t);
                rows.Add(KV(L.T("Mask in use"), Explain.MaskLabel(Masks.Options(t).FirstOrDefault(o => string.Equals(o.Key, Settings.Get("Mask"), StringComparison.OrdinalIgnoreCase)) ?? new MaskOption { Label = Settings.Get("Mask") })
                                                + (gi.Adjusted ? "   ·   " + L.F("adapted: LP {0}", Masks.Ranges(gi.Final)) : "")));
            }
            _cpuInfo.Set(rows);

            _advice.SuspendLayout();
            _advice.ClearStack();
            if (t != null)
            {
                int n = 1;
                string cur = Settings.Get("Mask");
                foreach (var a in Advice(t))
                {
                    var r = Masks.Build(t, a.Key);
                    bool inUse = string.Equals(a.Key, cur, StringComparison.OrdinalIgnoreCase) || string.Equals(Masks.ToSpec(a.Key), Masks.ToSpec(cur), StringComparison.OrdinalIgnoreCase);
                    var b = new Btn(inUse ? L.T("In use") : L.T("Use"), inUse ? Btn.Style.Accent : Btn.Style.Normal, Theme.D(90));
                    string key = a.Key;
                    b.Click += (s, e) => BeginInvoke((Action)(() => UseMask(key)));   // rebuilds this list: never inside the click
                    string label = Explain.MaskLabel(Masks.Options(t).FirstOrDefault(o => o.Key == a.Key) ?? new MaskOption { Label = a.Key });
                    _advice.Stack(new SettingRow(L.F("Step {0}:", n) + "  " + label + "      " + r.Count + " LP  ·  " + Masks.LpsShort(r.Mask), a.Value, b));
                    n++;
                }
            }
            _advice.ResumeLayout(true);
        }

        private static KeyValuePair<string, string> KV(string k, string v) { return new KeyValuePair<string, string>(k, v ?? ""); }

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
                add("NoCpu0", L.T("Leaves the first core to interrupts and Windows; the game keeps 14+ logical processors."));
                add("SmtOffNoCpu0", L.T("Also one thread per core: most isolation. Only if it measurably helps on your PC."));
                add("AllCores", L.T("Baseline: compare every step with this."));
            }
            else if (t.PhysicalCount >= 6)
            {
                add("NoCpu0", L.T("6-7 cores: leave the first core to interrupts and Windows, keep SMT for the game's worker threads."));
                add("AllCores", L.T("Baseline. With 6 cores the difference is small; keep whichever measures better."));
            }
            else add("AllCores", L.T("4 cores or fewer: every narrower mask takes more than it gives. Keep all cores."));
            return list;
        }

        private void UseMask(string key)
        {
            bool cleared = GameMask.SetBase(key);
            M.Log("[AF] game core mask = " + key + (cleared ? "  (grid edits cleared)" : ""));
            M.NotifyMask();
            if (M.Active) M.ApplyNow();
        }

        private void RefreshMap(ulong highlight = 0)
        {
            var t = T;
            if (t == null) { _mapInfo.Text = L.T("CPU topology unavailable (GetSystemCpuSetInformation)."); return; }
            var game = GameMask.Effective(t);
            ulong bg = GameMask.Background(t);
            _map.Highlight = highlight;
            _map.Set(t, game.Mask, bg);
            _map.Overlay(t.Split.CanSplit ? t.Split.Game : 0, t.Split.CanSplit ? t.Split.NonGame : 0, Reservation.Reserved());
            string baseName = Settings.Get("Mask") + (GameMask.Info(t).Adjusted ? " " + L.T("(adapted)") : "");
            _mapInfo.Text = L.F("Game mask {0} = {1}  ({2} LP: {3})   ·   background {4}", baseName, game.Hex, game.Count, game.LPs, "0x" + bg.ToString("X"));
        }

        // ------------------------------------------------------------------ mask list
        private void BuildMaskList()
        {
            Title(L.T("All masks for this CPU"));
            var card = NewCard();
            card.Stack(new TextBlock(L.T("The cores cs2 (and any target) may run on. Only masks that exist on this CPU are listed. A narrower mask is not automatically better: compare with All cores by frame-time and Contour A."), Theme.Dim, 9f));
            _opts = card.Stack(new StackBox());
        }

        private void FillMaskOptions()
        {
            var t = T;
            _opts.SuspendLayout();
            _opts.ClearStack();
            string cur = Settings.Get("Mask");
            if (t != null)
                foreach (var o in Masks.Options(t))
                {
                    var r = Masks.Build(t, o.Key);
                    bool sel = string.Equals(o.Key, cur, StringComparison.OrdinalIgnoreCase);
                    var b = new Btn(sel ? L.T("In use") : L.T("Use"), sel ? Btn.Style.Accent : Btn.Style.Normal, Theme.D(90));
                    string key = o.Key;
                    b.Click += (s, e) => BeginInvoke((Action)(() => UseMask(key)));   // rebuilds this list: never inside the click
                    string when = Explain.MaskWhen(o.Key, t);
                    var row = new SettingRow(string.Format("{0}      {1}  ·  {2} LP  ·  {3}", Explain.MaskLabel(o), r.Hex, r.Count, Masks.LpsShort(r.Mask)),
                                             Explain.Mask(o.Key, t) + (when.Length > 0 ? "\n" + L.T("When:") + " " + when : ""), b);
                    _opts.Stack(row);
                }
            _opts.ResumeLayout(true);
        }

        private void BuildCustom()
        {
            Title(L.T("Custom rule"));
            var card = NewCard();
            _custom = Theme.Box(Theme.D(260));
            var prev = new Btn(L.T("Preview"), Btn.Style.Normal, Theme.D(90));
            var use = new Btn(L.T("Use"), Btn.Style.Accent, Theme.D(90));
            var row = card.Stack(new SettingRow(L.T("Custom rule"), L.T("Type a rule, Preview shows it on the map, Use makes it the game mask."), new Row(_custom, prev, use)));
            row.Divider = false;
            card.Stack(new TextBlock(Explain.MaskGrammar, Theme.Dim, 9f));
            _customInfo = card.Stack(new TextBlock("", Theme.Dim, 9f));
            prev.Click += (s, e) => PreviewCustom(false);
            use.Click += (s, e) => PreviewCustom(true);
        }

        private void PreviewCustom(bool use)
        {
            var t = T; if (t == null) return;
            string err; var r = Masks.Resolve(t, _custom.Text, out err);
            if (r == null) { _customInfo.ForeColor = Theme.Warn; _customInfo.Text = "'" + _custom.Text + "' " + err; return; }
            _customInfo.ForeColor = r.Satisfied ? Theme.Dim : Theme.Warn;
            _customInfo.Text = string.Format("{0} -> {1}  ({2} LP: {3}){4}{5}", r.Spec, r.Hex, r.Count, r.LPs, r.Portable ? "" : "  ·  " + L.T("fixed LPs (not portable)"), r.Notes.Count > 0 ? "\n" + string.Join("\n", r.Notes) : "");
            _map.Set(t, r.Mask, Masks.BgMask(t, r.Mask));
            if (use)
            {
                bool cleared = GameMask.SetBase(r.Portable ? r.Spec : r.Hex);
                M.Log("[AF] game core mask = " + Settings.Get("Mask") + " -> " + r.Hex + (cleared ? "  (grid edits cleared)" : ""));
                M.NotifyMask();
                if (M.Active) M.ApplyNow();
            }
        }

        // ------------------------------------------------------------------ per-process rules
        private void BuildAffRules()
        {
            Title(L.T("Per-process rules"));
            var card = NewCard();
            card.Stack(new TextBlock(L.T("A rule overrides the game mask for that process (targets), or pins any other process (voice, VPN, streaming). Rules are applied on every Apply when 'Per-process affinity rules' is on (Threads page)."), Theme.Dim, 9f));
            // editor: every field has its label; the list next to Rule fills the rule from a ready mask of this CPU
            var edit = card.Stack(new LineRow(Theme.D(44)));
            edit.L(Theme.Lbl(L.T("Process"), Theme.F(9.5f), Theme.Dim)).BackColor = Theme.Card;
            _rName = edit.L(Theme.Box(Theme.D(170)));
            edit.L(Theme.Lbl(L.T("Rule"), Theme.F(9.5f), Theme.Dim)).BackColor = Theme.Card;
            _rRule = edit.L(Theme.Box(Theme.D(150)));
            edit.L(Theme.Lbl(L.T("or pick"), Theme.F(9.5f), Theme.Dim)).BackColor = Theme.Card;
            _rQuick = edit.L(Theme.Combo(Theme.D(200)));
            _rQuick.SelectedIndexChanged += (s, e) => { var o = _rQuick.SelectedItem as Opt; if (o != null) _rRule.Text = Masks.ToSpec(o.Value) ?? o.Value; };
            edit.R(new Btn(L.T("Add / update"), Btn.Style.Accent)).Click += (s, e) => AffRuleAdd();
            edit.R(new Btn(L.T("Remove"), Btn.Style.Outline)).Click += (s, e) => AffRuleDel();

            _affRules = Theme.Grid(); _affRules.ReadOnly = true; _affRules.Height = Theme.D(220);
            _affRules.Columns.Add(Theme.Col(L.T("Process"), 26)); _affRules.Columns.Add(Theme.Col(L.T("Rule"), 24)); _affRules.Columns.Add(Theme.Col(L.T("On this PC"), 14)); _affRules.Columns.Add(Theme.Col(L.T("Logical processors"), 22)); _affRules.Columns.Add(Theme.Col(L.T("State"), 16));
            _affRules.SelectionChanged += (s, e) => { if (_affRules.CurrentRow != null && !_affBuilding) { _rName.Text = Convert.ToString(_affRules.CurrentRow.Cells[0].Value); _rRule.Text = Convert.ToString(_affRules.CurrentRow.Cells[1].Value); } };
            card.Stack(_affRules);
            card.Stack(new Spacer(8));
            var strip = card.Stack(new WrapRow());
            strip.Add(new Btn(L.T("Fill from topology"), Btn.Style.Outline)).Click += (s, e) => AffRuleFill();
            strip.Add(new Btn(L.T("Apply rules now"), Btn.Style.Outline)).Click += (s, e) => { M.Log(M.Eng.ApplyAffRules()); RefreshAffRules(); };
            card.Stack(new TextBlock(L.T("Fill from topology writes a starting scheme for this PC: cs2.exe on the recommended mask, exitlag.exe and ts3client on free cores outside it. Edit or remove what you do not use."), Theme.Faint, 9f));
        }

        private void RefreshAffRules()
        {
            try { RefreshAffRulesCore(); } finally { PlSync(); }
        }

        private void RefreshAffRulesCore()
        {
            var t = T;
            _affBuilding = true;
            try
            {
                _rQuick.Items.Clear();
                if (t != null) foreach (var o in Masks.Options(t)) _rQuick.Items.Add(new Opt(o.Key, Explain.MaskLabel(o)));
                _affRules.Rows.Clear();
                foreach (var k in M.Eng.AffRules.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string rule = M.Eng.AffRules[k], err = "";
                    MaskResult mr = t != null ? Masks.Resolve(t, rule, out err) : null;
                    int run = MainForm.ByNameCount(k);
                    string state = run > 0 ? L.T("running") : "-";
                    if (mr == null) state = L.T("invalid");
                    else if (!mr.Satisfied) state += " / " + L.T("fallback");
                    else if (!mr.Portable) state += " / " + L.T("fixed LPs");
                    int i = _affRules.Rows.Add(k, rule, mr != null ? mr.Hex : "-", mr != null ? mr.LPs : err, state);
                    if (mr == null || !mr.Satisfied) _affRules.Rows[i].DefaultCellStyle.ForeColor = Theme.Warn;
                    else if (run > 0) _affRules.Rows[i].DefaultCellStyle.ForeColor = Theme.Ok;
                }
            }
            finally { _affBuilding = false; }
            Theme.FitRows(_affRules, 3, 10);
        }

        private void AffRuleAdd()
        {
            string n = _rName.Text.Trim();
            if (n.Length == 0) { Dlg.Info(this, L.T("Type a process name first (e.g. discord.exe).")); return; }
            if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
            string gate = Engine.Gate(n, Engine.Op.Affinity, Engine.ProtectedPriorityAllowed);
            if (gate != null) { Dlg.Info(this, Engine.IsAntiCheat(n) ? L.T("Anti-cheat - grobyq does not touch it") : L.T("Protected system process - its class and cores are never changed")); return; }
            var t = T; if (t == null) return;
            string err; var mr = Masks.Resolve(t, _rRule.Text, out err);
            if (mr == null) { Dlg.Info(this, L.F("'{0}' is not a valid rule: {1}", _rRule.Text, err)); return; }
            foreach (var note in mr.Notes) M.Log("[AF] " + note);
            string stored = mr.Portable ? mr.Spec : mr.Hex;
            M.Eng.AffRules[n] = stored;
            Settings.SaveAffRules(M.Eng.AffRules);
            RefreshAffRules();
            M.Log(string.Format("[AF] rule saved: {0} -> {1} = {2} LP {3}{4}", n, stored, mr.Hex, mr.LPs, mr.Portable ? "" : "  (fixed LPs)"));
        }

        private void AffRuleDel()
        {
            string n = _rName.Text.Trim();
            if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
            if (!M.Eng.AffRules.Remove(n)) return;
            Settings.SaveAffRules(M.Eng.AffRules);
            RefreshAffRules();
            M.Log("[AF] rule removed: " + n);
            M.Log(M.Eng.ReleaseAffinity(n));              // its running process gets the original cores back now
            if (M.Active) M.ApplyNow();                   // ...then the live layer (game mask / background) again
        }

        private void AffRuleFill()
        {
            var t = T; if (t == null) return;
            string name = Masks.Recommended(t);
            var game = Masks.Build(t, name);
            ulong rest = Masks.BgMask(t, game.Mask);
            var lps = Enumerable.Range(0, t.LogicalCount).Where(i => ((rest >> i) & 1) != 0).ToList();
            M.Eng.AffRules["cs2.exe"] = Masks.ToSpec(name) ?? game.Hex;
            if (lps.Count >= 2)
            {
                M.Eng.AffRules["exitlag.exe"] = "0x" + (((ulong)1 << lps[0]) | ((ulong)1 << lps[1])).ToString("X");
                M.Eng.AffRules["ts3client_win64.exe"] = "0x" + ((ulong)1 << lps[lps.Count - 1]).ToString("X");
            }
            else if (lps.Count == 1)
            {
                M.Eng.AffRules["exitlag.exe"] = "0x" + ((ulong)1 << lps[0]).ToString("X");
                M.Eng.AffRules["ts3client_win64.exe"] = "0x" + ((ulong)1 << lps[0]).ToString("X");
            }
            Settings.SaveAffRules(M.Eng.AffRules);
            RefreshAffRules();
            M.Log(string.Format("[AF] scheme from live topology: cs2.exe = {0}; exitlag / ts3 = fixed LPs of this PC", M.Eng.AffRules["cs2.exe"]));
        }
    }
}
