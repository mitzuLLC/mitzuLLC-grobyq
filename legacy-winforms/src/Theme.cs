using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Grobyq
{
    // One dark palette, fonts and DPI scale for the whole app.
    public static class Theme
    {
        public static float K = 1f;                       // DPI scale, set once in MainForm
        public static int D(int v) { return (int)Math.Round(v * K); }
        public static float Df(float v) { return v * K; }

        // One theme (no switcher): near-black canvas, quiet cards, hairlines; accent only on the selected nav item
        // and primary buttons.
        public static readonly Color Bg     = Color.FromArgb(6, 6, 6);        // #060606
        public static readonly Color Side   = Color.FromArgb(10, 10, 12);     // #0A0A0C
        public static readonly Color Card   = Color.FromArgb(16, 16, 20);     // #101014
        public static readonly Color CardHi = Color.FromArgb(22, 22, 27);
        public static readonly Color Input  = Color.FromArgb(24, 24, 30);
        public static readonly Color Line   = Color.FromArgb(28, 28, 34);     // #1C1C22 hairline
        public static readonly Color Edge   = Color.FromArgb(42, 42, 50);     // #2A2A32 control borders, scrollbar thumb
        public static readonly Color EdgeHi = Color.FromArgb(58, 58, 68);     // #3A3A44 hover
        public static readonly Color EdgeDn = Color.FromArgb(74, 74, 85);     // #4A4A55 drag
        public static readonly Color Fg     = Color.FromArgb(237, 237, 237);  // #EDEDED
        public static readonly Color Dim    = Color.FromArgb(138, 138, 148);  // #8A8A94
        public static readonly Color Faint  = Color.FromArgb(92, 92, 102);
        public static readonly Color Acc    = Color.FromArgb(74, 139, 255);
        public static readonly Color AccDim = Color.FromArgb(30, 52, 98);
        public static readonly Color Ok     = Color.FromArgb(76, 214, 138);
        public static readonly Color Warn   = Color.FromArgb(232, 180, 72);
        public static readonly Color Bad    = Color.FromArgb(224, 90, 90);
        public static readonly Color LogBg  = Color.FromArgb(9, 9, 11);

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private static Font Get(string family, float size, FontStyle st)
        {
            string key = family + "|" + size + "|" + st;
            lock (_fonts)                       // the splash thread and the UI thread both ask for fonts
            {
                Font f;
                if (!_fonts.TryGetValue(key, out f))
                {
                    try { f = new Font(family, size, st); } catch { f = new Font(FontFamily.GenericSansSerif, size, st); }
                    _fonts[key] = f;
                }
                return f;
            }
        }
        // UI font follows the interface language (Segoe UI for Latin / Cyrillic, the Windows CJK UI font for zh / ja / ko).
        public static Font F(float size, FontStyle st = FontStyle.Regular) { return Get(L.Family, size, st); }
        public static Font Semi(float size)
        {
            if (L.Cur.Cjk || L.Family != "Segoe UI") return Get(L.Family, size, FontStyle.Bold);
            return Get(HasSemibold ? "Segoe UI Semibold" : "Segoe UI", size, HasSemibold ? FontStyle.Regular : FontStyle.Bold);
        }
        public static Font Bold(float size) { return Get(L.Family, size, FontStyle.Bold); }
        public static Font Mono(float size) { return Get("Consolas", size, FontStyle.Regular); }

        private static bool? _semi;
        private static bool HasSemibold
        {
            get
            {
                if (_semi.HasValue) return _semi.Value;
                try { using (var ff = new FontFamily("Segoe UI Semibold")) _semi = ff.Name == "Segoe UI Semibold"; }
                catch { _semi = false; }
                return _semi.Value;
            }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---- native dark chrome (Win10 1809+ / Win11); silently no-op elsewhere ----
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr h, string app, string idList);

        public static void DarkTitle(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int cap = Rgb(Bg);                                      // COLORREF 0x00BBGGRR
                DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);       // DWMWA_CAPTION_COLOR (Win11)
                int txt = Rgb(Fg);
                DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);       // DWMWA_TEXT_COLOR (Win11)
                int brd = Rgb(Line);
                DwmSetWindowAttribute(f.Handle, 34, ref brd, 4);       // DWMWA_BORDER_COLOR (Win11)
            }
            catch { }
        }

        private static int Rgb(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Every form: dark caption as soon as the window handle exists (also if it already exists).
        public static void DarkForm(Form f)
        {
            if (f.IsHandleCreated) DarkTitle(f);
            f.HandleCreated += (s, e) => DarkTitle(f);
        }

        public static void DarkScroll(Control c)
        {
            try
            {
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                else c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            }
            catch { }
        }

        // ---- styled stock controls ----
        public static DataGridView Grid()
        {
            var g = new DarkGrid
            {
                BackgroundColor = Card, BorderStyle = BorderStyle.None, GridColor = Line,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AllowUserToOrderColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = D(34),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = F(9.5f), ForeColor = Fg, BackColor = Card,
                EditMode = DataGridViewEditMode.EditOnEnter,
            };
            g.DefaultCellStyle.BackColor = Card;
            g.DefaultCellStyle.ForeColor = Fg;
            g.DefaultCellStyle.SelectionBackColor = AccDim;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Padding = new Padding(D(6), 0, D(6), 0);
            g.ColumnHeadersDefaultCellStyle.BackColor = Card;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Dim;
            g.ColumnHeadersDefaultCellStyle.Font = Semi(9f);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(D(6), 0, D(6), 0);
            g.RowTemplate.Height = D(30);
            try { typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(g, true, null); } catch { }
            g.EditingControlShowing += (s, e) => { e.Control.BackColor = Input; e.Control.ForeColor = Fg; };
            g.CellPainting += (s, e) =>
            {
                // drop-down cells: the value + a chevron (DarkGrid opens the list on click / F4 / Space)
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var cell = g.Rows[e.RowIndex].Cells[e.ColumnIndex] as DropCell;
                if (cell == null || cell.ReadOnly || g.ReadOnly) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                float cx = e.CellBounds.Right - D(14), cy = e.CellBounds.Top + e.CellBounds.Height / 2f;
                bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
                Chevron(e.Graphics, cx, cy, sel ? Color.White : Dim);
                e.Handled = true;
            };
            g.DataError += (s, e) => { e.ThrowException = false; };
            return g;
        }

        // Adds a row whose cells in comboCols are dark drop-downs with the given items (null items = plain row).
        // Builds the cells explicitly: replacing row.Cells[i] after Rows.Add is not portable.
        public static int AddRow(DataGridView g, IEnumerable<string> items, int[] comboCols, params object[] values)
        {
            var row = (DataGridViewRow)g.RowTemplate.Clone();
            for (int i = 0; i < values.Length; i++)
            {
                DataGridViewCell c;
                if (items != null && comboCols != null && Array.IndexOf(comboCols, i) >= 0)
                {
                    var cb = new DropCell();
                    foreach (var it in items) if (!cb.Items.Contains(it)) cb.Items.Add(it);
                    if (values[i] != null && !cb.Items.Contains(values[i])) cb.Items.Add(values[i]);
                    c = cb;
                }
                else c = new DataGridViewTextBoxCell();
                c.Value = values[i];
                row.Cells.Add(c);
            }
            return g.Rows.Add(row);
        }

        // Forces a full measure/arrange pass over a control tree (children first), then repaints it.
        // Used after the language switch rebuilds the window: no minimize/restore needed.
        public static void Reflow(Control c)
        {
            if (c == null || c.IsDisposed) return;
            foreach (Control ch in c.Controls) Reflow(ch);
            c.PerformLayout();
            c.Invalidate();
        }

        // Height that shows the rows (between min and max rows, then it scrolls): no empty grid area.
        public static void FitRows(DataGridView g, int minRows, int maxRows)
        {
            int n = Math.Max(minRows, Math.Min(maxRows, g.Rows.Count));
            int h = g.ColumnHeadersHeight + n * g.RowTemplate.Height + D(3);
            if (g.AutoSizeRowsMode != DataGridViewAutoSizeRowsMode.None && g.Rows.Count > 0)
            {
                // wrapped rows: sum the real heights of the rows that fit
                int sum = g.ColumnHeadersHeight + D(3), rows = 0;
                foreach (DataGridViewRow r in g.Rows) { if (rows >= maxRows) break; sum += r.Height; rows++; }
                h = Math.Max(h, sum);
            }
            if (g.Height != h) g.Height = h;
        }

        public static DataGridViewTextBoxColumn Col(string header, int fillWeight, bool readOnly = true)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = fillWeight, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        public static DropColumn ComboCol(string header, int fillWeight, params string[] items)
        {
            var c = new DropColumn { HeaderText = header, FillWeight = fillWeight };
            c.Items.AddRange(items);
            c.DefaultCellStyle.BackColor = Card;
            return c;
        }

        public static DropBox Combo(int width, params string[] items)
        {
            var c = NewCombo(width);
            c.Items.AddRange(items);
            return c;
        }

        // A drop-down whose items show translated text but store a fixed value (Opt).
        public static DropBox Combo(int width, Opt first, params Opt[] rest)
        {
            var c = NewCombo(width);
            c.Items.Add(first);
            c.Items.AddRange(rest);
            return c;
        }

        // grobyq's own drop-down (Inputs.cs): the list is as wide as the longest item, up to 640 px.
        private static DropBox NewCombo(int width)
        {
            return new DropBox { Width = width, DropDownWidth = Math.Max(width, D(160)) };
        }

        public static void Chevron(Graphics g, float cx, float cy, Color c)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, Df(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - Df(4), cy - Df(2)), new PointF(cx, cy + Df(2)), new PointF(cx + Df(4), cy - Df(2)) });
            g.SmoothingMode = old;
        }

        public static string Val(DropBox c)
        {
            var o = c.SelectedItem as Opt;
            return o != null ? o.Value : c.SelectedItem == null ? "" : c.SelectedItem.ToString();
        }

        public static void SelectVal(DropBox c, string value)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                var o = c.Items[i] as Opt;
                if (o != null ? string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase) : string.Equals(c.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; return; }
            }
            if (c.Items.Count > 0 && c.SelectedIndex < 0) c.SelectedIndex = 0;
        }

        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip { Font = F(9.5f), ShowImageMargin = false, BackColor = Card, ForeColor = Fg, RenderMode = ToolStripRenderMode.ManagerRenderMode };
        }

        public static TextBox Box(int width, string text = "")
        {
            var t = new TextBox { Width = width, Text = text, BackColor = Input, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = F(9.5f) };
            new BoxBorder(t);            // 1 px Edge frame (EdgeDn with focus) instead of the light system border
            return t;
        }

        public static Stepper Num(int width, int min, int max, int val)
        {
            var n = new Stepper { Width = width, Minimum = min, Maximum = max };
            n.Value = Math.Max(min, Math.Min(max, val));
            return n;
        }

        public static Label Lbl(string text, Font font, Color color, bool auto = true)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = auto, UseMnemonic = false };
        }

        public static void Select(DropBox c, string value)
        {
            for (int i = 0; i < c.Items.Count; i++)
                if (string.Equals(c.GetItemText(c.Items[i]), value, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; return; }
            if (c.Items.Count > 0 && c.SelectedIndex < 0) c.SelectedIndex = 0;
        }
    }

    // Combo item: fixed value, translated text.
    public sealed class Opt
    {
        public string Value, Text;
        public Opt(string value, string text) { Value = value; Text = text; }
        public override string ToString() { return Text; }
    }

    // Dark context menus / tray menu (every ToolStrip in ManagerRenderMode).
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Fg : Theme.Faint; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item != null && e.Item.Enabled ? Theme.Dim : Theme.Faint; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X, e.ImageRectangle.Y, e.ImageRectangle.Width, e.ImageRectangle.Height);
            using (var b = new SolidBrush(Theme.AccDim)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(Color.White, Theme.Df(1.6f)))
                e.Graphics.DrawLines(p, new[] { new PointF(r.Left + r.Width * 0.25f, r.Top + r.Height * 0.52f), new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.7f), new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.3f) });
        }
    }

    internal sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color MenuItemBorder { get { return Theme.CardHi; } }
        public override Color MenuItemSelected { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHi; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardHi; } }
        public override Color MenuItemPressedGradientMiddle { get { return Theme.CardHi; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardHi; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
        public override Color CheckBackground { get { return Theme.AccDim; } }
        public override Color CheckSelectedBackground { get { return Theme.AccDim; } }
        public override Color CheckPressedBackground { get { return Theme.AccDim; } }
        public override Color ButtonSelectedHighlight { get { return Theme.CardHi; } }
        public override Color ButtonSelectedBorder { get { return Theme.Line; } }
    }
}

