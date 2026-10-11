using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    // System cleanup: file categories with a size scan, plus the Windows tools (DISM, Disk Cleanup handlers, event logs).
    internal sealed class CleanupPage : Page
    {
        private readonly MainForm M;
        private readonly List<CleanItem> _items;
        private readonly List<CheckRow> _rows = new List<CheckRow>();      // one row per file category (Tag = CleanItem)
        private readonly List<CheckRow> _hRows = new List<CheckRow>();     // Disk Cleanup handlers (Tag = Handler)
        private Card _hCard;
        private Strip _hStrip;
        private TextBlock _hEmpty;
        private Label _total;
        private Btn _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun;
        private Toggle _resetBase;
        private TextBlock _dismInfo;
        private List<Cleaner.Handler> _hList = new List<Cleaner.Handler>();
        // One cleanup / DISM / Disk Cleanup at a time for the whole app: a language switch rebuilds this page while the
        // worker thread still runs, and the new page must not start a second one.
        private static volatile bool _busy;

        public CleanupPage(MainForm m)
        {
            M = m;
            _items = Cleaner.Items();
            ColumnMax = 800;            // one centered column, same shell as the GPU page
            Title(L.T("System cleanup"));
            Add(new TextBlock(L.T("Removes what Windows and apps leave behind: temp files, update caches, old logs, error reports, dumps, shader and browser caches. Scan first to see the sizes; nothing is deleted until you press Clean. Files in use are skipped, never forced."), Theme.Dim, 9.5f));
            Heading(L.T("Quick cleanup"), true);
            BuildFiles();
            Heading(L.T("Windows tools (deep)"), false);
            BuildTools();
            Heading(L.T("What is not cleaned"), false);
            BuildNever();
        }

        // ------------------------------------------------------------------ files
        private void BuildFiles()
        {
            var card = NewCard();
            var bar = new Strip();
            _scan = bar.Add(new Btn(L.T("Scan"), Btn.Style.Accent));
            _clean = bar.Add(new Btn(L.T("Clean selected"), Btn.Style.Danger));
            _recommended = bar.Add(new Btn(L.T("Select recommended")));
            _total = bar.Add(Theme.Lbl("", Theme.Semi(10f), Theme.Fg));
            _total.Margin = new Padding(Theme.D(10), Theme.D(8), 0, 0);
            card.Stack(bar);

            // Vain rows: category + what it removes | found | big checkbox (whole row toggles)
            foreach (var it in _items)
            {
                var row = new CheckRow(it.Title, it.Desc, it.Note) { Caution = it.Caution, Tag = it, Value = "—", Checked = it.Recommended };
                row.CheckedChanged += (s, e) => UpdateTotal();
                _rows.Add(row);
                card.Stack(row);
            }
            if (_rows.Count > 0) _rows[_rows.Count - 1].Divider = false;
            card.Stack(new TextBlock(L.T("Amber = read the warning first (unchecked by default). grobyq runs as administrator, so Windows folders are reachable; anything that is open by a program stays."), Theme.Dim, 9f));

            _scan.Click += (s, e) => ScanAll();
            _clean.Click += (s, e) => CleanSelected();
            _recommended.Click += (s, e) => { foreach (var r in _rows) { var ci = r.Tag as CleanItem; if (ci != null) r.Checked = ci.Recommended; } UpdateTotal(); };
        }

        private IEnumerable<CleanItem> Selected()
        {
            foreach (var r in _rows)
                if (r.Checked && r.Tag is CleanItem) yield return (CleanItem)r.Tag;
        }

        private void UpdateTotal()
        {
            if (_total == null) return;
            var sel = Selected().ToList();
            long sum = sel.Where(i => i.Scanned).Sum(i => i.Size);
            bool all = sel.All(i => i.Scanned);
            _total.Text = sel.Count == 0 ? L.T("nothing selected") : (all ? "" : "≥ ") + L.F("selected: {0} · {1}", sel.Count, Cleaner.Human(sum));
        }

        private void SetBusy(bool b)
        {
            _busy = b;
            BusyUi(b);
        }
        private void BusyUi(bool b)
        {
            foreach (var x in new[] { _scan, _clean, _recommended, _dismAnalyze, _dismRun, _cmRun, _evRun }) if (x != null) x.Enabled = !b;
            Cursor = b ? Cursors.AppStarting : Cursors.Default;
        }

        // a job started by the previous page instance (before a language switch): buttons follow the shared flag
        public override void OnTick(int tick)
        {
            bool b = _busy;
            if (_scan != null && _scan.Enabled == b) BusyUi(b);
            if (!b && !_loaded) OnEnter();                    // the first scan waited for that job
        }

        private void ScanAll(IEnumerable<CleanItem> only = null)
        {
            if (_busy) return;
            var todo = (only ?? _items).ToList();
            SetBusy(true);
            foreach (var r in _rows) if (r.Tag is CleanItem && todo.Contains((CleanItem)r.Tag)) { r.ValueColor = Theme.Dim; r.Value = L.T("scanning…"); }
            var th = new Thread(() =>
            {
                foreach (var it in todo)
                {
                    try { Cleaner.Scan(it); } catch { }
                    var item = it;
                    Ui(() => ShowSize(item));
                }
                Ui(() => { SetBusy(false); UpdateTotal(); });
            }) { IsBackground = true };
            th.Start();
        }

        private void ShowSize(CleanItem it)
        {
            foreach (var r in _rows)
                if (r.Tag == it)
                {
                    r.ValueColor = it.Size > 500L * 1024 * 1024 ? Theme.Warn : it.Size == 0 ? Theme.Faint : Theme.Fg;
                    r.Value = it.Special == "recycle" ? L.F("{0} · {1} items", Cleaner.Human(it.Size), it.Files) : L.F("{0} · {1} files", Cleaner.Human(it.Size), it.Files);
                }
        }

        private void CleanSelected()
        {
            if (_busy) return;
            var sel = Selected().ToList();
            if (sel.Count == 0) { Dlg.Info(this, L.T("Tick at least one category.")); return; }
            string list = string.Join("\n", sel.Select(i => "• " + i.Title + (i.Scanned ? "  (" + Cleaner.Human(i.Size) + ")" : "")));
            bool caution = sel.Any(i => i.Caution);
            if (!Dlg.Ask(this, L.T("Delete now?") + "\n\n" + list + (caution ? "\n\n" + L.T("Some categories have a warning - read it in the list.") : ""), caution)) return;
            SetBusy(true);
            M.Log("[CL] cleanup started: " + string.Join(", ", sel.Select(i => i.Key)));
            var th = new Thread(() =>
            {
                long total = 0;
                foreach (var it in sel)
                {
                    try { total += Cleaner.Clean(it, s => Ui(() => M.Log(s))); }
                    catch (Exception ex) { var msg = ex.Message; Ui(() => M.Log("[!!] " + msg)); }
                }
                long t = total;
                Ui(() =>
                {
                    M.Log("[CL] cleanup finished: " + Cleaner.Human(t) + " freed");
                    SetBusy(false);
                    Dlg.Info(IsDisposed ? (IWin32Window)M : this, L.F("Cleanup finished: {0} freed. Details are in the log.", Cleaner.Human(t)));
                    if (!IsDisposed) ScanAll(sel);
                });
            }) { IsBackground = true };
            th.Start();
        }

        // ------------------------------------------------------------------ Windows tools
        private void BuildTools()
        {
            var dc = NewCard();
            dc.Stack(new TextBlock(L.T("Component store (WinSxS) cleanup. DISM removes superseded components with Microsoft's own servicing stack - the only safe way to shrink WinSxS. Deleting files in WinSxS by hand breaks Windows Update."), Theme.Dim, 9f));
            var row = new Strip();
            _dismAnalyze = row.Add(new Btn(L.T("Analyze")));
            _dismRun = row.Add(new Btn(L.T("Clean component store"), Btn.Style.Accent));
            dc.Stack(row);
            _resetBase = new Toggle(false);
            var rb = dc.Stack(new SettingRow(L.T("Also /ResetBase"), L.T("Frees more space, but every installed update becomes permanent (cannot be uninstalled). Leave off unless disk space is critical."), _resetBase));
            rb.Divider = false;
            _dismInfo = dc.Stack(new TextBlock(L.T("Takes a few minutes; the progress is written to the log."), Theme.Faint, 9f));
            _dismAnalyze.Click += (s, e) => RunDism(true);
            _dismRun.Click += (s, e) => RunDism(false);

            var hc = _hCard = NewCard();
            hc.Stack(new TextBlock(L.T("Windows Disk Cleanup handlers (cleanmgr /sagerun). This is how Windows itself removes Windows.old, superseded updates and upgrade leftovers. Tick what to remove and press Run; a small Disk Cleanup window shows the progress."), Theme.Dim, 9f));
            _hEmpty = hc.Stack(new TextBlock(L.T("reading…"), Theme.Faint, 9f));      // replaced by the handler rows on first open
            var hr = _hStrip = new Strip();
            _cmRun = hr.Add(new Btn(L.T("Run Disk Cleanup"), Btn.Style.Accent));
            hc.Stack(hr);
            _cmRun.Click += (s, e) => RunCleanmgr();

            var ec = NewCard();
            _evRun = new Btn(L.T("Clear event logs"), Btn.Style.Danger);
            _evRun.Click += (s, e) => ClearEvents();
            var er = ec.Stack(new SettingRow(L.T("Windows event logs"), L.T("Empties every Event Viewer log. Frees little space and removes the history you need to diagnose crashes, WHEA errors and driver problems. Only for a clean A/B start."), _evRun));
            er.Divider = false;
        }

        private void FillHandlers()
        {
            _hList = Cleaner.Handlers();
            // rows go between the intro text and the Run strip: rebuild the card stack in that order
            _hCard.SuspendLayout();
            try
            {
                _hCard.Unstack(_hStrip);
                foreach (var r in _hRows) { _hCard.Unstack(r); r.Dispose(); }
                _hRows.Clear();
                _hEmpty.Visible = _hList.Count == 0;
                _hEmpty.Text = L.T("Disk Cleanup handlers not found on this Windows");
                foreach (var h in _hList)
                {
                    var row = new CheckRow(h.Title, h.Desc) { Caution = h.Caution, Tag = h, Checked = h.Default };
                    _hRows.Add(row);
                    _hCard.Stack(row);
                }
                if (_hRows.Count > 0) _hRows[_hRows.Count - 1].Divider = false;
                _hCard.Stack(_hStrip);
            }
            finally { _hCard.ResumeLayout(true); }
        }

        private void RunDism(bool analyze)
        {
            if (_busy) return;
            bool reset = _resetBase.Checked;
            if (!analyze && !Dlg.Ask(this, reset ? L.T("Run DISM component cleanup with /ResetBase? Installed updates become permanent.") : L.T("Run DISM component store cleanup now? It takes a few minutes."), reset)) return;
            SetBusy(true);
            string args = analyze ? "/Online /Cleanup-Image /AnalyzeComponentStore" : "/Online /Cleanup-Image /StartComponentCleanup" + (reset ? " /ResetBase" : "");
            M.Log("[CL] dism " + args);
            _dismInfo.Text = L.T("DISM is running…");
            var th = new Thread(() =>
            {
                var summary = new List<string>();
                string o = Cleaner.RunTool("dism.exe", args, line =>
                {
                    if (line.StartsWith("[") && line.Contains("%")) return;       // progress bar lines
                    if (line.IndexOf(":", StringComparison.Ordinal) > 0 || line.Contains("operation")) { lock (summary) summary.Add(line); }
                    Ui(() => M.Log("[DISM] " + line));
                }, 2 * 60 * 60 * 1000);
                Ui(() =>
                {
                    SetBusy(false);
                    var keep = summary.Where(l => l.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Recommended", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Размер", StringComparison.OrdinalIgnoreCase) >= 0).Take(8).ToList();
                    _dismInfo.Text = keep.Count > 0 ? string.Join("\n", keep) : L.T("DISM finished - see the log.");
                });
            }) { IsBackground = true };
            th.Start();
        }

        private void RunCleanmgr()
        {
            if (_busy) return;
            var keys = new List<string>();
            foreach (var r in _hRows)
            {
                var h = r.Tag as Cleaner.Handler;
                if (h != null && r.Checked) keys.Add(h.Key);
            }
            if (keys.Count == 0) { Dlg.Info(this, L.T("Tick at least one item.")); return; }
            bool caution = _hList.Where(h => keys.Contains(h.Key)).Any(h => h.Caution);
            if (!Dlg.Ask(this, L.T("Run Windows Disk Cleanup for the ticked items?") + "\n\n" + string.Join("\n", _hList.Where(h => keys.Contains(h.Key)).Select(h => "• " + h.Title)), caution)) return;
            SetBusy(true);
            var th = new Thread(() =>
            {
                string err = Cleaner.RunDiskCleanup(keys, s => Ui(() => M.Log(s)));
                Ui(() => { SetBusy(false); M.Log(err.Length == 0 ? "[CL] Disk Cleanup finished" : "[!!] Disk Cleanup: " + err); });
            }) { IsBackground = true };
            th.Start();
        }

        private void ClearEvents()
        {
            if (_busy) return;
            if (!Dlg.Ask(this, L.T("Clear every Windows event log? The crash / WHEA history is lost."), true)) return;
            SetBusy(true);
            var th = new Thread(() =>
            {
                Cleaner.ClearEventLogs(s => Ui(() => M.Log(s)));
                Ui(() => SetBusy(false));
            }) { IsBackground = true };
            th.Start();
        }

        // ------------------------------------------------------------------ never
        private void BuildNever()
        {
            var c = NewCard();
            c.Stack(new TextBlock(L.T("Not offered on purpose:"), Theme.Fg, 10f));
            c.Stack(new TextBlock(L.T("• Prefetch: Windows uses it to start apps faster. Emptying it makes the next launches slower and brings nothing back (T3 folklore)."), Theme.Dim, 9.5f));
            c.Stack(new TextBlock(L.T("• Deleting WinSxS, System32 or DriverStore files by hand: breaks updates and driver installs. Use the DISM and Disk Cleanup buttons instead."), Theme.Dim, 9.5f));
            c.Stack(new TextBlock(L.T("• Registry cleaners: no measurable speed gain, real risk of broken apps."), Theme.Dim, 9.5f));
            c.Stack(new TextBlock(L.T("• SoftwareDistribution\\DataStore: the update history database. Resetting it only makes sense when Windows Update is broken."), Theme.Dim, 9.5f));
            c.Stack(new TextBlock(L.T("• Standby memory 'cleaners': Windows reuses standby memory instantly; flushing it just forces re-reads from disk."), Theme.Dim, 9.5f));
        }

        // Back on the UI thread; if this page was replaced meanwhile (language switch), through the main window so the
        // log lines and the busy flag still land. A failing step on a disposed page is swallowed.
        private void Ui(Action a)
        {
            Action safe = () => { try { a(); } catch { } };
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(safe);
                else if (M != null && M.IsHandleCreated && !M.IsDisposed) M.BeginInvoke(safe);
                else _busy = false;
            }
            catch { }
        }

        private bool _loaded;
        public override void OnEnter()
        {
            if (_busy) BusyUi(true);
            if (_loaded) return;
            if (_busy) return;                                // a job from before a language switch: scan once it ends
            _loaded = true;
            FillHandlers();
            ScanAll();
        }
    }
}
