using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Grobyq.Pages
{
    // System cleanup: file categories with a size scan, plus the Windows tools (DISM, Disk Cleanup handlers, event logs).
    // Nothing is deleted until a Clean / Run button is pressed and confirmed (the v3.6.8 page).
    public sealed class CleanupPage : PageBase
    {
        private readonly List<CleanItem> _items;
        private readonly List<CheckRow> _rows = new List<CheckRow>();      // one row per file category (Data = CleanItem)
        private readonly List<CheckRow> _hRows = new List<CheckRow>();     // Disk Cleanup handlers (Data = Handler)
        private readonly StackPanel _hList;
        private readonly TextBlock _hEmpty, _total, _dismInfo;
        private readonly Button _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun;
        private readonly CheckBox _resetBase;
        private List<Cleaner.Handler> _handlers = new List<Cleaner.Handler>();
        private bool _loaded;
        // One cleanup / DISM / Disk Cleanup at a time for the whole app (a language switch rebuilds this page while a
        // worker still runs; the new page must not start a second one).
        private static volatile bool _busy;

        public CleanupPage() : base(800)
        {
            try { _items = Cleaner.Items(); } catch { _items = new List<CleanItem>(); }
            Title(L.T("System cleanup"));
            Add(Ui.Text(L.T("Removes what Windows and apps leave behind: temp files, update caches, old logs, error reports, dumps, shader and browser caches. Scan first to see the sizes; nothing is deleted until you press Clean. Files in use are skipped, never forced."), 9.5, "Dim"), 4);

            // ---- quick cleanup ----
            Heading(L.T("Quick cleanup"), true);
            var card = Box();
            _scan = Ui.Btn(L.T("Scan"), "BtnAccent");
            _clean = Ui.Btn(L.T("Clean selected"), "BtnDanger");
            _recommended = Ui.Btn(L.T("Select recommended"));
            _total = Ui.Semi("", 10, "Fg");
            _total.VerticalAlignment = VerticalAlignment.Center;
            var bar = Ui.Strip(44, _scan, _clean, _recommended, _total);
            bar.Margin = new Thickness(0, 8, 0, 4);
            _total.Margin = new Thickness(10, 7, 0, 0);
            card.Add(bar);
            foreach (var it in _items)
            {
                var row = new CheckRow(it.Title, it.Desc, it.Note, it.Caution) { Data = it, Value = "—" };
                row.Checked = it.Recommended;
                row.CheckedChanged += UpdateTotal;
                _rows.Add(row);
                card.Add(row);
            }
            if (_rows.Count > 0) _rows[_rows.Count - 1].Divider = false;
            var amber = card.Add(Ui.Text(L.T("Amber = read the warning first (unchecked by default). grobyq runs as administrator, so Windows folders are reachable; anything that is open by a program stays."), 9, "Dim"));
            amber.Margin = new Thickness(0, 8, 0, 8);
            _scan.Click += (s, e) => ScanAll(null);
            _clean.Click += (s, e) => CleanSelected();
            _recommended.Click += (s, e) => { foreach (var r in _rows) { var ci = r.Data as CleanItem; if (ci != null) r.Checked = ci.Recommended; } UpdateTotal(); };

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
            UpdateTotal();
        }

        private IEnumerable<CleanItem> Selected()
        {
            foreach (var r in _rows) if (r.Checked && r.Data is CleanItem ci) yield return ci;
        }

        private void UpdateTotal()
        {
            var sel = Selected().ToList();
            long sum = sel.Where(i => i.Scanned).Sum(i => i.Size);
            bool all = sel.All(i => i.Scanned);
            _total.Text = sel.Count == 0 ? L.T("nothing selected") : (all ? "" : "≥ ") + L.F("selected: {0} · {1}", sel.Count, Cleaner.Human(sum));
        }

        private void SetBusy(bool b) { _busy = b; BusyUi(b); }
        private void BusyUi(bool b)
        {
            foreach (var x in new[] { _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun }) if (x != null) x.IsEnabled = !b;
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
                if (r.Data == it)
                {
                    r.ValueBrush = it.Size > 500L * 1024 * 1024 ? Ui.B("Warn") : it.Size == 0 ? Ui.B("Faint") : Ui.B("Fg");
                    r.Value = it.Special == "recycle" ? L.F("{0} · {1} items", Cleaner.Human(it.Size), it.Files) : L.F("{0} · {1} files", Cleaner.Human(it.Size), it.Files);
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
            SetBusy(true);
            AppLog.Add("[CL] cleanup started: " + string.Join(", ", sel.Select(i => i.Key)));
            var th = new Thread(() =>
            {
                long total = 0;
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
