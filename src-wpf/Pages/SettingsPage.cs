using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;

namespace Grobyq.Pages
{
    // Language, startup and window behaviour, data folder (the v3.6.8 Settings page).
    // App-level only: what grobyq optimizes is chosen on Threads / Affinity.
    public sealed class SettingsPage : PageBase
    {
        private readonly CheckBox _auto;
        private bool _syncAuto;

        public SettingsPage()
        {
            Title(L.T("Settings"));

            // ---- A. language ----
            Section(L.T("Interface language"), true);
            var lc = Card();
            var intro = lc.Add(Ui.Text(L.T("The whole interface switches immediately, with the right font for the script. Technical names (registry keys, API names, timings, tiers T0–T3) stay in English on purpose, and so does the log: it is A/B evidence and must read the same on every PC."), 9, "Dim"));
            intro.Margin = new Thickness(0, 6, 0, 0);
            var chips = new UniformGrid { Columns = L.All.Length, Rows = 1, Margin = new Thickness(0, 10, 0, 6) };
            for (int i = 0; i < L.All.Length; i++)
            {
                var lang = L.All[i];
                string code = lang.Code;
                var b = Ui.Btn(lang.Chip ?? lang.Native, code == L.Code ? "BtnAccent" : "BtnOutline");
                b.Height = 34;
                b.Margin = new Thickness(i == 0 ? 0 : 3, 0, i == L.All.Length - 1 ? 0 : 3, 0);
                if (!string.Equals(lang.Native, lang.English, StringComparison.Ordinal)) b.ToolTip = lang.Native + "  ·  " + lang.English;
                b.Click += (s, e) => Dispatcher.BeginInvoke((Action)(() => Switch(code)));
                chips.Children.Add(b);
            }
            lc.Add(chips);
            var cur = lc.Add(Ui.Text(L.F("Current: {0}  ·  font: {1}", L.Cur.Native, L.Family), 8.75, "Faint"));
            cur.Margin = new Thickness(0, 0, 0, 6);

            // ---- B. startup and window: one toggle pattern, control on the title line ----
            Section(L.T("Startup and window"), false);
            var bc = Card();
            bc.Add(ToggleRow(L.T("Close to tray"), L.T("The window close button hides grobyq to the tray instead of exiting (exit from the tray menu)."), "CloseToTray"));
            bc.Add(ToggleRow(L.T("Restore Windows defaults on exit"), L.T("On exit every process and thread grobyq changed goes back to its captured baseline. Off = changes stay until a reboot or until the optimizers are switched off."), "RestoreOnExit"));
            _auto = Ui.Switch(false);
            _auto.IsEnabled = false;                       // the real state is read from Task Scheduler on enter
            _auto.Click += (s, e) => SetAutostart(_auto.IsChecked == true);
            bc.Add(Ui.Row(L.T("Start with Windows"), L.T("A Task Scheduler task at logon with highest privileges starts grobyq in the tray (/tray). No service, no driver."), _auto, false));

            // ---- C. data ----
            Section(L.T("Data"), false);
            var dc = Card();
            var open = Ui.Btn(L.T("Open folder"), "BtnOutline");
            open.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(Store.Dir); Process.Start(new ProcessStartInfo { FileName = Store.Dir, UseShellExecute = true }); } catch { }
            };
            dc.Add(Ui.Row(L.T("Data folder"), L.F("Presets, profiles and the backups grobyq makes before it changes anything: {0}", Store.Dir), open));
            var reset = Ui.Btn(L.T("Reset settings"), "BtnDangerQuiet");
            reset.Click += (s, e) => ResetSettings();
            dc.Add(Ui.Row(L.T("Reset grobyq settings"), L.T("Mask, watch list, toggles and window options back to the first-run defaults (HKCU\\Software\\grobyq). Presets, backups and the license are not touched."), reset, false));
        }

        private static Border ToggleRow(string title, string desc, string key)
        {
            var t = Ui.Switch(Settings.GetBool(key));
            t.Click += (s, e) =>
            {
                bool on = t.IsChecked == true;
                Settings.SetBool(key, on);
                AppLog.Add("[UI] " + key + " = " + (on ? "on" : "off"));
            };
            return Ui.Row(title, desc, t);
        }

        // ------------------------------------------------------------------ autostart = the scheduled task
        public override void OnEnter() { RefreshAutostart(); }

        // Reads the real state from Task Scheduler (not from a remembered caption).
        private void RefreshAutostart()
        {
            _auto.IsEnabled = false;
            var th = new Thread(() =>
            {
                bool on = TaskExists();
                try
                {
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _syncAuto = true; try { _auto.IsChecked = on; } finally { _syncAuto = false; }
                        _auto.IsEnabled = true;
                    }));
                }
                catch { }
            }) { IsBackground = true };
            th.Start();
        }

        private void SetAutostart(bool on)
        {
            if (_syncAuto) return;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                Autostart(on);                          // schtasks /Create (ONLOGON, HIGHEST, /tray) or /Delete
                bool real = TaskExists();
                if (real != on)
                {
                    _syncAuto = true; try { _auto.IsChecked = real; } finally { _syncAuto = false; }
                    Dlg.Info(null, on ? L.T("The autostart task could not be created. Run grobyq as administrator.") : L.T("The autostart task could not be removed. Run grobyq as administrator."));
                }
            }
            finally { Mouse.OverrideCursor = null; }
        }

        public static void Autostart(bool enable)
        {
            string exe = ExePath();
            try
            {
                if (enable)
                {
                    Run("schtasks", string.Format("/Delete /TN \"{0}\" /F", Brand.OldTaskName));   // v2 task would start the old exe
                    Run("schtasks", string.Format("/Create /TN \"{0}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{1}\\\" /tray\" /F", Brand.TaskName, exe));
                    AppLog.Add("[i] autostart task created (runs " + Path.GetFileName(exe) + " /tray at logon)");
                }
                else { Run("schtasks", string.Format("/Delete /TN \"{0}\" /F", Brand.TaskName)); AppLog.Add("[i] autostart task removed"); }
            }
            catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
        }

        private static string ExePath()
        {
            try { if (!string.IsNullOrEmpty(Environment.ProcessPath)) return Environment.ProcessPath; } catch { }
            try { return Process.GetCurrentProcess().MainModule.FileName; } catch { return "grobyq.exe"; }
        }

        private static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
            }
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
            AppLog.Add("[UI] interface language = " + L.Cur.English);
            var w = MainWindow.Current;
            if (w != null) w.RebuildUi();
        }

        private void ResetSettings()
        {
            if (!Dlg.Ask(null, L.T("Reset all grobyq settings to the first-run defaults? Presets, backups and the license stay."), true)) return;
            string lang = L.Code;
            var o = Optimizer.Instance;
            if (o != null) o.StopAll();                    // the switches go off with the settings: every baseline back first
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\grobyq", false);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\grobyq")) k.SetValue("Language", lang);
            }
            catch (Exception ex) { AppLog.Add("[!!] reset: " + ex.Message); return; }
            AppLog.Add("[UI] settings reset to defaults - restart grobyq to reload everything");
            Dlg.Info(null, L.T("Done. Restart grobyq so every page reloads the defaults."));
        }
    }
}
