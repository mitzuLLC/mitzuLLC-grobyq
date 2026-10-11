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
                string action = ParseAction(e.Args);       // panel | tray | selftest

                if (action == "selftest") { Shutdown(SelfTest()); return; }

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
                if (s == "tray" || s == "panel" || s == "selftest") return s;
            }
            return "panel";
        }

        // Headless smoke test (CI runs it on the protected build): the app resources, languages, topology, masks, presets
        // and every page must load with the obfuscated IL. No window, no tray, no single-instance lock, nothing applied.
        // Writes %TEMP%\grobyq-selftest.txt; exit code 0 = pass, 3 = a check failed.
        private int SelfTest()
        {
            var log = new System.Collections.Generic.List<string> { Brand.Name + " " + Brand.Version + " selftest" };
            int fails = 0;
            Action<string, Func<bool>> chk = (name, f) =>
            {
                try { bool ok = f(); log.Add((ok ? "OK    " : "FAIL  ") + name); if (!ok) fails++; }
                catch (Exception ex) { log.Add("FAIL  " + name + ": " + ex.GetType().Name + ": " + ex.Message); fails++; }
            };
            chk("resources", () => Resources["Acc"] is System.Windows.Media.Brush && Resources["BtnAccent"] is Style);
            chk("settings", () => { Settings.MigrateOnce(); return Settings.Get("WatchList").Length > 0; });
            chk("lang ru", () => { L.Load("ru"); return L.T("Settings") == "Настройки"; });
            chk("lang uk", () => { L.Load("uk"); return L.T("Settings") == "Параметри"; });
            chk("lang en", () => { L.Load("en"); return L.T("Settings") == "Settings"; });
            chk("topology", () => Topo.Live().LogicalCount > 0);
            chk("masks", () => { var t = Topo.Live(); return Masks.Options(t).Count > 0 && Masks.Build(t, Masks.Recommended(t)).Mask != 0; });
            chk("game mask", () => GameMask.Info(Topo.Live(), false, null).Base != 0);
            chk("presets", () => new Engine(new[] { "cs2.exe" }).Presets != null);
            chk("cleaner", () => Cleaner.Items().Count > 0);
            chk("hwid", () => GrobLic.Hwid().Length > 0);
            chk("license url", () => Licensing.ListUrl.StartsWith("https://", StringComparison.Ordinal));
            chk("pages", () =>
            {
                L.Load("ru");
                var pages = new System.Windows.Controls.UserControl[] { new Pages.HomePage(), new Pages.ThreadsPage(), new Pages.AffinityPage(), new Pages.CleanupPage(), new Pages.SettingsPage(), new Pages.AboutPage(), new Pages.JournalPage() };
                return pages.Length == 7;
            });
            // JIT every method of the assembly: an obfuscation layer that emits invalid IL fails here, at build time,
            // instead of in a code path the checks above never reach (InvalidProgramException on first call).
            chk("jit", () =>
            {
                int n = 0, bad = 0;
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var type in typeof(App).Assembly.GetTypes())
                {
                    if (type.ContainsGenericParameters) continue;
                    var methods = new System.Collections.Generic.List<System.Reflection.MethodBase>();
                    methods.AddRange(type.GetMethods(all));
                    methods.AddRange(type.GetConstructors(all));
                    foreach (var m in methods)
                    {
                        if (m.IsAbstract || m.ContainsGenericParameters) continue;
                        if ((m.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0) continue;
                        var impl = m.MethodImplementationFlags;
                        if ((impl & System.Reflection.MethodImplAttributes.Runtime) != 0 || (impl & System.Reflection.MethodImplAttributes.InternalCall) != 0) continue;
                        try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); n++; }
                        catch (Exception ex)
                        {
                            bad++;
                            if (bad <= 5) log.Add("      jit " + type.FullName + "." + m.Name + ": " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                }
                log.Add("      jit: " + n + " methods compiled, " + bad + " failed");
                return bad == 0;
            });
            log.Add(fails == 0 ? "PASS" : "FAILED: " + fails);
            try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "grobyq-selftest.txt"), log); } catch { }
            return fails == 0 ? 0 : 3;
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
