using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Grobyq
{
    // ------------------------------------------------------------------ icons
    // Small line icons drawn with GDI+ in a 16x16 design box (no icon font needed).
    public static class Icons
    {
        public static void Draw(Graphics g, string kind, RectangleF r, Color c, float stroke = 1.5f)
        {
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 16f, r.Height / 16f);
            using (var p = new Pen(c, stroke / Math.Max(0.01f, r.Width / 16f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var b = new SolidBrush(c))
            {
                switch (kind)
                {
                    case "threads":
                        g.DrawLine(p, 5.5f, 4f, 14f, 4f); g.DrawLine(p, 5.5f, 8f, 12f, 8f); g.DrawLine(p, 5.5f, 12f, 14f, 12f);
                        g.FillEllipse(b, 1.2f, 2.8f, 2.4f, 2.4f); g.FillEllipse(b, 1.2f, 6.8f, 2.4f, 2.4f); g.FillEllipse(b, 1.2f, 10.8f, 2.4f, 2.4f);
                        break;
                    case "chip":
                    case "affinity":
                        using (var rr = Theme.Round(new RectangleF(3.5f, 3.5f, 9f, 9f), 1.5f)) g.DrawPath(p, rr);
                        g.DrawRectangle(p, 6.2f, 6.2f, 3.6f, 3.6f);
                        foreach (float t in new[] { 6f, 10f })
                        {
                            g.DrawLine(p, t, 1f, t, 3.5f); g.DrawLine(p, t, 12.5f, t, 15f);
                            g.DrawLine(p, 1f, t, 3.5f, t); g.DrawLine(p, 12.5f, t, 15f, t);
                        }
                        break;
                    case "gpu":
                        using (var rr = Theme.Round(new RectangleF(1.5f, 2.5f, 13f, 9f), 1.2f)) g.DrawPath(p, rr);
                        g.DrawLine(p, 8f, 11.5f, 8f, 14f); g.DrawLine(p, 5f, 14f, 11f, 14f);
                        break;
                    case "nic":
                        using (var rr = Theme.Round(new RectangleF(1.5f, 3f, 13f, 10f), 1.2f)) g.DrawPath(p, rr);
                        g.DrawLines(p, new[] { new PointF(5f, 13f), new PointF(5f, 9.5f), new PointF(6.3f, 9.5f), new PointF(6.3f, 7.3f), new PointF(9.7f, 7.3f), new PointF(9.7f, 9.5f), new PointF(11f, 9.5f), new PointF(11f, 13f) });
                        break;
                    case "about":
                        g.DrawEllipse(p, 1.5f, 1.5f, 13f, 13f);
                        g.DrawLine(p, 8f, 7.2f, 8f, 11.5f); g.FillEllipse(b, 7.1f, 3.9f, 1.8f, 1.8f);
                        break;
                    case "ram":
                        using (var rr = Theme.Round(new RectangleF(1.5f, 4.5f, 13f, 6.5f), 1f)) g.DrawPath(p, rr);
                        g.DrawRectangle(p, 3.8f, 6.6f, 2f, 2.4f); g.DrawRectangle(p, 7f, 6.6f, 2f, 2.4f); g.DrawRectangle(p, 10.2f, 6.6f, 2f, 2.4f);
                        g.DrawLine(p, 4f, 11f, 4f, 13f); g.DrawLine(p, 8f, 11f, 8f, 13f); g.DrawLine(p, 12f, 11f, 12f, 13f);
                        break;
                    case "board":
                        using (var rr = Theme.Round(new RectangleF(2f, 2f, 12f, 12f), 1.5f)) g.DrawPath(p, rr);
                        g.DrawRectangle(p, 4.6f, 4.6f, 4f, 4f); g.DrawEllipse(p, 10f, 10f, 1.8f, 1.8f); g.DrawLine(p, 10.5f, 4.6f, 11.5f, 4.6f);
                        break;
                    case "log":
                        using (var rr = Theme.Round(new RectangleF(2.5f, 1.5f, 11f, 13f), 1.2f)) g.DrawPath(p, rr);
                        g.DrawLine(p, 5f, 5f, 11f, 5f); g.DrawLine(p, 5f, 8f, 11f, 8f); g.DrawLine(p, 5f, 11f, 9f, 11f);
                        break;
                    case "settings":
                        g.DrawEllipse(p, 5.2f, 5.2f, 5.6f, 5.6f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(p, 8f + (float)Math.Cos(a) * 4.6f, 8f + (float)Math.Sin(a) * 4.6f, 8f + (float)Math.Cos(a) * 6.6f, 8f + (float)Math.Sin(a) * 6.6f);
                        }
                        break;
                    case "clean":
                        g.DrawPolygon(p, new[] { new PointF(6.5f, 1.5f), new PointF(7.9f, 5.1f), new PointF(11.5f, 6.5f), new PointF(7.9f, 7.9f), new PointF(6.5f, 11.5f), new PointF(5.1f, 7.9f), new PointF(1.5f, 6.5f), new PointF(5.1f, 5.1f) });
                        g.DrawPolygon(p, new[] { new PointF(12f, 9.5f), new PointF(12.7f, 11.3f), new PointF(14.5f, 12f), new PointF(12.7f, 12.7f), new PointF(12f, 14.5f), new PointF(11.3f, 12.7f), new PointF(9.5f, 12f), new PointF(11.3f, 11.3f) });
                        break;
                    case "x":
                        g.DrawLine(p, 3f, 2.5f, 13f, 13.5f); g.DrawLine(p, 13f, 2.5f, 3f, 13.5f);
                        break;
                    case "chat":
                        using (var cb = Theme.Round(new RectangleF(1.8f, 2.5f, 12.4f, 8.8f), 2.4f)) g.DrawPath(p, cb);
                        g.DrawLine(p, 5f, 11.3f, 4.2f, 14f); g.DrawLine(p, 4.2f, 14f, 8f, 11.3f);
                        break;
                    case "window":
                        using (var wb = Theme.Round(new RectangleF(1.8f, 2.5f, 12.4f, 11f), 2f)) g.DrawPath(p, wb);
                        g.DrawLine(p, 1.8f, 5.8f, 14.2f, 5.8f);
                        break;
                    case "globe":
                        g.DrawEllipse(p, 1.5f, 1.5f, 13f, 13f);
                        g.DrawEllipse(p, 5f, 1.5f, 6f, 13f);
                        g.DrawLine(p, 1.5f, 8f, 14.5f, 8f);
                        break;
                    case "home":
                        g.DrawLines(p, new[] { new PointF(1.8f, 7.6f), new PointF(8f, 2f), new PointF(14.2f, 7.6f) });
                        g.DrawLines(p, new[] { new PointF(3.6f, 6.2f), new PointF(3.6f, 14f), new PointF(12.4f, 14f), new PointF(12.4f, 6.2f) });
                        g.DrawLines(p, new[] { new PointF(6.6f, 14f), new PointF(6.6f, 10f), new PointF(9.4f, 10f), new PointF(9.4f, 14f) });
                        break;
                    case "plane":        // paper plane (Telegram card)
                        g.DrawPolygon(p, new[] { new PointF(1.5f, 7.4f), new PointF(14.5f, 2f), new PointF(12f, 14f), new PointF(7.6f, 10.4f) });
                        g.DrawLine(p, 7.6f, 10.4f, 14.5f, 2f);
                        g.DrawLines(p, new[] { new PointF(7.6f, 10.4f), new PointF(6.8f, 14f), new PointF(9.4f, 11.9f) });
                        break;
                    case "ext":          // external link tick
                        g.DrawLine(p, 5f, 11f, 11.5f, 4.5f);
                        g.DrawLines(p, new[] { new PointF(6.5f, 4.5f), new PointF(11.5f, 4.5f), new PointF(11.5f, 9.5f) });
                        break;
                    case "usb":
                        g.DrawLine(p, 8f, 1.5f, 8f, 12f);
                        g.DrawLines(p, new[] { new PointF(6.2f, 3.6f), new PointF(8f, 1.5f), new PointF(9.8f, 3.6f) });
                        g.DrawLines(p, new[] { new PointF(8f, 9.4f), new PointF(4.2f, 7.2f), new PointF(4.2f, 5.2f) });
                        g.DrawLines(p, new[] { new PointF(8f, 7.6f), new PointF(11.8f, 5.6f), new PointF(11.8f, 4.2f) });
                        g.FillEllipse(b, 6.4f, 11.6f, 3.2f, 3.2f);
                        break;
                    case "audio":
                        g.DrawPolygon(p, new[] { new PointF(2f, 6f), new PointF(5f, 6f), new PointF(8.6f, 2.8f), new PointF(8.6f, 13.2f), new PointF(5f, 10f), new PointF(2f, 10f) });
                        g.DrawArc(p, 7.5f, 5f, 4f, 6f, -55, 110);
                        g.DrawArc(p, 7.5f, 2.6f, 7f, 10.8f, -55, 110);
                        break;
                    case "storage":
                        using (var rr = Theme.Round(new RectangleF(1.5f, 4f, 13f, 8f), 1.4f)) g.DrawPath(p, rr);
                        g.FillEllipse(b, 11f, 7.2f, 1.6f, 1.6f);
                        g.DrawLine(p, 3.8f, 8f, 8.5f, 8f);
                        break;
                    case "bolt-dot":
                        g.FillEllipse(b, 4f, 4f, 8f, 8f);
                        break;
                    default:
                        g.DrawEllipse(p, 3f, 3f, 10f, 10f);
                        break;
                }
            }
            g.Restore(st);
        }
    }

    // ------------------------------------------------------------------ card
    public class Card : Panel
    {
        public Color Fill = Theme.Card;
        public int Radius = 10;
        public bool AutoHeight = true;
        private readonly List<Control> _stack = new List<Control>();
        public int Gap = 0;

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Padding = new Padding(Theme.D(18), Theme.D(8), Theme.D(18), Theme.D(8));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(Radius)))
            using (var b = new SolidBrush(Fill))
            using (var pen = new Pen(Theme.Line))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
        }

        // Vertical stack: rows take the inner width, card height follows the content.
        public T Stack<T>(T c) where T : Control
        {
            _stack.Add(c);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            Controls.Add(c);
            PerformLayout();
            return c;
        }

        public void ClearStack() { foreach (var c in _stack) { Controls.Remove(c); c.Dispose(); } _stack.Clear(); PerformLayout(); }
        // take one control out of the stack (not disposed); Stack() it again to move it to the end
        public void Unstack(Control c) { if (_stack.Remove(c)) Controls.Remove(c); PerformLayout(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_stack.Count == 0) return;
            int y = Padding.Top, w = Math.Max(10, ClientSize.Width - Padding.Horizontal);
            foreach (var c in _stack)
            {
                if (!c.Visible) continue;
                c.SetBounds(Padding.Left, y, w, c.Height);
                y += c.Height + Gap;
            }
            if (AutoHeight)
            {
                int h = y - Gap + Padding.Bottom;
                if (Height != h) Height = h;
            }
        }
    }

    // Borderless vertical stack (same rules as Card) for editors nested in a card.
    public class StackBox : Panel
    {
        private readonly List<Control> _stack = new List<Control>();
        public StackBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Padding = Padding.Empty; Height = Theme.D(20);
        }
        public T Stack<T>(T c) where T : Control { _stack.Add(c); Controls.Add(c); PerformLayout(); return c; }
        public void ClearStack() { foreach (var c in _stack) { Controls.Remove(c); c.Dispose(); } _stack.Clear(); PerformLayout(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int y = 0, w = Math.Max(10, ClientSize.Width);
            foreach (var c in _stack) { if (!c.Visible) continue; c.SetBounds(0, y, w, c.Height); y += c.Height; }
            int h = Math.Max(Theme.D(4), y);
            if (Height != h) Height = h;
        }
    }

    // ------------------------------------------------------------------ nav
    public sealed class NavItem : Control
    {
        public string Kind;
        private bool _sel, _hot, _badge;
        public bool Selected { get { return _sel; } set { _sel = value; Invalidate(); } }
        // Small accent dot: something new is waiting behind this item (the folded log got new lines).
        public bool Badge { get { return _badge; } set { if (_badge == value) return; _badge = value; Invalidate(); } }

        public NavItem(string text, string kind)
        {
            Text = text; Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.D(36); Cursor = Cursors.Hand; Font = Theme.F(10f); BackColor = Theme.Side;
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Side);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_sel || _hot)
                using (var path = Theme.Round(new RectangleF(Theme.D(6), Theme.D(2), Width - Theme.D(12), Height - Theme.D(4)), Theme.Df(6)))
                using (var b = new SolidBrush(_sel ? Theme.CardHi : Color.FromArgb(17, 17, 21)))
                    g.FillPath(b, path);
            if (_sel)
                using (var path = Theme.Round(new RectangleF(Theme.D(7), Height / 2f - Theme.D(8), Theme.D(3), Theme.D(16)), Theme.Df(1.5f)))
                using (var b = new SolidBrush(Theme.Acc))
                    g.FillPath(b, path);
            float ic = Theme.D(16);
            Icons.Draw(g, Kind, new RectangleF(Theme.D(20), (Height - ic) / 2f, ic, ic), Theme.Fg, Theme.Df(1.4f));
            var tr = new Rectangle(Theme.D(50), 0, Width - Theme.D(56), Height);
            TextRenderer.DrawText(g, Text, Font, tr, _sel ? Color.White : Theme.Fg, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_badge)
            {
                int tw = TextRenderer.MeasureText(Text, Font).Width;
                float d = Theme.Df(7), x = Math.Min(Width - Theme.D(18), Theme.D(50) + tw + Theme.D(6));
                using (var b = new SolidBrush(Theme.Acc)) g.FillEllipse(b, x, Height / 2f - d / 2f, d, d);
            }
        }
    }

    public sealed class NavHeader : Control
    {
        public NavHeader(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.D(46); Font = Theme.Semi(10f); BackColor = Theme.Side;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Side);
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, Theme.D(4), Width, Theme.D(4));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Theme.D(16), Theme.D(10), Width - Theme.D(16), Height - Theme.D(10)), Theme.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------------------ toggle switch
    public sealed class Toggle : Control
    {
        private bool _on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return _on; }
            set { if (_on == value) return; _on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public void SetSilently(bool v) { _on = v; Invalidate(); }

        public Toggle(bool on = false)
        {
            _on = on;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(Theme.D(44), Theme.D(22)); Cursor = Cursors.Hand; BackColor = Color.Transparent; TabStop = true;
        }
        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color fill = !Enabled ? Theme.Line : _on ? Theme.Acc : Theme.Input;
            using (var path = Theme.Round(r, r.Height / 2))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(_on ? Theme.Acc : Theme.Faint, 1f))
            { g.FillPath(b, path); g.DrawPath(pen, path); }
            float k = r.Height - Theme.Df(8);
            float x = _on ? r.Right - k - Theme.Df(4) : r.X + Theme.Df(4);
            using (var b = new SolidBrush(_on ? Color.White : Theme.Dim)) g.FillEllipse(b, x, r.Y + Theme.Df(4), k, k);
        }
    }

    // ------------------------------------------------------------------ button
    public sealed class Btn : Control
    {
        public enum Style { Normal, Accent, Danger, Ok, Ghost, Outline, DangerQuiet }
        public Style Look;
        private bool _hot, _down;
        public string Icon;

        private readonly int _minW;
        public bool AutoGrow = true;
        // opt-in: a disabled button keeps a visible neutral outline (reads as "disabled", not as loose grey text)
        public bool DisabledOutline;

        public Btn(string text, Style look = Style.Normal, int width = 0)
        {
            Look = look; _minW = width;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Font = Theme.F(9.5f); Cursor = Cursors.Hand; BackColor = Color.Transparent; TabStop = true;
            Height = Theme.D(32);
            Text = text;
            Fit();
        }
        // Width = the given minimum or the text width, whichever is larger (translations are longer than English).
        public void Fit() { int need = TextRenderer.MeasureText(Text ?? "", Font).Width + Theme.D(30); Width = Math.Max(_minW, need); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoGrow) Fit(); }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { if (AutoGrow && IsHandleCreated) Fit(); Invalidate(); base.OnTextChanged(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, fg, border;
            switch (Look)
            {
                case Style.Accent: fill = Theme.Acc; fg = Color.White; border = Theme.Acc; break;
                case Style.Danger: fill = Color.FromArgb(70, 30, 34); fg = Color.FromArgb(255, 170, 170); border = Color.FromArgb(110, 45, 50); break;
                case Style.Ok:     fill = Theme.Ok; fg = Color.FromArgb(8, 24, 14); border = Theme.Ok; break;
                case Style.Ghost:  fill = Color.Transparent; fg = Theme.Dim; border = Color.Transparent; break;
                case Style.Outline: fill = Color.Transparent; fg = Theme.Fg; border = Theme.Edge; break;
                case Style.DangerQuiet: fill = Color.Transparent; fg = Color.FromArgb(240, 140, 140); border = Color.FromArgb(96, 44, 50); break;
                default:           fill = Theme.Input; fg = Theme.Fg; border = Theme.Edge; break;
            }
            if (!Enabled) { fill = DisabledOutline ? Color.Transparent : Theme.Card; fg = Theme.Faint; border = DisabledOutline ? Theme.Edge : Theme.Line; }
            else if (_down) fill = ControlPaint.Dark(fill.A == 0 ? Theme.Input : fill, 0.05f);
            else if (_hot) fill = fill.A == 0 ? Theme.Input : ControlPaint.Light(fill, 0.12f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(6)))
            {
                if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.A > 0) using (var p = new Pen(Focused ? (Look == Style.Accent ? Theme.Acc : Theme.EdgeDn) : border)) g.DrawPath(p, path);   // focus ring stays neutral
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------ setting row
    // Title + explanation on the left, the control on the right, divider below.
    public sealed class SettingRow : Panel
    {
        private readonly Label _t, _d;
        public readonly Control Ctl;
        public bool Divider = true;

        public SettingRow(string title, string desc, Control right)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            _t = Theme.Lbl(title, Theme.Semi(10f), Theme.Fg, false);
            _t.AutoEllipsis = true; _t.Height = TextRenderer.MeasureText("Ag", _t.Font).Height + Theme.D(2);
            Controls.Add(_t);
            if (!string.IsNullOrEmpty(desc))
            {
                _d = Theme.Lbl(desc, Theme.F(8.75f), Theme.Dim, false);
                Controls.Add(_d);
            }
            Ctl = right;
            if (right != null) Controls.Add(right);
            Height = Theme.D(_d == null ? 42 : 54);
        }

        public string Desc { set { if (_d != null) { _d.Text = value; PerformLayout(); } } }

        // true = the control sits on the title line instead of the vertical middle of the row
        // (toggles stay next to their title when the description wraps in a narrow window).
        public bool AlignTop;

        // One pass computes the height AND positions the children. Returning early after "Height = h" relied on a
        // re-entrant Layout that .NET Framework defers while this control's own layout runs: the rows built during a
        // language switch kept the text measured at their construction width (one character per line) until a resize.
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int rw = Ctl != null ? Ctl.Width : 0;
            int textW = Math.Max(Theme.D(80), Width - rw - Theme.D(24));
            _t.Width = textW;
            int h;
            if (_d == null)
            {
                h = Math.Max(Theme.D(42), (Ctl != null ? Ctl.Height : 0) + Theme.D(10));
                if (Height != h) Height = h;
                _t.Location = new Point(0, (h - _t.Height) / 2);
                if (Ctl != null) Ctl.Location = new Point(Math.Max(0, Width - rw), (h - Ctl.Height) / 2);
                return;
            }
            var sz = TextRenderer.MeasureText(_d.Text, _d.Font, new Size(textW, 1000), TextFormatFlags.WordBreak);
            h = Math.Max(Theme.D(48), Theme.D(9) + _t.Height + Theme.D(1) + sz.Height + Theme.D(9));
            if (Ctl != null) h = Math.Max(h, Ctl.Height + Theme.D(10));
            if (Height != h) Height = h;
            _t.Location = new Point(0, Theme.D(9));
            _d.SetBounds(0, _t.Bottom + Theme.D(1), textW, sz.Height);
            if (Ctl != null)
            {
                int y = AlignTop ? Math.Max(Theme.D(6), _t.Top + (_t.Height - Ctl.Height) / 2) : (h - Ctl.Height) / 2;
                Ctl.Location = new Point(Math.Max(0, Width - rw), y);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Divider) using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    // A strip of controls laid out left to right (buttons, pickers).
    public sealed class Strip : FlowLayoutPanel
    {
        public Strip(int height = 0)
        {
            FlowDirection = FlowDirection.LeftToRight; WrapContents = false; AutoSize = false;
            Height = height > 0 ? height : Theme.D(40); BackColor = Theme.Card; Padding = new Padding(0, Theme.D(4), 0, 0);
        }
        public T Add<T>(T c, int gap = 8) where T : Control { c.Margin = new Padding(0, 0, Theme.D(gap), 0); Controls.Add(c); return c; }
    }

    // Auto-sized row of controls for the right side of a SettingRow (box + buttons). Grows with translated text.
    public sealed class Row : FlowLayoutPanel
    {
        public Row(params Control[] items)
        {
            FlowDirection = FlowDirection.LeftToRight; WrapContents = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Card; Padding = Padding.Empty; Margin = Padding.Empty;
            foreach (var c in items) Add(c);
        }
        public T Add<T>(T c, int gap = 8) where T : Control
        {
            int top = c is TextBox ? Theme.D(4) : (c is Label ? Theme.D(8) : 0);
            c.Margin = new Padding(0, top, Theme.D(gap), 0);
            Controls.Add(c);
            return c;
        }
    }

    // Plain wrapped text block inside a card.
    public sealed class TextBlock : Label
    {
        public TextBlock(string text, Color color, float size = 9f)
        {
            Text = text; ForeColor = color; Font = Theme.F(size); BackColor = Color.Transparent; AutoSize = false; UseMnemonic = false;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
        private bool _fitting;
        private void Fit()
        {
            if (_fitting || Width < 20) return;
            _fitting = true;
            var sz = TextRenderer.MeasureText(Text, Font, new Size(Width, 10000), TextFormatFlags.WordBreak);
            Height = sz.Height + Theme.D(4);
            _fitting = false;
        }
    }

    // ------------------------------------------------------------------ page base
    public sealed class Flow : FlowLayoutPanel
    {
        public Flow() { DoubleBuffered = true; }

        // .NET Framework drops a layout request raised while this panel is already laying out (the deferred flag is
        // cleared in PerformLayout's finally). A card that grows during that pass (wrapped text, a grid switching
        // columns) left the scroll extent short or tall. Re-run once, after the pass, when a child changed size in it.
        private bool _inLayout, _again;
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); e.Control.SizeChanged += ChildSized; }
        protected override void OnControlRemoved(ControlEventArgs e) { e.Control.SizeChanged -= ChildSized; base.OnControlRemoved(e); }
        private void ChildSized(object sender, EventArgs e) { if (_inLayout) _again = true; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            _inLayout = true;
            try { base.OnLayout(e); } finally { _inLayout = false; }
            if (_again && IsHandleCreated)
            {
                _again = false;
                BeginInvoke((Action)(() => { if (!IsDisposed) PerformLayout(); }));
            }
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) PerformLayout(); }));
        }
        // Jump bug root cause: when a control gets focus (a click on a drop-down arrow, a button, a row), the
        // ContainerControl calls ScrollControlIntoView -> ScrollToControl, and SetDisplayRectLocation() is applied to
        // the point returned here. DisplayRectangle.Location is the scroll origin DEFLATED BY Padding (Left 34+, Top 22),
        // so every focus change moved the origin by Padding.Top: the page jumped up by about one row whenever it was
        // scrolled (at the top the value is clamped to 0, so it never showed there). Return the current origin itself:
        // focus never moves the page.
        protected override Point ScrollToControl(Control activeControl) { return AutoScrollPosition; }
    }

    // ------------------------------------------------------------------ small layout rows
    // One line: Left items in order, Right items right-aligned, all vertically centered. Fill (one of the Left items)
    // takes the width that is left. When both groups do not fit, the Right group moves to a second line.
    public sealed class LineRow : Panel
    {
        private readonly List<Control> _left = new List<Control>(), _right = new List<Control>();
        public Control Fill;
        public int Gap = 8;
        private readonly int _minH;
        public LineRow(int height = 0) { BackColor = Theme.Card; _minH = height > 0 ? height : Theme.D(40); Height = _minH; }
        public T L<T>(T c) where T : Control { _left.Add(c); Controls.Add(c); PerformLayout(); return c; }
        public T R<T>(T c) where T : Control { _right.Add(c); Controls.Add(c); PerformLayout(); return c; }
        private static int W(Control c) { return c.Visible ? c.Width : 0; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int gap = Theme.D(Gap);
            var lv = _left.Where(c => c.Visible).ToList(); var rv = _right.Where(c => c.Visible).ToList();
            int lw = lv.Sum(c => c == Fill ? Theme.D(60) : c.Width) + Math.Max(0, lv.Count - 1) * gap;
            int rw = rv.Sum(c => c.Width) + Math.Max(0, rv.Count - 1) * gap;
            bool wrap = rv.Count > 0 && lv.Count > 0 && lw + gap * 2 + rw > Width;
            int lineH = Math.Max(_minH, Math.Max(lv.Count == 0 ? 0 : lv.Max(c => c.Height), rv.Count == 0 ? 0 : rv.Max(c => c.Height)) + Theme.D(8));
            int fillW = Math.Max(Theme.D(40), Width - (lw - (Fill != null && lv.Contains(Fill) ? Theme.D(60) : 0)) - (wrap || rv.Count == 0 ? 0 : rw + gap * 2));
            int x = 0;
            foreach (var c in lv)
            {
                if (c == Fill) c.Width = fillW;
                c.Location = new Point(x, (lineH - c.Height) / 2);
                x += c.Width + gap;
            }
            int y0 = wrap ? lineH : 0;
            int xr = wrap ? 0 : Width - rw;
            foreach (var c in rv) { c.Location = new Point(xr, y0 + (lineH - c.Height) / 2); xr += c.Width + gap; }
            int h = wrap ? lineH * 2 : lineH;
            if (Height != h) Height = h;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
    }

    // Buttons left to right with an even gap; a new line only when the next one does not fit.
    public sealed class WrapRow : Panel
    {
        public int Gap = 8;
        public WrapRow() { BackColor = Theme.Card; Height = Theme.D(40); }
        public T Add<T>(T c) where T : Control { Controls.Add(c); PerformLayout(); return c; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int gap = Theme.D(Gap), x = 0, y = 0, rowH = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                if (x > 0 && x + c.Width > Width) { x = 0; y += rowH + gap; rowH = 0; }
                c.Location = new Point(x, y);
                x += c.Width + gap; rowH = Math.Max(rowH, c.Height);
            }
            int h = Math.Max(Theme.D(8), y + rowH);
            if (Height != h) Height = h;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
    }

    // Small caption above a larger value ("Active" / "CS2 - 9900X3D").
    public sealed class TwoLine : Control
    {
        private string _cap, _val;
        public Color ValueColor = Theme.Fg;
        public TwoLine(string caption, string value)
        {
            _cap = caption ?? ""; _val = value ?? "";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Height = Theme.D(44);
        }
        public string Value { get { return _val; } set { _val = value ?? ""; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            var f = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int ch = TextRenderer.MeasureText("Ag", Theme.F(9f)).Height;
            TextRenderer.DrawText(g, _cap, Theme.F(9f), new Rectangle(0, Theme.D(1), Width, ch), Theme.Dim, f);
            TextRenderer.DrawText(g, _val, Theme.Semi(11f), new Rectangle(0, ch + Theme.D(2), Width, Height - ch - Theme.D(2)), ValueColor, f);
        }
    }

    // Vertical space inside a card stack.
    public sealed class Spacer : Control { public Spacer(int h) { BackColor = Theme.Card; Height = Theme.D(h); TabStop = false; } }

    // A tooltip that dies with its control. A bare "new ToolTip()" hooks the form and keeps every disposed control
    // (and after a language switch, every old page) alive, plus one native window each.
    public static class Tips
    {
        public static ToolTip Set(Control c, string text)
        {
            var t = new ToolTip();
            t.SetToolTip(c, text);
            c.Disposed += (s, e) => { try { t.Dispose(); } catch { } };
            return t;
        }
        public static ToolTip Owned(Control c) { var t = new ToolTip(); c.Disposed += (s, e) => { try { t.Dispose(); } catch { } }; return t; }
    }

    public abstract class Page : Panel
    {
        // WS_EX_COMPOSITED: the whole child tree is buffered and painted in one shot, so switching pages
        // never shows the "controls first, image after" flash. Cheap for these pages.
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; } }

        protected readonly Flow Body;
        private readonly Panel _clip, _tail;
        // > 0: content is a centered column at most this wide (unscaled px); 0 = full width (grids, browsers).
        protected int ColumnMax;
        private static int PadL { get { return Theme.D(26); } }
        private static int PadR { get { return Theme.D(18); } }

        public Page()
        {
            Dock = DockStyle.Fill; BackColor = Theme.Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            // The page scrolls natively (wheel, keyboard, ScrollToControl), but the classic bar is pushed outside a
            // clipping panel; a thin rounded bar drawn by grobyq sits on the right instead.
            _clip = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Body = new Flow { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(PadL, Theme.D(12), PadR, 0) };
            // bottom space as a real control: scroll extents always end 28 px under the last card (Padding.Bottom
            // is not counted the same way by every WinForms implementation)
            _tail = new Panel { Height = Theme.D(18), BackColor = Theme.Bg, Margin = Padding.Empty };
            Body.Controls.Add(_tail);
            _clip.Controls.Add(Body);
            _clip.Resize += (s, e) => SizeBody();
            var bar = new ThinScrollBar(Body) { Dock = DockStyle.Right };
            Controls.Add(_clip);
            Controls.Add(bar);
            Body.Resize += (s, e) => FitWidths();
            SizeBody();
        }

        private void SizeBody()
        {
            Body.SetBounds(0, 0, _clip.Width + SystemInformation.VerticalScrollBarWidth, Math.Max(1, _clip.Height));
        }

        private bool _fitting;
        protected void FitWidths()
        {
            if (_fitting) return;
            _fitting = true;
            try
            {
                // visible width = Body minus the native bar pushed outside the clip; always leave that room so the
                // content never reflows when the bar appears
                int vis = Body.Width - SystemInformation.VerticalScrollBarWidth - Theme.D(2);
                int padL = PadL, padR = PadR, w = vis - padL - padR;
                if (ColumnMax > 0 && w > Theme.D(ColumnMax))
                {
                    int extra = w - Theme.D(ColumnMax);
                    padL += extra / 2; padR += extra - extra / 2; w = Theme.D(ColumnMax);
                }
                if (Body.Padding.Left != padL || Body.Padding.Right != padR)
                    Body.Padding = new Padding(padL, Body.Padding.Top, padR, Body.Padding.Bottom);
                foreach (Control c in Body.Controls)
                {
                    int cw = Math.Max(200, w - c.Margin.Horizontal);
                    if (c.Width != cw) c.Width = cw;
                }
            }
            finally { _fitting = false; }
        }

        private void Tail() { Body.Controls.SetChildIndex(_tail, Body.Controls.Count - 1); }

        // After a language switch: widths, wrapped text and the scroll extent measured again, back to the top.
        // The scroll extent follows the content (no viewport-tall void under the last card).
        public void Relayout()
        {
            FitWidths();
            Theme.Reflow(Body);
            Body.PerformLayout();
            ScrollTo(0);
        }

        protected Label Title(string text)
        {
            var l = new Label { Text = text, Font = Theme.Semi(13f), ForeColor = Theme.Fg, BackColor = Theme.Bg, AutoSize = false, Height = Theme.D(30), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, AutoEllipsis = true };
            l.Margin = new Padding(0, Theme.D(4), 0, Theme.D(6));
            Register(l);
            Body.Controls.Add(l); Tail(); FitWidths();
            return l;
        }

        // Quiet section heading + large-radius card (Home / About / Threads column pages).
        protected Label Heading(string text, bool first)
        {
            var l = new Label { Text = text, Font = Theme.Semi(12f), ForeColor = Theme.Fg, BackColor = Theme.Bg, AutoSize = false, Height = Theme.D(26), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, AutoEllipsis = true };
            Add(l, 6).Margin = new Padding(0, first ? Theme.D(2) : Theme.D(12), 0, Theme.D(6));
            return l;
        }

        protected Card Box(int bottom = 8)
        {
            return Add(new Card { Radius = 12, Padding = new Padding(Theme.D(18), Theme.D(8), Theme.D(18), Theme.D(8)) }, bottom);
        }

        // Button caption confirms the action for a moment, then returns.
        private static readonly HashSet<Btn> _flashing = new HashSet<Btn>();
        public static void Flash(Btn b, string text)
        {
            if (!_flashing.Add(b)) return;               // a second click while flashing must not keep "Copied" forever
            string was = b.Text;
            b.Text = text;
            var t = new Timer { Interval = 1400 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); _flashing.Remove(b); if (!b.IsDisposed) b.Text = was; };
            t.Start();
        }

        protected T Add<T>(T c, int bottom = 10) where T : Control
        {
            c.Margin = new Padding(0, 0, 0, Theme.D(bottom));
            Register(c);
            Body.Controls.Add(c); Tail(); FitWidths();
            return c;
        }

        // ---- sections: a tab strip at the top shows one block of the page at a time ----
        protected Tabs PageTabs;
        private string _section;
        private readonly Dictionary<Control, string> _secOf = new Dictionary<Control, string>();

        protected Tabs AddTabs()
        {
            PageTabs = new Tabs();
            Add(PageTabs, 10);
            PageTabs.Changed += k => ShowSection(k);
            return PageTabs;
        }

        protected void Section(string key, string title)
        {
            _section = key;
            if (PageTabs != null) PageTabs.AddTab(key, title);
        }

        private void Register(Control c)
        {
            if (_group != null && c != _group)
            {
                _group.Members.Add(c);
                if (!_group.Open) c.Visible = false;
            }
            if (_section == null || c == PageTabs) return;
            _secOf[c] = _section;
            if (PageTabs != null && PageTabs.Selected != _section) c.Visible = false;
        }

        // ---- collapsible groups: everything added after Group(...) until EndGroup() hides with the header ----
        private Expander _group;
        private readonly List<Expander> _groups = new List<Expander>();
        protected Expander Group(string icon, string title, string sub, bool open = false)
        {
            _group = null;
            var x = new Expander(icon, title, sub) { Open = open };
            Add(x, 12);
            _groups.Add(x);
            x.Toggled += () => ShowGroup(x);
            _group = x;
            return x;
        }
        protected void EndGroup() { _group = null; }

        private bool InClosedGroup(Control c)
        {
            foreach (var g in _groups) if (!g.Open && g.Members.Contains(c)) return true;
            return false;
        }

        private void ShowGroup(Expander x)
        {
            Body.SuspendLayout();
            string sec = PageTabs == null ? null : PageTabs.Selected;
            foreach (var c in x.Members)
            {
                string cs;
                bool inSec = sec == null || !_secOf.TryGetValue(c, out cs) || cs == sec;
                c.Visible = x.Open && inSec;
            }
            Body.ResumeLayout(true);
            FitWidths();
            if (x.Open) { foreach (var c in x.Members) Theme.Reflow(c); Body.PerformLayout(); }
        }

        public string CurrentSection { get { return PageTabs == null ? null : PageTabs.Selected; } }

        public void ShowSection(string key)
        {
            if (PageTabs == null) return;
            if (PageTabs.Selected != key) { PageTabs.Select(key); return; }   // Select raises Changed -> back here
            Body.SuspendLayout();
            foreach (var kv in _secOf) kv.Key.Visible = kv.Value == key && !InClosedGroup(kv.Key);
            Body.ResumeLayout(true);
            FitWidths();
            ScrollTo(0);
            OnSection(key);
        }

        protected virtual void OnSection(string key) { }

        protected Card NewCard() { return Add(new Card()); }

        public void ScrollTo(int y) { if (y <= 0) Body.AutoScrollPosition = Point.Empty; else ThinScrollBar.SetV(Body, y); }

        // Scroll so that a control sits near the top of the page (after the current layout pass).
        protected void ScrollIntoView(Control c)
        {
            if (c == null || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (c.IsDisposed || !c.Visible) return;
                var pt = Body.PointToClient(c.PointToScreen(Point.Empty));
                ScrollTo(Math.Max(0, Body.VerticalScroll.Value + pt.Y - Theme.D(90)));
            }));
        }

        // true when any part of the control is inside the visible part of the page
        protected bool OnScreen(Control c)
        {
            if (c == null || !c.Visible || !Body.IsHandleCreated) return false;
            var r = Body.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            return r.Bottom > 0 && r.Top < Body.ClientSize.Height;
        }
