using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Grobyq
{
    // Startup in the order of the WinForms Program.Main: settings migration, interface language, license list URL,
    // single instance, then the window (hidden in the tray with /tray, the logon autostart task).
    public partial class App : Application
    {
        private const string ShowEventName = "Local\\grobyq-mitzu-show";   // second start -> the running one shows its window

        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;
        private static int _crashRestore;                                   // 1 = the crash restore has started

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                string action = ParseAction(e.Args);       // panel | tray

                Settings.MigrateOnce();
                L.Load(Settings.Language());
                LoadListUrlFromSettingsFile();

                // single instance (the same mutex as v3, so v3 and v4 never run two optimizers side by side)
                bool created;
                _mutex = new Mutex(true, Brand.Mutex, out created);
                if (!created)
                {
                    bool signalled = false;
                    try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) { ev.Set(); signalled = true; } } catch { }
                    if (!signalled) Dlg.Info(null, L.T("grobyq is already running. Look for the icon in the tray."));
                    Shutdown(0);
                    return;
                }
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        try { _showEvent.WaitOne(); } catch { return; }
                        try { Dispatcher.BeginInvoke((Action)(() => { var w = Grobyq.MainWindow.Current; if (w != null) w.ShowPanel(); })); } catch { return; }
                    }
                }) { IsBackground = true, Name = "grobyq-show" };
                waiter.Start();

                // A crash must not leave priorities and cores changed: UI-thread errors are logged and the app keeps going;
                // a fatal error on another thread restores the captured baselines before the process dies.
                DispatcherUnhandledException += OnUiException;
                AppDomain.CurrentDomain.UnhandledException += OnFatal;
                SessionEnding += (s, a) => { var w = Grobyq.MainWindow.Current; if (w != null) w.ExitNow(); };

                bool tray = action == "tray";
                var win = new MainWindow(tray);
                MainWindow = win;
                if (!tray) win.Show();
            }
            catch (Exception ex)
            {
                try { Dlg.Info(null, "grobyq: " + ex.GetType().Name + ": " + ex.Message); } catch { }
                Shutdown(1);
            }
        }

        private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try { AppLog.Add("[!!] internal error: " + e.Exception.GetType().Name + ": " + e.Exception.Message); } catch { }
            e.Handled = true;
        }

        private static void OnFatal(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var o = Optimizer.Instance;
                if (o == null || !Settings.GetBool("RestoreOnExit")) return;
                // the engine serializes its own state (lock), so the restore can run on this thread; exactly one ever starts
                if (Interlocked.Exchange(ref _crashRestore, 1) == 0) { try { o.Eng.Revert(); } catch { } }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { if (_mutex != null) { _mutex.ReleaseMutex(); _mutex.Dispose(); } } catch { }
            try { if (_showEvent != null) _showEvent.Dispose(); } catch { }
            base.OnExit(e);
        }

        private static string ParseAction(string[] args)
        {
            foreach (var a in args ?? new string[0])
            {
                string s = (a ?? "").TrimStart('/', '-').ToLowerInvariant();
                if (s == "tray" || s == "panel") return s;
            }
            return "panel";
        }

        // grobyq.settings.txt (next to the exe; grobPC.settings.txt is still read) can carry LICENSE_LIST_URL.
        // A placeholder URL is ignored, the compiled one is used instead.
        private static void LoadListUrlFromSettingsFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Brand.SettingsFile);
                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Brand.OldSettingsFile);
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (string.Equals(k, "LICENSE_LIST_URL", StringComparison.OrdinalIgnoreCase)) Licensing.SetListUrl(v);
                }
            }
            catch { }
        }
    }
}
