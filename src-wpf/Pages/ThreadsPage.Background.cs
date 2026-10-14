using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Grobyq.Pages
{
    // Threads > Background processes: the list as chips (program, what it gets while the game runs - a lower class, or
    // Close for the game mode; click to change - and remove), plus Add (a running program or an .exe). Part of the page
    // draft: Save writes the list (Settings "BgProcs") and the classes (presets.json "background"), Cancel drops the edits.
    // The bottom of the page also has the thread-settings reset.
    public sealed partial class ThreadsPage
    {
        private static readonly string[] BgClassOrder = { "BelowNormal", "Idle", "Normal", "Close" };

        private WrapPanel _bgPanel;
        private Button _bgAdd;
        private readonly List<string> _bgList = new List<string>();                                                     // draft list
        private readonly Dictionary<string, string> _bgCls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // draft classes

        private void BuildBackgroundList(CardBox bg)
        {
            var desc = bg.Add(Ui.Text(L.T("Each program gets what its chip says (click to change): a lower priority class with VeryLow I/O and Low memory while the game runs, or Close - closed when the game launches and reopened when it exits (only when the switch above is on). System processes (dwm, audiodg, csrss, explorer, svchost...) are protected and never touched."), 8.75, "Dim"));
            desc.Margin = new Thickness(0, 10, 0, 8);
            _bgPanel = bg.Add(new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 0, 0, 10) });
            _bgAdd = Ui.Btn("+  " + L.T("Add"), "BtnOutline");
            _bgAdd.Height = 28; _bgAdd.Padding = new Thickness(12, 0, 12, 0); _bgAdd.Margin = new Thickness(4, 0, 0, 6);
            _bgAdd.Click += (s, e) => AddBg();
        }

        private static string BgClassText(string c)
        {
            return c == "Idle" ? L.T("Idle") : c == "Normal" ? L.T("Normal") : c == "Close" ? L.T("Close") : L.T("Below normal");
        }

        private string BgClassOf(string exe)
        {
            string c;
            if (!_bgCls.TryGetValue(exe, out c) || !BgClassOrder.Contains(c)) return "BelowNormal";
            if (c == "Close" && Engine.Companions.Contains(Engine.BareName(exe))) return "BelowNormal";   // what the engine does with it
            return c;
        }

        private void FillBg()
        {
            if (_bgPanel == null) return;
            _bgPanel.Children.Clear();
            foreach (var n in _bgList) _bgPanel.Children.Add(BgChip(n));
            _bgPanel.Children.Add(_bgAdd);
        }

        private Border BgChip(string exe)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var t = Ui.Text(exe, 9.5, "Fg", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.MaxWidth = 220;
            p.Children.Add(t);

            // the class pill: click = next class (Below normal -> Idle -> Normal)
            string cls = BgClassOf(exe);
            var ct = Ui.Text(BgClassText(cls), 8.25, cls == "Idle" ? "Faint" : cls == "Normal" ? "Fg" : cls == "Close" ? "Warn" : "Dim", false);
            ct.VerticalAlignment = VerticalAlignment.Center;
            var pill = new Border
            {
                Height = 20, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(10),
                Background = Ui.B("Hot"), BorderBrush = Ui.B("Edge"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Child = ct, ToolTip = L.T("What happens to it while the game runs - click to change")
            };
            pill.MouseEnter += (s, e) => pill.BorderBrush = Ui.B("EdgeDn");
            pill.MouseLeave += (s, e) => pill.BorderBrush = Ui.B("Edge");
            pill.MouseLeftButtonUp += (s, e) => { e.Handled = true; CycleBg(exe); };
            p.Children.Add(pill);

            var x = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = L.T("Remove") };
            var xi = new Ico { Kind = "x", Width = 9, Height = 9, Stroke = Ui.B("Dim"), StrokeWidth = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            x.Child = xi;
            x.MouseEnter += (s, e) => { x.Background = Ui.B("Edge"); xi.Stroke = Ui.B("Fg"); };
            x.MouseLeave += (s, e) => { x.Background = Brushes.Transparent; xi.Stroke = Ui.B("Dim"); };
            x.MouseLeftButtonUp += (s, e) => { e.Handled = true; RemoveBg(exe); };
            p.Children.Add(x);

            return new Border
            {
                Height = 30,
                Padding = new Thickness(11, 0, 4, 0),
                Margin = new Thickness(4, 0, 8, 6),
                CornerRadius = new CornerRadius(15),
                Background = Ui.B("Input"),
                BorderBrush = Ui.B("Edge"),
                BorderThickness = new Thickness(1),
                Child = p
            };
        }

        private void CycleBg(string exe)
        {
            int i = Array.IndexOf(BgClassOrder, BgClassOf(exe));
            string next = BgClassOrder[(i + 1) % BgClassOrder.Length];
            // Steam and its helpers are never "Close": the game's Steam API calls need them running
            if (next == "Close" && Engine.Companions.Contains(Engine.BareName(exe))) next = BgClassOrder[(i + 2) % BgClassOrder.Length];
            if (next == "BelowNormal") _bgCls.Remove(exe); else _bgCls[exe] = next;
            BgChanged();
        }

        private void AddBg()
        {
            var exclude = new HashSet<string>(_bgList, StringComparer.OrdinalIgnoreCase);
            foreach (var g in _games) exclude.Add(g);
            string exe = ProcPicker.Pick(Window.GetWindow(this), L.T("Add a background program"), exclude);
            if (string.IsNullOrEmpty(exe)) return;
            if (Engine.IsAntiCheat(exe)) { Dlg.Info(Window.GetWindow(this), L.T("Anti-cheat - grobyq does not touch it")); return; }
            if (Engine.IsProtected(exe)) { Dlg.Info(Window.GetWindow(this), L.F("{0} is a protected system process: lowering it hurts.", exe)); return; }
            if (_games.Any(g => string.Equals(g, exe, StringComparison.OrdinalIgnoreCase))) return;     // a game is never background
            if (_bgList.Any(n => string.Equals(n, exe, StringComparison.OrdinalIgnoreCase))) return;
            _bgList.Add(exe);
            BgChanged();
        }

        private void RemoveBg(string exe)
        {
            _bgList.RemoveAll(n => string.Equals(n, exe, StringComparison.OrdinalIgnoreCase));
            _bgCls.Remove(exe);
            BgChanged();
        }

        private void BgChanged()
        {
            FillBg();
            Draft("BgProcs", string.Join(" ", _bgList));   // the classes are compared in BgClassesDirty
        }

        // the classes saved now (presets.json "background"); the engine's copy, else the built-in defaults
        private static Dictionary<string, string> SavedBgClasses()
        {
            var o = Optimizer.Instance;
            var src = o != null && o.Eng.BgClasses != null ? o.Eng.BgClasses : Store.DefaultBg();
            return new Dictionary<string, string>(src, StringComparer.OrdinalIgnoreCase);
        }

        private bool BgClassesDirty()
        {
            var saved = SavedBgClasses();
            foreach (var n in _bgList)
            {
                string a, b;
                string ca = _bgCls.TryGetValue(n, out a) ? a : "BelowNormal";
                string cb = saved.TryGetValue(n, out b) ? b : "BelowNormal";
                if (!string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void SaveBgClasses()
        {
            if (!BgClassesDirty()) return;
            var o = Optimizer.Instance;
            if (o == null) return;
            var keep = SavedBgClasses();
            foreach (var n in _bgList) { string c; if (_bgCls.TryGetValue(n, out c) && c != "BelowNormal") keep[n] = c; else keep.Remove(n); }
            o.Eng.UnderLock(() => o.Eng.BgClasses = keep);
            string err = o.Eng.SavePresets();
            AppLog.Add(err.Length == 0 ? "[BG] background classes saved" : "[!!] presets.json: " + err);
        }

        private void ResyncBg()
        {
            _bgList.Clear();
            _bgList.AddRange(Settings.BgProcs());
            _bgCls.Clear();
            foreach (var kv in SavedBgClasses()) _bgCls[kv.Key] = kv.Value;
            FillBg();
        }

        // ------------------------------------------------------------------ reset (Threads only)
        private void ResetThreads()
        {
            if (!Dlg.Ask(Window.GetWindow(this), L.T("Reset the thread settings? Both optimizers turn off and every change goes back to its Windows default; the games, parts, game presets and the background list return to the first-run defaults. Affinity and the app settings stay."), true)) return;
            var o = Optimizer.Instance;
            if (o != null) o.StopAll();
            Settings.Reset("WatchList", "GameLabels", "DoProcess", "DoThreads", "PeriodicReapply", "Period", "TopN",
                           "DoBackground", "CloseBackground", "BgAffinity", "BgForeground", "BgProcs", "ApplyAllPresets", "AllowProtectedPriority",
                           "AdvancedThreadMode", "GameOpt", "SystemThreadOpt", "AutoEnable", "StarveGuard");
            if (o != null)
            {
                o.Eng.UnderLock(() => { o.Eng.Presets = Store.Defaults(); o.Eng.BgClasses = Store.DefaultBg(); });
                string err = o.Eng.SavePresets();
                if (err.Length > 0) AppLog.Add("[!!] presets.json: " + err);
                o.Reapply();                                // nothing is on now: only the game list is taken over
            }
            AppLog.Add("[i] thread settings reset to the first-run defaults");
            _draft.Clear();
            _edit.Clear();
            Resync();
            UpdateSaveBar(L.T("Thread settings reset to the defaults."));
            UpdateState();
        }
    }
}
