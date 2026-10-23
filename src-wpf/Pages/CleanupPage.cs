using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Grobyq.Pages
{
    // System cleanup: Windows and browser rows, app caches (the catalog apps found on this PC, CS2 replays, other
    // Chromium-based apps), the Windows tools (DISM, Disk Cleanup handlers, hibernation file, event logs). One Scan /
    // Clean selected bar pinned under the page acts on every ticked row. Nothing is deleted until Clean selected is
    // pressed and confirmed; a running app is skipped, never closed.
    public sealed class CleanupPage : PageBase
    {
        private readonly List<CleanItem> _items;
        private readonly List<CheckRow> _rows = new List<CheckRow>();      // one row per category (Data = CleanItem)
        private readonly List<CheckRow> _hRows = new List<CheckRow>();     // Disk Cleanup handlers (Data = Handler)
        private readonly StackPanel _hList;
        private readonly TextBlock _hEmpty, _total, _dismInfo, _hiberInfo;
        private readonly Button _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun, _hiberBtn;
        private readonly CheckBox _resetBase;
        private List<Cleaner.Handler> _handlers = new List<Cleaner.Handler>();
        private bool _loaded;
        // One cleanup / DISM / Disk Cleanup at a time for the whole app (a language switch rebuilds this page while a
        // worker still runs; the new page must not start a second one).
        private static volatile bool _busy;

        public CleanupPage() : base(800)
        {
            List<AppDef> catalog;
            List<CleanItem> win, apps;
            try { catalog = AppCaches.Catalog(); } catch { catalog = new List<AppDef>(); }
            try { win = Cleaner.Items(); } catch { win = new List<CleanItem>(); }
            try { apps = Cleaner.AppItems(catalog); } catch { apps = new List<CleanItem>(); }
            _items = win.Concat(apps).ToList();

            Title(L.T("System cleanup"));
            Add(Ui.Text(L.T("Removes what Windows and apps leave behind: temp files, update caches, old logs, error reports, dumps, shader, browser and app caches. Sign-ins, settings and your files stay. Scan first to see the sizes; nothing is deleted until you press Clean selected. Files in use are skipped, never forced."), 9.5, "Dim"), 4);

            // ---- Windows and browsers ----
            Heading(L.T("Windows and browsers"), true);
            var card = Box();
            foreach (var it in win) card.Add(MakeRow(it));
            if (_rows.Count > 0) _rows[_rows.Count - 1].Divider = false;
            var amber = card.Add(Ui.Text(L.T("Amber = read the warning first (unchecked by default). grobyq runs as administrator, so Windows folders are reachable; anything that is open by a program stays."), 9, "Dim"));
            amber.Margin = new Thickness(0, 8, 0, 8);

            // ---- app caches ----
            Heading(L.T("App caches"), false);
            var ac = Box();
            int found = apps.Count(a => a.Key.StartsWith("app:", StringComparison.Ordinal) && a.Key != "app:other");
            var intro = ac.Add(Ui.Text(L.F("Found {0} of {1} supported apps on this PC. Only caches, old logs and crash reports are removed: sign-ins, settings, chats, saves and downloads stay. A running app is skipped - close it first; grobyq never closes programs.", found, catalog.Count), 9, "Dim"));
            intro.Margin = new Thickness(0, 8, 0, 2);
            foreach (var g in AppCaches.GroupOrder)
            {
                var inGroup = apps.Where(a => a.AppGroup == g).ToList();
                if (inGroup.Count == 0) continue;
                var gh = ac.Add(Ui.Semi(AppCaches.GroupTitle(g), 9.5, "Faint"));
                gh.Margin = new Thickness(10, 12, 0, 2);
                CheckRow last = null;
                foreach (var it in inGroup) last = ac.Add(MakeRow(it));
                if (last != null) last.Divider = false;
            }
            var sup = ac.Add(Ui.Text(L.F("Supported: {0}.", string.Join(", ", catalog.Select(c => c.Name))), 8.5, "Faint"));
            sup.Margin = new Thickness(0, 10, 0, 8);

            // ---- Windows tools ----
            Heading(L.T("Windows tools (deep)"), false);
            var dc = Box();
            var di = dc.Add(Ui.Text(L.T("Component store (WinSxS) cleanup. DISM removes superseded components with Microsoft's own servicing stack - the only safe way to shrink WinSxS. Deleting files in WinSxS by hand breaks Windows Update."), 9, "Dim"));
            di.Margin = new Thickness(0, 8, 0, 4);
            _dismAnalyze = Ui.Btn(L.T("Analyze"));
            _dismRun = Ui.Btn(L.T("Clean component store"), "BtnAccent");
            dc.Add(Ui.Strip(44, _dismAnalyze, _dismRun));
            _resetBase = Ui.Switch(false);
            dc.Add(Ui.Row(L.T("Also /ResetBase"), L.T("Frees more space, but every installed update becomes permanent (cannot be uninstalled). Leave off unless disk space is critical."), _resetBase, false));
            _dismInfo = dc.Add(Ui.Text(L.T("Takes a few minutes; the progress is written to the log."), 9, "Faint"));
            _dismInfo.Margin = new Thickness(0, 0, 0, 8);
            _dismAnalyze.Click += (s, e) => RunDism(true);
            _dismRun.Click += (s, e) => RunDism(false);

            var hc = Box();
            var hi = hc.Add(Ui.Text(L.T("Windows Disk Cleanup handlers (cleanmgr /sagerun). This is how Windows itself removes Windows.old, superseded updates and upgrade leftovers. Tick what to remove and press Run; a small Disk Cleanup window shows the progress."), 9, "Dim"));
            hi.Margin = new Thickness(0, 8, 0, 4);
            _hEmpty = hc.Add(Ui.Text(L.T("reading…"), 9, "Faint"));
            _hList = hc.Add(new StackPanel());
            _cmRun = Ui.Btn(L.T("Run Disk Cleanup"), "BtnAccent");
            _cmRun.Click += (s, e) => RunCleanmgr();
            var hs = Ui.Strip(48, _cmRun);
            hs.Margin = new Thickness(0, 8, 0, 4);
            hc.Add(hs);

            var hb = Box();
            _hiberBtn = Ui.Btn(L.T("Turn off hibernation"), "BtnOutline");
            _hiberBtn.Click += (s, e) => ToggleHibernate();
            hb.Add(Ui.Row(L.T("Hibernation file (hiberfil.sys)"), L.T("Windows keeps the RAM contents for Hibernate and Fast Startup in C:\\hiberfil.sys. Turning hibernation off deletes it: Hibernate and Fast Startup stop working and every shutdown is a full one. On a laptop the critical battery action can no longer be Hibernate. It can be turned back on here at any time."), _hiberBtn, false));
            _hiberInfo = hb.Add(Ui.Text("", 9, "Faint"));
            _hiberInfo.Margin = new Thickness(0, 0, 0, 8);
            UpdateHiber();

            var ec = Box();
            _evRun = Ui.Btn(L.T("Clear event logs"), "BtnDanger");
            _evRun.Click += (s, e) => ClearEvents();
            ec.Add(Ui.Row(L.T("Windows event logs"), L.T("Empties every Event Viewer log. Frees little space and removes the history you need to diagnose crashes, WHEA errors and driver problems. Only for a clean A/B start."), _evRun, false));

            // ---- never ----
            Heading(L.T("What is not cleaned"), false);
            var nc = Box();
            var nt = nc.Add(Ui.Text(L.T("Not offered on purpose:"), 10, "Fg"));
            nt.Margin = new Thickness(0, 8, 0, 4);
            foreach (var line in new[]
            {
                L.T("• Sign-ins, cookies, history and saved passwords of browsers and apps: cleaning them only logs you out."),
                L.T("• Spotify downloads, Telegram chats and keys, Steam settings, game saves and recordings."),
                L.T("• Anti-cheat folders (FACEIT, Vanguard, Easy Anti-Cheat, BattlEye): never touched."),
                L.T("• Prefetch: Windows uses it to start apps faster. Emptying it makes the next launches slower and brings nothing back (T3 folklore)."),
                L.T("• Deleting WinSxS, System32 or DriverStore files by hand: breaks updates and driver installs. Use the DISM and Disk Cleanup buttons instead."),
                L.T("• Registry cleaners: no measurable speed gain, real risk of broken apps."),
                L.T("• SoftwareDistribution\\DataStore: the update history database. Resetting it only makes sense when Windows Update is broken."),
                L.T("• Standby memory 'cleaners': Windows reuses standby memory instantly; flushing it just forces re-reads from disk."),
            })
            {
                var t = nc.Add(Ui.Text(line, 9.5, "Dim"));
                t.Margin = new Thickness(0, 2, 0, 2);
            }
            nc.Stack.Children.Add(new Border { Height = 8 });

            // ---- the Scan / Clean bar, pinned under the page ----
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _total = Ui.Semi("", 10, "Fg");
            _total.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(_total);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            _recommended = Ui.Btn(L.T("Select recommended"), "BtnOutline");
            _scan = Ui.Btn(L.T("Scan"), "BtnOutline", 110);
            _scan.Margin = new Thickness(8, 0, 0, 0);
            _clean = Ui.Btn(L.T("Clean selected"), "BtnDanger", 150);
            _clean.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(_recommended); btns.Children.Add(_scan); btns.Children.Add(_clean);
            Grid.SetColumn(btns, 1);
            bar.Children.Add(btns);
            Footer(bar);
            _scan.Click += (s, e) => ScanAll(null);
            _clean.Click += (s, e) => CleanSelected();
            _recommended.Click += (s, e) => { foreach (var r in _rows) { var ci = r.Data as CleanItem; if (ci != null) r.Checked = ci.Recommended; } UpdateTotal(); };

            UpdateTotal();
        }

        private CheckRow MakeRow(CleanItem it)
        {
            var row = new CheckRow(it.Title, it.Desc, it.Note, it.Caution) { Data = it, Value = "—" };
            row.Checked = it.Recommended;
            row.CheckedChanged += UpdateTotal;
            _rows.Add(row);
            return row;
        }

        private IEnumerable<CleanItem> Selected()
        {
            foreach (var r in _rows) if (r.Checked && r.Data is CleanItem ci) yield return ci;
        }

        private void UpdateTotal()
        {
            if (_total == null) return;
            var sel = Selected().ToList();
            long sum = sel.Where(i => i.Scanned).Sum(i => i.Size);
            bool all = sel.All(i => i.Scanned);
            _total.Text = sel.Count == 0 ? L.T("nothing selected") : (all ? "" : "≥ ") + L.F("selected: {0} · {1}", sel.Count, Cleaner.Human(sum));
        }

        private void SetBusy(bool b) { _busy = b; BusyUi(b); }
        private void BusyUi(bool b)
        {
            foreach (var x in new[] { _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun, _hiberBtn }) if (x != null) x.IsEnabled = !b;
            Cursor = b ? Cursors.AppStarting : null;
        }

        // a job started by the previous page instance (before a language switch): buttons follow the shared flag
        public override void OnTick(int tick)
        {
            bool b = _busy;
            if (_scan != null && _scan.IsEnabled == b) BusyUi(b);
            if (!b && !_loaded) OnEnter();
        }

        // Back on the UI thread (also if this page was replaced meanwhile: the log line and the busy flag still land).
        private void Post(Action a)
        {
            Action safe = () => { try { a(); } catch { } };
            try { var app = Application.Current; if (app != null) app.Dispatcher.BeginInvoke(safe); else _busy = false; } catch { _busy = false; }
        }

        private void ScanAll(IEnumerable<CleanItem> only)
        {
            if (_busy) return;
            var todo = (only ?? _items).ToList();
            SetBusy(true);
            foreach (var r in _rows) if (r.Data is CleanItem ci && todo.Contains(ci)) { r.ValueBrush = Ui.B("Dim"); r.Value = L.T("scanning…"); }
            var th = new Thread(() =>
            {
                try { Cleaner.BeginPass(); } catch { }
                foreach (var it in todo)
                {
                    try { Cleaner.Scan(it); } catch { }
                    var item = it;
                    Post(() => ShowSize(item));
                }
                Post(() => { SetBusy(false); UpdateTotal(); });
            }) { IsBackground = true };
            th.Start();
        }

        private void ShowSize(CleanItem it)
        {
            foreach (var r in _rows)
            {
                if (r.Data != it) continue;
                r.ValueBrush = it.Size > 500L * 1024 * 1024 ? Ui.B("Warn") : it.Size == 0 ? Ui.B("Faint") : Ui.B("Fg");
                r.Value = it.Special == "recycle" ? L.F("{0} · {1} items", Cleaner.Human(it.Size), it.Files) : L.F("{0} · {1} files", Cleaner.Human(it.Size), it.Files);
                string run = it.Running.Count == 0 ? "" : it.Running.Count == 1 ? L.F("{0} is running: close it to clean.", it.Running[0]) : L.F("Running: {0} - close them to clean.", string.Join(", ", it.Running));
                if (it.Key == "app:other")
                {
                    string found = it.Found.Count > 0 ? L.F("Found: {0}", string.Join(", ", it.Found)) : L.T("No other Chromium-based apps found.");
                    r.SetHint(run.Length > 0 ? found + " · " + run : found, run.Length > 0 ? "Warn" : "Dim");
                }
                else r.SetHint(run);
            }
        }

        private void CleanSelected()
        {
            if (_busy) return;
            var sel = Selected().ToList();
            if (sel.Count == 0) { Dlg.Info(null, L.T("Tick at least one category.")); return; }
            string list = string.Join("\n", sel.Select(i => "• " + i.Title + (i.Scanned ? "  (" + Cleaner.Human(i.Size) + ")" : "")));
            bool caution = sel.Any(i => i.Caution);
            if (!Dlg.Ask(null, L.T("Delete now?") + "\n\n" + list + (caution ? "\n\n" + L.T("Some categories have a warning - read it in the list.") : ""), caution)) return;
            // rows that ask again (CS2 replays): how many files, how big, which dates
            foreach (var it in sel.Where(i => i.AskFirst).ToList())
            {
                try { if (!it.Scanned) { Cleaner.BeginPass(); Cleaner.Scan(it); ShowSize(it); } } catch { }
                if (it.Files == 0) { sel.Remove(it); continue; }
                string q = L.F("Delete {0} replay files ({1}) downloaded between {2} and {3}? Valve keeps replays for about 30 days, so they cannot be downloaded again.",
                    it.Files, Cleaner.Human(it.Size), it.Oldest.ToString("d"), it.Newest.ToString("d"));
                if (!Dlg.Ask(null, q, true)) sel.Remove(it);
            }
            if (sel.Count == 0) return;
            SetBusy(true);
            AppLog.Add("[CL] cleanup started: " + string.Join(", ", sel.Select(i => i.Key)));
            var th = new Thread(() =>
            {
                long total = 0;
                try { Cleaner.BeginPass(); } catch { }
                foreach (var it in sel)
                {
                    try { total += Cleaner.Clean(it, s => AppLog.Add(s)); }
                    catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
                }
                long t = total;
                Post(() =>
                {
                    AppLog.Add("[CL] cleanup finished: " + Cleaner.Human(t) + " freed");
                    SetBusy(false);
                    Dlg.Info(null, L.F("Cleanup finished: {0} freed. Details are in the log.", Cleaner.Human(t)));
                    ScanAll(sel);
                });
            }) { IsBackground = true };
            th.Start();
        }

        // ---------------------------------------------------------------- hibernation file
        private void UpdateHiber()
        {
            long sz = -1;
            try { sz = Cleaner.HiberSize(); } catch { }
            if (sz >= 0)
            {
                _hiberInfo.Text = L.F("hiberfil.sys: {0}", Cleaner.Human(sz));
                _hiberBtn.Content = L.T("Turn off hibernation");
            }
            else
            {
                _hiberInfo.Text = L.T("Hibernation is off: there is no hiberfil.sys.");
                _hiberBtn.Content = L.T("Turn on hibernation");
            }
        }

        private void ToggleHibernate()
        {
            if (_busy) return;
            long sz = Cleaner.HiberSize();
            bool on = sz < 0;
            string q = on ? L.T("Turn hibernation back on? Windows creates hiberfil.sys again, and Hibernate and Fast Startup work again.")
                          : L.F("Turn hibernation off? hiberfil.sys ({0}) is deleted; Hibernate and Fast Startup stop working.", Cleaner.Human(sz));
            if (!Dlg.Ask(null, q, !on)) return;
            SetBusy(true);
            var th = new Thread(() =>
            {
                try { Cleaner.SetHibernate(on, s => AppLog.Add(s)); } catch (Exception ex) { AppLog.Add("[!!] powercfg: " + ex.Message); }
                Post(() => { SetBusy(false); UpdateHiber(); });
            }) { IsBackground = true };
            th.Start();
        }

        private void FillHandlers()
        {
            try { _handlers = Cleaner.Handlers(); } catch { _handlers = new List<Cleaner.Handler>(); }
            _hList.Children.Clear(); _hRows.Clear();
            _hEmpty.Visibility = _handlers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _hEmpty.Text = L.T("Disk Cleanup handlers not found on this Windows");
            foreach (var h in _handlers)
            {
                var row = new CheckRow(h.Title, h.Desc, null, h.Caution) { Data = h };
                row.Checked = h.Default;
                _hRows.Add(row);
                _hList.Children.Add(row);
            }
            if (_hRows.Count > 0) _hRows[_hRows.Count - 1].Divider = false;
        }

        private void RunDism(bool analyze)
        {
            if (_busy) return;
            bool reset = _resetBase.IsChecked == true;
            if (!analyze && !Dlg.Ask(null, reset ? L.T("Run DISM component cleanup with /ResetBase? Installed updates become permanent.") : L.T("Run DISM component store cleanup now? It takes a few minutes."), reset)) return;
            SetBusy(true);
            string args = analyze ? "/Online /Cleanup-Image /AnalyzeComponentStore" : "/Online /Cleanup-Image /StartComponentCleanup" + (reset ? " /ResetBase" : "");
            AppLog.Add("[CL] dism " + args);
            _dismInfo.Text = L.T("DISM is running…");
            var th = new Thread(() =>
            {
                var summary = new List<string>();
                Cleaner.RunTool("dism.exe", args, line =>
                {
                    if (line.StartsWith("[", StringComparison.Ordinal) && line.Contains("%")) return;       // progress bar lines
                    if (line.IndexOf(":", StringComparison.Ordinal) > 0 || line.Contains("operation")) { lock (summary) summary.Add(line); }
                    AppLog.Add("[DISM] " + line);
                }, 2 * 60 * 60 * 1000);
                Post(() =>
                {
                    SetBusy(false);
                    List<string> keep;
                    lock (summary) keep = summary.Where(l => l.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Recommended", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Размер", StringComparison.OrdinalIgnoreCase) >= 0).Take(8).ToList();
                    _dismInfo.Text = keep.Count > 0 ? string.Join("\n", keep) : L.T("DISM finished - see the log.");
                });
            }) { IsBackground = true };
            th.Start();
        }

        private void RunCleanmgr()
        {
            if (_busy) return;
            var keys = new List<string>();
            foreach (var r in _hRows) { var h = r.Data as Cleaner.Handler; if (h != null && r.Checked) keys.Add(h.Key); }
            if (keys.Count == 0) { Dlg.Info(null, L.T("Tick at least one item.")); return; }
            bool caution = _handlers.Where(h => keys.Contains(h.Key)).Any(h => h.Caution);
            if (!Dlg.Ask(null, L.T("Run Windows Disk Cleanup for the ticked items?") + "\n\n" + string.Join("\n", _handlers.Where(h => keys.Contains(h.Key)).Select(h => "• " + h.Title)), caution)) return;
            SetBusy(true);
            var th = new Thread(() =>
            {
                string err = Cleaner.RunDiskCleanup(keys, s => AppLog.Add(s));
                Post(() => { SetBusy(false); AppLog.Add(err.Length == 0 ? "[CL] Disk Cleanup finished" : "[!!] Disk Cleanup: " + err); });
            }) { IsBackground = true };
            th.Start();
        }

        private void ClearEvents()
        {
            if (_busy) return;
            if (!Dlg.Ask(null, L.T("Clear every Windows event log? The crash / WHEA history is lost."), true)) return;
            SetBusy(true);
            var th = new Thread(() =>
            {
                Cleaner.ClearEventLogs(s => AppLog.Add(s));
                Post(() => SetBusy(false));
            }) { IsBackground = true };
            th.Start();
        }

        public override void OnEnter()
        {
            if (_busy) BusyUi(true);
            if (_loaded || _busy) return;          // a job from before a language switch: scan once it ends
            _loaded = true;
            FillHandlers();
            ScanAll(null);
        }
    }
}
