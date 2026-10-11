using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Grobyq
{
    // One checkbox look for the whole app: a 20 px rounded square, Edge border; checked = accent fill + white tick,
    // hover = brighter border. The whole control (box and caption) is the click target.
    public sealed class BoxCheck : Control
    {
        private bool _checked, _hot;
        public event EventHandler CheckedChanged;

        public BoxCheck(string text = "")
        {
            Text = text ?? "";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card; ForeColor = Theme.Fg; Font = Theme.F(9.5f); Cursor = Cursors.Hand; TabStop = true;
            Height = Theme.D(28);
            Width = Text.Length == 0 ? Theme.D(28) : Theme.D(28) + TextRenderer.MeasureText(Text, Font).Width + Theme.D(6);
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        public static int BoxSize { get { return Theme.D(20); } }

        // small accent dot on the right: marks a core of the current game mask (a hint, not a state)
        private bool _mark;
        public bool Mark { get { return _mark; } set { if (_mark == value) return; _mark = value; Invalidate(); } }
        // dot colour: accent by default; the game-mask grid paints interrupt LPs amber
        private Color _markColor = Color.Empty;
        // locked: drawn like a disabled box and not toggled by clicks / Space, but still Enabled so its tooltip shows
        private bool _locked;
        public bool Locked { get { return _locked; } set { if (_locked == value) return; _locked = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); } }
        public Color MarkColor { get { return _markColor; } set { if (_markColor == value) return; _markColor = value; Invalidate(); } }

        // Shared painter (rows that draw their own checkbox use the same look).
        public static void PaintBox(Graphics g, Rectangle r, bool on, bool hot, bool enabled)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Theme.Round(rf, Theme.Df(5)))
            {
                Color fill = on ? (enabled ? Theme.Acc : Theme.AccDim) : Theme.Input;
                Color edge = on ? fill : hot ? Theme.EdgeHi : Theme.Edge;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(edge, Theme.Df(1.2f))) g.DrawPath(p, path);
            }
            if (on)
            {
                float w = r.Width, x = r.X, y = r.Y;
                using (var p = new Pen(enabled ? Color.White : Theme.Dim, Theme.Df(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new PointF(x + w * 0.26f, y + w * 0.52f), new PointF(x + w * 0.43f, y + w * 0.69f), new PointF(x + w * 0.75f, y + w * 0.33f) });
            }
            g.SmoothingMode = sm;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { if (Enabled && !_locked) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled && !_locked) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int bs = BoxSize;
            var box = new Rectangle(Theme.D(4), (Height - bs) / 2, bs, bs);
            bool live = Enabled && !_locked;
            if (_hot && Text.Length > 0 && !_locked)
                using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Df(7)))
                using (var b = new SolidBrush(Theme.CardHi)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(b, path); }
            PaintBox(g, box, _checked, (_hot || Focused) && !_locked, live);
            if (_mark)
            {
                // the dot sits right after the caption (reads as part of "CPU 4 ●"), never beyond the control
                int d = Theme.D(6);
                int tx = box.Right + Theme.D(8) + (Text.Length > 0 ? TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width : 0);
                int dx = Math.Min(Width - d - Theme.D(4), tx + Theme.D(6));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(_markColor != Color.Empty ? _markColor : live ? Theme.Acc : Theme.Faint)) g.FillEllipse(b, dx, (Height - d) / 2, d, d);
            }
            if (Text.Length > 0)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(box.Right + Theme.D(8), 0, Width - box.Right - Theme.D(8) - (_mark ? Theme.D(14) : 0), Height),
                    !live ? Theme.Faint : _checked ? Theme.Fg : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Vain-style checkable row: bold title + grey description (+ amber warning) on the left, a value column
    // (size / "found") and a big checkbox on the right. The whole row toggles; hover lights the row.
    public sealed class CheckRow : Control
    {
        private bool _checked, _hot, _fitting;
        private string _title, _desc, _note, _value = "";
        public Color ValueColor = Theme.Fg;
        public bool Caution;
        public bool Divider = true;
        public event EventHandler CheckedChanged;

        public CheckRow(string title, string desc, string note = null)
        {
            _title = title ?? ""; _desc = desc ?? ""; _note = note;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card; Cursor = Cursors.Hand; TabStop = true; Height = Theme.D(46);
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public string Value { get { return _value; } set { _value = value ?? ""; Invalidate(); } }

        private int ValueW { get { return Theme.D(150); } }
        private int RightW { get { return ValueW + BoxCheck.BoxSize + Theme.D(28); } }
        private int TextW { get { return Math.Max(Theme.D(120), Width - RightW - Theme.D(24)); } }
        private static Font TitleFont { get { return Theme.Semi(10f); } }
        private static Font DescFont { get { return Theme.F(8.75f); } }
        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;

        private int Measure(string s, Font f) { return string.IsNullOrEmpty(s) ? 0 : TextRenderer.MeasureText(s, f, new Size(TextW, 10000), Wrap).Height; }
        private string NoteText { get { return string.IsNullOrEmpty(_note) ? null : "⚠ " + _note; } }

        private void Fit()
        {
            if (_fitting || Width < 40) return;
            _fitting = true;
            try
            {
                int h = Theme.D(8) + TextRenderer.MeasureText("Ag", TitleFont).Height + Theme.D(1) + Measure(_desc, DescFont);
                if (NoteText != null) h += Theme.D(2) + Measure(NoteText, DescFont);
                h = Math.Max(Theme.D(44), h + Theme.D(8));
                if (Height != h) Height = h;
            }
            finally { _fitting = false; }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Fit(); }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            if (_hot || Focused)
                using (var path = Theme.Round(new RectangleF(0.5f, 1.5f, Width - 1.5f, Height - 3.5f), Theme.Df(8)))
                using (var b = new SolidBrush(Theme.CardHi)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(b, path); g.SmoothingMode = SmoothingMode.Default; }
            int x = Theme.D(10), y = Theme.D(8), tw = TextW;
            int th = TextRenderer.MeasureText("Ag", TitleFont).Height;
            TextRenderer.DrawText(g, _title, TitleFont, new Rectangle(x, y, tw, th), Caution ? Theme.Warn : Theme.Fg, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            y += th + Theme.D(1);
            if (_desc.Length > 0)
            {
                int dh = Measure(_desc, DescFont);
                TextRenderer.DrawText(g, _desc, DescFont, new Rectangle(x, y, tw, dh), Theme.Dim, Wrap);
                y += dh;
            }
            if (NoteText != null)
            {
                y += Theme.D(2);
                int nh = Measure(NoteText, DescFont);
                TextRenderer.DrawText(g, NoteText, DescFont, new Rectangle(x, y, tw, nh), Theme.Warn, Wrap);
            }
            int bs = BoxCheck.BoxSize;
            var box = new Rectangle(Width - bs - Theme.D(14), (Height - bs) / 2, bs, bs);
            TextRenderer.DrawText(g, _value, Theme.F(9.5f), new Rectangle(box.X - Theme.D(12) - ValueW, 0, ValueW, Height), Enabled ? ValueColor : Theme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            BoxCheck.PaintBox(g, box, _checked, _hot || Focused, Enabled);
            if (Divider && !_hot) using (var p = new Pen(Theme.Line)) g.DrawLine(p, Theme.D(10), Height - 1, Width - Theme.D(10), Height - 1);
        }
    }
}
