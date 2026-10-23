using System;
using System.Windows;

namespace Grobyq.Pages
{
    // Threads > reset (this page only; the whole app resets in Settings). 4.0.2: the background-process list that used to
    // live in this file is removed (owner decision); ctfmon.exe and TextInputHost.exe moved into the system threads
    // optimizer (bundled profile).
    public sealed partial class ThreadsPage
    {
        // ------------------------------------------------------------------ reset (Threads only)
        private void ResetThreads()
        {
            if (!Dlg.Ask(Window.GetWindow(this), L.T("Reset the thread settings? Both optimizers turn off and every change goes back to its Windows default; the games, parts and game presets return to the first-run defaults. Affinity and the app settings stay."), true)) return;
            var o = Optimizer.Instance;
            if (o != null) o.StopAll();
            Settings.Reset("WatchList", "GameLabels", "DoThreads", "Period", "TopN",
                           "DoBackground", "CloseBackground", "BgAffinity", "BgProcs", "ApplyAllPresets", "AllowProtectedPriority",
                           "AdvancedThreadMode", "GameOpt", "SystemThreadOpt", "AutoEnable");
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
