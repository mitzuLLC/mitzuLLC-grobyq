using System;
using System.Threading;
using System.Windows;

namespace Grobyq
{
    // App-wide facts the pages show, read once on worker threads so the window opens at once:
    // the system overview (WMI) and the license state (HWID + the signed list check). Events arrive on the UI thread.
    public static class Shell
    {
        public static SysInfo Sys { get; private set; }
        public static event Action SysLoaded;
        public static event Action LicenseChanged;

        private static int _licBusy;
        public static bool LicenseChecking { get { return Volatile.Read(ref _licBusy) == 1; } }

        public static void Start()
        {
            var th = new Thread(() =>
            {
                SysInfo s; try { s = SysInfo.Read(); } catch { s = new SysInfo(); }
                Post(() => { Sys = s; var h = SysLoaded; if (h != null) h(); });
            }) { IsBackground = true, Name = "grobyq-sysinfo" };
            th.Start();
            // the startup gate has just validated the license: show that result, no second request to the list
            if (Licensing.LastKnown() != null) { AppLog.Add("[LIC] " + Licensing.LastKnown().Message); Post(() => { var h = LicenseChanged; if (h != null) h(); }); }
            else RecheckLicense(false);
        }

        // Background license re-check (Home > Recheck): fills Home > License. log = write the result line to the log.
        public static void RecheckLicense(bool log)
        {
            if (Interlocked.CompareExchange(ref _licBusy, 1, 0) != 0) return;
            Post(() => { var h = LicenseChanged; if (h != null) h(); });     // "checking…"
            if (log) AppLog.Add("[LIC] checking...");
            var th = new Thread(() =>
            {
                LicResult r;
                try { GrobLic.Hwid(); r = Licensing.Validate(); }
                catch (Exception ex) { r = new LicResult { Message = ex.Message }; }
                Interlocked.Exchange(ref _licBusy, 0);
                AppLog.Add("[LIC] " + (r == null ? "?" : r.Message));
                Post(() => { var h = LicenseChanged; if (h != null) h(); });
            }) { IsBackground = true, Name = "grobyq-license" };
            th.Start();
        }

        private static void Post(Action a)
        {
            var app = Application.Current;
            if (app == null) return;
            try { app.Dispatcher.BeginInvoke(a); } catch { }
        }
    }
}