#if DEMO
        private static void Collect<T>(Control c, List<T> into) where T : Control
        {
            foreach (Control ch in c.Controls) { if (!ch.Visible) continue; var t = ch as T; if (t != null) into.Add(t); Collect(ch, into); }
        }
        private void Reveal(Control c)
        {
            var pt = Body.PointToClient(c.PointToScreen(Point.Empty));
            ScrollTo(Math.Max(0, Body.VerticalScroll.Value + pt.Y - Theme.D(150)));
        }
        // screenshot helpers: open the n-th drop-down / a grid cell list of this page
        public void DemoOpen(int n)
        {
            var l = new List<DropBox>(); Collect(Body, l);
            if (n < 0 || n >= l.Count) return;
            Reveal(l[n]);
            var d = l[n]; BeginInvoke((Action)(() => d.Open()));
        }
        public void DemoCell(int g, int row, int col)
        {
            var l = new List<DarkGrid>(); Collect(Body, l);
            if (g < 0 || g >= l.Count) return;
            var dg = l[g]; Reveal(dg);
            BeginInvoke((Action)(() => { try { dg.CurrentCell = dg.Rows[row].Cells[col]; } catch { } dg.OpenCell(row, col); }));
        }
        public virtual void DemoExpand(int n) { }

        public string DebugDump()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("VS max " + Body.VerticalScroll.Maximum + " lc " + Body.VerticalScroll.LargeChange + " val " + Body.VerticalScroll.Value + " vis " + Body.VerticalScroll.Visible + " HS vis " + Body.HorizontalScroll.Visible + " max " + Body.HorizontalScroll.Maximum);
            sb.AppendLine("Body " + Body.Bounds + " client " + Body.ClientSize + " disp " + Body.DisplayRectangle + " pad " + Body.Padding + " pos " + Body.AutoScrollPosition + " minsize " + Body.AutoScrollMinSize);
            foreach (Control c in Body.Controls) sb.AppendLine("  " + c.GetType().Name + " " + c.Bounds + " m " + c.Margin + " vis " + c.Visible);
            Action<Control> grids = null;
            grids = c => { foreach (Control ch in c.Controls) { var g = ch as DataGridView; if (g != null) sb.AppendLine("  GRID rows " + g.Rows.Count + " first " + g.FirstDisplayedScrollingRowIndex + " cur " + (g.CurrentCell == null ? "-" : g.CurrentCell.RowIndex + "," + g.CurrentCell.ColumnIndex) + " edit " + g.IsCurrentCellInEditMode + " focus " + g.ContainsFocus + " h " + g.Height); grids(ch); } };
            grids(Body);
            sb.AppendLine("  ACTIVE " + (FindForm() != null && FindForm().ActiveControl != null ? FindForm().ActiveControl.GetType().Name : "-"));
            return sb.ToString();
        }
