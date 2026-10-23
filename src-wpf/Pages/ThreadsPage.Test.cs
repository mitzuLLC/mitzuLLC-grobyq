using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    // Threads > Priority test (4.0.5): the main thread, render thread and job pool of CS2 / Dota 2 at a picked level instead
    // of the preset's - an A/B without launch options (PrioTest). A pick is stored and applied at once (a running session
    // restores and re-applies); the log notes every pick with the whole variant, the journal adds the time. Turn the test
    // off clears all three. Time critical (15) asks once per run: it is the top of the normal range, not a real-time level.
    public sealed partial class ThreadsPage
    {
        private readonly Dictionary<string, ComboBox> _test = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        private TextBlock _testState;
        private Button _testOff;
        private bool _tload;
        private static bool _tcAsked;                  // the Time critical question, once per run

        private void BuildTest()
        {
            Title(L.T("Priority test"));
            var card = Card();
            var intro = card.Add(Ui.Text(L.T("Other priorities for the three frame threads of CS2 and Dota 2, without launch options. A pick applies at once while the game threads optimizer runs and stays until Turn the test off; the log notes every pick with its time - match it to your CapFrameX runs. A launch option such as -mainthreadpriority is overridden unless the row is set to Game default."), 8.75, "Dim"));
            intro.Margin = new Thickness(0, 8, 0, 4);
            foreach (var slot in PrioTest.Slots)
            {
                var s = slot;
                var c = Combo.New(250);
                c.SelectionChanged += (o, e) => TestPicked(s, c);
                _test[s.Key] = c;
                card.Add(Ui.Row(s.Name, ThreadNotes.For(s.Pattern), c));
            }
            _testState = card.Add(Ui.Text("", 8.75, "Dim"));
            _testState.Margin = new Thickness(0, 8, 0, 2);
            _testOff = Ui.Btn(L.T("Turn the test off"), "BtnOutline");
            _testOff.Height = 30;
            _testOff.Click += (o, e) => TestOff();
            card.Add(Ui.Strip(42, _testOff));
            FillTest();
        }

        // The rows: first the preset's own level ("Reference: High (Highest) · 10" - the line that means no test), then Game
        // default and the other levels with the priority they give. The levels come from the saved cs2 preset.
        private void FillTest()
        {
            if (_testState == null) return;
            var o = Optimizer.Instance;
            Preset p = null;
            if (o != null && o.Eng.Presets != null) o.Eng.Presets.TryGetValue("cs2.exe", out p);
            if (p == null) Store.Defaults().TryGetValue("cs2.exe", out p);
            bool builtin = p != null && Store.IsBuiltin("cs2.exe", p);
            int cb = ClassBase(p != null ? p.Class : "Normal");
            _tload = true;
            try
            {
                foreach (var s in PrioTest.Slots)
                {
                    var c = _test[s.Key];
                    c.Items.Clear();
                    string pre = PresetPrio(p, s.Pattern);
                    if (pre.Length > 0 && string.Equals(PrioTest.Get(s), pre, StringComparison.OrdinalIgnoreCase)) PrioTest.Set(s, "");   // the same as the preset: no test
                    string lvl = pre.Length == 0 ? L.T("not touched") : TestLevel(pre, cb);
                    c.Items.Add(new Opt("", builtin ? L.F("Reference: {0}", lvl) : L.F("Preset: {0}", lvl)));
                    foreach (var v in s.Values)
                    {
                        if (v.Length == 0 || string.Equals(v, pre, StringComparison.OrdinalIgnoreCase)) continue;   // the first line already says it
                        c.Items.Add(new Opt(v, v == "keep" ? L.T("Game default (grobyq does not touch it)") : TestLevel(v, cb)));
                    }
                    Combo.Select(c, PrioTest.Get(s));
                }
            }
            finally { _tload = false; }
            UpdateTestState();
        }

        // the priority the preset's own rule for this thread sets ("" = no rule, or keep)
        private static string PresetPrio(Preset p, string pattern)
        {
            if (p == null) return "";
            var r = p.Threads.FirstOrDefault(x => x.Enabled && string.Equals((x.Pattern ?? "").Trim(), pattern, StringComparison.OrdinalIgnoreCase));
            return r != null ? r.Prio ?? "" : "";
        }

        // "Above normal · 9" - the level and the base priority it gives with the preset's class (Time critical: 15 always)
        private static string TestLevel(string v, int classBase)
        {
            string name;
            switch (v)
            {
                case "TimeCritical": return L.T("Time critical") + " · 15";
                case "Highest": name = L.T("High (Highest)"); break;
                case "AboveNormal": name = L.T("Above normal"); break;
                case "Normal": name = L.T("Normal"); break;
                case "BelowNormal": name = L.T("Below normal"); break;
                case "Lowest": name = L.T("Lowest"); break;
                case "Idle": return L.T("Idle") + " · 1";
                default: return v;
            }
            int off;
            if (!Engine.ThreadPrio(v, false, out off)) return name;
            return name + " · " + Math.Max(1, Math.Min(15, classBase + off)).ToString(CultureInfo.InvariantCulture);
        }

        private void TestPicked(PrioTest.Slot s, ComboBox c)
        {
            if (_tload) return;
            string v = Combo.Val(c), was = PrioTest.Get(s);
            if (string.Equals(v, was, StringComparison.Ordinal)) return;
            if (v == "TimeCritical" && !_tcAsked)
            {
                if (!Dlg.Ask(Window.GetWindow(this), L.T("Time critical (15) is the top of the normal priority range: on its core the thread runs before every thread that is not real-time, the input and audio helpers of other programs included. It is not a real-time level - grobyq never sets one - and it stays a test until you turn the test off. Use it for a test run?")))
                {
                    _tload = true;
                    try { Combo.Select(c, was); } finally { _tload = false; }
                    return;
                }
                _tcAsked = true;
            }
            PrioTest.Set(s, v);
            var o = Optimizer.Instance;
            if (o != null) o.TestChanged(s.Name + " " + (was.Length == 0 ? "preset" : was) + " -> " + (v.Length == 0 ? "preset" : v));
            UpdateTestState();
            UpdateState();
        }

        private void TestOff()
        {
            if (!PrioTest.Active) return;
            PrioTest.Clear();
            FillTest();
            var o = Optimizer.Instance;
            if (o != null) o.TestChanged("turned off");
            UpdateState();
        }

        private void UpdateTestState()
        {
            if (_testState == null) return;
            bool on = PrioTest.Active;
            _testOff.IsEnabled = on;
            if (!on)
            {
                _testState.Text = L.T("No test: the preset's priorities apply.");
                _testState.Foreground = Ui.B("Dim");
                return;
            }
            var o = Optimizer.Instance;
            string parts = string.Join("   ·   ", PrioTest.Slots.Where(s => PrioTest.Get(s).Length > 0).Select(s => s.Name + " " + ComboText(_test[s.Key], PrioTest.Get(s))));
            _testState.Text = L.F("Test on: {0}", parts) + (o != null && !o.GameOn ? "   ·   " + L.T("the game threads optimizer is off") : "");
            _testState.Foreground = Ui.B("Warn");
        }
    }
}
