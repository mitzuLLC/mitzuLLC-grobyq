using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Grobyq.Pages
{
    // Threads. Top: the two optimizer switches (game threads / system threads) with one small status line each - they act
    // at once and keep their position, the same switches as in the tray menu. Below: the games the game optimizer waits
    // for and WHAT it may change. v4 manual mode: every part starts off, edits are a draft until Save (Cancel throws the
    // draft away); Save while an optimizer runs restores every baseline and applies only the saved parts.
    public sealed partial class ThreadsPage : PageBase
    {
        // games offered as ready checkboxes: exe -> label
        private static readonly string[][] KnownGames = { new[] { "cs2.exe", "CS2" }, new[] { "dota2.exe", "Dota 2" } };

        private readonly CheckBox _gameSw, _sysSw, _frontSw, _guardSw, _closeSw;
        private readonly Ellipse _gameDot, _sysDot;
        private readonly TextBlock _gameStat, _sysStat;
        private readonly Dictionary<string, BoxCheck> _known = new Dictionary<string, BoxCheck>(StringComparer.OrdinalIgnoreCase);
        private readonly WrapPanel _gamesPanel;
        private readonly Button _addGame;
        private readonly List<string> _games = new List<string>();                                                   // draft: watched exes
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // draft: added-game labels

        private readonly Button _save, _cancel;
        private readonly TextBlock _saveText;
        private readonly Dictionary<string, CheckBox> _switches = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _draft = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _loading;

        public ThreadsPage()
        {
            Title(L.T("Threads"));

            // ---- the two optimizers: a switch and one status line each ----
            var top = Card(14);
            _gameSw = Ui.Switch(false);
            _gameSw.Click += (s, e) => { OptUi.Game(_gameSw.IsChecked == true); UpdateState(); };
            top.Add(OptimizerRow(L.T("Game threads optimizer"),
                L.T("When a game below starts, applies what is switched on below and saved. The game closes - everything goes back."),
                _gameSw, out _gameDot, out _gameStat, true));
            _sysSw = Ui.Switch(false);
            _sysSw.Click += (s, e) =>
            {
                if (!OptUi.System(_sysSw.IsChecked == true, Window.GetWindow(this))) { var o = Optimizer.Instance; _sysSw.IsChecked = o != null && o.SysOn; }
                UpdateState();
            };
            top.Add(OptimizerRow(L.T("System threads optimizer"),
                L.T("grobyq's built-in profile while a game runs: thread priorities of system processes, NVIDIA, Steam and Discord. Priority and boost only, fully reversible."),
                _sysSw, out _sysDot, out _sysStat, true));
            // WHEN both apply: acts at once, like the switches above
            _frontSw = Ui.Switch(Settings.GetBool("BgForeground"));
            _frontSw.Click += (s, e) =>
            {
                bool on = _frontSw.IsChecked == true;
                Settings.SetBool("BgForeground", on);
                AppLog.Add("[UI] only while the game is in front = " + (on ? "on" : "off"));
                var o = Optimizer.Instance; if (o != null) o.Refresh();
                UpdateState();
            };
            top.Add(Ui.Row(L.T("Only while the game is in front"), L.T("Off (default): applied once when the game starts and kept until it closes - alt-tab costs nothing, nothing is re-applied when you come back. On: minimize or alt-tab puts everything back to normal; back in the game - applied again."), _frontSw));
            _guardSw = Ui.Switch(Settings.GetBool("StarveGuard"));
            _guardSw.Click += (s, e) =>
            {
                bool on = _guardSw.IsChecked == true;
                Settings.SetBool("StarveGuard", on);
                AppLog.Add("[UI] freeze guard = " + (on ? "on" : "off"));
                var o = Optimizer.Instance; if (o != null) o.ApplyNow();      // in a game session: restore, apply with the new rule
            };
            top.Add(Ui.Row(L.T("Freeze guard"), L.T("Recommended on. DWM, Windows services and Steam never go below Below normal: under the game, above the background apps. Without it a busy browser can starve a thread the game waits on, and the game freezes for seconds."), _guardSw, false));

            // ---- games ----
            Title(L.T("Games"));
            var gc = Card();
            var gdesc = gc.Add(Ui.Text(L.T("The game threads optimizer waits for these. Your own game: Add game and pick its .exe."), 8.75, "Dim"));
            gdesc.Margin = new Thickness(0, 8, 0, 8);
            _gamesPanel = gc.Add(new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 0, 0, 10) });
            foreach (var g in KnownGames)
            {
                var b = new BoxCheck(g[1]) { Margin = new Thickness(0, 0, 14, 6), Padding = new Thickness(0, 0, 8, 0) };
                string exe = g[0];
                b.ToolTip = exe;
                b.CheckedChanged += () => { if (_loading) return; ToggleGame(exe, b.Checked); };
                _known[exe] = b;
            }
            _addGame = Ui.Btn("+  " + L.T("Add game"), "BtnOutline");
            _addGame.Height = 28; _addGame.Padding = new Thickness(12, 0, 12, 0); _addGame.Margin = new Thickness(4, 0, 0, 6);
            _addGame.Click += (s, e) => AddGame();

            // ---- what the game optimizer changes (each part off until you switch it on and Save) ----
            Title(L.T("Game optimizer"));
            var card = Card();
            card.Add(SwitchRow(L.T("Game process priority"), L.T("The game gets its preset class (Normal by default - raise it in the game preset; never Realtime), normal I/O and memory priority and EcoQoS off while it runs."), "DoProcess"));
            card.Add(SwitchRow(L.T("Optimize threads"), L.T("A scheduler hint for the game: Ideal CPU plus a priority on its busiest threads (named rules and hot threads). Off = no thread of the game is touched."), "DoThreads"));
            card.Add(SwitchRow(L.T("Catch new threads"), L.T("While the game runs: every 2 s new threads of the game and of the profile's processes get their settings. What is already set is read, never written again."), "PeriodicReapply"));
            var toAff = Ui.Btn(L.T("Open Affinity"), "BtnOutline");
            toAff.Click += (s, e) => { var w = MainWindow.Current; if (w != null) w.Navigate("affinity"); };
            card.Add(Ui.Row(L.T("Cores"), L.T("The game core mask and the per-process rules have their own switches on Affinity (Cores / Processes), saved there."), toAff, false));

            // ---- the picked game's preset and thread rules (ThreadsPage.Preset.cs) ----
            BuildPreset();

            // ---- background processes ----
            Title(L.T("Background processes"));
            var bg = Card();
            bg.Add(SwitchRow(L.T("Lower background processes"), L.T("The processes in the list below get a lower class, VeryLow I/O and Low memory priority while the game runs."), "DoBackground"));
            bg.Add(SwitchRow(L.T("Pin background off the game cores"), L.T("Background processes get the cores the game mask leaves free (E-cores, SMT siblings, the other CCD). Helps most on multi-CCD CPUs or with heavy background; neutral otherwise - measure."), "BgAffinity"));
            // game mode: an immediate switch (acts at once, like the ones at the top), not a draft - closing / reopening is a
            // runtime action, fully reversible. Only the chips marked "Close" below are affected.
            _closeSw = Ui.Switch(Settings.GetBool("CloseBackground"));
            _closeSw.Click += (s, e) =>
            {
                bool on = _closeSw.IsChecked == true;
                Settings.SetBool("CloseBackground", on);
                AppLog.Add("[UI] close background on game launch = " + (on ? "on" : "off"));
                var o = Optimizer.Instance; if (o != null) o.CloseModeChanged();
            };
            bg.Add(Ui.Row(L.T("Close background on game launch"), L.T("Acts at once. When a game from the list starts, the programs you mark 'Close' below are asked to close (grobyq never force-kills), then reopened when the game exits. Mark only apps safe to close - unsaved work in them may be lost; an app set to minimize to tray will go to its tray."), _closeSw));
            BuildBackgroundList(bg);                         // chips + Add (ThreadsPage.Background.cs)

            // ---- reset: this page only (the whole app resets in Settings) ----
            var rs = Card();
            var resetBtn = Ui.Btn(L.T("Reset thread settings"), "BtnDangerQuiet");
            resetBtn.Click += (s, e) => ResetThreads();
            rs.Add(Ui.Row(L.T("Reset thread settings"), L.T("Both optimizers off, every change back to its Windows default; games, parts, game presets and the background list back to the first-run defaults. Affinity and the app settings stay."), resetBtn, false));

            // ---- the Save bar, pinned under the page ----
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _saveText = Ui.Text("", 9.5, "Dim", false);
            _saveText.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(_saveText);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            _cancel = Ui.Btn(L.T("Cancel"), "BtnOutline", 110);
            _cancel.Click += (s, e) => CancelDraft();
            _save = Ui.Btn(L.T("Save"), "BtnAccent", 130);
            _save.Margin = new Thickness(8, 0, 0, 0);
            _save.Click += (s, e) => SaveDraft();
            btns.Children.Add(_cancel); btns.Children.Add(_save);
            Grid.SetColumn(btns, 1);
            bar.Children.Add(btns);
            Footer(bar);

            Resync();
            UpdateSaveBar(null);
            Loaded += (s, e) => { if (Optimizer.Instance != null) Optimizer.Instance.StateChanged += UpdateState; UpdateState(); };
            Unloaded += (s, e) => { if (Optimizer.Instance != null) Optimizer.Instance.StateChanged -= UpdateState; };
        }

        // title + explanation + a small status line (dot + text) on the left, the switch on the title line
        private static Border OptimizerRow(string title, string desc, CheckBox sw, out Ellipse dot, out TextBlock stat, bool divider)
        {
            var g = new Grid { MinHeight = 64 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 10, 24, 10) };
            left.Children.Add(Ui.Semi(title, 10.5, "Fg"));
            var d = Ui.Text(desc, 8.75, "Dim");
            d.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(d);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            dot = new Ellipse { Width = 6, Height = 6, Fill = Ui.B("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 1, 7, 0) };
            stat = Ui.Text("", 8.5, "Faint", false);
            stat.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(dot); line.Children.Add(stat);
            left.Children.Add(line);
            g.Children.Add(left);
            sw.VerticalAlignment = VerticalAlignment.Top;
            sw.Margin = new Thickness(0, 11, 0, 0);
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            return new Border { Child = g, BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(0, 0, 0, divider ? 1 : 0), SnapsToDevicePixels = true };
        }

        private Border SwitchRow(string title, string desc, string key, bool divider = true)
        {
            var t = Ui.Switch(Settings.GetBool(key));
            t.Click += (s, e) => { if (!_loading) Draft(key, t.IsChecked == true ? "1" : "0"); };
            _switches[key] = t;
            return Ui.Row(title, desc, t, divider);
        }

        // ------------------------------------------------------------------ games
        private static Dictionary<string, string> ParseLabels(string v)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in (v ?? "").Split('|'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) d[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }
            return d;
        }

        private static string JoinLabels(Dictionary<string, string> d)
        {
            return string.Join("|", d.Select(kv => kv.Key + "=" + kv.Value));
        }

        private void ToggleGame(string exe, bool on)
        {
            int i = _games.FindIndex(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
            if (on && i < 0) _games.Add(exe);
            if (!on && i >= 0) _games.RemoveAt(i);
            GamesChanged();
        }

        private void AddGame()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = L.T("Add game"),
                Filter = L.T("Game") + " (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            try { if (dlg.ShowDialog(Window.GetWindow(this)) != true) return; } catch { return; }
            string path = dlg.FileName ?? "";
            string exe = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(exe)) return;
            if (Engine.IsAntiCheat(exe)) { Dlg.Info(Window.GetWindow(this), L.T("Anti-cheat - grobyq does not touch it")); return; }
            if (Engine.IsProtected(exe)) { Dlg.Info(Window.GetWindow(this), L.F("{0} is a protected system process: lowering it hurts.", exe)); return; }
            if (_games.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase))) return;
            string label = "";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                label = (vi.ProductName ?? "").Trim();
                if (label.Length == 0 || label.Length > 28) label = (vi.FileDescription ?? "").Trim();
            }
            catch { }
            if (label.Length == 0 || label.Length > 28) label = System.IO.Path.GetFileNameWithoutExtension(exe);
            label = label.Replace("|", " ").Replace("=", " ");
            _games.Add(exe);
            _labels[exe] = label;
            GamesChanged();
        }

        private void RemoveGame(string exe)
        {
            _games.RemoveAll(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
            _labels.Remove(exe);
            GamesChanged();
        }

        private void GamesChanged()
        {
            FillGames();
            FillPick();
            Draft("WatchList", string.Join(" ", _games));
            var keep = _labels.Where(kv => _games.Any(x => string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase)))
                              .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            Draft("GameLabels", JoinLabels(keep));
        }

        // the ready checkboxes, then a chip per added game (label + remove), then Add game
        private void FillGames()
        {
            _loading = true;
            try
            {
                _gamesPanel.Children.Clear();
                foreach (var g in KnownGames)
                {
                    var b = _known[g[0]];
                    b.SetSilently(_games.Any(x => string.Equals(x, g[0], StringComparison.OrdinalIgnoreCase)));
                    _gamesPanel.Children.Add(b);
                }
                foreach (var exe in _games)
                {
                    if (KnownGames.Any(g => string.Equals(g[0], exe, StringComparison.OrdinalIgnoreCase))) continue;
                    string label; if (!_labels.TryGetValue(exe, out label) || string.IsNullOrEmpty(label)) label = exe;
                    _gamesPanel.Children.Add(Chip(label, exe));
                }
                _gamesPanel.Children.Add(_addGame);
            }
            finally { _loading = false; }
        }

        private Border Chip(string label, string exe)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Ui.B("Acc"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
            p.Children.Add(dot);
            var t = Ui.Text(label, 9.5, "Fg", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.MaxWidth = 220;
            p.Children.Add(t);
            var x = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 0, 0, 0), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = L.T("Remove") };
            var xi = new Ico { Kind = "x", Width = 9, Height = 9, Stroke = Ui.B("Dim"), StrokeWidth = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            x.Child = xi;
            x.MouseEnter += (s, e) => { x.Background = Ui.B("Edge"); xi.Stroke = Ui.B("Fg"); };
            x.MouseLeave += (s, e) => { x.Background = Brushes.Transparent; xi.Stroke = Ui.B("Dim"); };
            x.MouseLeftButtonUp += (s, e) => { e.Handled = true; RemoveGame(exe); };
            p.Children.Add(x);
            return new Border
            {
                Height = 28,
                Padding = new Thickness(10, 0, 4, 0),
                Margin = new Thickness(4, 0, 10, 6),
                CornerRadius = new CornerRadius(14),
                Background = Ui.B("Input"),
                BorderBrush = Ui.B("Edge"),
                BorderThickness = new Thickness(1),
                Child = p,
                ToolTip = exe
            };
        }

        // ------------------------------------------------------------------ draft / save
        private void Draft(string key, string value)
        {
            // the background list keeps a stored empty list empty (Get would answer the default list)
            string cur = string.Equals(key, "BgProcs", StringComparison.OrdinalIgnoreCase) ? string.Join(" ", Settings.BgProcs()) : Settings.Get(key) ?? "";
            if (string.Equals(value ?? "", cur, StringComparison.Ordinal)) _draft.Remove(key);
            else _draft[key] = value ?? "";
            UpdateSaveBar(null);
        }

        private void UpdateSaveBar(string done)
        {
            int changes = _draft.Count + DirtyPresets().Count + (BgClassesDirty() ? 1 : 0);
            bool dirty = changes > 0;
            bool noGame = _games.Count == 0;
            _save.IsEnabled = dirty && !noGame;
            _cancel.IsEnabled = dirty;
            if (noGame) { _saveText.Text = L.T("Pick at least one game."); _saveText.Foreground = Ui.B("Warn"); }
            else if (dirty) { _saveText.Text = L.F("Unsaved changes: {0}", changes); _saveText.Foreground = Ui.B("Warn"); }
            else if (done != null) { _saveText.Text = done; _saveText.Foreground = Ui.B("Ok"); }
            else { _saveText.Text = L.T("Edit, then Save. The switches at the top turn the optimizers on and off at once."); _saveText.Foreground = Ui.B("Dim"); }
        }

        private void SaveDraft()
        {
            if ((_draft.Count == 0 && DirtyPresets().Count == 0 && !BgClassesDirty()) || _games.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in _draft)
            {
                Settings.Set(kv.Key, kv.Value);
                parts.Add(kv.Key + "=" + kv.Value);
            }
            _draft.Clear();
            if (parts.Count > 0) AppLog.Add("[i] saved: " + string.Join(", ", parts));
            SavePresets();
            SaveBgClasses();
            var o = Optimizer.Instance;
            bool active = o != null && o.Active;
            if (o != null) o.Reapply();
            Resync();
            UpdateSaveBar(active ? L.T("Saved - applied now.") : L.T("Saved. Turn the game threads optimizer on to apply."));
        }

        private void CancelDraft()
        {
            _draft.Clear();
            _edit.Clear();
            Resync();
            UpdateSaveBar(null);
        }

        // Every control shows the stored value again (no side effects).
        private void Resync()
        {
            _loading = true;
            try
            {
                foreach (var kv in _switches) kv.Value.IsChecked = Settings.GetBool(kv.Key);
                if (_frontSw != null) _frontSw.IsChecked = Settings.GetBool("BgForeground");
                if (_guardSw != null) _guardSw.IsChecked = Settings.GetBool("StarveGuard");
                if (_closeSw != null) _closeSw.IsChecked = Settings.GetBool("CloseBackground");
                _games.Clear();
                _games.AddRange(Settings.WatchList());
                _labels.Clear();
                foreach (var kv in ParseLabels(Settings.Get("GameLabels"))) _labels[kv.Key] = kv.Value;
            }
            finally { _loading = false; }
            FillGames();
            FillPick();
            ResyncBg();
        }

        // ------------------------------------------------------------------ state
        private void UpdateState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)UpdateState); return; }
            var o = Optimizer.Instance;
            bool game = o != null && o.GameOn, sys = o != null && o.SysOn, session = o != null && o.InSession;
            _gameSw.IsChecked = game;
            _sysSw.IsChecked = sys;
            string live = o != null ? o.LiveTarget : "";
            string targets = o != null && o.Eng.Targets != null ? string.Join(", ", o.Eng.Targets) : "cs2.exe";
            bool userBg = game && Settings.GetBool("DoBackground");

            if (!game) Stat(_gameDot, _gameStat, "Faint", L.T("off - games are not touched"));
            else if (string.IsNullOrEmpty(live)) Stat(_gameDot, _gameStat, "Warn", L.F("armed - waiting for {0}", targets));
            else if (!session) Stat(_gameDot, _gameStat, "Warn", L.F("{0} is minimized - normal mode", live));
            else
            {
                string t = L.F("optimizing {0}", live) + "   ·   " + L.F("threads: {0}", o.Eng.GameThreadCount);
                if (userBg) t += "   ·   " + L.F("background: {0}", o.Eng.BgOn ? o.Eng.LastBg : 0);
                Stat(_gameDot, _gameStat, "Ok", t);
            }

            if (!sys) Stat(_sysDot, _sysStat, "Faint", L.T("off - system processes are not touched"));
            else if (!session) Stat(_sysDot, _sysStat, "Warn", string.IsNullOrEmpty(live) ? L.T("waiting for a game - nothing is changed now") : L.F("{0} is minimized - normal mode", live));
            else
            {
                string t = L.F("threads: {0}   ·   processes: {1}", o.Eng.SysThreadCount, o.Eng.SysProcCount);
                if (!userBg && o.Eng.BgOn) t += "   ·   " + L.F("background: {0}", o.Eng.LastBg);
                Stat(_sysDot, _sysStat, "Ok", t);
            }
        }

        private static void Stat(Ellipse dot, TextBlock text, string brush, string value)
        {
            dot.Fill = Ui.B(brush);
            text.Text = value;
            text.Foreground = Ui.B(brush == "Faint" ? "Faint" : "Dim");
        }

        public override void OnEnter() { UpdateState(); }
        public override void OnTick(int tick) { if (tick % 2 == 0) UpdateState(); }
    }
}