#endif

        public virtual void OnEnter() { }
        public virtual void OnLeave() { }
        public virtual void OnTick(int tick) { }
    }

    // ------------------------------------------------------------------ thin scrollbar
    // A thin rounded vertical bar that mirrors a ScrollableControl's AutoScroll state (drag, click, wheel).
    // Track = canvas, thumb #2A2A32 -> hover #3A3A44 -> drag #4A4A55, eased over ~140 ms.
    // Dragging only moves the scroll origin (ScrollWindowEx inside WinForms): no layout pass per mouse move.
    public sealed class ThinScrollBar : Control, IWheel
    {
        private readonly ScrollableControl _t;
        private readonly Timer _poll, _anim;
        private bool _hot, _drag;
        private int _dragY, _dragPos, _sig;
        private float _h, _d;                   // eased hover / drag amounts 0..1
        private DateTime _last = DateTime.UtcNow;
        private const float EaseMs = 140f;

        public ThinScrollBar(ScrollableControl target)
        {
            _t = target;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Width = Theme.D(12);
            _t.Scroll += (s, e) => Invalidate();
            _t.Layout += (s, e) => Invalidate();
            _poll = new Timer { Interval = 120 };
            // a hidden page never tells its children (WinForms skips invisible children): stop here, VisibleChanged restarts
            _poll.Tick += (s, e) => { if (!Visible) { _poll.Stop(); return; } int sig = Total * 31 + View * 7 + Pos; if (sig != _sig) { _sig = sig; Invalidate(); } };
            _anim = new Timer { Interval = 15 };
            _anim.Tick += (s, e) => Step();
            VisibleChanged += (s, e) => { if (Visible) _poll.Start(); else _poll.Stop(); };
            _poll.Start();
        }

        // The native scroll range (content incl. padding), not DisplayRectangle (that one is deflated by Padding,
        // so the thumb could never reach the last pixels).
        private int Total { get { return _t.VerticalScroll.Visible ? _t.VerticalScroll.Maximum + 1 : View; } }
        private int View { get { return Math.Max(1, _t.VerticalScroll.Visible ? _t.VerticalScroll.LargeChange : _t.ClientSize.Height); } }
        private int Pos { get { return _t.VerticalScroll.Visible ? _t.VerticalScroll.Value : 0; } }
        private int Max { get { return Math.Max(0, Total - View); } }

        public static void SetV(ScrollableControl t, int pos)
        {
            var vs = t.VerticalScroll;
            if (!vs.Visible) return;
            int max = Math.Max(0, vs.Maximum - vs.LargeChange + 1);
            pos = Math.Max(0, Math.Min(max, pos));
            if (vs.Value != pos) vs.Value = pos;
        }

        private Rectangle Thumb()
        {
            if (Max <= 0 || Height < 10) return Rectangle.Empty;
            int track = Height - Theme.D(8);
            int h = Math.Max(Theme.D(32), (int)(track * (View / (double)Total)));
            int y = Theme.D(4) + (int)((track - h) * (Pos / (double)Max));
            return new Rectangle(0, y, Width, h);
        }

        private void ScrollTo(int pos)
        {
            pos = Math.Max(0, Math.Min(Max, pos));
            if (pos == Pos) return;
            SetV(_t, pos);
            Invalidate(); Update();             // the thumb follows the pointer in the same frame
        }

        private void Kick() { _last = DateTime.UtcNow; if (!_anim.Enabled) _anim.Start(); }
        private void Step()
        {
            var now = DateTime.UtcNow;
            float dt = (float)(now - _last).TotalMilliseconds / EaseMs; _last = now;
            float th = (_hot || _drag) ? 1f : 0f, td = _drag ? 1f : 0f;
            _h = th > _h ? Math.Min(th, _h + dt) : Math.Max(th, _h - dt);
            _d = td > _d ? Math.Min(td, _d + dt) : Math.Max(td, _d - dt);
            if (_h == th && _d == td) _anim.Stop();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Kick(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Kick(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            var th = Thumb();
            if (th.IsEmpty) return;
            if (th.Contains(e.Location)) { _drag = true; _dragY = e.Y; _dragPos = Pos; Capture = true; Kick(); }
            else ScrollTo(Pos + (e.Y < th.Top ? -View : View));
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag)
            {
                var th = Thumb();
                int track = Height - Theme.D(8) - th.Height;
                if (track > 0) ScrollTo(_dragPos + (int)((e.Y - _dragY) * (Max / (double)track)));
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; Kick(); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollTo(Pos - e.Delta / 120 * Theme.D(60)); base.OnMouseWheel(e); }
        public bool Wheel(int delta) { if (Max <= 0) return false; Smooth.By(_t, -delta * Theme.K); return true; }

        // Shared look for every thin bar in the app (pages, log, license viewer).
        public static void PaintThumb(Graphics g, RectangleF r, float hover, float drag)
        {
            var c = Theme.Mix(Theme.Mix(Theme.Edge, Theme.EdgeHi, hover), Theme.EdgeDn, drag);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(r.Width, r.Height) / 2f;
            using (var path = Theme.Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            var th = Thumb();
            if (th.IsEmpty) return;
            float w = Theme.Df(4) + Theme.Df(2) * _h;
            PaintThumb(g, new RectangleF((Width - w) / 2f, th.Y, w, th.Height), _h, _d);
        }

        protected override void Dispose(bool disposing) { if (disposing) { _poll.Dispose(); _anim.Dispose(); } base.Dispose(disposing); }
    }

    // ------------------------------------------------------------------ log view
    // Read-only monospace readout: [SECTION] headers, muted keys / bright values ("  key<padded> value"),
    // the current section pinned at the top while scrolling, thin rounded scrollbars on both axes.
    // Plain mode (PlainColor set) = the app log: one colour, follows the tail while you are at the bottom.
    public sealed class LogView : Control, IWheel
    {
        private string[] _lines = new string[0];
        private int _top, _left, _charW, _lineH, _dragAxis, _hotAxis, _dragFrom, _dragStart, _contentW;
        private float _hv, _hh, _dv, _dh;
        private readonly Timer _anim = new Timer { Interval = 15 };
        private DateTime _last = DateTime.UtcNow;
        public string Placeholder = "";
        public Color? PlainColor;
        public const int KeyCol = 2, ValueCol = 35;

        public LogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.LogBg; Font = Theme.Mono(9f); Height = Theme.D(300); TabStop = true;
            _anim.Tick += (s, e) => Step();
            Measure();
        }

        public override string Text
        {
            get { return string.Join("\r\n", _lines); }
            set { SetLines((value ?? "").Replace("\r\n", "\n").Split('\n'), false); _top = 0; _left = 0; Invalidate(); }
        }

        // Replace the lines; follow = jump to the end, otherwise stay at the end only if the view was there.
        public void SetLines(IList<string> lines, bool follow)
        {
            bool atEnd = _top >= MaxTop - _lineH;
            _lines = lines == null ? new string[0] : lines.ToArray();
            if (_lines.Length == 1 && _lines[0].Length == 0) _lines = new string[0];
            int m = 0; foreach (var l in _lines) m = Math.Max(m, l.Length);
            _contentW = m * _charW + Pad * 2;
            if (follow || atEnd) _top = MaxTop;
            _top = Math.Min(_top, MaxTop); _left = Math.Min(_left, MaxLeft);
            Invalidate();
        }

        private void Measure()
        {
            var sz = TextRenderer.MeasureText("MMMMMMMMMM", Font, new Size(1000, 100), TextFormatFlags.NoPadding);
            _charW = Math.Max(1, sz.Width / 10);
            _lineH = sz.Height + Theme.D(5);
        }
        protected override void OnFontChanged(EventArgs e) { Measure(); SetLines(_lines, false); base.OnFontChanged(e); }

        private int Pad { get { return Theme.D(12); } }
        private int Bar { get { return Theme.D(6); } }
        private int ContentH { get { return _lines.Length * _lineH + Pad * 2; } }
        private int ContentW { get { return _contentW; } }
        private int MaxTop { get { return Math.Max(0, ContentH - Height); } }
        private int MaxLeft { get { return Math.Max(0, ContentW - Width); } }

        private static bool IsHeader(string l) { string t = l.Trim(); return t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']'; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = e.Delta / 120 * _lineH * 3;
            if ((ModifierKeys & Keys.Shift) != 0) { _left = Math.Max(0, Math.Min(MaxLeft, _left - step)); Invalidate(); }
            else Wheel(e.Delta);
            if (e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true;   // do not scroll the page too
        }

        // wheel router: scroll the log; at the top / bottom the wheel goes on to the page
        public bool Wheel(int delta)
        {
            int was = _top;
            _top = Math.Max(0, Math.Min(MaxTop, _top - delta / 120 * _lineH * 3));
            if (_top == was) return false;
            Invalidate();
            return true;
        }

        private Rectangle VThumb()
        {
            if (MaxTop <= 0) return Rectangle.Empty;
            int track = Height - Bar * 2;
            int h = Math.Max(Theme.D(28), (int)(track * (Height / (double)ContentH)));
            return new Rectangle(Width - Bar - Theme.D(3), Bar + (int)((track - h) * (_top / (double)MaxTop)), Bar, h);
        }
        private Rectangle HThumb()
        {
            if (MaxLeft <= 0) return Rectangle.Empty;
            int track = Width - Bar * 3;
            int w = Math.Max(Theme.D(28), (int)(track * (Width / (double)ContentW)));
            return new Rectangle(Bar + (int)((track - w) * (_left / (double)MaxLeft)), Height - Bar - Theme.D(3), w, Bar);
        }

        private void Kick() { _last = DateTime.UtcNow; if (!_anim.Enabled) _anim.Start(); }
        private void Step()
        {
            var now = DateTime.UtcNow; float dt = (float)(now - _last).TotalMilliseconds / 140f; _last = now;
            Func<float, float, float> go = (v, t) => t > v ? Math.Min(t, v + dt) : Math.Max(t, v - dt);
            float thv = _hotAxis == 1 || _dragAxis == 1 ? 1 : 0, thh = _hotAxis == 2 || _dragAxis == 2 ? 1 : 0;
            _hv = go(_hv, thv); _hh = go(_hh, thh); _dv = go(_dv, _dragAxis == 1 ? 1 : 0); _dh = go(_dh, _dragAxis == 2 ? 1 : 0);
            if (_hv == thv && _hh == thh && _dv == (_dragAxis == 1 ? 1 : 0) && _dh == (_dragAxis == 2 ? 1 : 0)) _anim.Stop();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var v = VThumb(); var h = HThumb();
            if (!v.IsEmpty && v.Contains(e.Location)) { _dragAxis = 1; _dragFrom = e.Y; _dragStart = _top; Capture = true; Kick(); }
            else if (!h.IsEmpty && h.Contains(e.Location)) { _dragAxis = 2; _dragFrom = e.X; _dragStart = _left; Capture = true; Kick(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragAxis == 1) { var v = VThumb(); int tr = Height - Bar * 2 - v.Height; if (tr > 0) _top = Math.Max(0, Math.Min(MaxTop, _dragStart + (int)((e.Y - _dragFrom) * (MaxTop / (double)tr)))); Invalidate(); Update(); }
            else if (_dragAxis == 2) { var h = HThumb(); int tr = Width - Bar * 3 - h.Width; if (tr > 0) _left = Math.Max(0, Math.Min(MaxLeft, _dragStart + (int)((e.X - _dragFrom) * (MaxLeft / (double)tr)))); Invalidate(); Update(); }
            else
            {
                var vv = VThumb(); var hh = HThumb();
                int hot = !vv.IsEmpty && e.X >= Width - Theme.D(16) ? 1 : !hh.IsEmpty && e.Y >= Height - Theme.D(16) ? 2 : 0;
                if (hot != _hotAxis) { _hotAxis = hot; Kick(); }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (_hotAxis != 0) { _hotAxis = 0; Kick(); } base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _dragAxis = 0; Capture = false; Kick(); base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int d = e.KeyCode == Keys.Up ? -_lineH : e.KeyCode == Keys.Down ? _lineH : e.KeyCode == Keys.PageUp ? -Height : e.KeyCode == Keys.PageDown ? Height : 0;
            if (e.KeyCode == Keys.Home) _top = 0; else if (e.KeyCode == Keys.End) _top = MaxTop; else _top = Math.Max(0, Math.Min(MaxTop, _top + d));
            Invalidate(); base.OnKeyDown(e);
        }
        protected override void OnResize(EventArgs e) { _top = Math.Min(_top, MaxTop); _left = Math.Min(_left, MaxLeft); base.OnResize(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (_lines.Length == 0)
            {
                TextRenderer.DrawText(g, Placeholder, Theme.F(9.5f), new Rectangle(Pad, Pad, Width - Pad * 2, _lineH * 2), Theme.Faint, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            Color hdr = Color.FromArgb(176, 180, 192), key = Theme.Dim, val = Theme.Fg;
            int first = Math.Max(0, (_top - Pad) / _lineH);
            int x0 = Pad - _left;
            for (int i = first; i < _lines.Length; i++)
            {
                int y = Pad + i * _lineH - _top;
                if (y > Height) break;
                if (PlainColor.HasValue) TextRenderer.DrawText(g, _lines[i], Font, new Point(x0, y), PlainColor.Value, fl);
                else DrawLine(g, _lines[i], x0, y, hdr, key, val, fl);
            }
            // pinned section: the header of the section at the top of the viewport
            int topLine = Math.Max(0, (_top - Pad + _lineH / 2) / _lineH);
            if (!PlainColor.HasValue && _top > Pad && topLine < _lines.Length && !IsHeader(_lines[topLine]))
            {
                string h = null;
                for (int i = topLine; i >= 0; i--) if (IsHeader(_lines[i])) { h = _lines[i]; break; }
                if (h != null)
                {
                    using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, 0, 0, Width, _lineH + Theme.D(6));
                    using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, _lineH + Theme.D(6), Width, _lineH + Theme.D(6));
                    TextRenderer.DrawText(g, h.Trim(), Font, new Point(x0, Theme.D(4)), hdr, fl);
                }
            }
            var vt = VThumb(); var ht = HThumb();
            if (!vt.IsEmpty) ThinScrollBar.PaintThumb(g, vt, _hv, _dv);
            if (!ht.IsEmpty) ThinScrollBar.PaintThumb(g, ht, _hh, _dh);
        }

        private void DrawLine(Graphics g, string l, int x0, int y, Color hdr, Color key, Color val, TextFormatFlags fl)
        {
            if (IsHeader(l)) { TextRenderer.DrawText(g, l.Trim(), Font, new Point(x0, y), hdr, fl); return; }
            if (l.Length > ValueCol && l.StartsWith("  "))
            {
                TextRenderer.DrawText(g, l.Substring(0, ValueCol).TrimEnd(), Font, new Point(x0, y), key, fl);
                TextRenderer.DrawText(g, l.Substring(ValueCol), Font, new Point(x0 + ValueCol * _charW, y), val, fl);
                return;
            }
            TextRenderer.DrawText(g, l, Font, new Point(x0, y), val, fl);
        }

        protected override void Dispose(bool disposing) { if (disposing) _anim.Dispose(); base.Dispose(disposing); }
    }

    // ------------------------------------------------------------------ side list + two panes
    // A vertical list of groups (title + small second line), used as the left pane of a two-pane browser.
    public sealed class SideList : Control
    {
        public sealed class Item { public string Key, Text, Sub; }
        public readonly List<Item> Items = new List<Item>();
        public string Selected { get; private set; }
        public event Action<string> Changed;
        private int _hot = -1;
        private int H { get { return Theme.D(46); } }

        public SideList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card; Font = Theme.F(9.5f); TabStop = true; Cursor = Cursors.Hand; Height = H;
        }

        public void SetItems(IEnumerable<Item> items, string keep)
        {
            Items.Clear(); Items.AddRange(items);
            Selected = Items.Any(i => i.Key == keep) ? keep : Items.Count > 0 ? Items[0].Key : null;
            Height = Math.Max(H, Items.Count * H);
            Invalidate();
        }

        public void Select(string key)
        {
            if (key == null || Selected == key || !Items.Any(i => i.Key == key)) return;
            Selected = key; Invalidate();
            var h = Changed; if (h != null) h(key);
        }

        public void Step(int delta)
        {
            int i = Items.FindIndex(x => x.Key == Selected);
            int j = Math.Max(0, Math.Min(Items.Count - 1, i + delta));
            if (j >= 0 && j < Items.Count) Select(Items[j].Key);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Up) Step(-1); if (e.KeyCode == Keys.Down) Step(1); base.OnKeyDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { int i = e.Y / H; if (i != _hot) { _hot = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int i = e.Y / H; if (i >= 0 && i < Items.Count) Select(Items[i].Key); Focus(); base.OnMouseDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                var r = new Rectangle(0, i * H, Width, H);
                bool sel = it.Key == Selected;
                if (sel || i == _hot)
                    using (var path = Theme.Round(new RectangleF(r.X + 1, r.Y + Theme.D(2), r.Width - 2, r.Height - Theme.D(4)), Theme.Df(6)))
                    using (var b = new SolidBrush(sel ? Theme.CardHi : Color.FromArgb(24, 24, 31)))
                        g.FillPath(b, path);
                if (sel)
                    using (var path = Theme.Round(new RectangleF(r.X + Theme.D(3), r.Y + H / 2f - Theme.D(9), Theme.D(3), Theme.D(18)), Theme.Df(1.5f)))
                    using (var b = new SolidBrush(Theme.Acc))
                        g.FillPath(b, path);
                var fl = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(g, it.Text, sel ? Theme.Semi(9.5f) : Font, new Rectangle(r.X + Theme.D(14), r.Y + Theme.D(5), r.Width - Theme.D(20), Theme.D(20)), sel ? Color.White : Theme.Fg, fl);
                if (!string.IsNullOrEmpty(it.Sub))
                    TextRenderer.DrawText(g, it.Sub, Theme.F(8.5f), new Rectangle(r.X + Theme.D(14), r.Y + Theme.D(24), r.Width - Theme.D(20), Theme.D(18)), Theme.Dim, fl);
            }
        }
    }

    // Left pane (fixed width) + right pane (the rest); height follows the taller one.
    public sealed class TwoPane : Panel
    {
        private readonly Control _l, _r;
        private readonly int _lw;
        public TwoPane(Control left, Control right, int leftWidth)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; _l = left; _r = right; _lw = leftWidth;
            Controls.Add(left); Controls.Add(right);
            left.SizeChanged += (s, e) => PerformLayout();
            right.SizeChanged += (s, e) => PerformLayout();
            Height = Theme.D(200);
        }
        private int Gap { get { return Theme.D(20); } }
        private int LeftW { get { return Math.Min(_lw, Math.Max(Theme.D(170), Width / 3)); } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int lw = LeftW;
            if (_l.Width != lw || _l.Left != 0) _l.SetBounds(0, 0, lw, _l.Height);
            int rw = Math.Max(Theme.D(200), Width - lw - Gap);
            if (_r.Width != rw || _r.Left != lw + Gap) _r.SetBounds(lw + Gap, 0, rw, _r.Height);
            int h = Math.Max(_l.Height, _r.Height);
            if (Height != h) Height = h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int x = LeftW + Gap / 2;
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, x, 0, x, Height);
        }
    }

    // ------------------------------------------------------------------ tabs
    // Text tabs with an accent underline; the page shows the block of the selected tab.
    public sealed class Tabs : Control
    {
        private readonly List<KeyValuePair<string, string>> _tabs = new List<KeyValuePair<string, string>>();
        private readonly List<Rectangle> _rects = new List<Rectangle>();
        private int _hot = -1;
        public string Selected { get; private set; }
        public event Action<string> Changed;

        public Tabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Height = Theme.D(42); Font = Theme.Semi(10f); Cursor = Cursors.Hand;
        }

        public void AddTab(string key, string title)
        {
            _tabs.Add(new KeyValuePair<string, string>(key, title));
            if (Selected == null) Selected = key;
            Invalidate();
        }

        public void Select(string key)
        {
            if (!_tabs.Any(t => t.Key == key) || Selected == key) return;
            Selected = key; Invalidate();
            var h = Changed; if (h != null) h(key);
        }

        private void Measure()
        {
            _rects.Clear();
            int x = 0;
            foreach (var t in _tabs)
            {
                int w = TextRenderer.MeasureText(t.Value, Font).Width + Theme.D(28);
                _rects.Add(new Rectangle(x, 0, w, Height));
                x += w + Theme.D(4);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = _rects.FindIndex(r => r.Contains(e.Location));
            if (h != _hot) { _hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = _rects.FindIndex(r => r.Contains(e.Location));
            if (i >= 0) Select(_tabs[i].Key);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Measure();
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < _tabs.Count; i++)
            {
                var r = _rects[i];
                bool sel = _tabs[i].Key == Selected;
                if (i == _hot && !sel)
                    using (var path = Theme.Round(new RectangleF(r.X + 1, r.Y + Theme.D(4), r.Width - 2, r.Height - Theme.D(10)), Theme.Df(6)))
                    using (var b = new SolidBrush(Theme.CardHi)) g.FillPath(b, path);
                TextRenderer.DrawText(g, _tabs[i].Value, Font, new Rectangle(r.X, 0, r.Width, r.Height - Theme.D(4)), sel ? Color.White : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (sel)
                    using (var path = Theme.Round(new RectangleF(r.X + Theme.D(10), Height - Theme.D(3), r.Width - Theme.D(20), Theme.D(3)), Theme.Df(1.5f)))
                    using (var b = new SolidBrush(Theme.Acc)) g.FillPath(b, path);
            }
        }
    }

    // ------------------------------------------------------------------ CPU map
    // One tile per logical processor, grouped by physical core and L3 domain.
    // Accent = game mask, amber outline = background mask, E = efficiency core.
    public sealed class CpuMap : Control
    {
        public Topo Topo;
        public ulong Game, Bgm;
        public ulong Highlight;       // e.g. interrupt target preview
        public ulong SplitGame, SplitNon;   // game / non-game set of the CCD split (0 = no split)
        public ulong Reserved;        // ReservedCpuSets (0 = none)
        public bool Pickable;         // click a tile to toggle it in Highlight (interrupt target picker)
        public event Action<ulong> Picked;
        private readonly List<KeyValuePair<RectangleF, int>> _hit = new List<KeyValuePair<RectangleF, int>>();
        public static readonly Color Target = Color.FromArgb(200, 120, 255);
        public static readonly Color NonGameC = Color.FromArgb(52, 190, 170);
        public CpuMap()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Height = Theme.D(150);
        }

        public void Set(Topo t, ulong game, ulong bg) { Topo = t; Game = game; Bgm = bg; Relayout(); Invalidate(); }

        // split / reservation overlay; call after Set (or alone, the masks stay)
        public void Overlay(ulong splitGame, ulong splitNon, ulong reserved)
        {
            SplitGame = splitGame; SplitNon = splitNon; Reserved = reserved;
            Relayout(); Invalidate();
        }

        private void Relayout()
        {
            if (Topo == null) return;
            int need = Measure(Width);
            if (need > 0 && Height != need) Height = need;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Pickable) return;
            foreach (var kv in _hit)
                if (kv.Key.Contains(e.Location))
                {
                    Highlight ^= (ulong)1 << kv.Value;
                    Invalidate();
                    var h = Picked; if (h != null) h(Highlight);
                    return;
                }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Pickable) { Cursor = Cursors.Default; return; }
            bool over = _hit.Any(kv => kv.Key.Contains(e.Location));
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        private int Tile { get { return Theme.D(30); } }
        private int Strip { get { return SplitGame != 0 || SplitNon != 0 ? Theme.D(4) : 0; } }

        // lays out domains in rows; returns total height (tiles + legend lines)
        private int Measure(int width)
        {
            if (Topo == null || width < 50) return 0;
            int y = 0;
            foreach (var dom in Domains())
            {
                int x = 0;
                y += Theme.D(20);
                foreach (var core in dom.Value)
                {
                    int cw = core.Count * Tile + (core.Count - 1) * Theme.D(3) + Theme.D(8);
                    if (x + cw > width) { x = 0; y += Tile + Strip + Theme.D(14); }
                    x += cw + Theme.D(8);
                }
                y += Tile + Strip + Theme.D(22);
            }
            int lines = 1, lx = 0;
            foreach (var it in Legend())
            {
                int w = Theme.D(16) + TextRenderer.MeasureText(it.Text, Theme.F(8.5f)).Width + Theme.D(18);
                if (lx > 0 && lx + w > width) { lines++; lx = 0; }
                lx += w;
            }
            return y + lines * Theme.D(20) + Theme.D(6);
        }

        private List<KeyValuePair<byte, List<List<Native.CpuSet>>>> Domains()
        {
            var res = new List<KeyValuePair<byte, List<List<Native.CpuSet>>>>();
            foreach (var llc in Topo.LLCGroups)
            {
                var cores = Topo.Sets.Where(s => s.LLC == llc).GroupBy(s => s.Core).OrderBy(g => g.Min(s => s.LP))
                                     .Select(g => g.OrderBy(s => s.LP).ToList()).ToList();
                res.Add(new KeyValuePair<byte, List<List<Native.CpuSet>>>(llc, cores));
            }
            return res;
        }

        private enum Look { Fill, Outline, Strip, Hatch, Text }
        private sealed class Leg { public Color C; public Look L; public string Text; }

        private List<Leg> Legend()
        {
            var l = new List<Leg>();
            l.Add(new Leg { C = Theme.Acc, L = Look.Fill, Text = L.T("game mask") });
            l.Add(new Leg { C = Theme.Warn, L = Look.Outline, Text = L.T("background") });
            l.Add(new Leg { C = Theme.Input, L = Look.Fill, Text = L.T("unused") });
            if (SplitGame != 0) l.Add(new Leg { C = Theme.Acc, L = Look.Strip, Text = L.T("game set") });
            if (SplitNon != 0) l.Add(new Leg { C = NonGameC, L = Look.Strip, Text = L.T("non-game set") });
            if (Reserved != 0) l.Add(new Leg { C = Theme.Warn, L = Look.Hatch, Text = L.T("reserved (ReservedCpuSets)") });
            if (Highlight != 0 || Pickable) l.Add(new Leg { C = Target, L = Look.Outline, Text = Pickable ? L.T("interrupt target (click to toggle)") : L.T("interrupt target") });
            if (Topo != null && Topo.Hybrid) l.Add(new Leg { L = Look.Text, Text = L.T("E = efficiency core") });
            return l;
        }

        private static Brush HatchFor()
        {
            return new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.Mix(Theme.Input, Theme.Warn, 0.45f), Theme.Input);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            _hit.Clear();
            if (Topo == null) { TextRenderer.DrawText(g, L.T("CPU topology unavailable"), Theme.F(9f), ClientRectangle, Theme.Dim); return; }
            byte maxEff = Topo.Sets.Max(s => s.Eff);
            byte firstCore = Topo.FirstCore();
            int y = 0, idx = 0;
            foreach (var dom in Domains())
            {
                string head = Topo.LLCGroups.Count > 1 ? L.F("L3 domain {0}", idx) + "  ·  " + Topo.L3Text(dom.Key) : "L3  ·  " + Topo.L3Text(dom.Key);
                if (Topo.VCacheLlc.HasValue && Topo.VCacheLlc.Value == dom.Key) head += "  ·  V-Cache";
                ulong dm = Masks.Mask(dom.Value.SelectMany(c => c));
                Color hc = Theme.Dim;
                if (SplitGame != 0 && (dm & ~SplitGame) == 0) { head += "  ·  " + L.T("GAME SET"); hc = Theme.Mix(Theme.Dim, Theme.Acc, 0.6f); }
                else if (SplitNon != 0 && (dm & ~SplitNon) == 0) { head += "  ·  " + L.T("NON-GAME SET"); hc = Theme.Mix(Theme.Dim, NonGameC, 0.6f); }
                TextRenderer.DrawText(g, head, Theme.Semi(8.75f), new Point(0, y), hc);
                y += Theme.D(20);
                int x = 0;
                foreach (var core in dom.Value)
                {
                    int cw = core.Count * Tile + (core.Count - 1) * Theme.D(3) + Theme.D(8);
                    if (x + cw > Width) { x = 0; y += Tile + Strip + Theme.D(14); }
                    bool eCore = Topo.Hybrid && core[0].Eff < maxEff;
                    ulong cm = Masks.Mask(core);
                    using (var path = Theme.Round(new RectangleF(x, y, cw, Tile + Theme.D(8) + Strip), Theme.Df(6)))
                    using (var b = new SolidBrush(Theme.Bg))
                    using (var p = new Pen(core[0].Core == firstCore ? Theme.Faint : Theme.Line))
                    { g.FillPath(b, path); g.DrawPath(p, path); }
                    if (Strip > 0)
                    {
                        Color sc = (cm & ~SplitGame) == 0 ? Theme.Acc : (cm & ~SplitNon) == 0 ? NonGameC : Color.Empty;
                        if (sc != Color.Empty)
                            using (var path = Theme.Round(new RectangleF(x + Theme.D(5), y + Tile + Theme.D(6), cw - Theme.D(10), Theme.D(3)), Theme.Df(1.5f)))
                            using (var b = new SolidBrush(sc)) g.FillPath(b, path);
                    }
                    int lx = x + Theme.D(4);
                    foreach (var s in core)
                    {
                        bool inGame = ((Game >> s.LP) & 1) != 0, inBg = ((Bgm >> s.LP) & 1) != 0, hi = ((Highlight >> s.LP) & 1) != 0, res = ((Reserved >> s.LP) & 1) != 0;
                        var tr = new RectangleF(lx, y + Theme.D(4), Tile, Tile);
                        _hit.Add(new KeyValuePair<RectangleF, int>(tr, s.LP));
                        using (var path = Theme.Round(tr, Theme.Df(4)))
                        {
                            if (inGame) using (var b = new SolidBrush(Theme.Acc)) g.FillPath(b, path);
                            else if (res) using (var b = HatchFor()) g.FillPath(b, path);
                            else using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                            if (inBg) using (var p = new Pen(Theme.Warn, Theme.Df(1.6f))) g.DrawPath(p, path);
                            if (hi) using (var p = new Pen(Target, Theme.Df(2.4f))) g.DrawPath(p, path);
                        }
                        TextRenderer.DrawText(g, s.LP.ToString(), Theme.Semi(8.5f), Rectangle.Round(tr), inGame ? Color.White : hi ? Target : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        if (eCore) TextRenderer.DrawText(g, "E", Theme.F(6.5f), new Point((int)tr.Right - Theme.D(9), (int)tr.Y), inGame ? Color.White : Theme.Warn);
                        lx += Tile + Theme.D(3);
                    }
                    x += cw + Theme.D(8);
                }
                y += Tile + Strip + Theme.D(22);
                idx++;
            }
            // legend, wrapped to the width
            int ly = y, lx2 = 0;
            foreach (var it in Legend())
            {
                int tw = TextRenderer.MeasureText(it.Text, Theme.F(8.5f)).Width;
                int w = (it.L == Look.Text ? 0 : Theme.D(16)) + tw + Theme.D(18);
                if (lx2 > 0 && lx2 + w > Width) { lx2 = 0; ly += Theme.D(20); }
                if (it.L != Look.Text)
                {
                    var r = new RectangleF(lx2, ly + Theme.D(3), Theme.D(12), Theme.D(12));
                    using (var path = Theme.Round(r, Theme.Df(3)))
                    {
                        switch (it.L)
                        {
                            case Look.Fill: using (var b = new SolidBrush(it.C)) g.FillPath(b, path); break;
                            case Look.Outline: using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path); using (var p = new Pen(it.C, Theme.Df(1.6f))) g.DrawPath(p, path); break;
                            case Look.Hatch: using (var b = HatchFor()) g.FillPath(b, path); break;
                            case Look.Strip:
                                using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, path);
                                using (var p2 = Theme.Round(new RectangleF(r.X + Theme.D(1), r.Bottom - Theme.D(4), r.Width - Theme.D(2), Theme.D(3)), Theme.Df(1.5f)))
                                using (var b = new SolidBrush(it.C)) g.FillPath(b, p2);
                                break;
                        }
                    }
                    TextRenderer.DrawText(g, it.Text, Theme.F(8.5f), new Point(lx2 + Theme.D(16), ly), Theme.Dim);
                }
                else TextRenderer.DrawText(g, it.Text, Theme.F(8.5f), new Point(lx2, ly), Theme.Dim);
                lx2 += w;
            }
        }
    }

    // ------------------------------------------------------------------ key/value grid
    public sealed class InfoGrid : Control
    {
        private readonly List<KeyValuePair<string, string>> _rows = new List<KeyValuePair<string, string>>();
        public int KeyWidth = 180;
        public InfoGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }
        private readonly Dictionary<int, Color> _tint = new Dictionary<int, Color>();
        public void Set(IEnumerable<KeyValuePair<string, string>> rows)
        {
            _rows.Clear(); _rows.AddRange(rows); _tint.Clear();
            Height = Math.Max(Theme.D(24), _rows.Count * Theme.D(24) + Theme.D(4));
            Invalidate();
        }
        // value colour of one row (after Set)
        public void Tint(int row, Color c) { _tint[row] = c; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            // the key column grows with the longest translated key (never past half the width)
            int kw = Theme.D(KeyWidth), i = 0, y = 0;
            foreach (var kv in _rows) kw = Math.Max(kw, TextRenderer.MeasureText(kv.Key ?? "", Theme.F(9f)).Width + Theme.D(18));
            kw = Math.Min(kw, Math.Max(Theme.D(KeyWidth), Width / 2));
            foreach (var kv in _rows)
            {
                Color vc; if (!_tint.TryGetValue(i, out vc)) vc = Theme.Fg;
                TextRenderer.DrawText(g, kv.Key, Theme.F(9f), new Rectangle(0, y, kw, Theme.D(24)), Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, kv.Value ?? "", Theme.F(9.5f), new Rectangle(kw, y, Width - kw, Theme.D(24)), vc, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += Theme.D(24); i++;
            }
        }
    }

    // ------------------------------------------------------------------ hero
    // Page head on the pitch-black canvas: no grain / star field, optional logo mark (centered or at the right).
    public sealed class Hero : Panel
    {
        public int MarkHeight = 0;          // 0 = no mark
        public int MarkTop = 0;
        public bool MarkCenter = true;

        public Hero()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Height = Theme.D(160);
        }

        public Rectangle MarkRect
        {
            get
            {
                var img = Brand.Img("hero_logo.png");
                if (img == null || MarkHeight <= 0) return Rectangle.Empty;
                int w = (int)(img.Width * (MarkHeight / (float)img.Height));
                return new Rectangle(MarkCenter ? (Width - w) / 2 : Width - w - Theme.D(8), MarkTop, w, MarkHeight);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            var r = MarkRect;
            var img = Brand.Img("hero_logo.png");
            if (!r.IsEmpty && img != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.85f });
                    g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                }
            }
        }
    }

    // ------------------------------------------------------------------ collapsible group header
    // Vain-style group row: icon tile, title + grey subtitle, chevron on the right. Click (or Enter / Space) opens
    // and closes the group; the page shows / hides every control registered under it.
    public sealed class Expander : Control
    {
        private readonly string _icon, _title, _sub;
        private bool _hot, _open;
        public event Action Toggled;
        public readonly List<Control> Members = new List<Control>();
        public Expander(string icon, string title, string sub)
        {
            _icon = icon; _title = title; _sub = sub; Text = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg; Height = Theme.D(50); Cursor = Cursors.Hand; TabStop = true;
        }
        public bool Open { get { return _open; } set { if (_open == value) return; _open = value; Invalidate(); var h = Toggled; if (h != null) h(); } }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Open = !Open; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Open = !Open; base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(12)))
            using (var b = new SolidBrush(_hot ? Theme.CardHi : Theme.Card))
            using (var p = new Pen(_open ? Theme.Acc : _hot || Focused ? Theme.Edge : Theme.Line))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            int tile = Theme.D(30), tx = Theme.D(12), ty = (Height - tile) / 2;
            using (var path = Theme.Round(new RectangleF(tx, ty, tile, tile), Theme.Df(8)))
            using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
            float ic = Theme.Df(15);
            Icons.Draw(g, _icon, new RectangleF(tx + (tile - ic) / 2f, ty + (tile - ic) / 2f, ic, ic), _open ? Theme.Acc : Theme.Fg, Theme.Df(1.4f));
            int x = tx + tile + Theme.D(14), w = Width - x - Theme.D(48);
            TextRenderer.DrawText(g, _title, Theme.Semi(10.5f), new Rectangle(x, Height / 2 - Theme.D(21), w, Theme.D(22)), Theme.Fg, TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _sub, Theme.F(9f), new Rectangle(x, Height / 2 + Theme.D(1), w, Theme.D(20)), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            // chevron: right-pointing when closed, down when open
            float cx = Width - Theme.Df(28), cy = Height / 2f, a = Theme.Df(5);
            using (var p = new Pen(_hot || _open ? Theme.Fg : Theme.Dim, Theme.Df(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (_open) g.DrawLines(p, new[] { new PointF(cx - a, cy - a / 2f), new PointF(cx, cy + a / 2f), new PointF(cx + a, cy - a / 2f) });
                else g.DrawLines(p, new[] { new PointF(cx - a / 2f, cy - a), new PointF(cx + a / 2f, cy), new PointF(cx - a / 2f, cy + a) });
            }
        }
    }

    // ------------------------------------------------------------------ link card
    // Community card: icon (or the grobyq G mark) on the left, title + one line, external-link tick on the right.
    public sealed class LinkCard : Control
    {
        private readonly string _icon, _title, _sub, _url;
        private bool _hot;
        public LinkCard(string icon, string title, string sub, string url)
        {
            _icon = icon; _title = title; _sub = sub; _url = url; Text = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg; Height = Theme.D(58); Cursor = Cursors.Hand; TabStop = true;
            Tips.Set(this, url);
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Brand.Open(_url); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Brand.Open(_url); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.Df(12)))
            using (var b = new SolidBrush(_hot ? Theme.CardHi : Theme.Card))
            using (var p = new Pen(_hot || Focused ? Theme.Edge : Theme.Line))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            // icon tile (smaller in the 4-up community row)
            bool compact = Width < Theme.D(260);
            int tile = Theme.D(compact ? 32 : 36), tx = Theme.D(compact ? 12 : 15), ty = (Height - tile) / 2;
            using (var path = Theme.Round(new RectangleF(tx, ty, tile, tile), Theme.Df(9)))
            using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
            if (_icon == "mark")
            {
                var img = Brand.Img("glyph_40.png");
                if (img != null)
                {
                    int ih = Theme.D(20), iw = (int)(ih * img.Width / (float)img.Height);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, new Rectangle(tx + (tile - iw) / 2, ty + (tile - ih) / 2, iw, ih));
                }
            }
            else if (_icon.StartsWith("img:", StringComparison.Ordinal))
            {
                // brand logo (grey silhouette on transparent, e.g. img:aerialos_64.png), fitted into the icon tile
                var img = Brand.Img(_icon.Substring(4));
                if (img != null)
                {
                    int side = Theme.D(compact ? 22 : 24);
                    float k = Math.Min(side / (float)img.Width, side / (float)img.Height);
                    int iw = (int)(img.Width * k), ih = (int)(img.Height * k);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(img, new Rectangle(tx + (tile - iw) / 2, ty + (tile - ih) / 2, iw, ih));
                }
            }
            else
            {
                float ic = Theme.Df(16);
                Icons.Draw(g, _icon, new RectangleF(tx + (tile - ic) / 2f, ty + (tile - ic) / 2f, ic, ic), Theme.Fg, Theme.Df(1.4f));
            }
            int x = tx + tile + Theme.D(compact ? 10 : 14), w = Width - x - Theme.D(compact ? 26 : 44);
            TextRenderer.DrawText(g, _title, Theme.Semi(10.5f), new Rectangle(x, Height / 2 - Theme.D(21), w, Theme.D(22)), Theme.Fg, TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _sub, Theme.F(9f), new Rectangle(x, Height / 2 + Theme.D(1), w, Theme.D(20)), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            float ex = Theme.Df(14);
            Icons.Draw(g, "ext", new RectangleF(Width - Theme.D(compact ? 22 : 30), compact ? Theme.D(10) : (Height - ex) / 2f, ex, ex), _hot ? Theme.Fg : Theme.Faint, Theme.Df(1.4f));
        }
    }

    // Equal cards in rows of MaxCols (fewer when a card would get narrower than MinCard); an incomplete last
    // row is centered under the full rows, never left-aligned under the first card.
    public sealed class CardGrid : Panel
    {
        private readonly List<LinkCard> _cards = new List<LinkCard>();
        public int MaxCols = 4, MinCard = 176;
        public CardGrid() { BackColor = Theme.Bg; Height = Theme.D(60); }
        public LinkCard Add(LinkCard c) { _cards.Add(c); Controls.Add(c); PerformLayout(); return c; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_cards.Count == 0 || Width < 20) return;
            int gap = Theme.D(10), cols = Math.Max(1, Math.Min(MaxCols, _cards.Count));
            while (cols > 1 && (Width - gap * (cols - 1)) / cols < Theme.D(MinCard)) cols = cols > 2 ? cols / 2 : 1;
            int cw = (Width - gap * (cols - 1)) / cols, y = 0;
            for (int i = 0; i < _cards.Count; i += cols)
            {
                int n = Math.Min(cols, _cards.Count - i);
                int x0 = (Width - (n * cw + (n - 1) * gap)) / 2;       // full rows start at 0, the last one is centered
                int rowH = 0;
                for (int k = 0; k < n; k++)
                {
                    var c = _cards[i + k];
                    c.SetBounds(x0 + k * (cw + gap), y, cw, c.Height);
                    rowH = Math.Max(rowH, c.Height);
                }
                y += rowH + gap;
            }
            int h = Math.Max(1, y - gap);
            if (Height != h) Height = h;
        }
    }

    // Known system facts: icon + muted caption + value, two columns; empty values are left out.
    public sealed class FactGrid : Control
    {
        private readonly List<string[]> _rows = new List<string[]>();     // icon, caption, value
        private readonly ToolTip _tip;
        private int _tipAt = -1;
        public FactGrid()
        {
            _tip = Tips.Owned(this);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card; Height = Theme.D(60);
        }
        private int Cols { get { return Width >= Theme.D(480) ? 2 : 1; } }
        private int CellH { get { return Theme.D(48); } }
        public void Set(IEnumerable<string[]> rows)
        {
            _rows.Clear();
            foreach (var r in rows) if (!string.IsNullOrWhiteSpace(r[2])) _rows.Add(r);
            Fit(); Invalidate();
        }
        private void Fit() { int n = Math.Max(1, (_rows.Count + Cols - 1) / Cols); int h = n * CellH; if (Height != h) Height = h; }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        private Rectangle Cell(int i) { int c = Cols, cw = Width / c; return new Rectangle((i % c) * cw, (i / c) * CellH, cw, CellH); }
        private bool Truncated(int i)
        {
            var r = Cell(i); int w = r.Width - Theme.D(28) - Theme.D(16);
            return TextRenderer.MeasureText(_rows[i][2] ?? "", Theme.F(10f), new Size(int.MaxValue, Theme.D(22)), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width > w;
        }
        protected override void OnMouseLeave(EventArgs e) { _tipAt = -1; _tip.SetToolTip(this, null); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int at = -1;
            for (int i = 0; i < _rows.Count; i++) if (Cell(i).Contains(e.Location)) at = i;
            // tooltip only when the value is actually cut off; a fully visible value never pops up twice
            if (at >= 0 && !Truncated(at)) at = -1;
            if (at != _tipAt) { _tipAt = at; _tip.SetToolTip(this, at >= 0 ? _rows[at][2] : null); }
            base.OnMouseMove(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (_rows.Count == 0) { TextRenderer.DrawText(g, L.T("reading…"), Theme.F(9.5f), new Rectangle(0, 0, Width, CellH), Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); return; }
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = Cell(i);
                float ic = Theme.Df(16);
                Icons.Draw(g, _rows[i][0], new RectangleF(r.X, r.Y + Theme.D(6), ic, ic), Theme.Dim, Theme.Df(1.4f));
                int x = r.X + Theme.D(28), w = r.Width - Theme.D(28) - Theme.D(16);
                TextRenderer.DrawText(g, _rows[i][1], Theme.F(8.75f), new Rectangle(x, r.Y + Theme.D(2), w, Theme.D(22)), Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, _rows[i][2], Theme.F(10f), new Rectangle(x, r.Y + Theme.D(22), w, Theme.D(22)), Theme.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // ------------------------------------------------------------------ prose view
    // Legal / long text as readable prose: paragraphs reflowed to a comfortable measure (about 72 characters),
    // UI font ~12.5 px, even leading, left-aligned; the same thin rounded scrollbar as the pages.
    public sealed class ProseView : Control, IWheel
    {
        private string _text = "";
        private readonly List<KeyValuePair<string, int>> _lines = new List<KeyValuePair<string, int>>();   // text, y
        private int _top, _contentH, _wrapW = -1, _dragFrom, _dragStart;
        private bool _drag, _hot;
        public ProseView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.LogBg; Font = Theme.F(9.5f); TabStop = true;
        }
        public override string Text { get { return _text; } set { _text = value ?? ""; _top = 0; _wrapW = -1; Invalidate(); } }

        private int Pad { get { return Theme.D(22); } }
        private int LineH { get { return (int)Math.Round(Font.GetHeight() * 1.5f); } }
        private int MaxTop { get { return Math.Max(0, _contentH - Height); } }

        // Blank line = new paragraph; single line breaks inside a paragraph are reflowed.
        private void Wrap()
        {
            int measure = Math.Min(Width - Pad * 2 - Theme.D(10), TextRenderer.MeasureText(new string('n', 72), Font).Width);
            if (measure == _wrapW) return;
            _wrapW = measure; _lines.Clear();
            int y = Pad, lh = LineH;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            foreach (var para in _text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None))
            {
                string p = string.Join(" ", para.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                if (p.Length == 0) continue;
                string line = "";
                foreach (var word in p.Split(' '))
                {
                    if (word.Length == 0) continue;
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && TextRenderer.MeasureText(next, Font, new Size(10000, 100), flags).Width > measure)
                    { _lines.Add(new KeyValuePair<string, int>(line, y)); y += lh; line = word; }
                    else line = next;
                }
                if (line.Length > 0) { _lines.Add(new KeyValuePair<string, int>(line, y)); y += lh; }
                y += lh * 2 / 3;                 // paragraph gap
            }
            _contentH = y + Pad - lh * 2 / 3;
            _top = Math.Min(_top, MaxTop);
        }

        protected override void OnResize(EventArgs e) { _wrapW = -1; base.OnResize(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wheel(e.Delta);
            if (e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true;
        }

        // wheel router: at the first / last line the wheel goes on to the parent
        public bool Wheel(int delta)
        {
            int was = _top;
            _top = Math.Max(0, Math.Min(MaxTop, _top - delta / 120 * LineH * 3));
            if (_top == was) return false;
            Invalidate();
            return true;
        }
        private Rectangle Thumb()
        {
            if (MaxTop <= 0) return Rectangle.Empty;
            int bar = Theme.D(6), track = Height - bar * 2;
            int h = Math.Max(Theme.D(28), (int)(track * (Height / (double)_contentH)));
            return new Rectangle(Width - bar - Theme.D(3), bar + (int)((track - h) * (_top / (double)MaxTop)), bar, h);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var t = Thumb();
            if (!t.IsEmpty && e.X >= Width - Theme.D(16)) { _drag = true; _dragFrom = e.Y; _dragStart = _top; Capture = true; if (!t.Contains(e.Location)) { _top = Math.Max(0, Math.Min(MaxTop, _top + (e.Y < t.Top ? -Height : Height))); _dragStart = _top; } Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) { var t = Thumb(); int tr = Height - Theme.D(12) - t.Height; if (tr > 0) { _top = Math.Max(0, Math.Min(MaxTop, _dragStart + (int)((e.Y - _dragFrom) * (MaxTop / (double)tr)))); Invalidate(); Update(); } }
            else { bool hot = e.X >= Width - Theme.D(16); if (hot != _hot) { _hot = hot; Invalidate(); } }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int d = e.KeyCode == Keys.Up ? -LineH : e.KeyCode == Keys.Down ? LineH : e.KeyCode == Keys.PageUp ? -Height : e.KeyCode == Keys.PageDown ? Height : 0;
            if (e.KeyCode == Keys.Home) _top = 0; else if (e.KeyCode == Keys.End) _top = MaxTop; else _top = Math.Max(0, Math.Min(MaxTop, _top + d));
            Invalidate(); base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            Wrap();
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int lh = LineH;
            foreach (var l in _lines)
            {
                int y = l.Value - _top;
                if (y + lh < 0) continue;
                if (y > Height) break;
                TextRenderer.DrawText(g, l.Key, Font, new Point(Pad, y + (lh - Font.Height) / 2), Theme.Fg, flags);
            }
            var t = Thumb();
            if (!t.IsEmpty) ThinScrollBar.PaintThumb(g, t, _hot || _drag ? 1f : 0f, _drag ? 1f : 0f);
        }
    }

    // ------------------------------------------------------------------ in-app modal
    // Dims the window and shows a rounded dark card over it (tabs + monospace text + Close). Esc, Close or a click
    // outside the card closes it. Used for the license texts: no notepad, no external process.
    public sealed class TextOverlay : Control
    {
        private readonly Bitmap _back;
        private readonly Tabs _tabs;
        private readonly ProseView _view;
        private readonly Btn _close;
        private readonly Label _title;
        private readonly List<KeyValuePair<string, string>> _pages;

        private TextOverlay(Form host, string title, List<KeyValuePair<string, string>> pages)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _pages = pages;
            try
            {
                var cs = host.ClientSize;
                _back = new Bitmap(Math.Max(1, cs.Width), Math.Max(1, cs.Height));
                using (var g = Graphics.FromImage(_back)) g.CopyFromScreen(host.PointToScreen(Point.Empty), Point.Empty, cs);
            }
            catch { _back = null; }
            BackColor = Theme.Bg;
            _title = new Label { Text = title, Font = Theme.Semi(12f), ForeColor = Theme.Fg, BackColor = Theme.Card, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            _tabs = new Tabs { BackColor = Theme.Card };
            for (int i = 0; i < pages.Count; i++) _tabs.AddTab(i.ToString(), pages[i].Key);
            _tabs.Changed += k => { int i; if (int.TryParse(k, out i)) _view.Text = _pages[i].Value; };
            _view = new ProseView { Text = pages.Count > 0 ? pages[0].Value : "" };
            _close = new Btn(L.T("Close"), Btn.Style.Outline) { Height = Theme.D(34) };
            _close.Click += (s, e) => Close();
            Controls.Add(_title); Controls.Add(_tabs); Controls.Add(_view); Controls.Add(_close);
        }

        public static void Show(Form host, string title, List<KeyValuePair<string, string>> pages)
        {
            if (host == null) return;
            var o = new TextOverlay(host, title, pages);
            o.Dock = DockStyle.None; o.Bounds = host.ClientRectangle;          // over the sidebar too (Fill would stop at it)
            o.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            host.Controls.Add(o);
            o.BringToFront();
            o._view.Focus();
        }

        private void Close()
        {
            var p = Parent;
            if (p != null) p.Controls.Remove(this);
            Dispose();
        }

        private Rectangle CardRect
        {
            get
            {
                int w = Math.Min(Theme.D(780), Width - Theme.D(64)), h = Math.Min(Theme.D(620), Height - Theme.D(64));
                return new Rectangle((Width - w) / 2, (Height - h) / 2, Math.Max(Theme.D(300), w), Math.Max(Theme.D(240), h));
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_view == null) return;
            var r = CardRect; int pad = Theme.D(20);
            _title.SetBounds(r.X + pad, r.Y + Theme.D(12), r.Width - pad * 2, Theme.D(34));
            _tabs.SetBounds(r.X + pad, _title.Bottom, r.Width - pad * 2, Theme.D(40));
            _close.Fit();
            _close.Location = new Point(r.Right - pad - _close.Width, r.Bottom - pad - _close.Height);
            _view.SetBounds(r.X + pad, _tabs.Bottom + Theme.D(10), r.Width - pad * 2, _close.Top - Theme.D(12) - _tabs.Bottom - Theme.D(10));
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (!CardRect.Contains(e.Location)) Close(); base.OnMouseDown(e); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_back != null) g.DrawImage(_back, 0, 0); else g.Clear(Theme.Bg);
            using (var b = new SolidBrush(Color.FromArgb(200, 3, 3, 4))) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = CardRect;
            using (var path = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.Df(14)))
            using (var b = new SolidBrush(Theme.Card)) using (var p = new Pen(Theme.Edge)) { g.FillPath(b, path); g.DrawPath(p, path); }
        }

        protected override void Dispose(bool disposing) { if (disposing && _back != null) _back.Dispose(); base.Dispose(disposing); }
    }
}
