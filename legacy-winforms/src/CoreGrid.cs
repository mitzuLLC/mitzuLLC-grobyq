using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Grobyq
{
    // Checkbox grid of logical processors, one column per physical core: "Core N" on top, then the first LP of the
    // core and its SMT sibling below (same layout as the reference "Cores" picker). Every label is shown in full: the
    // column width is measured from the widest "CPU nn" in the current font. Cores are grouped by L3 domain (CCD / CCX,
    // the V-Cache CCD marked) or, on a hybrid CPU, by P-cores / E-cores; each group wraps to new bands when the card
    // is narrow. Mask get/set works on group 0 (the same scope as every other mask in grobyq).
    internal sealed class CoreGrid : Panel
    {
        private sealed class Group { public string Head = ""; public bool Accent; public Label Label; public List<List<int>> Cores = new List<List<int>>(); public List<Label> CoreHeads = new List<Label>(); }
        private readonly List<Group> _groups = new List<Group>();
        private readonly Dictionary<int, BoxCheck> _byLp = new Dictionary<int, BoxCheck>();
        private bool _setting;
        public event Action Changed;

        private int _colW;
        private int ColW { get { return _colW > 0 ? _colW : Theme.D(104); } }
        private static int CoreHeadH { get { return Theme.D(18); } }
        private static int RowH { get { return Theme.D(30); } }
        private static int HeadH { get { return Theme.D(22); } }
        private static int GroupGap { get { return Theme.D(8); } }

        public CoreGrid()
        {
            BackColor = Theme.Card;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Height = RowH * 2 + Theme.D(4);
        }

        public void Build(Topo t)
        {
            SuspendLayout();
            try
            {
                foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
                Controls.Clear(); _groups.Clear(); _byLp.Clear();
                if (t == null) return;
                var g0 = t.Sets.Where(s => s.Group == 0).ToList();
                // box + gap + the widest label + the game-mask dot + padding: nothing is ever cut to "CP..."
                int maxLp = g0.Count == 0 ? 0 : g0.Max(s => (int)s.LP);
                int tw = TextRenderer.MeasureText("CPU " + Math.Max(10, maxLp), Theme.F(9.5f)).Width;
                _colW = Math.Max(Theme.D(96), Theme.D(4) + BoxCheck.BoxSize + Theme.D(8) + tw + Theme.D(14) + Theme.D(10));
                // physical core numbers in LP order (the same numbering Windows tools show: core 0 holds LP 0)
                var coreNo = new Dictionary<byte, int>();
                foreach (var c in g0.GroupBy(s => s.Core).OrderBy(c => c.Min(s => s.LP))) coreNo[c.Key] = coreNo.Count;
                IEnumerable<IGrouping<int, Native.CpuSet>> parts;
                if (t.Hybrid) parts = g0.GroupBy(s => -(int)s.Eff).OrderBy(g => g.Key);                  // P-cores (higher class) first
                else if (t.LLCGroups.Count > 1) parts = g0.GroupBy(s => (int)s.LLC).OrderBy(g => g.Key);
                else parts = g0.GroupBy(s => 0);
                int idx = 0;
                foreach (var part in parts)
                {
                    var grp = new Group();
                    if (t.Hybrid) grp.Head = idx == 0 ? L.T("P-cores") : idx == 1 ? L.T("E-cores") : L.T("LP E-cores");
                    else if (t.LLCGroups.Count > 1)
                    {
                        byte llc = (byte)part.Key;
                        bool vc = t.VCacheLlc.HasValue && t.VCacheLlc.Value == llc;
                        grp.Head = L.F("L3 domain {0}", idx) + "  ·  " + t.L3Text(llc) + (vc ? "  ·  V-Cache" : "");
                        grp.Accent = vc;
                    }
                    foreach (var core in part.GroupBy(s => s.Core).OrderBy(c => c.Min(s => s.LP)))
                    {
                        grp.Cores.Add(core.Select(s => (int)s.LP).OrderBy(x => x).ToList());
                        var hl = new Label { Text = L.F("Core {0}", coreNo[core.Key]), AutoSize = false, Height = CoreHeadH, Font = Theme.F(8.5f), ForeColor = Theme.Faint,
                                             BackColor = BackColor, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, AutoEllipsis = true };
                        grp.CoreHeads.Add(hl);
                        Controls.Add(hl);
                    }
                    if (grp.Head.Length > 0)
                    {
                        grp.Label = new Label { Text = grp.Head, AutoSize = false, Height = HeadH, Font = Theme.Semi(9f), ForeColor = grp.Accent ? Theme.Acc : Theme.Dim,
                                                BackColor = BackColor, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, AutoEllipsis = true };
                        Controls.Add(grp.Label);
                    }
                    foreach (var core in grp.Cores)
                        foreach (int lp in core)
                        {
                            var cb = new BoxCheck("CPU " + lp) { Width = ColW - Theme.D(6), Height = RowH - Theme.D(2), BackColor = BackColor };
                            cb.CheckedChanged += (s, e) => { if (!_setting) Raise(); };
                            _byLp[lp] = cb;
                            Controls.Add(cb);
                        }
                    _groups.Add(grp);
                    idx++;
                }
            }
            finally { ResumeLayout(false); }
            PerformLayout();
        }

        private void Raise() { var h = Changed; if (h != null) h(); }

        public ulong Mask
        {
            get { ulong m = 0; foreach (var kv in _byLp) if (kv.Value.Checked && kv.Key < 64) m |= 1UL << kv.Key; return m; }
            set
            {
                _setting = true;
                try { foreach (var kv in _byLp) kv.Value.Checked = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; }
                finally { _setting = false; }
                Raise();
            }
        }

        public bool IsEmpty { get { return _byLp.Count == 0; } }
        // accent dot on the LPs of this mask (the current game cores), as a picking hint
        public ulong MarkMask { set { foreach (var kv in _byLp) kv.Value.Mark = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; } }
        // dot in a colour of its own on these LPs (the game-mask grid: interrupt LPs); overrides MarkMask on them
        public void MarkWith(ulong mask, System.Drawing.Color c)
        {
            foreach (var kv in _byLp)
            {
                bool on = kv.Key < 64 && ((mask >> kv.Key) & 1) != 0;
                if (on) { kv.Value.Mark = true; kv.Value.MarkColor = c; }
                else kv.Value.MarkColor = System.Drawing.Color.Empty;
            }
        }
        // LPs that cannot be ticked here (taken by an automatic rule); the rest stay editable
        public ulong LockedMask
        {
            set { foreach (var kv in _byLp) kv.Value.Locked = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; }
        }
        // hover text per LP ("CPU 11 - interrupts: Realtek ...")
        public void TipFor(int lp, string text)
        {
            BoxCheck cb;
            if (!_byLp.TryGetValue(lp, out cb)) return;
            if (_tips == null) _tips = Tips.Owned(this);
            _tips.SetToolTip(cb, text ?? "");
        }
        private ToolTip _tips;
        // every logical processor of group 0 (Select All)
        public ulong AllMask { get { ulong m = 0; foreach (var k in _byLp.Keys) if (k < 64) m |= 1UL << k; return m; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_groups.Count == 0) return;
            int perBand = Math.Max(1, Width / ColW);
            int y = 0;
            foreach (var g in _groups)
            {
                if (g.Label != null) { g.Label.SetBounds(0, y, Width, HeadH); y += HeadH + Theme.D(2); }
                int rows = g.Cores.Count == 0 ? 0 : g.Cores.Max(c => c.Count);
                int bandH = CoreHeadH + Theme.D(2) + rows * RowH;
                for (int i = 0; i < g.Cores.Count; i++)
                {
                    int band = i / perBand, col = i % perBand;
                    int by = y + band * (bandH + Theme.D(4));
                    g.CoreHeads[i].SetBounds(col * ColW + Theme.D(4), by, ColW - Theme.D(10), CoreHeadH);
                    for (int r = 0; r < g.Cores[i].Count; r++)
                    {
                        BoxCheck cb;
                        if (_byLp.TryGetValue(g.Cores[i][r], out cb))
                            cb.SetBounds(col * ColW, by + CoreHeadH + Theme.D(2) + r * RowH, ColW - Theme.D(6), RowH - Theme.D(2));
                    }
                }
                int bands = (g.Cores.Count + perBand - 1) / perBand;
                y += bands * bandH + Math.Max(0, bands - 1) * Theme.D(4) + GroupGap;
            }
            int h = Math.Max(RowH, y - GroupGap + Theme.D(4));
            if (Height != h) Height = h;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
    }
}
