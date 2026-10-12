using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    // Affinity: everything about cores, in four tabs so nothing is a long scroll (the v3.6.8 page).
    //   Cores        - the game core mask (base rule, per-LP grid, interrupt cores), this CPU + advice, the CPU map
    //   Processes    - per-process affinity rules (pick a process, tick its cores) and the rule list
    //   Interrupts   - one row per PCI device, click to edit MSI / limit / policy / target CPUs / priority
    //   Reservation  - ReservedCpuSets for the non-game set (families A / C)
    //
    // v4 manual mode: the two switches (game core mask, per-process rules) and every mask edit are a draft until Save;
    // Cancel throws the draft away. Nothing reaches Windows until the optimizer is ENABLEd, and DISABLE restores it.
    // Interrupt and reservation changes are their own explicit buttons (registry, backed up, revertible).
    public sealed partial class AffinityPage : PageBase
    {
        private readonly Tabs _tabs;
        private readonly Dictionary<string, StackPanel> _sections = new Dictionary<string, StackPanel>();
        private readonly Button _save, _cancel;
        private readonly TextBlock _saveText;
        private bool _loading;

        private static Topo T { get { return SafeTopo(); } }
        private static Topo _topo;
        private static DateTime _topoAt;
        internal static Topo SafeTopo()
        {
            if (_topo != null && (DateTime.UtcNow - _topoAt).TotalSeconds < 30) return _topo;
            try { _topo = Topo.Live(); _topoAt = DateTime.UtcNow; } catch { }
            return _topo;
        }

        private static Engine Eng { get { return Optimizer.Instance != null ? Optimizer.Instance.Eng : null; } }

        public AffinityPage() : base(1100)
        {
            LoadDraft();
            Title(L.T("Affinity"));
            _tabs = Add(new Tabs(), 12);
            _tabs.AddTab("cores", L.T("Cores"));
            _tabs.AddTab("procs", L.T("Processes"));
            _tabs.AddTab("irq", L.T("Interrupts"));
            _tabs.AddTab("resv", L.T("Reservation"));
            _tabs.Changed += k => ShowSection(k);

            BuildCores(Section("cores"));
            BuildProcs(Section("procs"));
            BuildIrq(Section("irq"));
            BuildResv(Section("resv"));
            ShowSection("cores");

            // ---- the Save bar for the switches and the mask draft ----
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _saveText = Ui.Text("", 9.5, "Dim", false);
            _saveText.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(_saveText);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            _cancel = Ui.Btn(L.T("Cancel"), "BtnOutline", 110);
            _cancel.Click += (s, e) => { LoadDraft(); RefreshAll(); UpdateSaveBar(null); };
            _save = Ui.Btn(L.T("Save"), "BtnAccent", 130);
            _save.Margin = new Thickness(8, 0, 0, 0);
            _save.Click += (s, e) => SaveDraft();
            btns.Children.Add(_cancel); btns.Children.Add(_save);
            Grid.SetColumn(btns, 1);
            bar.Children.Add(btns);
            Footer(bar);

            CoresRefresh(true);
            UpdateSaveBar(null);
        }

        private StackPanel Section(string key)
        {
            var p = new StackPanel();
            _sections[key] = p;
            Body.Children.Add(p);
            return p;
        }

        private void ShowSection(string key)
        {
            foreach (var kv in _sections) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
            Scroll.ScrollToTop();
            if (key == "irq") IrqEnter();
            if (key == "resv") ResvReload();
            if (key == "procs") PlRefresh();
        }

        // Section helpers inside a tab (same look as the page helpers).
        private static TextBlock SubTitle(StackPanel p, string text, bool first = false)
        {
            var l = Ui.Semi(text, 13f, "Fg");
            l.Padding = new Thickness(0, 6, 0, 0);
            l.Margin = new Thickness(0, first ? 0 : 10, 0, 6);
            p.Children.Add(l);
            return l;
        }

        private static CardBox SubCard(StackPanel p, double padH = 20, double padTop = 10, double padBottom = 14)
        {
            var c = new CardBox(10, padH, padTop) { Margin = new Thickness(0, 0, 0, 12) };
            c.Padding = new Thickness(padH, padTop, padH, padBottom);
            p.Children.Add(c);
            return c;
        }

        // ------------------------------------------------------------------ draft (switches + game mask)
        private string _pBase = "", _pMode = GameMask.IrqThread;
        private ulong _pManual;
        private bool _pDoMask, _pDoAff;

        private void LoadDraft()
        {
            var t = T;
            _pBase = Settings.Get("Mask");
            _pMode = GameMask.Mode;
            _pManual = t != null ? GameMask.Info(t, false, null).Manual : 0;
            _pDoMask = Settings.GetBool("DoMask");
            _pDoAff = Settings.GetBool("DoAffRules");
        }

        private int DirtyCount()
        {
            var t = T;
            int n = 0;
            if (!string.Equals(_pBase, Settings.Get("Mask"), StringComparison.OrdinalIgnoreCase)) n++;
            if (_pMode != GameMask.Mode) n++;
            if (t != null && _pManual != GameMask.Info(t, false, null).Manual && string.Equals(_pBase, Settings.Get("Mask"), StringComparison.OrdinalIgnoreCase)) n++;
            if (_pDoMask != Settings.GetBool("DoMask")) n++;
            if (_pDoAff != Settings.GetBool("DoAffRules")) n++;
            return n;
        }

        private void UpdateSaveBar(string done)
        {
            if (_save == null || _cancel == null || _saveText == null) return;     // still building
            int n = DirtyCount();
            _save.IsEnabled = n > 0;
            _cancel.IsEnabled = n > 0;
            if (n > 0) { _saveText.Text = L.F("Unsaved changes: {0}", n); _saveText.Foreground = Ui.B("Warn"); }
            else if (done != null) { _saveText.Text = done; _saveText.Foreground = Ui.B("Ok"); }
            else { _saveText.Text = L.T("Nothing is changed until you switch a part on and press Save. It applies while the game threads optimizer is on."); _saveText.Foreground = Ui.B("Dim"); }
        }

        private void SaveDraft()
        {
            var t = T;
            if (t == null) return;
            ulong baseM = Masks.Build(t, _pBase).Mask;
            if ((_pManual & t.FullMask()) == 0) { Dlg.Info(null, L.T("Tick at least one core.")); return; }
            bool cleared = GameMask.SetBase(_pBase);
            ulong add, drop;
            GameMask.EditsFrom(baseM, _pManual, out add, out drop);
            GameMask.SetEdits(add, drop);
            GameMask.SetMode(_pMode);
            Settings.SetBool("DoMask", _pDoMask);
            Settings.SetBool("DoAffRules", _pDoAff);
            GameMask.Invalidate();
            AppLog.Add(string.Format("[AF] saved: game core mask {0}, base {1}{2}{3}, interrupt cores {4}, per-process rules {5}{6}",
                _pDoMask ? "on" : "off", _pBase, add != 0 ? ", +LP " + Masks.Ranges(add) : "", drop != 0 ? ", -LP " + Masks.Ranges(drop) : "",
                _pMode, _pDoAff ? "on" : "off", cleared ? "  (old grid edits cleared)" : ""));
            var o = Optimizer.Instance;
            bool active = o != null && o.Active;
            if (o != null) o.Reapply();
            LoadDraft();
            RefreshAll();
            UpdateSaveBar(active ? L.T("Saved - applied now.") : L.T("Saved. Turn the game threads optimizer on to apply."));
        }

        private void RefreshAll()
        {
            CoresRefresh(true);
            PlRefresh();
        }

        public override void OnEnter()
        {
            GameMask.Invalidate();            // interrupt pins re-read: another tool may have moved one
            if (DirtyCount() == 0) LoadDraft();
            CoresRefresh(true);
            if (_tabs.Selected == "procs") PlRefresh();
            if (_tabs.Selected == "irq") IrqEnter();
            if (_tabs.Selected == "resv") ResvReload();
            UpdateSaveBar(null);
        }

        private static int CountBits(ulong m) { int n = 0; while (m != 0) { m &= m - 1; n++; } return n; }
        private static KeyValuePair<string, string> KV(string k, string v) { return new KeyValuePair<string, string>(k, v ?? ""); }
    }
}
