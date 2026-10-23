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
    // at once and keep their position, the same switches as in the tray menu; each runs a reference built into grobyq.
    // Below: the games the game optimizer waits for and its reference for the picked game (read-only, an editor folded
    // under it), then the factory reset. Game list and editor changes are a draft until Save (Cancel throws the draft
    // away); Save while an optimizer runs restores every baseline and applies the saved state afresh.
    public sealed partial class ThreadsPage : PageBase
    {
        // games offered as ready checkboxes: exe -> label
        private static readonly string[][] KnownGames = { new[] { "cs2.exe", "CS2" }, new[] { "dota2.exe", "Dota 2" } };

        private readonly CheckBox _gameSw, _sysSw;
        private readonly Ellipse _gameDot, _sysDot;
        private readonly TextBlock _gameStat, _sysStat;
        private readonly Dictionary<string, BoxCheck> _known = new Dictionary<string, BoxCheck>(StringComparer.OrdinalIgnoreCase);
        private readonly WrapPanel _gamesPanel;
        private readonly Button _addGame;
        private readonly List<string> _games = new List<string>();                                                   // draft: watched exes
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // draft: added-game labels

        private readonly Button _save, _cancel;
        private readonly TextBlock _saveText;
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
                L.T("The built-in reference for the games below: the thread priorities of the game's main thread, job pool and render thread. Applied while the game runs; the game closes - everything goes back."),
                _gameSw, out _gameDot, out _gameStat, true));
            _sysSw = Ui.Switch(false);
            _sysSw.Click += (s, e) =>
            {
                if (!OptUi.System(_sysSw.IsChecked == true, Window.GetWindow(this))) { var o = Optimizer.Instance; _sysSw.IsChecked = o != null && o.SysOn; }
                UpdateState();
            };
            top.Add(OptimizerRow(L.T("System threads optimizer"),
                L.T("The built-in system profile while a game runs: lower thread priorities for system processes, NVIDIA, Steam and Discord (most at the lowest, Idle), ctfmon and TextInputHost entirely at the lowest. Priority boost stays where a thread has to wake on time (audio, input, composition) and is off only for busy background apps. Fully reversible."),
                _sysSw, out _sysDot, out _sysStat, false));

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

            // ---- the picked game's reference (ThreadsPage.Preset.cs) ----
            BuildPreset();

            // ---- factory reset: the whole app, the same as Settings > Reset to factory settings ----
            var rs = Card();
            var resetBtn = Ui.Btn(L.T("Factory reset"), "BtnDangerQuiet");
            resetBtn.Click += (s, e) => OptUi.FactoryReset(Window.GetWindow(this));   // rebuilds every page, this one too
            rs.Add(Ui.Row(L.T("Reset to factory settings"), L.T("grobyq as after a fresh install: both optimizers off and every change back, settings, games and the thread reference to the first-run defaults, autostart off. The language, the license and the Affinity backups stay."), resetBtn, false));

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
            string cur = Settings.Get(key) ?? "";
            if (string.Equals(value ?? "", cur, StringComparison.Ordinal)) _draft.Remove(key);
            else _draft[key] = value ?? "";
            UpdateSaveBar(null);
        }

        private void UpdateSaveBar(string done)
        {
            int changes = _draft.Count + DirtyPresets().Count;
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
            if ((_draft.Count == 0 && DirtyPresets().Count == 0) || _games.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in _draft)
            {
                Settings.Set(kv.Key, kv.Value);
                parts.Add(kv.Key + "=" + kv.Value);
            }
            _draft.Clear();
            if (parts.Count > 0) AppLog.Add("[i] saved: " + string.Join(", ", parts));
            SavePresets();
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
                _games.Clear();
                _games.AddRange(Settings.WatchList());
                _labels.Clear();
                foreach (var kv in ParseLabels(Settings.Get("GameLabels"))) _labels[kv.Key] = kv.Value;
            }
            finally { _loading = false; }
            FillGames();
            FillPick();
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

            if (!game) Stat(_gameDot, _gameStat, "Faint", L.T("off - games are not touched"));
            else if (string.IsNullOrEmpty(live) || !session) Stat(_gameDot, _gameStat, "Warn", L.F("armed - waiting for {0}", targets));
            else
            {
                string t = L.F("optimizing {0}", live) + "   ·   " + RunningRules(o, live) + "   ·   " + L.F("threads: {0}", o.Eng.GameThreadCount);
                Stat(_gameDot, _gameStat, "Ok", t);
            }

            if (!sys) Stat(_sysDot, _sysStat, "Faint", L.T("off - system processes are not touched"));
            else if (!session) Stat(_sysDot, _sysStat, "Warn", L.T("waiting for a game - nothing is changed now"));
            else
            {
                string t = L.F("threads: {0}   ·   processes: {1}", o.Eng.SysThreadCount, o.Eng.SysProcCount);
                if (o.Eng.BgOn) t += "   ·   " + L.F("background: {0}", o.Eng.LastBg);   // ctfmon / TextInputHost
                Stat(_sysDot, _sysStat, "Ok", t);
            }
        }

        // "built-in reference" when every running game runs the reference of this version, else "your rules"
        private static string RunningRules(Optimizer o, string live)
        {
            bool all = true;
            foreach (var exe in (live ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Preset p;
                if (o.Eng.Presets == null || !o.Eng.Presets.TryGetValue(exe, out p) || !Store.IsBuiltin(exe, p)) { all = false; break; }
            }
            return all ? L.T("built-in reference") : L.T("your rules");
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
