using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Grobyq
{
    // Affinity: everything about cores, in four tabs so nothing is a long scroll.
    //   Cores        - game core mask, layout templates, CCD split, this CPU + map, all masks, custom rule
    //   Processes    - mini Process Lasso (cores grid + mask entry) and the per-process rule table
    //   Interrupts   - one row per PCI device, click to expand MSI / limit / policy / target CPUs / priority
    //   Reservation  - ReservedCpuSets for the non-game set (families A / C)
    internal sealed partial class AffinityPage : Page
    {
        private readonly MainForm M;
        private readonly SearchBox _search;
        private readonly CountBadge _badge;
        private readonly DeviceTable _table;
        private bool _loaded;

        public AffinityPage(MainForm m)
        {
            M = m;
            ColumnMax = 1100;

            Title(L.T("Affinity"));
            AddTabs();
            Section("cores", L.T("Cores"));
            BuildCoresTab();
            Section("procs", L.T("Processes"));
            BuildProcessesTab();
            Section("irq", L.T("Interrupts"));

            var head = Add(new Panel { BackColor = Theme.Bg, Height = Theme.D(58) }, 2);
            var title = new Label { Text = L.T("Device interrupts"), Font = Theme.Semi(15f), ForeColor = Theme.Fg, BackColor = Theme.Bg, AutoSize = true, UseMnemonic = false };
            _search = new SearchBox();
            _search.Changed += q => { _table.Filter(q); UpdateBadge(); };
            _badge = new CountBadge();
            var rescan = new Btn(L.T("Rescan"), Btn.Style.Outline) { Height = Theme.D(34) };
            rescan.Click += (s, e) => { try { M.Pci = Devices.ListPci(); } catch { } Reload(); };
            head.Controls.AddRange(new Control[] { title, _badge, _search, rescan });
            head.Layout += (s, e) =>
            {
                int h = head.Height, x = head.Width;
                rescan.Location = new Point(x - rescan.Width, (h - rescan.Height) / 2); x = rescan.Left - Theme.D(10);
                _search.SetBounds(x - Theme.D(250), (h - Theme.D(34)) / 2, Theme.D(250), Theme.D(34)); x = _search.Left - Theme.D(10);
                _badge.Location = new Point(x - _badge.Width, (h - _badge.Height) / 2);
                title.Location = new Point(0, (h - title.Height) / 2);
            };

            Add(new TextBlock(L.T("Click a device to change MSI, the message limit, the affinity policy and the priority. The originals are backed up on the first change; changes apply after Restart device or a reboot. Measure DPC/ISR per CPU before and after (WPA, Contour A)."), Theme.Dim, 9f), 10);

            _sets = Add(new CoreSetStrip(), 6);
            _table = Add(new DeviceTable(M), 4);
            _table.Edited += () => { UpdateBadge(); GameMask.Invalidate(); M.NotifyMask(); if (M.Active) M.ApplyNow(); };      // interrupt pins feed the adaptive game mask

            Section("resv", L.T("Reservation"));
            Title(L.T("Core reservation (ReservedCpuSets)"));
            var card = NewCard();
            _resv = card.Stack(new ReservationBox(M));
            _resv.Changed += () => { _sets.Reload(); _table.RefreshData(); };
            M.MaskChanged += () => { SyncMask(); if (Visible) { RefreshCoreAffinity(); _sets.Reload(); _resv.Reload(); } };
        }

        private readonly CoreSetStrip _sets;
        private readonly ReservationBox _resv;

        private void Reload()
        {
            _table.Load(M.Pci);
            _table.Filter(_search.Query);
            UpdateBadge();
        }

        private void UpdateBadge()
        {
            int total = _table.VisibleCount, msi = _table.VisibleMsiCount;
            _badge.Set(msi > 0 ? L.F("MSI capable: {0} of {1}", msi, total) : "");
            if (_badge.Parent != null) _badge.Parent.PerformLayout();
        }

#if DEMO
        public override void DemoExpand(int n) { _table.DemoToggle(n); }
#endif
        public override void OnEnter()
        {
            if (!_loaded) { _loaded = true; Reload(); }
            else _table.RefreshData();
            GameMask.Invalidate();                // interrupt pins re-read: another tool may have moved one
            RefreshCoreAffinity();
            _sets.Reload();
            _resv.Reload();
        }
    }

    // ------------------------------------------------------------------ core sets strip
    // "CORE SETS   ● GAME LP 0-15   ● NON-GAME LP 16-31   ● ReservedCpuSets: not set" above the column header.
    internal sealed class CoreSetStrip : Control
    {
        private sealed class Chip { public Color Dot; public string Text; public Color Fg; }
        private readonly List<Chip> _chips = new List<Chip>();

        public CoreSetStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Height = Theme.D(34); Font = Theme.F(9f);
        }

        public void Reload()
        {
            _chips.Clear();
            var t = ThreadsPage.SafeTopo();
            if (t != null)
            {
                var v = CoreSets.Effective(t, GameMask.Rule(t));
                if (!v.Split) v.NonGame = GameMask.Background(t);      // what background pinning really uses (base + edits)
                if (v.Split)
                {
                    _chips.Add(new Chip { Dot = Theme.Acc, Text = L.T("GAME") + "  LP " + Masks.Ranges(v.Game), Fg = Theme.Fg });
                    _chips.Add(new Chip { Dot = CpuMap.NonGameC, Text = L.T("NON-GAME") + "  LP " + Masks.Ranges(v.NonGame), Fg = Theme.Fg });
                }
                else
                {
                    _chips.Add(new Chip { Dot = Theme.Acc, Text = L.T("GAME MASK") + "  LP " + Masks.Ranges(v.Game), Fg = Theme.Fg });
                    if (v.NonGame != 0) _chips.Add(new Chip { Dot = Theme.Warn, Text = L.T("BACKGROUND") + "  LP " + Masks.Ranges(v.NonGame), Fg = Theme.Fg });
                }
                var st = Reservation.Read();
                if (st.Kind == Reservation.Kind.NotSet) _chips.Add(new Chip { Dot = Theme.Faint, Text = L.T("ReservedCpuSets: not set"), Fg = Theme.Dim });
                else if (st.Kind != Reservation.Kind.Binary) _chips.Add(new Chip { Dot = Theme.Bad, Text = L.T("ReservedCpuSets: unreadable"), Fg = Theme.Warn });
                else
                {
                    string when = st.Pending ? L.T("pending reboot") : st.TimeKnown ? L.T("active") : L.T("write time unknown");
                    if (v.Split && st.Mask == v.NonGame) _chips.Add(new Chip { Dot = CpuMap.NonGameC, Text = L.F("ReservedCpuSets: = NON-GAME ({0})", when), Fg = st.Pending ? Theme.Warn : Theme.Fg });
                    else if ((st.Mask & v.Game) != 0) _chips.Add(new Chip { Dot = Theme.Bad, Text = L.F("ReservedCpuSets: {0} overlaps the game set ({1})", Masks.Hex(st.Mask), when), Fg = Theme.Warn });
                    else _chips.Add(new Chip { Dot = Theme.Warn, Text = L.F("ReservedCpuSets: other, LP {0} ({1})", Masks.Ranges(st.Mask), when), Fg = Theme.Fg });
                }
            }
            int need = Measure(Width);
            if (need != Height) Height = need;
            Invalidate();
        }

        private string Caption { get { return L.T("Core sets").ToUpper(System.Globalization.CultureInfo.CurrentCulture); } }

        // chips wrap to a second line in a narrow window
        private int Measure(int width)
        {
            if (width < 50) return Theme.D(34);
            int x = TextRenderer.MeasureText(Caption, Theme.Semi(8.25f)).Width + Theme.D(14), lines = 1;
            foreach (var c in _chips)
            {
                int w = ChipW(c);
                if (x + w > width && x > 0) { lines++; x = 0; }
                x += w + Theme.D(8);
            }
            return lines * Theme.D(34);
        }

        private int ChipW(Chip c) { return TextRenderer.MeasureText(c.Text, Font).Width + Theme.D(32); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); int n = Measure(Width); if (n != Height) Height = n; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            var f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var cf = Theme.Semi(8.25f);
            int capW = TextRenderer.MeasureText(Caption, cf).Width;
            TextRenderer.DrawText(g, Caption, cf, new Rectangle(0, 0, capW + Theme.D(4), Theme.D(34)), Theme.Faint, f);
            int x = capW + Theme.D(14), y = 0;
            foreach (var c in _chips)
            {
                int w = ChipW(c);
                if (x + w > Width && x > 0) { x = 0; y += Theme.D(34); }
                var r = new RectangleF(x + 0.5f, y + Theme.D(4) + 0.5f, w - 1, Theme.D(26) - 1);
                using (var path = Theme.Round(r, r.Height / 2f)) using (var b = new SolidBrush(Theme.Card)) using (var p = new Pen(Theme.Line)) { g.FillPath(b, path); g.DrawPath(p, path); }
                using (var b = new SolidBrush(c.Dot)) g.FillEllipse(b, x + Theme.D(11), y + Theme.D(17) - Theme.Df(3.5f), Theme.Df(7), Theme.Df(7));
                TextRenderer.DrawText(g, c.Text, Font, new Rectangle(x + Theme.D(22), y + Theme.D(4), w - Theme.D(26), Theme.D(26)), c.Fg, f);
                x += w + Theme.D(8);
            }
        }
    }

    // ------------------------------------------------------------------ core reservation card
    // Port of APPLY-RESERVE / REVERT / VERIFY (grobPC_ReservedCpuSets.ps1). Only the NON-GAME set of a verified split.
    internal sealed class ReservationBox : StackBox
    {
        private readonly MainForm M;
        private readonly InfoGrid _info;
        private readonly TextBlock _banner, _refuse, _status;
        private readonly Btn _apply, _revert, _verify;
        public event Action Changed;

        public ReservationBox(MainForm m)
        {
            M = m;
            Stack(new TextBlock(L.T("Reserves the NON-GAME set for pinned work only (T1 model, MS Learn: Soft Real-Time on Windows IoT Enterprise): reserved LPs run only what is pinned to them; Windows, unpinned programs and hardware interrupts go to the unreserved set - here the game set. The optimizer alone is the default A/B; this is an extra experiment. Judge it by frame-time and WPA (CPU usage, DPC/ISR per CPU)."), Theme.Dim, 9f));
            _info = Stack(new InfoGrid { KeyWidth = 150 });
            _banner = Stack(new TextBlock("", Theme.Warn, 9.5f));
            _refuse = Stack(new TextBlock("", Theme.Warn, 9f));
            var strip = new Strip();
            _apply = strip.Add(new Btn(L.T("Reserve the non-game set"), Btn.Style.Accent));
            _revert = strip.Add(new Btn(L.T("Revert"), Btn.Style.Normal));
            _verify = strip.Add(new Btn(L.T("Verify"), Btn.Style.Outline));
            Stack(strip);
            _status = Stack(new TextBlock("", Theme.Dim, 9f));
            Stack(new TextBlock(L.T("Read at boot: restart Windows to activate or undo it (a shutdown with Fast Startup does not reload it, T2). Never the game set, never LP 0, never half an SMT pair. If CS2 hitches more: Revert and restart."), Theme.Faint, 9f));
            _apply.Click += (s, e) => DoApply();
            _revert.Click += (s, e) => DoRevert();
            _verify.Click += (s, e) => DoVerify();
            Reload();
        }

        private static string Utc(DateTime d) { return d.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC"; }

        public void Reload()
        {
            var t = ThreadsPage.SafeTopo();
            var st = Reservation.Read();
            var p = t != null ? t.Split : null;
            var rows = new List<KeyValuePair<string, string>>();
            string val;
            if (st.Kind == Reservation.Kind.NotSet) val = L.T("not set");
            else if (st.Kind == Reservation.Kind.Binary) val = Reservation.HexText(st.Bytes) + "   =   " + Masks.Hex(st.Mask) + "   ·   LP " + Masks.Ranges(st.Mask);
            else if (st.Kind == Reservation.Kind.WrongType) val = L.F("{0}, not REG_BINARY", st.Error);
            else val = L.T("unreadable") + ": " + st.Error;
            rows.Add(new KeyValuePair<string, string>(L.T("Registry value"), val));
            string state = st.Kind != Reservation.Kind.Binary ? "—" : st.Pending ? L.T("pending reboot - written after the last boot") : st.TimeKnown ? L.T("active since boot") : L.T("set - write time unknown (restart once if it was just changed)");
            rows.Add(new KeyValuePair<string, string>(L.T("State"), state));
            bool can = p != null && p.CanSplit;
            rows.Add(new KeyValuePair<string, string>(L.T("Would reserve"), can ? L.T("NON-GAME") + "  " + Masks.Hex(p.NonGame) + "   ·   LP " + Masks.Ranges(p.NonGame) + "   ->   " + Reservation.HexText(Reservation.ToBytes(p.NonGame)) : "—"));
            string bk = st.HasBackup ? L.F("original: {0}", st.Original == "none" ? L.T("none (value did not exist)") : st.Original) + (st.TimeKnown ? "   ·   " + Utc(st.Applied) : "") : L.T("none yet - made before the first write");
            rows.Add(new KeyValuePair<string, string>(L.T("Backup"), bk));
            _info.Set(rows);
            if (st.Pending) _info.Tint(1, Theme.Warn);
            _banner.Text = st.Pending ? L.T("Restart Windows to activate the new ReservedCpuSets value. Until then the previous value stays in effect.") : "";
            _banner.Visible = st.Pending;
            var bad = t == null ? new List<string> { "family" } : Reservation.Refusals(t, can ? p.NonGame : 0);
            _refuse.Text = string.Join("\n", bad.Distinct().Select(Explain.Refusal));
            _refuse.Visible = bad.Count > 0;
            bool already = can && st.Kind == Reservation.Kind.Binary && st.Mask == p.NonGame;
            _apply.Enabled = bad.Count == 0 && !already;
            _revert.Enabled = st.Kind != Reservation.Kind.NotSet || st.HasBackup;
            PerformLayout();
        }

        private void Done(string line, bool warn)
        {
            _status.ForeColor = warn ? Theme.Warn : Theme.Dim;
            _status.Text = line;
            Reload();
            var h = Changed; if (h != null) h();
        }

        private void DoApply()
        {
            var t = ThreadsPage.SafeTopo(); if (t == null) return;
            var p = t.Split;
            var bad = Reservation.Refusals(t, p.NonGame);
            if (bad.Count > 0) { Done(string.Join("\n", bad.Distinct().Select(Explain.Refusal)), true); return; }
            string bytes = Reservation.HexText(Reservation.ToBytes(p.NonGame));
            string text = L.F("Write ReservedCpuSets = {0} (LP {1})?", Masks.Hex(p.NonGame), Masks.Ranges(p.NonGame)) + "\n\n" +
                          L.F("Bytes {0} (REG_BINARY, little-endian, bit N = LP N).", bytes) + "\n\n" +
                          L.F("After a restart the reserved LPs run only what is pinned to them (T1). Windows, services, unpinned programs and hardware interrupts move to the unreserved set = the game set {0}. That adds load next to CS2 unless background apps are pinned to the reserved set (Threads > background list).", "LP " + Masks.Ranges(p.Game)) + "\n\n" +
                          L.T("The original value is saved to backup-reservedcpusets.json; Revert restores it.");
            if (Dlg.Show(this, text, true, L.T("Write"), L.T("Cancel")) != 0) return;
            var r = Reservation.ApplyNonGame(t);
            if (!r.Ok) { M.Log("[RS] ReservedCpuSets not written: " + r.Error); Done("! " + r.Error, true); return; }
            M.Log(string.Format("[RS] ReservedCpuSets = {0} LP {1}  bytes {2}  read back ok  ({3}; backup-reservedcpusets.json). Restart to activate.", Masks.Hex(p.NonGame), Masks.Ranges(p.NonGame), Reservation.HexText(r.Written), r.Info));
            Done(r.Info == "already set" ? L.T("This exact value is already set.") : L.T("Written and read back. Restart Windows to activate it."), false);
        }

        private void DoRevert()
        {
            var st = Reservation.Read();
            string target = st.HasBackup ? st.Original : "none";
            string q = target == "none" ? L.T("Delete ReservedCpuSets? (It did not exist before the first grobyq write, or there is no grobyq backup.)") : L.F("Restore ReservedCpuSets = {0} (the value before the first grobyq write)?", target);
            if (!Dlg.Ask(this, q)) return;
            var r = Reservation.Revert();
            M.Log("[RS] revert: " + (r.Ok ? r.Info : r.Error));
            Done(r.Ok ? L.T("Reverted and read back. Restart Windows to apply.") : "! " + r.Error, !r.Ok);
        }

        private void DoVerify()
        {
            var t = ThreadsPage.SafeTopo(); if (t == null) return;
            var p = t.Split;
            var lines = new List<string>();
            bool pass = true;
            Action<bool, string> chk = (ok, txt) => { lines.Add((ok ? "OK   " : "!!   ") + txt); if (!ok) pass = false; };
            lines.Add(L.F("Family: {0}", Explain.SplitFamily(p)));
            if (!p.CanSplit)
            {
                lines.Add("--   " + Explain.SplitWhy(p));
                Done(string.Join("\n", lines), p.Family == "ABORT");
                return;
            }
            if (p.Family == "A") chk(p.Reported == p.Expected, L.F("LP count {0}, {1} expects {2}.", p.Reported, p.Sku, p.Expected));
            else chk(p.Reported == p.Probe && Count(p.Game) + Count(p.NonGame) == p.Reported, L.F("LP count {0} = {1} P-core LP + {2} E-core LP.", p.Reported, Count(p.Game), Count(p.NonGame)));
            chk(p.Problems.Count == 0, L.T("Game and non-game sets: disjoint, cover every LP, SMT pairs whole."));
            var st = Reservation.Read();
            if (st.Kind == Reservation.Kind.NotSet) lines.Add("--   " + L.T("ReservedCpuSets: not set (optimizer-only setup)."));
            else if (st.Kind != Reservation.Kind.Binary) chk(false, L.T("ReservedCpuSets: unreadable or not REG_BINARY."));
            else if (st.Mask == p.NonGame) chk(true, L.F("ReservedCpuSets = NON-GAME ({0}).", st.Pending ? L.T("pending reboot") : L.T("active")));
            else if ((st.Mask & p.Game) != 0) chk(false, L.F("ReservedCpuSets {0} overlaps the game set - Revert.", Masks.Hex(st.Mask)));
            else lines.Add("--   " + L.F("ReservedCpuSets {0} is not the non-game set.", Masks.Hex(st.Mask)));
            if (st.Pending) lines.Add("--   " + L.T("Written after the last boot: restart, then Verify again."));
            int seen = 0;
            foreach (var exe in Settings.WatchList())
            {
                string bn = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe.Substring(0, exe.Length - 4) : exe;
                System.Diagnostics.Process[] ps;
                try { ps = System.Diagnostics.Process.GetProcessesByName(bn); } catch { continue; }
                foreach (var pr in ps)
                {
                    seen++;
                    int lvl, err;
                    IntPtr hp = Native.OpenProcessGraceful((uint)pr.Id, out lvl, out err);
                    if (hp == IntPtr.Zero) { lines.Add("--   " + L.F("{0} PID {1}: affinity not readable.", exe, pr.Id)); continue; }
                    try
                    {
                        ulong a = Native.GetProcAffinity(hp);
                        chk((a & ~p.Game) == 0, L.F("{0} PID {1}: affinity {2} (LP {3}) inside the game set.", exe, pr.Id, Masks.Hex(a), Masks.Ranges(a)));
                        chk(CoreSplit.SmtWhole(t, a), L.F("{0} PID {1}: no SMT pair split.", exe, pr.Id));
                    }
                    finally { Native.CloseHandle(hp); }
                }
            }
            if (seen == 0) lines.Add("--   " + L.T("No target is running: start the game with the optimizer on the game set, then Verify again."));
            lines.Add("--   " + L.T("This checks masks, not performance. Task Manager > Details > cs2.exe > Set affinity shows the same LPs."));
            M.Log("[RS] verify: " + (pass ? "pass" : "FAIL"));
            Done(string.Join("\n", lines), !pass);
        }

        private static int Count(ulong m) { int n = 0; while (m != 0) { n += (int)(m & 1); m >>= 1; } return n; }
    }

    // ------------------------------------------------------------------ header parts
    // Rounded search field: magnifier + borderless text box, cue text "Search".
    internal sealed class SearchBox : Panel
    {
        private readonly TextBox _box;
        public event Action<string> Changed;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            _box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Fg, Font = Theme.F(9.75f) };
            _box.HandleCreated += (s, e) => { try { SendMessage(_box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, L.T("Search")); } catch { } };
            _box.TextChanged += (s, e) => { var h = Changed; if (h != null) h(Query); };
            _box.GotFocus += (s, e) => Invalidate();
            _box.LostFocus += (s, e) => Invalidate();
            Controls.Add(_box);
            Cursor = Cursors.IBeam;
            Click += (s, e) => _box.Focus();
        }
        public string Query { get { return (_box.Text ?? "").Trim(); } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_box == null) return;
            int x = Theme.D(34);
            _box.SetBounds(x, (Height - _box.PreferredHeight) / 2, Width - x - Theme.D(12), _box.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(8)))
            using (var b = new SolidBrush(Theme.Card)) using (var p = new Pen(_box.Focused ? Theme.EdgeDn : Theme.Edge)) { g.FillPath(b, path); g.DrawPath(p, path); }
            float ic = Theme.Df(14), cy = Height / 2f;
            using (var p = new Pen(Theme.Dim, Theme.Df(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(p, Theme.D(12), cy - ic / 2f, ic * 0.72f, ic * 0.72f);
                g.DrawLine(p, Theme.D(12) + ic * 0.64f, cy - ic / 2f + ic * 0.64f, Theme.D(12) + ic * 0.95f, cy - ic / 2f + ic * 0.95f);
            }
        }
    }

    // Quiet pill with a count ("MSI capable: 12 of 34"); hidden when empty.
    internal sealed class CountBadge : Control
    {
        public CountBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Font = Theme.F(9f); Height = Theme.D(30);
        }
        public void Set(string text)
        {
            Text = text ?? ""; Visible = Text.Length > 0;
            Width = TextRenderer.MeasureText(Text, Font).Width + Theme.D(28);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, r.Height / 2f)) using (var b = new SolidBrush(Theme.Card)) using (var p = new Pen(Theme.Line)) { g.FillPath(b, path); g.DrawPath(p, path); }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // ------------------------------------------------------------------ device table
    internal sealed class DeviceTable : Panel
    {
        private readonly MainForm M;
        private readonly DevHeader _head = new DevHeader();
        private readonly List<DevRow> _rows = new List<DevRow>();
        private readonly Card _editorCard;
        private readonly IrqEditor _editor;
        private DevRow _open;
        public event Action Edited;

        public DeviceTable(MainForm m)
        {
            M = m;
            BackColor = Theme.Bg; Height = Theme.D(60);
            Controls.Add(_head);
            _editorCard = new Card { Radius = 12, Padding = new Padding(Theme.D(20), Theme.D(8), Theme.D(20), Theme.D(12)), Visible = false };
            _editor = _editorCard.Stack(new IrqEditor(M));
            _editor.Others = () => _rows.Where(r => r != _open && r.Cfg.Assign.HasValue).Select(r => new KeyValuePair<string, ulong>(r.Dev.Class, r.Cfg.Assign.Value)).ToList();
            _editor.OtherNames = () => _rows.Where(r => r != _open && r.Cfg.Policy == 4 && r.Cfg.Assign.HasValue).Select(r => new KeyValuePair<string, ulong>(r.Dev.Name, r.Cfg.Assign.Value)).ToList();
            _editor.NetCount = () => _rows.Count(r => r.Dev.IsNet);
            _editor.Changed += () => { if (_open != null) { _open.Read(); _open.Invalidate(); } var h = Edited; if (h != null) h(); };
            Controls.Add(_editorCard);
        }

        public int VisibleCount { get { return _rows.Count(r => r.Visible); } }
        public int VisibleMsiCount { get { return _rows.Count(r => r.Visible && (r.Dev.MsiCapable || r.InUse.StartsWith("MSI"))); } }

        // GPU, USB, network, storage, audio, then everything else by name. No device is hidden.
        private static int Rank(PciDevice d)
        {
            switch ((d.Class ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return 0;
                case "USB": return 1;
                case "NET": return 2;
                case "SCSIADAPTER": case "HDC": return 3;
                case "MEDIA": return 4;
                default: return 5;
            }
        }

        public void Load(IEnumerable<PciDevice> list)
        {
            SuspendLayout();
            try
            {
                string keep = _open != null ? _open.Dev.InstanceId : null;
                foreach (var r in _rows) { Controls.Remove(r); r.Dispose(); }
                _rows.Clear(); _open = null;
                foreach (var d in (list ?? new List<PciDevice>()).OrderBy(Rank).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var row = new DevRow(d);
                    row.Click += (s, e) => Toggle(row);
                    _rows.Add(row);
                    Controls.Add(row);
                }
                if (keep != null) { var again = _rows.FirstOrDefault(r => r.Dev.InstanceId == keep); if (again != null) Toggle(again); }
                if (_open == null) _editorCard.Visible = false;
            }
            finally { ResumeLayout(true); }
        }

        public void RefreshData() { foreach (var r in _rows) { r.Read(); r.Invalidate(); } }
#if DEMO
        public void DemoToggle(int n) { if (n >= 0 && n < _rows.Count) Toggle(_rows[n]); }
#endif

        public void Filter(string q)
        {
            SuspendLayout();
            foreach (var r in _rows)
            {
                var d = r.Dev;
                r.Visible = string.IsNullOrEmpty(q) || new[] { d.Name, d.Class, d.Service, d.InstanceId, d.Mfg }.Any(t => (t ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (_open != null && !_open.Visible) Toggle(_open);
            ResumeLayout(true);
        }

        private void Toggle(DevRow row)
        {
            if (_open == row) { row.Expanded = false; _open = null; _editorCard.Visible = false; }
            else
            {
                if (_open != null) _open.Expanded = false;
                _open = row; row.Expanded = true;
                _editor.Bind(row.Dev);
                _editorCard.Visible = true;
            }
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_head == null) return;
            int w = Width, y = 0, gap = Theme.D(4);
            _head.SetBounds(0, 0, w, _head.Height); y = _head.Height + Theme.D(2);
            foreach (var r in _rows)
            {
                if (!r.Visible) continue;
                r.SetBounds(0, y, w, r.Height); y += r.Height + gap;
                if (r == _open && _editorCard.Visible)
                {
                    _editorCard.SetBounds(Theme.D(12), y, w - Theme.D(24), _editorCard.Height);   // Card measures its own height here
                    y += _editorCard.Height + Theme.D(10);
                }
            }
            if (_rows.Count(r => r.Visible) == 0) y += Theme.D(40);
            int h = Math.Max(Theme.D(60), y);
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_rows.Count > 0 && _rows.All(r => !r.Visible))
                TextRenderer.DrawText(e.Graphics, L.T("No device matches the search."), Theme.F(9.5f), new Rectangle(0, _head.Height + Theme.D(8), Width, Theme.D(30)), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Column geometry shared by the header and the rows: fixed columns on the right, the device name takes the rest.
    internal static class DevCols
    {
        public static int Chev { get { return Theme.D(34); } }
        public static int Prio { get { return Theme.D(84); } }
        public static int Modes { get { return Theme.D(128); } }
        public static int Policy { get { return Theme.D(170); } }
        public static int Limit { get { return Theme.D(64); } }
        public static int NameX { get { return Theme.D(56); } }
        public static int Gap { get { return Theme.D(12); } }
        // x of each right-hand column for a row of width w
        public static void X(int w, out int limit, out int policy, out int modes, out int prio, out int chev)
        {
            chev = w - Chev;
            prio = chev - Gap - Prio;
            modes = prio - Gap - Modes;
            policy = modes - Gap - Policy;
            limit = policy - Gap - Limit;
        }
    }

    internal sealed class DevHeader : Control
    {
        public DevHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Height = Theme.D(30); Font = Theme.Semi(8.25f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int lim, pol, mod, pri, chv; DevCols.X(Width, out lim, out pol, out mod, out pri, out chv);
            var f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            Action<string, int, int> cap = (t, x, w) => TextRenderer.DrawText(g, t.ToUpper(System.Globalization.CultureInfo.CurrentCulture), Font, new Rectangle(x, 0, w, Height), Theme.Faint, f);
            cap(L.T("Device"), DevCols.NameX, lim - DevCols.NameX - DevCols.Gap);
            cap(L.T("Limit"), lim, DevCols.Limit);
            cap(L.T("Policy"), pol, DevCols.Policy);
            cap(L.T("Modes"), mod, DevCols.Modes);
            cap(L.T("Priority"), pri, DevCols.Prio);
        }
    }

    // One device: icon, name + class / service, message limit current | max, target CPUs (or the policy), the
    // interrupt modes the device supports (the one in use filled), the priority, a chevron.
    internal sealed class DevRow : Control
    {
        public readonly PciDevice Dev;
        public IrqConfig Cfg = new IrqConfig();
        public string InUse = "";
        private bool _hot, _exp, _backup;
        private readonly ToolTip _tip;

        public DevRow(PciDevice d)
        {
            Dev = d;
            _tip = Tips.Owned(this);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg; Height = Theme.D(56); Cursor = Cursors.Hand; TabStop = true;
            Read();
        }

        public bool Expanded { get { return _exp; } set { _exp = value; Invalidate(); } }

        public void Read()
        {
            try { Cfg = Devices.ReadIrq(Dev.InstanceId); } catch { Cfg = new IrqConfig(); }
            try { InUse = Devices.IrqInUse(Dev.DevInst) ?? ""; } catch { InUse = ""; }
            try { _backup = Devices.HasBackup(Dev.InstanceId); } catch { _backup = false; }
            ulong res = Reservation.Reserved();
            InReserved = Cfg.Assign.HasValue && (Cfg.Assign.Value & res) != 0;
            string tip = Dev.Name + "\n" + Dev.InstanceId + "\n" + L.T("In use now:") + " " + InUse + "   ·   " + L.T("Policy") + ": " + Cfg.PolicyText + (Cfg.Assign.HasValue ? " (" + Masks.MaskLps(Cfg.Assign.Value).Replace(",", ", ") + ")" : "");
            if (InReserved) tip += "\n\n" + L.T("Target LPs inside the reserved set: interrupt routing to reserved LPs is unverified (T3). Prefer the unreserved set or revert the reservation.");
            if (string.Equals(Dev.Class, "USB", StringComparison.OrdinalIgnoreCase))
                tip += "\n\n" + L.T("The mouse interrupt belongs to its USB controller: everything on that controller moves with it (Device Manager > View > Devices by connection).");
            _tip.SetToolTip(this, tip);
        }

        public bool InReserved;

        private string Icon
        {
            get
            {
                switch ((Dev.Class ?? "").ToUpperInvariant())
                {
                    case "DISPLAY": return "gpu";
                    case "USB": return "usb";
                    case "NET": return "nic";
                    case "MEDIA": return "audio";
                    case "SCSIADAPTER": case "HDC": return "storage";
                    default: return "chip";
                }
            }
        }

        private string LimitText
        {
            get
            {
                bool msi = Dev.MsiCapable || InUse.StartsWith("MSI");
                if (!msi) return "–";
                string max = Dev.MsgMax > 0 ? Dev.MsgMax.ToString() : "–";
                string cur = Cfg.MsgLimit.HasValue ? Cfg.MsgLimit.Value.ToString() : max;
                return cur + " | " + max;
            }
        }

        // Modes the device supports (bit1 line, bit2 MSI, bit4 MSI-X); the one in use is filled.
        private List<KeyValuePair<string, bool>> Modes()
        {
            var l = new List<KeyValuePair<string, bool>>();
            bool msiNow = InUse.StartsWith("MSI"), lineNow = InUse.Length > 0 && !msiNow && InUse != "n/a" && InUse != L.T("none") && InUse != L.T("not allocated");
            int n = 0; if (msiNow) int.TryParse(InUse.Replace("MSI x", ""), out n);
            int s = Dev.IrqSupport;
            if (s < 0) s = (msiNow ? 2 : 0) | (lineNow ? 1 : 0);
            bool x = (s & 4) != 0, m = (s & 2) != 0;
            if (m) l.Add(new KeyValuePair<string, bool>("MSI", msiNow && !(x && n > 1)));
            if (x) l.Add(new KeyValuePair<string, bool>("MSI-X", msiNow && (n > 1 || !m)));
            if ((s & 1) != 0) l.Add(new KeyValuePair<string, bool>("Line", lineNow));
            return l;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(10)))
            using (var b = new SolidBrush(_exp || _hot ? Theme.CardHi : Theme.Card))
            using (var p = new Pen(_exp || Focused ? Theme.Edge : Theme.Line)) { g.FillPath(b, path); g.DrawPath(p, path); }

            float ic = Theme.Df(18);
            Icons.Draw(g, Icon, new RectangleF(Theme.D(20), (Height - ic) / 2f, ic, ic), Theme.Dim, Theme.Df(1.4f));

            int lim, pol, mod, pri, chv; DevCols.X(Width, out lim, out pol, out mod, out pri, out chv);
            var one = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            int nameW = lim - DevCols.NameX - DevCols.Gap;
            TextRenderer.DrawText(g, Dev.Name, Theme.Semi(10f), new Rectangle(DevCols.NameX, Height / 2 - Theme.D(20), nameW, Theme.D(20)), Theme.Fg, one);
            string sub = Dev.Class + (string.IsNullOrEmpty(Dev.Service) ? "" : "  ·  " + Dev.Service);
            TextRenderer.DrawText(g, sub, Theme.F(8.5f), new Rectangle(DevCols.NameX, Height / 2 + Theme.D(1), nameW, Theme.D(18)), Theme.Faint, one);
            if (_backup)       // grobyq changed this device (original kept in backup-irq.json)
                using (var b = new SolidBrush(Theme.Warn)) g.FillEllipse(b, Theme.D(40), Height / 2f - Theme.Df(12), Theme.Df(6), Theme.Df(6));

            TextRenderer.DrawText(g, LimitText, Theme.F(9.25f), new Rectangle(lim, 0, DevCols.Limit, Height), Theme.Fg, one);

            // target CPUs when the policy names them; another policy by name; unset = dash
            bool spec = Cfg.Assign.HasValue, set = Cfg.Policy.HasValue && Cfg.Policy.Value != 0;
            string polText = spec ? Masks.MaskLps(Cfg.Assign.Value).Replace(",", ", ") : set ? Cfg.PolicyText : "—";
            if (InReserved)
            {
                // amber triangle: the target sits in the ReservedCpuSets set (unverified routing, T3)
                float tx = pol, ty = Height / 2f, ts = Theme.Df(9);
                using (var b = new SolidBrush(Theme.Warn)) g.FillPolygon(b, new[] { new PointF(tx, ty + ts / 2f), new PointF(tx + ts / 2f, ty - ts / 2f), new PointF(tx + ts, ty + ts / 2f) });
                TextRenderer.DrawText(g, polText, Theme.F(9.25f), new Rectangle(pol + Theme.D(14), 0, DevCols.Policy - Theme.D(14), Height), Theme.Warn, one);
            }
            else TextRenderer.DrawText(g, polText, Theme.F(9.25f), new Rectangle(pol, 0, DevCols.Policy, Height), spec ? Theme.Fg : Theme.Dim, one);

            int x = mod;
            foreach (var m in Modes())
            {
                var f = Theme.Semi(8f);
                int w = TextRenderer.MeasureText(m.Key, f).Width + Theme.D(12), h = Theme.D(20);
                if (x + w > mod + DevCols.Modes) break;
                var pr = new RectangleF(x + 0.5f, (Height - h) / 2f + 0.5f, w - 1, h - 1);
                using (var path = Theme.Round(pr, Theme.Df(5)))
                {
                    if (m.Value) using (var b = new SolidBrush(Theme.EdgeDn)) g.FillPath(b, path);
                    using (var p = new Pen(m.Value ? Theme.EdgeDn : Theme.Edge)) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, m.Key, f, Rectangle.Round(pr), m.Value ? Color.White : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                x += w + Theme.D(5);
            }

            bool undef = !Cfg.Priority.HasValue || Cfg.Priority.Value == 0;
            TextRenderer.DrawText(g, undef ? "—" : Cfg.PriorityText, Theme.F(9.25f), new Rectangle(pri, 0, DevCols.Prio, Height), undef ? Theme.Dim : Theme.Fg, one);

            float cx = chv + DevCols.Chev / 2f - Theme.D(4), cy = Height / 2f;
            using (var p = new Pen(_hot || _exp ? Theme.Fg : Theme.Dim, Theme.Df(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float u = Theme.Df(4), v = Theme.Df(2.2f);
                if (_exp) g.DrawLines(p, new[] { new PointF(cx - u, cy + v), new PointF(cx, cy - v), new PointF(cx + u, cy + v) });
                else g.DrawLines(p, new[] { new PointF(cx - u, cy - v), new PointF(cx, cy + v), new PointF(cx + u, cy - v) });
            }
        }
    }
}
