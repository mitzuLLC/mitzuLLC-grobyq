using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();

        private static Mutex _mutex;
        private static int _crashRestore;                    // 1 = the crash restore has started (Interlocked)

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new DarkMenuRenderer();   // every context / tray menu in the dark theme
            WheelRouter.Install();                                 // the wheel scrolls what is under the pointer
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) Theme.K = Math.Max(1f, g.DpiX / 96f); } catch { }
            Settings.MigrateOnce();
            Settings.MaskPinNote = Settings.PinMaskDefault();
            Settings.MaskIrqNote = GameMask.PinModeDefault(Settings.ExistedBefore);
            L.Load(Settings.Language());

            LoadListUrlFromSettingsFile();

            string action = ParseAction(args);   // panel | tray | off | check | import | hwid

            // A crash must not leave priorities and cores changed: UI-thread errors are logged and the app keeps going;
            // a fatal error on another thread restores the captured baselines before the process dies.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try { var f = MainForm.Current; if (f != null && !f.IsDisposed) f.Log("[!!] internal error: " + e.Exception.GetType().Name + ": " + e.Exception.Message); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var f = MainForm.Current;
                    if (f == null || !Settings.GetBool("RestoreOnExit")) return;
                    // the engine is single-threaded (UI thread): run the restore there, wait a bounded time; exactly one restore
                    // ever starts (a slow one on the UI thread is waited for, never raced)
                    Action once = () => { if (Interlocked.Exchange(ref _crashRestore, 1) == 0) { try { f.Eng.Revert(); } catch { } } };
                    if (f.IsHandleCreated && !f.IsDisposed && f.InvokeRequired)
                    {
                        var ar = f.BeginInvoke(once);
                        if (ar.AsyncWaitHandle.WaitOne(3000)) return;
                        if (Interlocked.CompareExchange(ref _crashRestore, 0, 0) == 1) { ar.AsyncWaitHandle.WaitOne(5000); return; }   // running there
                    }
                    once();                                  // UI thread itself, or it did not answer in time
                }
                catch { }
            };

            // /hwid and /off never need a license.
            if (action == "hwid") { LicenseForm.ShowHwidOnly(); return 0; }
            if (action == "off")  { return ConsoleRevert(); }
            if (action == "check")  { return ConsoleFingerprint(); }
            if (action == "import") { return ConsoleVain(); }
            if (action == "aerial") { Dlg.Info(null, AerialOs.Report()); return 0; }

