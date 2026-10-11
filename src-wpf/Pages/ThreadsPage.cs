using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    // Threads: the optimizer switch on top, then WHAT it may change. v4 manual mode: every part starts off, edits are a
    // draft until Save (Cancel throws the draft away), and Save while the optimizer runs restores every baseline and
    // applies only the saved parts. DISABLE restores Windows defaults at any time.
    public sealed class ThreadsPage : PageBase
    {
        private readonly Button _toggle, _apply, _save, _cancel;
        private readonly TextBlock _state, _what, _stat, _saveText;
        private readonly TextBox _watch, _period;
        private readonly Dictionary<string, CheckBox> _switches = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _draft = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _loading;

        public ThreadsPage()
        {
            // ---- head: the switch, Apply now and what the optimizer is doing right now ----
            Title(L.T("Threads"));
            var head = Box(14);
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _toggle = Ui.Btn(L.T("ENABLE"), "BtnOk", 150);
            _toggle.Height = 36; _toggle.FontSize = Ui.Pt(11); _toggle.FontWeight = FontWeights.SemiBold;
            _toggle.Click += (s, e) => { var o = Optimizer.Instance; if (o != null) o.SetActive(!o.Active); };
            _apply = Ui.Btn(L.T("Apply now"), "BtnNormal", 120);
            _apply.Height = 36; _apply.Margin = new Thickness(12, 0, 0, 0);
            _apply.Click += (s, e) => { var o = Optimizer.Instance; if (o != null && o.Active) o.ApplyNow(); };
            _state = Ui.Semi(L.T("OFF"), 15, "Bad");
            _state.VerticalAlignment = VerticalAlignment.Center;
            _state.Margin = new Thickness(16, 0, 0, 0);
            top.Children.Add(_toggle); top.Children.Add(_apply); top.Children.Add(_state);
            head.Add(top);
            _what = head.Add(Ui.Text("", 9.5, "Dim", false));
            _what.Margin = new Thickness(1, 9, 0, 0);
            _stat = head.Add(Ui.Text("", 9, "Faint", false));
            _stat.Margin = new Thickness(1, 2, 0, 8);

            // ---- what the optimizer changes (each part off until you switch it on and Save) ----
            Title(L.T("Optimizer"));
            var card = Card();
            _watch = Ui.Box(Settings.Get("WatchList"), 300);
            _watch.TextChanged += (s, e) => { if (!_loading) Draft("WatchList", (_watch.Text ?? "").Trim()); };
            card.Add(Ui.Row(L.T("Target processes"), L.T("Games grobyq waits for (space separated). When one starts, the parts switched on below are applied; when it exits, the optimizer goes back to armed."), _watch));
            card.Add(SwitchRow(L.T("Game process priority"), L.T("The game gets its preset class (High, never Realtime), normal I/O and memory priority and EcoQoS off while it runs."), "DoProcess"));
            card.Add(SwitchRow(L.T("Game core mask"), L.F("The game is pinned to the core mask from Affinity. Current rule: {0}", Settings.Get("Mask")), "DoMask"));
            card.Add(SwitchRow(L.T("Optimize threads"), L.T("A scheduler hint for the game: Ideal CPU plus a priority on its busiest threads (named rules and hot threads). Off = no thread of the game is touched."), "DoThreads"));
            card.Add(SwitchRow(L.T("Avoid CPU 0 for hot threads"), L.T("On 6+ core CPUs no hot thread gets CPU 0 as its Ideal CPU (interrupt-heavy by default)."), "SkipCpu0"));
            card.Add(SwitchRow(L.T("Per-process affinity rules"), L.T("Apply the per-process affinity rules from Affinity > Processes (e.g. VCache+NoSMT for cs2.exe, fixed LPs for voice / VPN tools)."), "DoAffRules"));
            card.Add(SwitchRow(L.T("Thread presets for programs and services"), L.T("Apply the thread priority rules of every preset, not only the game's (NVIDIA containers, Steam, Discord, services). Priority and boost only; restored by DISABLE."), "ApplyAllPresets"));
            card.Add(SwitchRow(L.T("Re-apply periodically"), L.T("Every few seconds, while the game runs: catches new threads and anything that reset the priority."), "PeriodicReapply"));
            _period = Ui.Box(PeriodSeconds(Settings.Get("Period")), 80);
            _period.TextChanged += (s, e) =>
            {
                if (_loading) return;
                int sec;
                if (int.TryParse((_period.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sec))
                    Draft("Period", (Math.Max(1, Math.Min(60, sec)) * 1000).ToString(CultureInfo.InvariantCulture));
            };
            card.Add(Ui.Row(L.T("Re-apply period, seconds"), L.T("2 s is the balance: 1 s re-scans more than it helps, 5 s can miss threads created while a map loads."), _period, false));

            // ---- background processes ----
            Title(L.T("Background processes"));
            var bg = Card();
            bg.Add(SwitchRow(L.T("Lower background processes"), L.T("The processes in the list below get a lower class, VeryLow I/O and Low memory priority while the game runs."), "DoBackground"));
            bg.Add(SwitchRow(L.T("Pin background off the game cores"), L.T("Background processes get the cores the game mask leaves free (E-cores, SMT siblings, the other CCD). Helps most on multi-CCD CPUs or with heavy background; neutral otherwise - measure."), "BgAffinity"));
            bg.Add(SwitchRow(L.T("Release background on alt-tab"), L.T("Recommended on. When the game loses focus, background processes get their priority and cores back."), "BgForeground", false));
            var list = bg.Add(Ui.Text(string.Join("  ", Settings.BgProcs()), 9, "Faint"));
            list.Margin = new Thickness(0, 0, 0, 10);

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

            UpdateSaveBar(null);
            Loaded += (s, e) => { if (Optimizer.Instance != null) Optimizer.Instance.StateChanged += UpdateState; UpdateState(); };
            Unloaded += (s, e) => { if (Optimizer.Instance != null) Optimizer.Instance.StateChanged -= UpdateState; };
        }

        private static string PeriodSeconds(string ms)
        {
            int v; if (!int.TryParse(ms, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = 2000;
            return Math.Max(1, Math.Min(60, v / 1000)).ToString(CultureInfo.InvariantCulture);
        }

        private Border SwitchRow(string title, string desc, string key, bool divider = true)
        {
            var t = Ui.Switch(Settings.GetBool(key));
            t.Click += (s, e) => { if (!_loading) Draft(key, t.IsChecked == true ? "1" : "0"); };
            _switches[key] = t;
            return Ui.Row(title, desc, t, divider);
        }

        // ------------------------------------------------------------------ draft / save
        private void Draft(string key, string value)
        {
            if (string.Equals(value ?? "", Settings.Get(key) ?? "", StringComparison.Ordinal)) _draft.Remove(key);
            else _draft[key] = value ?? "";
            UpdateSaveBar(null);
        }

        private void UpdateSaveBar(string done)
        {
            bool dirty = _draft.Count > 0;
            _save.IsEnabled = dirty;
            _cancel.IsEnabled = dirty;
            if (dirty) { _saveText.Text = L.F("Unsaved changes: {0}", _draft.Count); _saveText.Foreground = Ui.B("Warn"); }
            else if (done != null) { _saveText.Text = done; _saveText.Foreground = Ui.B("Ok"); }
            else { _saveText.Text = L.T("Nothing is changed until you switch a part on, press Save and ENABLE."); _saveText.Foreground = Ui.B("Dim"); }
        }

        private void SaveDraft()
        {
            if (_draft.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in _draft)
            {
                string v = kv.Value;
                if (string.Equals(kv.Key, "WatchList", StringComparison.OrdinalIgnoreCase) && v.Trim().Length == 0) v = "cs2.exe";
                Settings.Set(kv.Key, v);
                parts.Add(kv.Key + "=" + v);
            }
            _draft.Clear();
            AppLog.Add("[i] saved: " + string.Join(", ", parts));
            var o = Optimizer.Instance;
            bool active = o != null && o.Active;
            if (o != null) o.Reapply();
            Resync();
            UpdateSaveBar(active ? L.T("Saved - applied now.") : L.T("Saved. Press ENABLE to apply."));
        }

        private void CancelDraft()
        {
            _draft.Clear();
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
                _watch.Text = Settings.Get("WatchList");
                _period.Text = PeriodSeconds(Settings.Get("Period"));
            }
            finally { _loading = false; }
        }

        // ------------------------------------------------------------------ state
        private void UpdateState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)UpdateState); return; }
            var o = Optimizer.Instance;
            string st = o != null ? o.StateName : "OFF";
            bool on = o != null && o.Active;
            _state.Text = st == "ACTIVE" ? L.T("ACTIVE") : st == "ARMED" ? L.T("ARMED") : L.T("OFF");
            _state.Foreground = Ui.B(st == "ACTIVE" ? "Ok" : st == "ARMED" ? "Warn" : "Bad");
            _toggle.Content = on ? L.T("DISABLE") : L.T("ENABLE");
            _toggle.Style = Ui.S(on ? "BtnDanger" : "BtnOk");
            _apply.IsEnabled = on;
            string targets = o != null && o.Eng.Targets != null ? string.Join(", ", o.Eng.Targets) : "cs2.exe";
            _what.Text = st == "ACTIVE" ? L.F("optimizing {0}", o.LiveTarget) : st == "ARMED" ? L.F("armed - waiting for {0}", targets) : L.T("optimizer disabled - nothing is changed");
            if (o != null)
                _stat.Text = L.F("threads {0}   ·   background {1}   ·   {2}", o.Eng.LastThreads, o.Eng.LastBg, o.Eng.BgOn ? L.T("background pinned") : L.T("background released"));
        }

        public override void OnEnter() { UpdateState(); }
        public override void OnTick(int tick) { if (tick % 2 == 0) UpdateState(); }
    }
}
