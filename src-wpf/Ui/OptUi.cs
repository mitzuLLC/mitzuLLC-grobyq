using System.Windows;

namespace Grobyq
{
    // The two optimizer switches as the user flips them (Threads page, tray menu). The system optimizer asks once before
    // it may tune protected system processes: the built-in profile has rules for dwm / svchost / audiodg / csrss.
    internal static class OptUi
    {
        public static void Game(bool on)
        {
            var o = Optimizer.Instance;
            if (o != null) o.SetGame(on);
        }

        // false = declined, nothing changed
        public static bool System(bool on, Window owner)
        {
            var o = Optimizer.Instance;
            if (o == null) return false;
            if (on && !Settings.GetBool("AllowProtectedPriority"))
            {
                int a = Dlg.Show(owner, L.T("System thread optimization also tunes protected system processes (dwm, svchost, audiodg, csrss, winlogon...). Enable 'Priority on protected processes' and turn it on?\n\nPriority and boost only - reversible; nothing is frozen or killed."),
                                 false, L.T("Yes"), L.T("Without protected"), L.T("Cancel"));
                if (a == 0) { Settings.Set("AllowProtectedPriority", "1"); AppLog.Add("[i] priority on protected processes: ON (advanced)"); }
                else if (a != 1) return false;
            }
            o.SetSystem(on);
            return true;
        }
    }
}
