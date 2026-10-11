using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    // Shell: sidebar navigation, page host, log drawer, tray and the engine loop.
    internal sealed class MainForm : Form
    {
        public readonly Engine Eng;
        public List<PciDevice> Pci = new List<PciDevice>();
        public SysInfo Sys;
        public event Action SysLoaded, StateChanged, MaskChanged, SettingsChanged;

        private Panel _side, _host, _pageHost, _logPanel;
        private LogView _log;
        private readonly List<string> _logLines = new List<string>();
        private readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>();
        private readonly Dictionary<string, NavItem> _nav = new Dictionary<string, NavItem>();
        private string _cur = "";
        private bool _started;          // after the startup lines: new log lines mark the folded log
        private NavItem _logNav;

        private NotifyIcon _tray;
        private ToolStripMenuItem _miActive;
        private System.Windows.Forms.Timer _timer;
        private bool _active, _reallyExit;
        private int _tick;
        private string _liveTarget = "";

        public bool Active { get { return _active; } }
        public string LiveTarget { get { return _liveTarget; } }

        // the live window (crash handler in Program restores through it)
        internal static MainForm Current;

        public MainForm(bool startTray)
        {
            Current = this;
            // DPI from the screen, not from CreateGraphics(): that would create the window handle here,
            // before the HandleCreated hooks below exist (dark caption and system info never ran in 3.0).
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.K = Math.Max(1f, g.DpiX / 96f); } catch { Theme.K = 1f; }
            AutoScaleMode = AutoScaleMode.None;
            Theme.DarkForm(this);

            Eng = new Engine(Settings.WatchList());
            Eng.AffRules = Settings.LoadAffRules();
            Eng.LoadSystemProfile(Settings.GetBool("SystemThreadOpt"));   // built-in profile (thread rules + background lists), one switch
            List<string> migrated = null;
            try { migrated = Store.Migrate362(Eng); } catch { }        // once: Steam on the background list, no special steamclient rule
            try { Pci = Devices.ListPci(); } catch { }

            BuildWindow();
            BuildShell();
            BuildTray();

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += Tick;

            Go("home");
            SetState();
            Log(new[] { Brand.Name + " " + Brand.Version + " " + Brand.Credit + "  -  " + Brand.XUrl,
                        "THREADS optimizes the game. AFFINITY pins cores and interrupts. DEVICES tune the path around it." });
            if (Settings.MigrationNote.Length > 0) Log("[i] " + Settings.MigrationNote);
            if (Settings.MaskPinNote.Length > 0) Log("[i] " + Settings.MaskPinNote);
            if (Settings.MaskIrqNote.Length > 0) Log("[i] " + Settings.MaskIrqNote);
            if (migrated != null && migrated.Count > 0) Log(migrated);
            LogTopology();

            if (Settings.GetBool("AutoEnable")) SetActive(true);

            // System overview (WMI) on a worker; waits for the window handle before reporting back.
            var th = new Thread(() =>
            {
                SysInfo s; try { s = SysInfo.Read(); } catch { s = new SysInfo(); }
                for (int i = 0; i < 600 && !IsHandleCreated && !IsDisposed; i++) Thread.Sleep(50);
                try { BeginInvoke((Action)(() => { Sys = s; var h = SysLoaded; if (h != null) h(); })); } catch { }
            }) { IsBackground = true };
            th.Start();

            if (startTray)
            {
                try { _tray.ShowBalloonTip(3000, Brand.Name, L.T("Running in the tray. Double click the icon to open the panel."), ToolTipIcon.Info); } catch { }
                Load += (s, e) => { Visible = false; };
            }
            _timer.Start();
            var arm = new System.Windows.Forms.Timer { Interval = 3000 };
            arm.Tick += (s, e) => { arm.Stop(); arm.Dispose(); _started = true; };
            Shown += (s, e) => { arm.Start(); Flush(); };      // lines logged before the window existed
        }

        // Window size that always fits the screen (taskbar excluded), so nothing hides below the edge.
        private void BuildWindow()
        {
            Text = Brand.Name;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; } catch { }
            int w = Math.Min(Theme.D(1340), wa.Width - Theme.D(24)), h = Math.Min(Theme.D(880), wa.Height - Theme.D(24));
            Size = new Size(Math.Max(Theme.D(760), w), Math.Max(Theme.D(520), h));
            MinimumSize = new Size(Math.Min(Theme.D(1000), wa.Width), Math.Min(Theme.D(600), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Fg; Font = Theme.F(9.5f);
            try { Icon = Brand.AppIcon; } catch { }
            FormClosing += OnClosing;
            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized && Settings.GetBool("CloseToTray")) Hide(); };
        }

        // Language switch: rebuild the sidebar, every page and the tray menu in the new language, keep the page.
        public void RebuildUi()
        {
            string keep = _cur;
            string sec = null; Page cp; if (_pages.TryGetValue(_cur, out cp)) sec = cp.CurrentSection;
            SuspendLayout();
            try
            {
                SysLoaded = null; StateChanged = null; MaskChanged = null; SettingsChanged = null;
                foreach (var pg in _pages.Values) { try { pg.OnLeave(); } catch { } }
                var old = new List<Control>(); foreach (Control c in Controls) old.Add(c);
                Controls.Clear();
                foreach (var c in old) { try { c.Dispose(); } catch { } }
                _pages.Clear(); _nav.Clear(); _cur = "";
                Font = Theme.F(9.5f);
                BuildShell();
                BuildTrayMenu();
            }
            finally { ResumeLayout(true); }
            Go(_pages.ContainsKey(keep) ? keep : "home");
            if (sec != null && _pages.TryGetValue(_cur, out cp)) cp.ShowSection(sec);
            Theme.Reflow(this);             // new fonts / texts: measure and arrange the rebuilt tree now
            if (_pages.TryGetValue(_cur, out cp)) cp.Relayout();      // and the visible page once more: widths, wrap, scroll extent
            SetState();
            if (Sys != null) { var h = SysLoaded; if (h != null) h(); }
            Flush();
        }

        // ------------------------------------------------------------------ shell
        private void BuildShell()
        {
            _side = new Panel { Dock = DockStyle.Left, Width = Theme.D(262), BackColor = Theme.Side };
            _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Controls.Add(_host);
            Controls.Add(_side);
            _host.BringToFront();

            _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            _logPanel = BuildLog();
            _host.Controls.Add(_pageHost);
            _host.Controls.Add(_logPanel);
            _pageHost.BringToFront();
            _logPanel.Visible = false;   // a full-area tab now, shown only when Log is selected

            // sidebar header: logo + name
            var head = new Panel { Height = Theme.D(62), Dock = DockStyle.Top, BackColor = Theme.Side };
            head.Paint += (s, e) =>
            {
                var g = e.Graphics; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var glyph = Brand.Img("glyph_40.png");
                int gh = Theme.D(26);
                if (glyph != null) g.DrawImage(glyph, new Rectangle(Theme.D(18), (head.Height - gh) / 2, (int)(gh * glyph.Width / (float)glyph.Height), gh));
                TextRenderer.DrawText(g, Brand.Name, Theme.Semi(12.5f), new Rectangle(Theme.D(44), 0, head.Width - Theme.D(50), head.Height), Theme.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);   // author line lives on About only
            };

            // nav list: native scrolling, the classic bar pushed outside a clip, the same thin bar as the pages
            var mid = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Side };
            var clip = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Side };
            var list = new Panel { BackColor = Theme.Side, AutoScroll = true };
            clip.Controls.Add(list);
            clip.Resize += (s, e) => list.SetBounds(0, 0, clip.Width + SystemInformation.VerticalScrollBarWidth, Math.Max(1, clip.Height));
            var navBar = new ThinScrollBar(list) { Dock = DockStyle.Right, Width = Theme.D(8), BackColor = Theme.Side };
            mid.Controls.Add(clip); mid.Controls.Add(navBar);
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Theme.D(128), BackColor = Theme.Side };
            _side.Controls.Add(mid);
            _side.Controls.Add(foot);
            _side.Controls.Add(head);
            mid.BringToFront();

            int y = Theme.D(4), itemW = _side.Width - Theme.D(8);
            Func<Control, Control> put = c => { c.SetBounds(0, y, itemW, c.Height); c.Anchor = AnchorStyles.Left | AnchorStyles.Top; list.Controls.Add(c); y += c.Height; return c; };

            put(Nav("home", L.T("Home"), "home", new HomePage(this)));
            put(new NavHeader(L.T("System Configuration")));
            put(Nav("threads", L.T("Threads"), "threads", new ThreadsPage(this)));
            put(Nav("affinity", L.T("Affinity"), "affinity", new AffinityPage(this)));
            // Devices & Drivers (per-GPU / per-NIC pages) removed by request, along with GpuPage/NicPage/NvDrs/Nvml/
            // NetAdapter. grobyq now presents only the optimizer. The PCI list (Devices) stays: Affinity uses it for IRQ.
            put(new NavHeader(L.T("Maintenance")));
            put(Nav("clean", L.T("System cleanup"), "clean", new CleanupPage(this)));
            // Log is a first-class destination in Maintenance, next to System cleanup (full-area tab, not a bottom drawer).
            _logNav = new NavItem(L.T("Log"), "log");
            _logNav.Click += (s, e) => Go("log");
            put(_logNav);

            // footer: settings, about
            foot.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
            foot.Padding = new Padding(0, Theme.D(6), 0, 0);
            var settings = Nav("settings", L.T("Settings"), "settings", new SettingsPage(this));
            settings.Dock = DockStyle.Top;
            var about = Nav("about", L.T("About"), "about", new AboutPage(this));
            about.Dock = DockStyle.Top;
            foot.Controls.Add(about);
            foot.Controls.Add(settings);
        }

        private NavItem Nav(string key, string text, string icon, Page page)
        {
            var n = new NavItem(text, icon);
            n.Click += (s, e) => Go(key);
            if (text.Length > 30) Tips.Set(n, text);
            _nav[key] = n;
            page.Visible = false;
            _pages[key] = page;
            _pageHost.Controls.Add(page);
            return n;
        }

        public void DemoScroll(int y) { Page p; if (_pages.TryGetValue(_cur, out p)) p.ScrollTo(y); }
        public void DemoTab(string key) { Page p; if (_pages.TryGetValue(_cur, out p)) p.ShowSection(key); }