namespace Grobyq
{
    // Tray menu in the card style: #101014 fill, hairline, rounded hover, a check for the armed optimizer.
    // Win11 rounds the popup natively (DWMWA_WINDOW_CORNER_PREFERENCE); older Windows keep square corners.
    internal static class TrayMenu
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(System.IntPtr h, int attr, ref int val, int size);

        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip
            {
                Font = Theme.F(9.75f), ShowImageMargin = false, ShowCheckMargin = true, BackColor = Theme.Card, ForeColor = Theme.Fg,
                Renderer = new TrayRenderer(), Padding = new Padding(Theme.D(4), Theme.D(6), Theme.D(4), Theme.D(6)), DropShadowEnabled = true
            };
            m.HandleCreated += (s, e) =>
            {
                try
                {
                    int round = 3; DwmSetWindowAttribute(m.Handle, 33, ref round, 4);          // DWMWCP_ROUNDSMALL
                    int brd = Theme.Line.R | (Theme.Line.G << 8) | (Theme.Line.B << 16); DwmSetWindowAttribute(m.Handle, 34, ref brd, 4);
                }
                catch { }
            };
            return m;
        }

        public static ToolStripMenuItem Item(string text, System.EventHandler click)
        {
            var i = new ToolStripMenuItem(text) { ForeColor = Theme.Fg, Padding = new Padding(Theme.D(4), Theme.D(7), Theme.D(18), Theme.D(7)) };
            i.Click += click;
            return i;
        }
    }

    internal sealed class TrayRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = e.ToolStrip.ClientRectangle;
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(Theme.D(2), 1, e.Item.Width - Theme.D(4), e.Item.Height - 2);
            using (var path = Theme.Round(r, Theme.Df(6))) using (var b = new SolidBrush(Theme.CardHi)) g.FillPath(b, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Fg : Theme.Faint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Theme.D(10), y, e.Item.Width - Theme.D(10), y);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle; var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, u = Math.Min(r.Width, r.Height) / 2.4f;
            using (var p = new Pen(Theme.Fg, Theme.Df(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - u, cy), new PointF(cx - u * 0.3f, cy + u * 0.7f), new PointF(cx + u, cy - u * 0.7f) });
        }
    }
}
