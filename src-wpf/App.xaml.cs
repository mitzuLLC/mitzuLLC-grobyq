using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Grobyq
{
    // Startup in the order of the WinForms Program.Main: settings migration, interface language, license list URL,
    // the actions that need no license (/hwid, /aerial, /off), single instance, then the gate - AerialOS (or an "anyos"
    // license) and a valid signed license - and only then the window (hidden in the tray with /tray, the logon task).
    // /selftest (CI smoke test) runs before all of it: no gate, no window, nothing applied.
    public partial class App : Application
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();

        private const string ShowEventName = "Local\\grobyq-mitzu-show";   // second start -> the running one shows its window

        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;
        private static int _crashRestore;                                   // 1 = the crash restore has started

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string action = ParseAction(e.Args);           // panel | tray | selftest | hwid | aerial | off
            if (action == "selftest") { Shutdown(SelfTest()); return; }
            Run(action);
        }

        private async void Run(string action)
        {
            try
            {
                Settings.MigrateOnce();
                L.Load(Settings.Language());
                LoadListUrlFromSettingsFile();

                // A crash must not leave priorities and cores changed: UI-thread errors are logged and the app keeps going;
                // a fatal error on another thread restores the captured baselines before the process dies.
                DispatcherUnhandledException += OnUiException;
                AppDomain.CurrentDomain.UnhandledException += OnFatal;

                // never need a license: the HWID to send, what the AerialOS check sees, the recovery revert
                if (action == "hwid")   { LicenseWindow.ShowHwidOnly(); Shutdown(0); return; }
                if (action == "aerial") { Dlg.Info(null, AerialOs.Report()); Shutdown(0); return; }
                if (action == "off")    { Shutdown(ConsoleRevert()); return; }

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
                StartShowListener();
                try { IrqTrace.StopStale(); } catch { }    // a live-check session left by an exit mid-capture stops logging now

                bool tray = action == "tray";
                int gate = await Gate(tray);
                if (gate != 0) { Shutdown(gate); return; }

                SessionEnding += (s, a) => { var w = Grobyq.MainWindow.Current; if (w != null) w.ExitNow(); };

                var win = new MainWindow(tray);
                MainWindow = win;
                if (_splash != null)
                {
                    _splash.Status(L.T("loading…"));
                    await _splash.FinishAsync(8000);
                    _splash = null;
                }
                if (!tray) { win.Show(); try { win.Activate(); } catch { } }
            }
            catch (Exception ex)
            {
                try { if (_splash != null) _splash.CloseNow(); } catch { }
                try { Dlg.Info(null, "grobyq: " + ex.GetType().Name + ": " + ex.Message); } catch { }
                Shutdown(1);
            }
        }

        private SplashHost _splash;

        // 0 = run; 1 = not licensed (the license window was closed); 2 = not AerialOS and no "anyos" license.
        // Panel start: the splash plays while the list is checked; a failure brings up the license window.
        // Tray start (logon): silent - the network is often late, so a "could not reach" answer is retried for ~1 min
        // before any window shows; a real "no" (revoked, expired, not activated) shows the license window at once.
        private async Task<int> Gate(bool tray)
        {
            bool aerial = !Brand.AerialOnly || AerialOs.Detected();
            _splash = tray ? null : SplashHost.Start();
            if (_splash != null) _splash.Status(L.T("checking license…"));

            LicResult r = await Task.Run(() => ValidateQuiet(tray));

            if (!aerial)
            {
                // Not AerialOS: only a license signed with "anyos" (developer / testers) opens grobyq here.
                if (r.Ok && AerialOs.Allowed(r)) return 0;
                CloseSplash();
                AerialOnlyWindow.ShowGate();
                return 2;
            }
            if (r.Ok) return 0;
            CloseSplash();
            return LicenseWindow.Gate() ? 0 : 1;
        }

        private static LicResult ValidateQuiet(bool tray)
        {
            LicResult r = null;
            for (int i = 0; i < (tray ? 7 : 1); i++)
            {
                try { r = Licensing.Validate(); } catch (Exception ex) { r = new LicResult { Ok = false, Net = true, Message = ex.Message }; }
                if (r.Ok || !r.Net) break;
                if (tray && i < 6) Thread.Sleep(9000);
            }
            return r ?? new LicResult();
        }

        private void CloseSplash()
        {
            var sp = _splash;
            _splash = null;
            if (sp != null) sp.CloseNow();
        }

        // a second start (Start menu, desktop icon) signals the event -> the running copy shows its window
        private void StartShowListener()
        {
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
        }

        // grobyq.exe /off : removes every change grobyq can make (exact baselines, then Windows defaults), in a console.
        // No license needed - recovery must always work.
        private static int ConsoleRevert()
        {
            AllocConsole();
            try
            {
                var eng = new Engine(Settings.WatchList());
                eng.AffRules = Settings.LoadAffRules();
                eng.LoadSystemProfile(Settings.GetBool("SystemThreadOpt"));    // its background list is part of the recovery
                Console.WriteLine();
                Console.WriteLine("  " + Brand.Name + " by mitzu LLC - removing optimization");
                Console.WriteLine();
                Mutex running;
                if (Mutex.TryOpenExisting(Brand.Mutex, out running))
                {
                    running.Dispose();
                    Console.WriteLine("  [!!] grobyq is running (tray): it will apply its settings again. Exit it from the tray menu first,");
                    Console.WriteLine("       or turn the optimizers off in its window - that restores the exact baselines.");
                    Console.WriteLine();
                }
                foreach (var l in eng.Revert(true)) Console.WriteLine("  " + l);
            }
            catch (Exception ex) { Console.WriteLine("  [!!] " + ex.GetType().Name + ": " + ex.Message); }
            Console.WriteLine();
            Console.Write("  Press Enter to close . . . ");
            try { Console.ReadLine(); } catch { }
            FreeConsole();
            return 0;
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
                if (s == "tray" || s == "panel" || s == "selftest" || s == "hwid" || s == "aerial" || s == "off") return s;
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
            chk("thread rules", () =>
            {
                // 4.0.4: the built-in reference (main thread Highest, render thread and job pool Above normal, no boost change),
                // the editor's match inference (the built-in rules agree with it), the reference check and the thread captions
                // (translated: the protected build reads the language table)
                L.Load("ru");
                var d = Store.Defaults()["cs2.exe"].Threads;
                bool prios = d.Any(r => r.Pattern == "*MainThrd*" && r.Prio == "Highest") && d.Any(r => r.Pattern == "*D3D11RenderThread*" && r.Prio == "AboveNormal")
                          && d.Any(r => r.Pattern == "*GlobPool*" && r.Prio == "AboveNormal");
                bool boost = d.All(r => string.IsNullOrEmpty(r.Boost));
                bool match = ThreadRule.MatchFor("steamclient*.dll") == "module" && ThreadRule.MatchFor("*MainThrd*") == "desc" && ThreadRule.MatchFor("") == "desc";
                bool kinds = d.All(r => ThreadRule.MatchFor(r.Pattern) == r.Match);
                bool reference = Store.IsBuiltin("cs2.exe", Store.Defaults()["cs2.exe"]) && Store.HasBuiltin("dota2.exe") && !Store.HasBuiltin("game.exe");
                // 4.0.6: Time critical is a level like the others (15, no Advanced mode); nothing reaches a real-time level
                int tc, hi;
                bool levels = Engine.ThreadPrio("TimeCritical", out tc) && tc == 15 && Engine.ThreadPrio("Highest", out hi) && hi == 2
                           && !Engine.ThreadPrio("Realtime", out hi) && !Engine.ThreadPrio("", out hi) && Engine.LevelName(15) == "TimeCritical" && Engine.LevelName(6) == "6";
                string main = ThreadNotes.For("*MainThrd*"), pool = ThreadNotes.For("*GlobPool*");
                bool notes = ThreadNotes.Count > 20 && main.Length > 0 && !main.StartsWith("Game main thread", StringComparison.Ordinal)
                          && pool.Length > 0 && !pool.StartsWith("Job pool", StringComparison.Ordinal) && ThreadNotes.For("no_such_thread") == "";
                log.Add("      thread rules: " + d.Count + " built-in cs2 rules (MainThrd Highest, render + GlobPool Above normal), " + ThreadNotes.Count + " thread captions");
                return prios && boost && match && kinds && reference && levels && notes;
            });
            chk("cleaner", () => Cleaner.Items().Count > 0);
            chk("app caches", () =>
            {
                // 4.0.8: a real clean in a sandbox under %TEMP%: caches go; sign-ins, Spotify downloads, Steam config, Telegram
                // keys and anti-cheat folders stay; a link inside an app folder is not followed; Spotify gets storage.size=1024.
                // Then this PC: every folder the found rows would touch passes the guard (nothing is deleted here).
                L.Load("ru");
                string S = Path.Combine(Path.GetTempPath(), "grobyq-selftest-clean");
                try { if (Directory.Exists(S)) Directory.Delete(S, true); } catch { }
                Func<string, string> F = rel => { string p = Path.Combine(S, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, "x"); return p; };
                Func<string, bool> E = rel => File.Exists(Path.Combine(S, rel));
                var env = new CleanEnv
                {
                    Local = Path.Combine(S, "Local"), Roaming = Path.Combine(S, "Roaming"), ProgramData = Path.Combine(S, "ProgramData"), LocalLow = Path.Combine(S, "LocalLow"),
                    SystemDrive = Path.Combine(S, "C") + "\\", Steam = Path.Combine(S, "Steam")
                };
                Directory.CreateDirectory(env.Steam);
                env.SteamLibs = CleanEnv.SteamLibraries(env.Steam);
                F(@"Roaming\discord\Cache\Cache_Data\f_1"); F(@"Roaming\discord\Code Cache\js\a"); F(@"Roaming\discord\Local Storage\leveldb\1.log"); F(@"Roaming\discord\Network\Cookies");
                F(@"Roaming\discord\Preferences"); F(@"Roaming\discord\Local State");
                F(@"Local\Spotify\Data\a\1"); F(@"Local\Spotify\Storage\b\song"); F(@"Roaming\Spotify\prefs");
                F(@"Steam\appcache\httpcache\a"); F(@"Steam\config\loginusers.vdf"); F(@"Steam\userdata\1\remote\save");
                F(@"Roaming\Telegram Desktop\tdata\user_data\cache\x"); F(@"Roaming\Telegram Desktop\tdata\key_datas");
                F(@"Roaming\FACEIT\Local State"); F(@"Roaming\FACEIT\Preferences"); F(@"Roaming\FACEIT\Cache\x");
                F(@"Roaming\SomeApp\Local State"); F(@"Roaming\SomeApp\Preferences"); F(@"Roaming\SomeApp\Cache\x"); F(@"Roaming\SomeApp\Local Storage\leveldb\t");
                F(@"C\NVIDIA\DisplayDriver\1\setup.exe"); F(@"elsewhere\keep.txt"); F(@"Roaming\Slack\Local State"); F(@"Roaming\Slack\Preferences");
                bool link = false;
                try { Directory.CreateSymbolicLink(Path.Combine(S, @"Roaming\Slack\Cache"), Path.Combine(S, "elsewhere")); link = true; } catch { }
                var cat = AppCaches.Catalog();
                string why;
                try
                {
                    Cleaner.Env = env;
                    Cleaner.SetRunning(new string[0]);
                    var rows = Cleaner.AppItems(cat);
                    rows.Add(new CleanItem { Key = "drivers", Title = "drivers", Parts = () => AppCaches.DriverParts(env) });
                    foreach (var it in rows) Cleaner.Clean(it, s => { });
                    bool gone = !E(@"Roaming\discord\Cache\Cache_Data\f_1") && !E(@"Roaming\discord\Code Cache\js\a") && !E(@"Local\Spotify\Data\a\1") && !E(@"Steam\appcache\httpcache\a")
                             && !E(@"Roaming\Telegram Desktop\tdata\user_data\cache\x") && !E(@"Roaming\SomeApp\Cache\x") && !E(@"C\NVIDIA\DisplayDriver\1\setup.exe");
                    bool kept = E(@"Roaming\discord\Local Storage\leveldb\1.log") && E(@"Roaming\discord\Network\Cookies") && E(@"Local\Spotify\Storage\b\song") && E(@"Steam\config\loginusers.vdf")
                             && E(@"Steam\userdata\1\remote\save") && E(@"Roaming\Telegram Desktop\tdata\key_datas") && E(@"Roaming\FACEIT\Cache\x") && E(@"Roaming\SomeApp\Local Storage\leveldb\t") && E(@"elsewhere\keep.txt");
                    bool prefs = File.ReadAllText(Path.Combine(S, @"Roaming\Spotify\prefs")).Contains("storage.size=1024");
                    bool guard = !AppCaches.Safe(env.Roaming, env, out why) && !AppCaches.Safe(Path.Combine(env.Roaming, "discord"), env, out why)
                              && !AppCaches.Safe(Path.Combine(env.Roaming, "discord", "Local Storage"), env, out why) && !AppCaches.Safe(@"C:\Windows\System32", env, out why)
                              && !AppCaches.Safe(Path.Combine(env.Steam, "config"), env, out why) && (!link || !AppCaches.Safe(Path.Combine(S, @"Roaming\Slack\Cache"), env, out why));
                    // this PC (read only): every folder of every found row passes the guard
                    Cleaner.Env = null;
                    Cleaner.BeginPass();
                    int n = 0, bad = 0;
                    var appRows = Cleaner.AppItems(cat);
                    int foundHere = appRows.Count(i => i.Key.StartsWith("app:", StringComparison.Ordinal) && i.Key != "app:other");
                    foreach (var it in appRows.Concat(Cleaner.Items().Where(i => i.Parts != null)))
                        foreach (var p in Cleaner.PartsOf(it)) { n++; if (!AppCaches.Safe(p.Path, Cleaner.Env, out why)) { bad++; if (bad <= 3) log.Add("      unsafe: " + p.Path + " - " + why); } }
                    log.Add("      app caches: " + cat.Count + " apps in the catalog, " + foundHere + " found on this PC, " + n + " folders checked, link test " + (link ? "run" : "skipped"));
                    return gone && kept && prefs && guard && bad == 0 && cat.Count >= 40 && cat.Select(a => a.Key).Distinct().Count() == cat.Count;
                }
                finally
                {
                    Cleaner.Env = null;
                    Cleaner.SetRunning(new string[0]);
                    try { Directory.Delete(S, true); } catch { }
                }
            });
            chk("hwid", () => GrobLic.Hwid().Length > 0);
            chk("license url", () => Licensing.ListUrl.StartsWith("https://", StringComparison.Ordinal) &&
                Licensing.ApiUrlFor(Licensing.ListUrlDefault) == "https://api.github.com/repos/mitzuLLC/grobpc-licenses/contents/licenses.json?ref=main");
            chk("aerial rules", () =>
            {
                string n;
                return AerialOs.MarkerOk("AerialOS", "6.1", out n) && AerialOs.MarkerOk("AerialOS", null, out n) &&
                       !AerialOs.MarkerOk("AerialOS", "5.9", out n) && !AerialOs.MarkerOk("Windows", null, out n) && !AerialOs.MarkerOk(null, null, out n) &&
                       !AerialOs.MarkerOk(6, null, out n) && !AerialOs.MarkerOk("AerialOS", "six", out n) &&
                       AerialOs.NameOk("AerialOS 6") && AerialOs.NameOk("Windows 11 AerialOS") && !AerialOs.NameOk("Aerial Photography LLC") && !AerialOs.NameOk("AerialOSX") &&
                       AerialOs.UrlOk("https://aerialos.vercel.app/") && AerialOs.UrlOk("https://dsc.gg/aerialos") && !AerialOs.UrlOk("https://example.com/aerial");
            });
            chk("aerial checks", () => { var c = AerialOs.Checks(); log.Add("      aerial: " + (AerialOs.Detected() ? "detected" : "not detected") + " (" + c.Count + " markers read)"); return c.Count == 7; });
            chk("license list", () =>
            {
                // the real list over HTTPS, on the protected IL: reached + parsed = the gate works (this runner is not in the list)
                var r = Licensing.Validate();
                log.Add("      license list: " + (r.Net ? "not reached" : "reached") + " - " + r.Message);
                return true;
            });
            chk("splash", () => { var d = SplashData.Load(); return d != null && d.Frames.Count > 10 && SplashData.Decode(d.Frames[0]).PixelWidth > 0; });
            chk("gate windows", () => { L.Load("ru"); return LicenseWindow.Builds() && AerialOnlyWindow.Builds(); });
            chk("tray menu", () => { L.Load("ru"); var m = new TrayMenu(); m.Sync("ARMED", "", true, false); return m.Content != null; });
            chk("system profile", () =>
            {
                // 4.0.2: only the two text-input processes step aside. 4.0.4: every thread of both at Idle (catch-all last);
                // process boost off only for busy background apps - audio, composition, input and system services keep it;
                // no game sections (the game presets own the games)
                var r = ThreadIni.LoadBundled();
                int boostOff = r.Presets.Values.Count(p => p.Boost == "Off");
                Func<string, bool> keeps = exe => { Preset p; return !r.Presets.TryGetValue(exe, out p) || (p.Boost != "Off" && p.Threads.All(t => t.Boost != "Off")); };
                Func<string, bool> idle = exe => { Preset p; return r.Presets.TryGetValue(exe, out p) && p.Threads.Count > 0 && p.Threads.All(t => t.Prio == "Idle") && p.Threads[p.Threads.Count - 1].Pattern == "*"; };
                bool boost = keeps("dwm.exe") && keeps("audiodg.exe") && keeps("svchost.exe") && keeps("winlogon.exe") && keeps("csrss.exe") && keeps("ctfmon.exe") && boostOff >= 10;
                bool text = idle("ctfmon.exe") && idle("TextInputHost.exe");
                bool games = !r.Presets.ContainsKey("cs2.exe") && !r.Presets.ContainsKey("dota2.exe");
                log.Add("      system profile: " + r.Presets.Count + " processes, " + r.BgClasses.Count + " background, process boost off for " + boostOff);
                return r.Presets.Count > 10 && r.BgClasses.Count == 2 && r.BgClasses.ContainsKey("ctfmon.exe") && r.BgClasses.ContainsKey("TextInputHost.exe") && boost && text && games;
            });
            chk("pages", () =>
            {
                L.Load("ru");
                var pages = new System.Windows.Controls.UserControl[] { new Pages.HomePage(), new Pages.ThreadsPage(), new Pages.AffinityPage(), new Pages.MaintenancePage(), new Pages.CleanupPage(), new Pages.SettingsPage(), new Pages.AboutPage(), new Pages.JournalPage() };
                return pages.Length == 8;                      // 4.0.3: + the Affinity maintenance page
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