#if DEMO
        public string DemoDump() { Page p; return _pages.TryGetValue(_cur, out p) ? p.DebugDump() : ""; }
        public Page DemoPage { get { Page p; return _pages.TryGetValue(_cur, out p) ? p : null; } }
        public void DemoLog() { Go("log"); }
#endif
        public void DemoTray() { try { _tray.ContextMenuStrip.Show(this, new Point(ClientSize.Width / 2, ClientSize.Height / 3)); } catch { } }

        public void Go(string key)
        {
            if (key == "log") { ShowLog(); return; }
            if (!_pages.ContainsKey(key)) return;
            if (_cur == key) return;
            foreach (var kv in _nav) kv.Value.Selected = kv.Key == key;
            if (_logNav != null) _logNav.Selected = false;
            _cur = key;
            var p = _pages[key];
            _pageHost.SuspendLayout();
            try
            {
                // show the new page first, then hide every other surface: no blank frame in between (composited pages).
                // "every other" (not just the previous page) because the Log tab may have been covering a still-visible page.
                p.Visible = true; p.BringToFront();
                _pageHost.BringToFront();
                if (_logPanel != null) _logPanel.Visible = false;
                foreach (var kv in _pages)
                    if (kv.Value != p && kv.Value.Visible) { try { kv.Value.OnLeave(); } catch { } kv.Value.Visible = false; }
            }
            finally { _pageHost.ResumeLayout(true); }
            try { p.OnEnter(); } catch (Exception ex) { Log("[!!] " + ex.Message); }
        }

        // The Log is a full-area destination (Maintenance section), not a bottom drawer. It overlays the page host while
        // selected; leaving it (Go to any page) hides it again. The underlying page stays as-is behind the opaque panel.
        private void ShowLog()
        {
            if (_cur == "log") return;
            foreach (var kv in _nav) kv.Value.Selected = false;
            if (_logNav != null) { _logNav.Selected = true; _logNav.Badge = false; }
            _cur = "log";
            _logPanel.Visible = true;
            _logPanel.BringToFront();
            Flush(); _log.SetLines(_logLines, true);
        }

        private Panel BuildLog()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.LogBg };
            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.D(30), BackColor = Theme.Side };
            bar.Paint += (s, e) => TextRenderer.DrawText(e.Graphics, L.T("LOG  ·  every Apply is written here (A/B evidence)"), Theme.Semi(8.5f), new Point(Theme.D(16), Theme.D(8)), Theme.Dim);
            var hide = new Btn(L.T("Hide"), Btn.Style.Ghost, Theme.D(60)) { Height = Theme.D(26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var clear = new Btn(L.T("Clear"), Btn.Style.Ghost, Theme.D(60)) { Height = Theme.D(26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var copy = new Btn(L.T("Copy"), Btn.Style.Ghost, Theme.D(60)) { Height = Theme.D(26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            bar.Controls.Add(hide); bar.Controls.Add(clear); bar.Controls.Add(copy);
            bar.Resize += (s, e) =>
            {
                int x = bar.Width - Theme.D(8);
                foreach (var b in new[] { hide, clear, copy }) { x -= b.Width; b.Location = new Point(x, Theme.D(2)); x -= Theme.D(4); }
            };
            hide.Click += (s, e) => Go("threads");                       // leave the log tab, back to the main screen
            clear.Click += (s, e) => { _logLines.Clear(); Flush(); };
            copy.Click += (s, e) => { try { Clipboard.SetText(string.Join("\r\n", _logLines)); } catch { } };
            _log = new LogView { Dock = DockStyle.Fill, BackColor = Theme.LogBg, PlainColor = Theme.Ok };     // same thin bars as the pages
            panel.Controls.Add(_log); panel.Controls.Add(bar);
            _log.BringToFront();
            return panel;
        }

        // ------------------------------------------------------------------ tray
        private void BuildTray()
        {
            _tray = new NotifyIcon { Icon = Brand.Tray(Theme.Bad), Text = Brand.Name, Visible = true };
            BuildTrayMenu();
            _tray.DoubleClick += (s, e) => { if (Visible) Hide(); else ShowPanel(); };
        }

        // Dark menu in the card style: open, the optimizer switch (check = armed), exit.
        private void BuildTrayMenu()
        {
            var old = _tray.ContextMenuStrip;
            var menu = TrayMenu.Create();
            menu.Items.Add(TrayMenu.Item(L.T("Open grobyq"), (s, e) => ShowPanel()));
            menu.Items.Add(new ToolStripSeparator());
            _miActive = TrayMenu.Item(L.T("Thread optimizer active"), (s, e) => SetActive(!_active));
            menu.Items.Add(_miActive);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(TrayMenu.Item(L.T("Exit"), (s, e) => { _reallyExit = true; Close(); }));
            _tray.ContextMenuStrip = menu;
            if (old != null) { try { old.Dispose(); } catch { } }
            if (_miActive != null) _miActive.Checked = _active;
        }

        private void ShowPanel() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        // ------------------------------------------------------------------ engine
        // The game mask the engine gets: base rule + Cores-tab edits + interrupt LPs (GameMask). A change the user did
        // not make on the Cores tab (an interrupt moved on the Interrupts tab, another tool) is logged once.
        private string _gameRuleLogged;
        public string GameRule()
        {
            var t = ThreadsPage.SafeTopo();
            if (t == null) return Settings.Get("Mask");
            var i = GameMask.Info(t);
            string rule = i.Final == i.Base ? i.BaseRule : Masks.Hex(i.Final);
            string sig = rule + "|" + i.IrqDrop + "|" + i.IrqIgnored;
            if (sig != _gameRuleLogged)
            {
                if (_gameRuleLogged != null || i.Adjusted || i.IrqIgnored)
                    Log(string.Format("[AF] game mask: {0} LP {1}  (base {2}{3}{4}{5})", Masks.Hex(i.Final), Masks.Ranges(i.Final), i.BaseRule,
                        i.Add != 0 ? ", +LP " + Masks.Ranges(i.Add) : "", i.Drop != 0 ? ", -LP " + Masks.Ranges(i.Drop) : "",
                        i.IrqDrop != 0 ? ", interrupts -LP " + Masks.Ranges(i.IrqDrop) : i.IrqIgnored ? ", interrupt LPs kept: taking them would leave the game less than half" : ""));
                _gameRuleLogged = sig;
            }
            return rule;
        }

        public ApplyOptions Opts()
        {
            return new ApplyOptions
            {
                MaskName = GameRule(),
                BgMaskName = GameMask.ManualRule(ThreadsPage.SafeTopo()),
                TopN = Math.Max(1, Math.Min(32, Settings.GetInt("TopN", 4))),
                SkipCpu0 = Settings.GetBool("SkipCpu0"),
                DoThreads = Settings.GetBool("DoThreads"),
                DoBackground = Settings.GetBool("DoBackground"),
                BgAffinity = Settings.GetBool("BgAffinity"),
                BgForeground = Settings.GetBool("BgForeground"),
                DoAffRules = Settings.GetBool("DoAffRules"),
                Warmup = false
            };
        }

        public void ApplyNow()
        {
            try
            {
                Log(Eng.ApplyAll(Opts()));
                _liveTarget = Eng.LiveTarget;
                foreach (var rep in Eng.LastReports)
                    Log(string.Format("[==] {0} L{1} mask {2} [{3}] named {4} cycle {5}{6}",
                        rep.Target, rep.HandleLevel, rep.MaskHex, rep.MaskLps, rep.NamedHits, rep.CycleFallbackTids.Count,
                        rep.Failures.Count > 0 ? " FAIL:" + string.Join(",", rep.Failures) : ""));
            }
            catch (Exception ex) { Log("[!!] " + ex.Message); }
            SetState();
        }

        private int _applyRunning;   // 0/1 guard: a periodic re-apply cycle is running on a worker; overlapping ticks skip

        // Lever 2: the periodic re-apply runs OFF the UI thread. ApplyAll (process/thread enumeration + priority set) must
        // never sit on the WinForms message pump. A background worker at Normal priority runs it (so its burst cannot
        // starve the game it is tuning), then marshals only the log/state back. The Engine serializes its own state, so
        // UI-thread Engine calls (live Snapshot, Revert on disable) stay race-free against the worker.
        public void ApplyNowAsync()
        {
            if (Interlocked.CompareExchange(ref _applyRunning, 1, 0) != 0) return;   // previous cycle still running: skip
            var opts = Opts();
            var th = new Thread(() =>
            {
                List<string> log = null; List<ApplyReport> reps = null; string live = null; string err = null;
                try { log = Eng.ApplyAll(opts); live = Eng.LiveTarget; reps = new List<ApplyReport>(Eng.LastReports); }
                catch (Exception ex) { err = ex.Message; }
                finally
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (err != null) Log("[!!] " + err);
                            else
                            {
                                if (log != null) Log(log);
                                _liveTarget = live;
                                if (reps != null) foreach (var rep in reps)
                                    Log(string.Format("[==] {0} L{1} mask {2} [{3}] named {4} cycle {5}{6}",
                                        rep.Target, rep.HandleLevel, rep.MaskHex, rep.MaskLps, rep.NamedHits, rep.CycleFallbackTids.Count,
                                        rep.Failures.Count > 0 ? " FAIL:" + string.Join(",", rep.Failures) : ""));
                                SetState();
                            }
                        }));
                    }
                    catch { }   // form closing / no handle: drop the UI update
                    Interlocked.Exchange(ref _applyRunning, 0);
                }
            }) { IsBackground = true, Priority = ThreadPriority.Normal };
            th.Start();
        }

        public void SetActive(bool on)
        {
            if (on == _active) return;
            _active = on;
            if (on) { Log("=== ENABLED ==="); ApplyNow(); }
            else { Log("=== DISABLED ==="); try { Log(Eng.Revert()); } catch (Exception ex) { Log("[!!] " + ex.Message); } _liveTarget = ""; }
            SetState();
        }

        public string StateName { get { return _active ? (string.IsNullOrEmpty(_liveTarget) ? "ARMED" : "ACTIVE") : "OFF"; } }
        public string StateText { get { string st = StateName; return st == "ACTIVE" ? L.T("ACTIVE") : st == "ARMED" ? L.T("ARMED") : L.T("OFF"); } }

        public void SetState()
        {
            string st = StateName;
            Color c = st == "ACTIVE" ? Theme.Ok : st == "ARMED" ? Theme.Warn : Theme.Bad;
            try { _tray.Icon = Brand.Tray(c); } catch { }
            _tray.Text = Trunc(st == "ACTIVE" ? Brand.Name + " - " + L.T("ACTIVE") + ": " + _liveTarget : st == "ARMED" ? Brand.Name + " - " + L.T("armed, waiting for a game") : Brand.Name + " - " + L.T("off"));
            if (_miActive != null) _miActive.Checked = _active;
            var h = StateChanged; if (h != null) h();
        }

        public void NotifyMask() { var h = MaskChanged; if (h != null) h(); }
        // switches changed outside their own page (a layout template, a profile): pages re-read their toggles
        public void NotifySettings() { var h = SettingsChanged; if (h != null) h(); }

        private void Tick(object sender, EventArgs e)
        {
            try
            {
                _tick++;
                var running = Eng.Targets.Where(t => ByNameCount(t) > 0).ToList();
                string now = string.Join(", ", running);
                if (_active && now != _liveTarget)
                {
                    _liveTarget = now;
                    if (now.Length > 0) { Log("[>>] target detected: " + now); ApplyNow(); }
                    else
                    {
                        Log("[<<] no target running - back to armed");
                        if (Eng.BgOn) Log(Eng.BgRelease());          // the game is gone: background gets its priority and cores back
                    }
                    SetState();
                }

                if (_active && now.Length > 0 && Settings.GetBool("DoBackground") && Settings.GetBool("BgForeground"))
                {
                    bool fg = Eng.GameForeground();
                    if (fg && !Eng.BgOn) { Log(Eng.BgApply(Settings.GetBool("BgAffinity"), GameMask.ManualRule(ThreadsPage.SafeTopo()))); SetState(); }
                    else if (!fg && Eng.BgOn) { Log(Eng.BgRelease()); SetState(); }
                }

                if (_active && Settings.GetBool("PeriodicReapply") && now.Length > 0)
                {
                    int period = Math.Max(1000, Settings.GetInt("Period", 2000));
                    int every = Math.Max(1, period / 1000);
                    if (_tick % every == 0) ApplyNowAsync();   // Lever 2: periodic re-apply off the UI thread
                }

                if (_tick % 60 == 0) Eng.Prune();
                if (Visible && WindowState != FormWindowState.Minimized)
                {
                    Page p; if (_pages.TryGetValue(_cur, out p)) p.OnTick(_tick);
                }
            }
            catch { }
        }

        public void RetargetWatchList()
        {
            Eng.Targets = Settings.WatchList();
            if (Eng.Targets.Length == 0) Eng.Targets = new[] { "cs2.exe" };
            Log("[i] watch list: " + string.Join(" ", Eng.Targets));
        }

        // ------------------------------------------------------------------ autostart
        public void Autostart(bool enable)
        {
            string exe = Application.ExecutablePath;
            try
            {
                if (enable)
                {
                    Run("schtasks", string.Format("/Delete /TN \"{0}\" /F", Brand.OldTaskName));   // v2 task would start the old exe
                    Run("schtasks", string.Format("/Create /TN \"{0}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{1}\\\" /tray\" /F", Brand.TaskName, exe));
                    Log("[i] autostart task created (runs " + System.IO.Path.GetFileName(exe) + " /tray at logon)");
                }
                else { Run("schtasks", string.Format("/Delete /TN \"{0}\" /F", Brand.TaskName)); Log("[i] autostart task removed"); }
            }
            catch (Exception ex) { Log("[!!] " + ex.Message); }
        }

        private static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) p.WaitForExit(5000);
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && Settings.GetBool("CloseToTray") && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            try { _timer.Stop(); } catch { }
            // Restore on exit: everything grobyq changed (a target, a manual edit from Live threads, a preset pass, the
            // background layer) goes back to its captured baseline, optimizer on or off.
            if (Settings.GetBool("RestoreOnExit") && (_active || Eng.HasChanges)) { try { Eng.Revert(); } catch { } }
            try { IrqTrace.StopStale(); } catch { }        // a live check running now: its kernel session ends with the app
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
        }

        // ------------------------------------------------------------------ log
        public void Log(IEnumerable<string> lines) { foreach (var l in lines) LogOne(l); Flush(); }
        public void Log(string line) { LogOne(line); Flush(); }
        private void LogOne(string l)
        {
            _logLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + l); while (_logLines.Count > 800) _logLines.RemoveAt(0);
            if (_logNav != null && _logPanel != null && !_logPanel.Visible && _started && (l.StartsWith("[!!]") || l.StartsWith("["))) _logNav.Badge = true;
        }
        private void Flush()
        {
            if (_log == null || _logPanel == null || !_logPanel.Visible) return;
            _log.SetLines(_logLines, false);       // follows the tail while the view is at the bottom
        }

        private void LogTopology()
        {
            try
            {
                var t = Topo.Live();
                Log(string.Format("CPU: {0} LP / {1} physical cores, SMT={2}", t.LogicalCount, t.PhysicalCount, t.SMT));
                if (t.EffClasses.Count > 1) Log("Hybrid CPU: only performance cores are used for Ideal Processor and SmtOff*.");
                if (t.Groups > 1) Log(string.Format("[!!] {0} processor groups detected. CPU Sets are the source of truth.", t.Groups));
                var doms = t.LLCGroups.Select((l, i) =>
                {
                    ulong dm = 0; foreach (var s in t.Sets.Where(s => s.LLC == l)) dm |= (ulong)1 << s.LP;
                    return string.Format("[{0}: {1}, LP {2}]", i, t.L3Text(l), Masks.MaskLps(dm));
                });
                Log(string.Format("L3 domains: {0}  {1}{2}", t.LLCGroups.Count, string.Join(" ", doms),
                    t.VCacheLlc.HasValue ? "  V-Cache = domain " + t.LLCGroups.IndexOf(t.VCacheLlc.Value) : ""));
                Log("Recommended mask for this CPU: " + Masks.Recommended(t) + "   (current: " + Settings.Get("Mask") + ")");
            }
            catch (Exception ex) { Log("[!!] topology: " + ex.Message); }
        }

        public static int ByNameCount(string exe)
        {
            try { return Process.GetProcessesByName(exe.ToLowerInvariant().EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe).Length; }
            catch { return 0; }
        }
        private static string Trunc(string s) { return s.Length > 63 ? s.Substring(0, 60) + "..." : s; }
    }
}
