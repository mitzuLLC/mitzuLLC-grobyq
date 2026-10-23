using System;
using System.Windows;
using Microsoft.Win32;

namespace Grobyq
{
    // The two optimizer switches as the user flips them (Threads page, tray menu), and the factory reset (Threads and
    // Settings pages).
    internal static class OptUi
    {
        public static void Game(bool on)
        {
            var o = Optimizer.Instance;
            if (o != null) o.SetGame(on);
        }

        // false = declined, nothing changed. 4.0.4: the system optimizer always runs its whole built-in profile, and that
        // profile includes protected system processes (dwm, svchost, audiodg, csrss, winlogon, explorer, sihost) - so the
        // first switch-on asks once (turn on / cancel). The yes is kept ("AllowProtectedPriority"); a factory reset asks again.
        public static bool System(bool on, Window owner)
        {
            var o = Optimizer.Instance;
            if (o == null) return false;
            if (on && !Settings.GetBool("AllowProtectedPriority"))
            {
                int a = Dlg.Show(owner, L.T("The system threads optimizer lowers the thread priorities of system processes too (dwm, svchost, audiodg, csrss, winlogon, explorer) while a game runs. Priority and boost only: nothing is frozen or killed, and everything goes back when the game closes.\n\nTurn it on?"),
                                 false, L.T("Turn on"), L.T("Cancel"));
                if (a != 0) return false;
                Settings.Set("AllowProtectedPriority", "1");
                AppLog.Add("[i] system threads optimizer: the built-in profile with protected system processes confirmed");
            }
            o.SetSystem(on);
            return true;
        }

        // Factory reset: grobyq as after a fresh install, without a restart. Both optimizers off and every baseline back
        // (Optimizer.FactoryReset waits for the restore), presets.json to the built-in reference, the settings key
        // (HKCU\Software\grobyq) cleared except the interface language, the autostart task removed, then every page is
        // rebuilt from the defaults. Kept: the language, the license, the Affinity backups (interrupt settings and
        // ReservedCpuSets are system settings with their own restore on the Affinity page).
        public static bool FactoryReset(Window owner)
        {
            string q = L.T("Reset grobyq to its factory settings? Both optimizers turn off and every change goes back; the settings, games and thread reference return to the first-run defaults, and autostart is removed. The language and the license stay.");
            bool sys = false;
            try { Reservation.State st = Reservation.Read(); sys = Devices.BackedUp().Count > 0 || (st != null && st.HasBackup); } catch { }
            if (sys) q += "\n\n" + L.T("Interrupt settings and ReservedCpuSets changed on the Affinity page are Windows settings: they stay until you restore them on that page.");
            if (!Dlg.Ask(owner, q, true)) return false;

            var o = Optimizer.Instance;
            if (o != null) { try { o.FactoryReset(); } catch (Exception ex) { AppLog.Add("[!!] factory reset: " + ex.Message); } }
            try { if (Pages.SettingsPage.TaskExists()) Pages.SettingsPage.Autostart(false); } catch { }
            string lang = L.Code;
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\grobyq", false);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\grobyq")) k.SetValue("Language", lang);
            }
            catch (Exception ex) { AppLog.Add("[!!] factory reset: settings key: " + ex.Message); }
            if (o != null) o.Reapply();                       // the default watch list (cs2.exe); both switches are off
            AppLog.Add("[i] factory reset: settings, games, presets and switches back to the first-run defaults (language, license and Affinity backups kept)");
            // after the click that started it has returned (the page that owns the button is replaced): every page reads
            // the defaults again, the current page stays
            Action rebuild = () =>
            {
                var w = MainWindow.Current;
                if (w != null) w.RebuildUi();
                Dlg.Info(null, L.T("grobyq is back to its factory settings."));
            };
            try { Application.Current.Dispatcher.BeginInvoke(rebuild); } catch { rebuild(); }
            return true;
        }
    }
}