#if DEMO
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_LICENSE") == "1") { LicenseForm.Gate(false); return 0; }
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_LICENSE") == "hwid") { LicenseForm.ShowHwidOnly(); return 0; }
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_AERIAL") == "1") { string al = Environment.GetEnvironmentVariable("GROBYQ_DEMO_LANG"); if (!string.IsNullOrEmpty(al)) L.Load(al); AerialOnlyForm.ShowGate(); return 0; }
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_SPLASH") == "1")
            {
                string sl = Environment.GetEnvironmentVariable("GROBYQ_DEMO_LANG"); if (!string.IsNullOrEmpty(sl)) L.Load(sl);
                var sp = SplashHost.Start(); if (sp != null) { sp.Status(L.T("checking license…")); Thread.Sleep(20000); }
                return 0;
            }
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO") == "1")
            {
                string dl = Environment.GetEnvironmentVariable("GROBYQ_DEMO_LANG");
                if (!string.IsNullOrEmpty(dl)) L.Load(dl);
                Demo.Install();
                using (var app = new MainForm(false))
                {
                    string pg = Environment.GetEnvironmentVariable("GROBYQ_DEMO_PAGE"), sc = Environment.GetEnvironmentVariable("GROBYQ_DEMO_SCROLL");
                    string tab = Environment.GetEnvironmentVariable("GROBYQ_DEMO_TAB");
                    if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_MAX") == "1") app.WindowState = FormWindowState.Maximized;
                    string sw = Environment.GetEnvironmentVariable("GROBYQ_DEMO_SWITCH");      // live language switch test
                    app.Shown += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(pg)) app.Go(pg); if (!string.IsNullOrEmpty(tab)) app.DemoTab(tab);
                        int y; if (int.TryParse(sc, out y) && string.IsNullOrEmpty(sw)) { app.DemoScroll(y); var ts = new System.Windows.Forms.Timer { Interval = 4000 }; ts.Tick += (a1, b1) => { ts.Stop(); app.DemoScroll(y); }; ts.Start(); }
                        if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_DUMP") == "1") { var td = new System.Windows.Forms.Timer { Interval = 9000 }; td.Tick += (a3, b3) => { td.Stop(); Console.Error.WriteLine(app.DemoDump()); }; td.Start(); }
                        if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_LOG") == "1") app.DemoLog();
                        string ex = Environment.GetEnvironmentVariable("GROBYQ_DEMO_EXPAND"), op = Environment.GetEnvironmentVariable("GROBYQ_DEMO_OPEN"), cl = Environment.GetEnvironmentVariable("GROBYQ_DEMO_CELL");
                        if (!string.IsNullOrEmpty(ex) || !string.IsNullOrEmpty(op) || !string.IsNullOrEmpty(cl))
                        {
                            var to = new System.Windows.Forms.Timer { Interval = 5500 };
                            to.Tick += (a4, b4) =>
                            {
                                to.Stop();
                                var pgx = app.DemoPage; if (pgx == null) return;
                                int n;
                                if (int.TryParse(ex, out n)) pgx.DemoExpand(n);
                                if (int.TryParse(op, out n)) { var t2 = new System.Windows.Forms.Timer { Interval = 1200 }; t2.Tick += (a5, b5) => { t2.Stop(); pgx.DemoOpen(n); }; t2.Start(); }
                                if (!string.IsNullOrEmpty(cl)) { var q = cl.Split(','); pgx.DemoCell(int.Parse(q[0]), int.Parse(q[1]), int.Parse(q[2])); }
                            };
                            to.Start();
                        }
                        if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_TRAY") == "1") { var tt = new System.Windows.Forms.Timer { Interval = 2500 }; tt.Tick += (a2, b2) => { tt.Stop(); app.DemoTray(); }; tt.Start(); }
                        if (!string.IsNullOrEmpty(sw))
                        {
                            // "ru" or a chain "ru,zh": one live switch every 3 s (EN -> RU -> ZH check)
                            var seq = new System.Collections.Generic.Queue<string>(sw.Split(','));
                            var t = new System.Windows.Forms.Timer { Interval = 3000 };
                            t.Tick += (a, b) =>
                            {
                                if (seq.Count == 0) { t.Stop(); return; }
                                L.Load(seq.Dequeue()); app.RebuildUi(); int yy; if (int.TryParse(sc, out yy)) app.DemoScroll(yy);
                                if (seq.Count == 0) t.Stop();
                            };
                            t.Start();
                        }
                    };
                    Application.Run(app);
                }
                return 0;
            }
