using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobyq
{
    // Language, startup and window behaviour, data folder.
    internal sealed class SettingsPage : Page
    {
        private readonly MainForm M;
        private Toggle _auto;
        private SettingRow _autoRow;
        private bool _syncAuto;

        public SettingsPage(MainForm m)
        {
            M = m;
            Title(L.T("Settings"));

            // ---- A. language ----
            Section(L.T("Interface language"), true);
            var lc = Card();
            lc.Stack(new TextBlock(L.T("The whole interface switches immediately, with the right font for the script. Technical names (registry keys, API names, timings, tiers T0–T3) stay in English on purpose, and so does the log: it is A/B evidence and must read the same on every PC."), Theme.Dim, 9f));
            var grid = lc.Stack(new ChipGrid());
            foreach (var lang in L.All)
            {
                var code = lang.Code;
                var b = new Btn(lang.Chip ?? lang.Native, code == L.Code ? Btn.Style.Accent : Btn.Style.Outline) { AutoGrow = false, Height = Theme.D(34) };
                if (!string.Equals(lang.Native, lang.English, StringComparison.Ordinal)) Tips.Set(b, lang.Native + "  ·  " + lang.English);
                b.Click += (s, e) => BeginInvoke((Action)(() => Switch(code)));   // the switch disposes this button: never inside its Click
                grid.Add(b);
            }
            lc.Stack(new TextBlock(L.F("Current: {0}  ·  font: {1}", L.Cur.Native, L.Family), Theme.Faint, 8.75f));

            // ---- B. startup and window: one toggle pattern, control on the title line ----
            Section(L.T("Startup and window"), false);
            var bc = Card();
            bc.Stack(ToggleRow(L.T("Enable at start"), L.T("Start armed: the optimizer waits for a target as soon as grobyq opens."), "AutoEnable"));
            bc.Stack(ToggleRow(L.T("Close to tray"), L.T("The window close button hides grobyq to the tray instead of exiting (exit from the tray menu)."), "CloseToTray"));
            bc.Stack(ToggleRow(L.T("Restore Windows defaults on exit"), L.T("On exit every process and thread grobyq changed goes back to its captured baseline, the optimizer on or off (manual Live threads edits included). Off = changes stay until a reboot or DISABLE."), "RestoreOnExit"));
            _auto = new Toggle(false) { Enabled = false };
            _auto.CheckedChanged += (s, e) => SetAutostart(_auto.Checked);
            _autoRow = bc.Stack(new SettingRow(L.T("Start with Windows"), L.T("A Task Scheduler task at logon with highest privileges starts grobyq in the tray (/tray). No service, no driver."), _auto) { AlignTop = true, Divider = false });

            // ---- C. data ----
            Section(L.T("Data"), false);
            var dc = Card();
            var open = new Btn(L.T("Open folder"), Btn.Style.Outline);
            open.Click += (s, e) => { try { Directory.CreateDirectory(Store.Dir); Process.Start(Store.Dir); } catch { } };
            dc.Stack(new SettingRow(L.T("Data folder"), L.F("Presets, profiles and the backups grobyq makes before it changes anything: {0}", Store.Dir), open) { AlignTop = true });
            var reset = new Btn(L.T("Reset settings"), Btn.Style.DangerQuiet);
            reset.Click += (s, e) => ResetSettings();
            dc.Stack(new SettingRow(L.T("Reset grobyq settings"), L.T("Mask, watch list, toggles and window options back to the first-run defaults (HKCU\\Software\\grobyq). Presets, backups and the license are not touched."), reset) { AlignTop = true, Divider = false });
        }

        // Section title with the Settings spacing (20 px between sections, 8 px to the card).
        private void Section(string text, bool first)
        {
            var l = new Label { Text = text, Font = Theme.Semi(13f), ForeColor = Theme.Fg, BackColor = Theme.Bg, AutoSize = false, Height = Theme.D(30), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, AutoEllipsis = true };
            Add(l, 6).Margin = new Padding(0, first ? Theme.D(2) : Theme.D(12), 0, Theme.D(6));
        }

        // Card with the Settings inner padding; 12 px to the next block.
        private Card Card()
        {
            var c = new Card { Padding = new Padding(Theme.D(16), Theme.D(8), Theme.D(16), Theme.D(8)) };
            return Add(c, 12);
        }

        private SettingRow ToggleRow(string title, string desc, string key)
        {
            var t = new Toggle(Settings.GetBool(key));
            t.CheckedChanged += (s, e) => Settings.SetBool(key, t.Checked);
            return new SettingRow(title, desc, t) { AlignTop = true };
        }

        // ------------------------------------------------------------------ autostart = the existing scheduled task
        public override void OnEnter() { RefreshAutostart(); }

        // Reads the real state from Task Scheduler (not from a remembered caption).
        private void RefreshAutostart()
        {
            _auto.Enabled = false;
            var th = new System.Threading.Thread(() =>
            {
                bool on = TaskExists();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _syncAuto = true; try { _auto.Checked = on; } finally { _syncAuto = false; }
                        _auto.Enabled = true;
                    }));
                }
                catch { }
            }) { IsBackground = true };
            th.Start();
        }

        private void SetAutostart(bool on)
        {
            if (_syncAuto) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                M.Autostart(on);                        // schtasks /Create (ONLOGON, HIGHEST, /tray) or /Delete
                bool real = TaskExists();
                if (real != on)
                {
                    _syncAuto = true; try { _auto.Checked = real; } finally { _syncAuto = false; }
                    Dlg.Info(this, on ? L.T("The autostart task could not be created. Run grobyq as administrator.") : L.T("The autostart task could not be removed. Run grobyq as administrator."));
                }
            }
            finally { Cursor = Cursors.Default; }
        }

        private static bool TaskExists()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", string.Format("/Query /TN \"{0}\"", Brand.TaskName)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(5000)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ language / reset
        private void Switch(string code)
        {
            if (code == L.Code) return;
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\grobyq")) k.SetValue("Language", code); } catch { }
            L.Load(code);
            M.Log("[UI] interface language = " + L.Cur.English);
            M.RebuildUi();
        }

        private void ResetSettings()
        {
            if (!Dlg.Ask(this, L.T("Reset all grobyq settings to the first-run defaults? Presets, backups and the license stay."), true)) return;
            string lang = L.Code;
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\grobyq", false);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\grobyq")) k.SetValue("Language", lang);
            }
            catch (Exception ex) { M.Log("[!!] reset: " + ex.Message); return; }
            M.Log("[UI] settings reset to defaults - restart grobyq to reload everything");
            Dlg.Info(this, L.T("Done. Restart grobyq so every page reloads the defaults."));
        }
    }

    // Equal-width chips in rows that fill evenly: 7 per row when the widest label fits (14 languages = 7 + 7),
    // otherwise the largest column count that fits. Width comes from the available space, not fixed pixels.
    internal sealed class ChipGrid : Panel
    {
        private readonly List<Btn> _chips = new List<Btn>();
        public ChipGrid() { BackColor = Theme.Card; Height = Theme.D(80); }

        public void Add(Btn b) { _chips.Add(b); Controls.Add(b); PerformLayout(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_chips.Count == 0 || Width < 20) return;
            int gap = Theme.D(6), top = Theme.D(6);
            int need = _chips.Max(b => TextRenderer.MeasureText(b.Text ?? "", b.Font).Width) + Theme.D(14);
            int cols = 1;
            foreach (int c in new[] { 7, 5, 4, 3, 2 })
                if ((Width - gap * (c - 1)) / c >= need) { cols = c; break; }
            cols = Math.Min(cols, _chips.Count);
            int w = (Width - gap * (cols - 1)) / cols, h = _chips[0].Height;
            for (int i = 0; i < _chips.Count; i++)
            {
                int r = i / cols, c = i % cols;
                _chips[i].SetBounds(c * (w + gap), top + r * (h + gap), w, h);
            }
            int rows = (_chips.Count + cols - 1) / cols;
            int total = top + rows * h + (rows - 1) * gap + Theme.D(4);
            if (Height != total) Height = total;
        }
    }
}
