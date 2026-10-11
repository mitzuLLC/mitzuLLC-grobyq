using System.Collections.Generic;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Grobyq
{
    // Label on the left (muted), static value on the right; optional leading widget (status dot).
    // The value is a Label, never a TextBox: display-only data must not take focus or show a caret.
    // AutoEllipsis gives the full text as a tooltip only when it is actually cut off.
    internal sealed class SheetRow : Panel
    {
        private readonly Label _k;
        private readonly Label _v;
        private readonly Control _lead;
        private readonly ToolTip _tip;

        public SheetRow(string key, Control lead = null, int height = 30)
        {
            _tip = Tips.Owned(this);
            BackColor = Theme.Card;
            _k = new Label { Text = key, Font = Theme.F(9.5f), ForeColor = Theme.Dim, BackColor = Theme.Card, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
            _v = new Label { BackColor = Theme.Card, ForeColor = Theme.Fg, Font = Theme.F(10f), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
            _lead = lead;
            Controls.Add(_k); Controls.Add(_v);
            if (lead != null) Controls.Add(lead);
            Height = Theme.D(height);
        }

        public int KeyWidth = 150;
        public string Value { get { return _v.Text; } set { _v.Text = value ?? ""; } }
        public string Tip { set { _tip.SetToolTip(_v, value); _tip.SetToolTip(this, value); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_k == null || _v == null) return;
            int kw = Math.Min(Theme.D(KeyWidth), Width / 3);
            _k.SetBounds(0, 0, kw, Height);
            int x = kw;
            if (_lead != null) { _lead.SetBounds(x, (Height - Theme.D(10)) / 2, Theme.D(10), Theme.D(10)); x += Theme.D(18); }
            _v.SetBounds(x, 0, Math.Max(Theme.D(40), Width - x), Height);
        }
    }

    // 0 = unknown (grey), 1 = ok (green), 2 = offline cache (amber), 3 = not licensed (red).
    internal sealed class StatusDot : Control
    {
        private int _kind;
        public int Kind { get { return _kind; } set { _kind = value; Invalidate(); } }
        public StatusDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Size = new Size(Theme.D(10), Theme.D(10));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = _kind == 1 ? Theme.Ok : _kind == 2 ? Theme.Warn : _kind == 3 ? Theme.Bad : Theme.Faint;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, Width - 3, Height - 3);
        }
    }

    internal sealed class SubHead : Label
    {
        public SubHead(string text)
        {
            Text = text; Font = Theme.Semi(9.5f); ForeColor = Theme.Dim; BackColor = Theme.Card; AutoSize = false; Height = Theme.D(28);
            TextAlign = ContentAlignment.BottomLeft; UseMnemonic = false;
        }
    }

    // Status lines: a coloured mark (✔ ok, ⚠ warning, ✖ problem, • note) and wrapped text; sizes itself to its lines.
    internal sealed class CheckList : Control
    {
        private readonly List<CheckLine> _lines = new List<CheckLine>();
        private bool _fitting;

        public CheckList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Font = Theme.F(9.25f); Height = 0;
        }

        public void Set(IEnumerable<CheckLine> lines)
        {
            _lines.Clear();
            if (lines != null) _lines.AddRange(lines);
            Fit(); Invalidate();
        }

        public int Count { get { return _lines.Count; } }

        private int MarkW { get { return Theme.D(22); } }
        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;

        private static string Mark(int level) { return level == IrqCheck.Ok ? "✔" : level == IrqCheck.Warn ? "⚠" : level == IrqCheck.Bad ? "✖" : "•"; }
        private static Color MarkColor(int level) { return level == IrqCheck.Ok ? Theme.Ok : level == IrqCheck.Warn ? Theme.Warn : level == IrqCheck.Bad ? Theme.Bad : Theme.Faint; }

        private int LineH(CheckLine l, int w)
        {
            return Math.Max(Theme.D(20), TextRenderer.MeasureText(l.Text, Font, new Size(Math.Max(40, w - MarkW), 10000), Wrap).Height + Theme.D(4));
        }

        private void Fit()
        {
            if (_fitting || Width < 40) return;
            _fitting = true;
            try
            {
                int h = 0;
                foreach (var l in _lines) h += LineH(l, Width);
                if (_lines.Count > 0) h += Theme.D(4);
                if (Height != h) Height = h;
            }
            finally { _fitting = false; }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int y = Theme.D(2);
            foreach (var l in _lines)
            {
                int h = LineH(l, Width);
                TextRenderer.DrawText(g, Mark(l.Level), Theme.Semi(9.5f), new Rectangle(0, y, MarkW, Theme.D(20)), MarkColor(l.Level), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, l.Text, Font, new Rectangle(MarkW, y, Width - MarkW, h), l.Level == IrqCheck.Info ? Theme.Dim : Theme.Fg, Wrap);
                y += h;
            }
        }
    }

    internal sealed class Hairline : Control
    {
        public Hairline() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); BackColor = Theme.Card; Height = Theme.D(21); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, Height / 2, Width, Height / 2);
        }
    }

    // Sheet rows in two columns (one when narrow), row-major; a wide row takes the whole line.
    internal sealed class SheetGrid : Panel
    {
        private readonly List<SheetRow> _rows = new List<SheetRow>();
        public SheetGrid() { BackColor = Theme.Card; Height = Theme.D(30); }
        private readonly HashSet<SheetRow> _wide = new HashSet<SheetRow>();
        // wide = the row takes the whole width (HWID is too long for half a card)
        public SheetRow Add(SheetRow r, bool wide = false) { r.KeyWidth = 100; _rows.Add(r); if (wide) _wide.Add(r); Controls.Add(r); PerformLayout(); return r; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_rows.Count == 0 || Width < 20) return;
            int cols = Width >= Theme.D(560) ? 2 : 1, gap = Theme.D(24), cw = (Width - gap * (cols - 1)) / cols, y = 0, c = 0, rowH = 0;
            foreach (var r in _rows)
            {
                bool wide = _wide.Contains(r) || cols == 1;
                if (wide && c > 0) { y += rowH; c = 0; rowH = 0; }
                r.SetBounds(wide ? 0 : c * (cw + gap), y, wide ? Width : cw, r.Height);
                rowH = Math.Max(rowH, r.Height);
                c = wide ? cols : c + 1;
                if (c >= cols) { y += rowH; c = 0; rowH = 0; }
            }
            if (c > 0) y += rowH;
            if (Height != y) Height = y;
        }
    }
}