#endif
            // panel / tray: single instance, then license, then run.
            bool created;
            _mutex = new Mutex(true, Brand.Mutex, out created);
            if (!created)
            {
                Dlg.Info(null, L.T("grobyq is already running. Look for the icon in the tray."));
                return 0;
            }
            IrqTrace.StopStale();          // a live-check session left by an exit mid-capture stops logging now

            bool tray = action == "tray";
            bool aerial = !Brand.AerialOnly || AerialOs.Detected();
            // Startup animation (panel start only; the tray start at logon stays silent).
            SplashHost splash = !tray ? SplashHost.Start() : null;      // always on an interactive start, never on /tray (a stored "Splash" value is ignored)
            bool licensed;
            if (!aerial)
            {
                // Not AerialOS: only a license signed with "anyos" (developer / testers) opens grobyq here.
                if (splash != null) splash.Status(L.T("checking license…"));
                LicResult r;
                try { r = Licensing.Validate(); } catch { r = new LicResult { Ok = false }; }
                if (!(r.Ok && AerialOs.Allowed(r)))
                {
                    if (splash != null) { splash.CloseNow(); splash = null; }
                    AerialOnlyForm.ShowGate();
                    return 2;
                }
                licensed = true;
            }
            else if (splash != null)
            {
                splash.Status(L.T("checking license…"));
                LicResult r;
                try { r = Licensing.Validate(); } catch { r = new LicResult { Ok = false }; }
                licensed = r.Ok;
                if (!licensed) { splash.CloseNow(); splash = null; licensed = LicenseForm.Gate(false); }
            }
            else licensed = LicenseForm.Gate(tray);
            if (!licensed) return 1;

            if (splash != null) splash.Status(L.T("loading…"));
            using (var app = new MainForm(tray))
            {
                if (splash != null) { splash.FinishAndWait(8000); app.Shown += (s, e) => { try { app.Activate(); } catch { } }; }
                Application.Run(app);
            }
            return 0;
        }

        private static string ParseAction(string[] args)
        {
            foreach (var a in args)
            {
                string s = a.TrimStart('/', '-').ToLowerInvariant();
                if (s == "tray" || s == "off" || s == "check" || s == "import" || s == "hwid" || s == "panel" || s == "aerial")
                    return s;
            }
            return "panel";
        }

        // grobyq.settings.txt (next to the exe; grobPC.settings.txt is still read) can carry
        // LICENSE_LIST_URL. A placeholder URL is ignored, the compiled one is used instead.
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
                    if (string.Equals(k, "LICENSE_LIST_URL", StringComparison.OrdinalIgnoreCase))
                        Licensing.SetListUrl(v);
                }
            }
            catch { }
        }

        // ---- console modes ----
        private static int ConsoleRevert()
        {
            AllocConsole();
            var eng = new Engine(Settings.WatchList());
            eng.AffRules = Settings.LoadAffRules();
            eng.LoadSystemProfile(Settings.GetBool("SystemThreadOpt"));   // its background list is part of the recovery
            Console.WriteLine();
            Console.WriteLine("  " + Brand.Name + " by mitzu LLC - removing optimization");
            Console.WriteLine();
            Mutex running;
            if (Mutex.TryOpenExisting(Brand.Mutex, out running))
            {
                running.Dispose();
                Console.WriteLine("  [!!] grobyq is running (tray): it will apply its settings again. Exit it from the tray menu first,");
                Console.WriteLine("       or press DISABLE in its window - that restores the exact baselines.");
                Console.WriteLine();
            }
            foreach (var l in eng.Revert(true)) Console.WriteLine("  " + l);
            Pause(); FreeConsole();
            return 0;
        }

        private static int ConsoleFingerprint()
        {
            if (!GateConsole()) return 1;
            AllocConsole();
            Console.WriteLine();
            Console.WriteLine("  " + Brand.Name + " by mitzu LLC - system fingerprint");
            Console.WriteLine("  ============================================================");
            foreach (var l in Fingerprint.Lines()) Console.WriteLine(l);
            Console.WriteLine();
            Console.WriteLine("  Run this on both Windows installs and diff the two outputs.");
            Pause(); FreeConsole();
            return 0;
        }

        private static int ConsoleVain()
        {
            if (!GateConsole()) return 1;
            AllocConsole();
            foreach (var l in Vain.Decode()) Console.WriteLine(l);
            Pause(); FreeConsole();
            return 0;
        }

        private static bool GateConsole()
        {
            AllocConsole();
            Console.WriteLine();
            Console.WriteLine("  " + Brand.Name + " by mitzu LLC - checking license...");
            var r = Licensing.Validate();
            if (r.Ok && !AerialOs.Allowed(r))
            {
                // the console modes follow the same AerialOS rule as the panel
                Console.WriteLine("  " + Brand.Name + " is for AerialOS users only: AerialOS is not detected on this PC.");
                Console.WriteLine("  Details: grobyq.exe /aerial");
                Pause(); FreeConsole();
                return false;
            }
            if (r.Ok) { Console.WriteLine("  " + r.Message + Dates(r)); return true; }
            Console.WriteLine("  License check failed: " + r.Message);
            Console.WriteLine();
            Console.WriteLine("  HWID: " + Licensing.Hwid());
            Console.WriteLine("  Send this HWID to the developer, or run  grobyq.exe /hwid  to copy it.");
            Pause(); FreeConsole();
            return false;
        }

        private static void Pause() { Console.WriteLine(); Console.Write("  Press Enter to close . . . "); try { Console.ReadLine(); } catch { } }

        public static string Dates(LicResult r, bool ui = false)
        {
            string s = "";
            if (r.Purchase.HasValue) s += "   " + (ui ? L.T("Purchased:") : "Purchased:") + " " + r.Purchase.Value.ToString("yyyy-MM-dd");
            if (r.Expires.HasValue) s += "   " + (ui ? L.T("Expires:") : "Expires:") + " " + r.Expires.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrEmpty(r.Source)) s += "   (" + r.Source + ")";
            return s;
        }
    }
}
