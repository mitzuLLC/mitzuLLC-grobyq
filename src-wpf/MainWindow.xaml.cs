using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Grobyq.Pages;

namespace Grobyq
{
    // Shell: sidebar navigation, page host, tray, close-to-tray / restore-on-exit and the language rebuild.
    // The engine loop itself lives in Optimizer.
    public partial class MainWindow : Window
    {
        public static MainWindow Current { get; private set; }

        private readonly Dictionary<string, Func<UserControl>> _factories;
        private readonly Dictionary<string, UserControl> _cache = new Dictionary<string, UserControl>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NavItem> _nav = new Dictionary<string, NavItem>(StringComparer.OrdinalIgnoreCase);
        private string _current = "";
        private readonly Tray _tray;
        private readonly DispatcherTimer _ui;
        private bool _reallyExit, _exited, _started;
        private static bool _shutdownStarted;
        private int _tick;

        public MainWindow(bool startTray)
        {
            Current = this;
            InitializeComponent();
            FitToScreen();
            FontFamily = new FontFamily(L.Family);

            // Pages are created on first visit and cached; a language switch drops the cache and rebuilds them.
            _factories = new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
            {
                { "home",     () => new HomePage() },
                { "threads",  () => new ThreadsPage() },
                { "affinity", () => new AffinityPage() },
                { "clean",    () => new CleanupPage() },
                { "log",      () => new JournalPage() },
                { "settings", () => new SettingsPage() },
                { "about",    () => new AboutPage() },
            };

            SeedLog();
            new Optimizer();                       // engine watch loop; sets Optimizer.Instance (arms only if "Enable at start" is on)
            Optimizer.Instance.StateChanged += OnState;

            _tray = new Tray();
            _tray.OpenRequested += ShowPanel;
            _tray.DoubleClicked += () => { if (IsVisible && WindowState != WindowState.Minimized) Hide(); else ShowPanel(); };
            _tray.GameToggled += on => OptUi.Game(on);
            _tray.SystemToggled += on => { if (!OptUi.System(on, null)) OnState(); };    // declined = the menu shows the real state
            _tray.AboutRequested += () => { ShowPanel(); Navigate("about"); };
            _tray.ExitRequested += ExitFromTray;

            BuildSidebar();
            Navigate("home");
            OnState();
            Shell.Start();                         // system overview + license check on workers

            AppLog.Entries.CollectionChanged += OnLogChanged;
            _ui = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ui.Tick += (s, e) => UiTick();
            _ui.Start();
            var arm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            arm.Tick += (s, e) => { arm.Stop(); _started = true; };
            arm.Start();

            if (startTray) _tray.Balloon(L.T("Running in the tray. Double click the icon to open the panel."));
        }

        // Window size that always fits the screen (taskbar excluded), so nothing hides below the edge.
        private void FitToScreen()
        {
            try
            {
                var wa = SystemParameters.WorkArea;
                Width = Math.Max(760, Math.Min(1340, wa.Width - 24));
                Height = Math.Max(520, Math.Min(880, wa.Height - 24));
                MinWidth = Math.Min(1000, wa.Width);
                MinHeight = Math.Min(600, wa.Height);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WinChrome.Apply(new WindowInteropHelper(this).Handle);
        }

        // ------------------------------------------------------------------ log seed (English on purpose: A/B evidence)
        private void SeedLog()
        {
            AppLog.Add(Brand.Name + " " + Brand.VersionText + " " + Brand.Credit + "  -  " + Brand.XUrl);
            AppLog.Add("THREADS optimizes the game. AFFINITY pins cores and interrupts.");
            if (Settings.MigrationNote.Length > 0) AppLog.Add("[i] " + Settings.MigrationNote);
            try
            {
                var t = Topo.Live();
                AppLog.Add(string.Format("CPU: {0} LP / {1} physical cores, SMT={2}", t.LogicalCount, t.PhysicalCount, t.SMT));
                if (t.EffClasses.Count > 1) AppLog.Add("Hybrid CPU: only performance cores are used for Ideal Processor and SmtOff*.");
                if (t.Groups > 1) AppLog.Add(string.Format("[!!] {0} processor groups detected. CPU Sets are the source of truth.", t.Groups));
                var doms = t.LLCGroups.Select((l, i) =>
                {
                    ulong dm = 0; foreach (var s in t.Sets.Where(s => s.LLC == l)) dm |= (ulong)1 << s.LP;
                    return string.Format("[{0}: {1}, LP {2}]", i, t.L3Text(l), Masks.MaskLps(dm));
                });
                AppLog.Add(string.Format("L3 domains: {0}  {1}{2}", t.LLCGroups.Count, string.Join(" ", doms),
                    t.VCacheLlc.HasValue ? "  V-Cache = domain " + t.LLCGroups.IndexOf(t.VCacheLlc.Value) : ""));
            }
            catch (Exception ex) { AppLog.Add("[!!] topology: " + ex.Message); }
            if (!Settings.GetBool("GameOpt") && !Settings.GetBool("SystemThreadOpt") && !Settings.GetBool("AutoEnable"))
                AppLog.Add("[i] manual mode: nothing is changed until you switch an optimizer on (Threads or the tray menu)");
        }

        // ------------------------------------------------------------------ sidebar
        private void BuildSidebar()
        {
            _nav.Clear();
            var dock = new DockPanel { LastChildFill = true };

            // header: logo + name (the author line lives on About only)
            var head = new Grid { Height = 62 };
            var glyph = Ui.Picture("glyph_40.png", 26);
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(18, 0, 0, 0);
            var name = Ui.Semi(Brand.Name, 12.5, "Fg");
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(44, 0, 0, 0);
            head.Children.Add(glyph);
            head.Children.Add(name);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            // footer: settings, about (hairline on top)
            var foot = new StackPanel { Margin = new Thickness(0, 6, 8, 0) };
            foot.Children.Add(Nav("settings", L.T("Settings"), "settings"));
            foot.Children.Add(Nav("about", L.T("About"), "about"));
            var footWrap = new Border { Height = 128, BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = foot, SnapsToDevicePixels = true };
            DockPanel.SetDock(footWrap, Dock.Bottom);
            dock.Children.Add(footWrap);

            var list = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };
            list.Children.Add(Nav("home", L.T("Home"), "home"));
            list.Children.Add(new NavHeader(L.T("System Configuration")));
            list.Children.Add(Nav("threads", L.T("Threads"), "threads"));
            list.Children.Add(Nav("affinity", L.T("Affinity"), "affinity"));
            list.Children.Add(new NavHeader(L.T("Maintenance")));
            list.Children.Add(Nav("clean", L.T("System cleanup"), "clean"));
            list.Children.Add(Nav("log", L.T("Log"), "log"));
            dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });

            SideHost.Child = dock;
            Highlight(_current);
        }

        private NavItem Nav(string key, string text, string icon)
        {
            var n = new NavItem(key, text, icon);
            n.Clicked += () => Navigate(key);
            _nav[key] = n;
            return n;
        }

        public void Navigate(string tag)
        {
            if (tag == null || !_factories.ContainsKey(tag)) return;
            if (string.Equals(tag, _current, StringComparison.OrdinalIgnoreCase) && PageHost.Content != null) return;
            UserControl old;
            if (_cache.TryGetValue(_current ?? "", out old) && old is PageBase ob) { try { ob.OnLeave(); } catch { } }
            UserControl page;
            if (!_cache.TryGetValue(tag, out page)) { page = _factories[tag](); _cache[tag] = page; }
            PageHost.Content = page;
            _current = tag;
            Highlight(tag);
            if (tag == "log") { NavItem ln; if (_nav.TryGetValue("log", out ln)) ln.Badge = false; }
            if (page is PageBase pb) { try { pb.OnEnter(); } catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); } }
        }

        private void Highlight(string tag)
        {
            foreach (var kv in _nav) kv.Value.Selected = string.Equals(kv.Key, tag, StringComparison.OrdinalIgnoreCase);
        }

        // Language switch: the sidebar, every page and the tray menu in the new language; the current page stays.
        public void RebuildUi()
        {
            string keep = string.IsNullOrEmpty(_current) ? "home" : _current;
            foreach (var p in _cache.Values) if (p is PageBase pb) { try { pb.OnLeave(); } catch { } }
            _cache.Clear();
            PageHost.Content = null;
            FontFamily = new FontFamily(L.Family);
            _current = "";
            BuildSidebar();
            try { _tray.BuildMenu(); } catch { }
            Navigate(keep);
            OnState();
        }

        // ------------------------------------------------------------------ state / tray
        private void OnState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)OnState); return; }
            var o = Optimizer.Instance;
            if (o == null) return;
            try { _tray.SetState(o.StateName, o.LiveTarget, o.GameOn, o.SysOn); } catch { }
        }

        public void ShowPanel()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;     // surface it even when another app has the foreground
            Focus();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && Settings.GetBool("CloseToTray")) Hide();
        }

        private void UiTick()
        {
            _tick++;
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            UserControl p;
            if (_cache.TryGetValue(_current ?? "", out p) && p is PageBase pb) { try { pb.OnTick(_tick); } catch { } }
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_started || e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            if (string.Equals(_current, "log", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var it in e.NewItems)
            {
                var le = it as LogEntry;
                if (le != null && le.Raw != null && le.Raw.StartsWith("[", StringComparison.Ordinal))
                {
                    NavItem ln; if (_nav.TryGetValue("log", out ln)) ln.Badge = true;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ close / exit
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_reallyExit && Settings.GetBool("CloseToTray")) { e.Cancel = true; Hide(); return; }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            ExitNow();
            StartShutdown();
        }

        private void ExitFromTray()
        {
            ExitNow();
            StartShutdown();
        }

        private static void StartShutdown()
        {
            if (_shutdownStarted) return;
            _shutdownStarted = true;
            try { Application.Current.Shutdown(0); } catch { }
        }

        // Restore on exit: everything grobyq changed goes back to its captured baseline, optimizer on or off.
        // Runs once (window close, tray Exit, Windows logoff / shutdown).
        public void ExitNow()
        {
            if (_exited) return;
            _exited = true;
            _reallyExit = true;
            try { _ui.Stop(); } catch { }
            var o = Optimizer.Instance;
            if (o != null)
            {
                try { o.Stop(); } catch { }
                try { o.Drain(3000); } catch { }          // queued apply / restore jobs first, so the final restore lands last
                if (Settings.GetBool("RestoreOnExit") && (o.Active || o.Eng.HasChanges)) { try { o.Eng.Revert(); } catch { } }
                if (Settings.GetBool("RestoreOnExit") && o.Eng.HasClosed) { try { o.Eng.RelaunchClosed(); } catch { } }   // game mode: reopen what it closed
            }
            try { IrqTrace.StopStale(); } catch { }
            try { _tray.Dispose(); } catch { }
        }
    }
}
