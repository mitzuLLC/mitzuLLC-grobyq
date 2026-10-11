using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Grobyq
{
    // Value controls drawn by grobyq instead of the Windows defaults: drop-down (DropBox + DropPopup), stepper,
    // grid drop-down cells, the dark grid scroll thumb and the text box border. No visual styles needed; DPI via Theme.D.

    // A control that takes the wheel itself. false = it is at its end (or cannot scroll): the router passes the wheel on.
    public interface IWheel { bool Wheel(int delta); }

    internal static class TypeAhead
    {
        // First item after 'from' (wrapping) whose text starts with prefix, case-insensitive; -1 if none.
        public static int Find(IList<string> items, string prefix, int from)
        {
            if (string.IsNullOrEmpty(prefix) || items.Count == 0) return -1;
            for (int k = 0; k < items.Count; k++)
            {
                int i = (from + k + items.Count) % items.Count;
                if (items[i].TrimStart().StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) return i;
            }
            return -1;
        }
    }

    // ================================================================== drop-down box
    // Closed box: Input fill, Edge border (EdgeHi hover, EdgeDn focus / open), radius 6, chevron. Same caller surface
    // as the ComboBox it replaces: Items, SelectedIndex, SelectedItem, GetItemText, SelectedIndexChanged, DropDownWidth.
    // Never takes the mouse wheel while closed (the page scrolls); the open list scrolls with the wheel.
    public class DropBox : Control
    {
        public sealed class ItemList : IList
        {
            private readonly List<object> _l = new List<object>();
            private readonly DropBox _o;
            internal ItemList(DropBox o) { _o = o; }
            public object this[int i] { get { return _l[i]; } set { _l[i] = value; _o.Invalidate(); } }
            public int Count { get { return _l.Count; } }
            public int Add(object o) { _l.Add(o); _o.Invalidate(); return _l.Count - 1; }
            public void AddRange(IEnumerable items) { foreach (var o in items) _l.Add(o); _o.Invalidate(); }
            public void Clear() { _l.Clear(); _o.Reset(); }
            public bool Contains(object o) { return _l.Contains(o); }
            public int IndexOf(object o) { return _l.IndexOf(o); }
            public void Insert(int i, object o) { _l.Insert(i, o); if (_o._sel >= i) _o._sel++; _o.Invalidate(); }
            public void Remove(object o) { int i = _l.IndexOf(o); if (i >= 0) RemoveAt(i); }
            public void RemoveAt(int i) { _l.RemoveAt(i); if (_o._sel == i) _o.Reset(); else if (_o._sel > i) _o._sel--; _o.Invalidate(); }
            public IEnumerator GetEnumerator() { return _l.GetEnumerator(); }
            bool IList.IsFixedSize { get { return false; } }
            bool IList.IsReadOnly { get { return false; } }
            bool ICollection.IsSynchronized { get { return false; } }
            object ICollection.SyncRoot { get { return this; } }
            void ICollection.CopyTo(Array a, int i) { ((ICollection)_l).CopyTo(a, i); }
        }

        private readonly ItemList _items;
        internal int _sel = -1;
        private bool _hot;
        private DropPopup _popup;
        private DateTime _closedByClick = DateTime.MinValue;
        private string _typed = "";
        private DateTime _typedAt = DateTime.MinValue;
        public int DropDownWidth;
        public event EventHandler SelectedIndexChanged, DropDown, DropDownClosed;

        public DropBox()
        {
            _items = new ItemList(this);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true; Font = Theme.F(9.5f); ForeColor = Theme.Fg; BackColor = Color.Transparent;
            Size = new Size(Theme.D(200), Theme.D(32)); Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
        }

        public ItemList Items { get { return _items; } }

        internal void Reset() { if (_sel != -1) { _sel = -1; } Invalidate(); }

        public int SelectedIndex
        {
            get { return _sel; }
            set
            {
                int v = value < -1 ? -1 : value >= _items.Count ? _items.Count - 1 : value;
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                OnSelectedIndexChanged(EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _sel >= 0 && _sel < _items.Count ? _items[_sel] : null; }
            set { SelectedIndex = value == null ? -1 : _items.IndexOf(value); }
        }

        public string GetItemText(object o) { return o == null ? "" : Convert.ToString(o); }

        public override string Text
        {
            get { return GetItemText(SelectedItem); }
            set { for (int i = 0; i < _items.Count; i++) if (GetItemText(_items[i]) == value) { SelectedIndex = i; return; } }
        }

        protected virtual void OnSelectedIndexChanged(EventArgs e) { var h = SelectedIndexChanged; if (h != null) h(this, e); }

        public bool DroppedDown
        {
            get { return _popup != null && !_popup.IsDisposed && _popup.Visible; }
            set { if (value) Open(); else if (DroppedDown) _popup.Close(); }
        }

        private List<string> Texts() { var l = new List<string>(); foreach (var o in _items) l.Add(GetItemText(o)); return l; }

        public void Open()
        {
            if (!Enabled || _items.Count == 0 || DroppedDown) return;
            if (!Focused) Focus();
            var h = DropDown; if (h != null) h(this, EventArgs.Empty);
            var p = new DropPopup(Texts(), _sel, Math.Max(Width, DropDownWidth), Font);
            p.Picked += i => SelectedIndex = i;
            p.Closed += (s, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && ClientRectangle.Contains(PointToClient(Cursor.Position)))
                    _closedByClick = DateTime.UtcNow;                 // the click that closed it must not reopen it
                if (_popup == p) _popup = null;
                // a pick or Esc gives the keyboard back to the box; a click elsewhere keeps its own focus
                if ((e.CloseReason == ToolStripDropDownCloseReason.ItemClicked || e.CloseReason == ToolStripDropDownCloseReason.Keyboard) && CanFocus) Focus();
                Invalidate();
                var c = DropDownClosed; if (c != null) c(this, EventArgs.Empty);
                if (IsHandleCreated) BeginInvoke((Action)(() => p.Dispose()));
            };
            _popup = p;
            p.ShowFor(this, new Rectangle(0, 0, Width, Height));
            Invalidate();
        }

        protected override bool IsInputKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            return kc == Keys.Up || kc == Keys.Down || kc == Keys.Home || kc == Keys.End || kc == Keys.PageUp || kc == Keys.PageDown || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (DroppedDown && _popup.View.HandleKey(e.KeyData)) { e.Handled = true; base.OnKeyDown(e); return; }
            if (e.KeyCode == Keys.F4 || (e.Alt && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)) || e.KeyCode == Keys.Space) { Open(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) { if (_sel > 0) SelectedIndex = Math.Max(0, _sel - (e.KeyCode == Keys.Up ? 1 : 8)); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) { if (_sel < _items.Count - 1) SelectedIndex = Math.Min(_items.Count - 1, _sel + (e.KeyCode == Keys.Down ? 1 : 8)); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { if (_items.Count > 0) SelectedIndex = 0; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { if (_items.Count > 0) SelectedIndex = _items.Count - 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && e.KeyChar != ' ')
            {
                if (DroppedDown) _popup.View.TypeChar(e.KeyChar);
                else
                {
                    if ((DateTime.UtcNow - _typedAt).TotalMilliseconds > 900) _typed = "";
                    _typedAt = DateTime.UtcNow; _typed += e.KeyChar;
                    int i = TypeAhead.Find(Texts(), _typed, _typed.Length == 1 ? _sel + 1 : Math.Max(0, _sel));
                    if (i >= 0) SelectedIndex = i;
                }
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
#if DEMO
            // jump check: the page offset before the click and after focus + open (must be equal)
            ScrollableControl page = null;
            for (var pc = Parent; pc != null; pc = pc.Parent) if (pc is Flow) { page = (ScrollableControl)pc; break; }
            if (page != null)
            {
                int before = page.VerticalScroll.Value; var pos0 = page.AutoScrollPosition;
                BeginInvoke((Action)(() => Console.Error.WriteLine(string.Format("[JUMP] {0}: offset before {1} ({2}) after {3} ({4}) {5}",
                    Text, before, pos0.Y, page.VerticalScroll.Value, page.AutoScrollPosition.Y, before == page.VerticalScroll.Value ? "same" : "MOVED"))));
            }
#endif
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if ((DateTime.UtcNow - _closedByClick).TotalMilliseconds < 300) { _closedByClick = DateTime.MinValue; return; }
            if (DroppedDown) _popup.Close(); else Open();
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); }     // closed: never changes the value

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            bool open = DroppedDown;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill = !Enabled ? Theme.Mix(Theme.Card, Theme.Input, 0.5f) : _hot && !open ? Theme.Mix(Theme.Input, Theme.CardHi, 0.7f) : Theme.Input;
            Color edge = !Enabled ? Theme.Line : open || Focused ? Theme.EdgeDn : _hot ? Theme.EdgeHi : Theme.Edge;
            using (var path = Theme.Round(r, Theme.Df(6)))
            using (var b = new SolidBrush(fill)) using (var p = new Pen(edge)) { g.FillPath(b, path); g.DrawPath(p, path); }
            int chevW = Theme.D(30);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.D(10), 0, Width - Theme.D(10) - chevW, Height), Enabled ? ForeColor : Theme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            float cx = Width - Theme.Df(16), cy = Height / 2f;
            Color cc = !Enabled ? Theme.Faint : _hot || open ? Theme.Fg : Theme.Dim;
            if (!open) Theme.Chevron(g, cx, cy, cc);
            else
                using (var p = new Pen(cc, Theme.Df(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new PointF(cx - Theme.Df(4), cy + Theme.Df(2)), new PointF(cx, cy - Theme.Df(2)), new PointF(cx + Theme.Df(4), cy + Theme.Df(2)) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _popup != null && !_popup.IsDisposed) { try { _popup.Close(); _popup.Dispose(); } catch { } }
            base.Dispose(disposing);
        }
    }

    // ================================================================== the open list
    // ToolStripDropDown host: closes on an outside click / deactivate / Esc. Card fill, hairline, radius 8 (DWM on
    // Win11, a region on Win10), opens below the anchor, above when there is no room.
    internal sealed class DropPopup : ToolStripDropDown
    {
        public static DropPopup Current;
        public readonly DropListView View;
        public event Action<int> Picked;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        private static readonly bool Win11 = Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        public DropPopup(List<string> items, int sel, int minWidth, Font font)
        {
            AutoSize = false; Padding = Padding.Empty; Margin = Padding.Empty; DropShadowEnabled = true;
            BackColor = Theme.Card; Renderer = new PlainRenderer();
            View = new DropListView(items, sel, font);
            int w = Math.Min(Theme.D(640), Math.Max(minWidth, View.PreferredWidth)), h = View.PreferredHeight;
            View.Size = new Size(w, h);
            var host = new ToolStripControlHost(View) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty, Size = View.Size };
            Items.Add(host);
            Size = View.Size;
            View.Commit += i => { var p = Picked; if (p != null) p(i); Close(ToolStripDropDownCloseReason.ItemClicked); };
            View.Cancel += () => Close(ToolStripDropDownCloseReason.Keyboard);
            if (!Win11) { try { Region = new Region(Theme.Round(new RectangleF(0, 0, w, h), Theme.Df(8))); } catch { } }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Win11) { try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); int brd = Theme.Line.R | (Theme.Line.G << 8) | (Theme.Line.B << 16); DwmSetWindowAttribute(Handle, 34, ref brd, 4); } catch { } }
        }

        // anchor = the box (or a grid cell) in 'owner' client coordinates
        public void ShowFor(Control owner, Rectangle anchor)
        {
            var scr = Screen.FromControl(owner).WorkingArea;
            var below = owner.PointToScreen(new Point(anchor.Left, anchor.Bottom + Theme.D(4)));
            var top = owner.PointToScreen(new Point(anchor.Left, anchor.Top));
            bool above = below.Y + Height > scr.Bottom && top.Y - Theme.D(4) - Height >= scr.Top;
            int x = Math.Max(scr.Left, Math.Min(below.X, scr.Right - Width));
            var at = new Point(x, above ? top.Y - Theme.D(4) - Height : below.Y);
            Current = this;
            Show(at);
            View.Focus();
            View.EnsureVisible(View.Hot);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (View.HandleKey(keyData)) return true;
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar)) View.TypeChar(e.KeyChar); base.OnKeyPress(e); }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e) { if (Current == this) Current = null; base.OnClosed(e); }

        private sealed class PlainRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        }
    }

    // 30 px rows, hover CardHi, a check on the selected row, thin thumb when the list scrolls.
    internal sealed class DropListView : Control, IWheel
    {
        private readonly List<string> _items;
        private readonly int _sel;
        private int _hot, _top, _dragY, _dragTop;
        private bool _drag, _barHot, _mouseHot;
        private string _typed = "";
        private DateTime _typedAt = DateTime.MinValue;
        public event Action<int> Commit;
        public event Action Cancel;
        private const int MaxRows = 10;

        public DropListView(List<string> items, int sel, Font font)
        {
            _items = items; _sel = sel; _hot = sel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card; Font = font; TabStop = true;
        }

        public int Hot { get { return _hot; } }
        private int RowH { get { return Theme.D(30); } }
        private int Pad { get { return Theme.D(5); } }
        public int PreferredHeight { get { return Pad * 2 + Math.Max(1, Math.Min(MaxRows, _items.Count)) * RowH; } }
        public int PreferredWidth
        {
            get
            {
                int w = 0;
                foreach (var t in _items) w = Math.Max(w, TextRenderer.MeasureText(t, Font).Width);
                return w + Theme.D(12) + Theme.D(40);
            }
        }
        private int ViewH { get { return Height - Pad * 2; } }
        private int ContentH { get { return _items.Count * RowH; } }
        private int MaxTop { get { return Math.Max(0, ContentH - ViewH); } }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            int y = i * RowH;
            if (y < _top) _top = y;
            else if (y + RowH > _top + ViewH) _top = y + RowH - ViewH;
            _top = Math.Max(0, Math.Min(MaxTop, _top));
            Invalidate();
        }

        public bool Wheel(int delta)
        {
            _top = Math.Max(0, Math.Min(MaxTop, _top - delta / 120 * RowH * 3));
            Invalidate();
            return true;                              // an open list always owns the wheel
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); if (e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true; }

        public bool HandleKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            int n = _items.Count;
            if (n == 0) return false;
            int cur = _hot < 0 ? Math.Max(0, _sel) : _hot;
            switch (kc)
            {
                case Keys.Up: _hot = Math.Max(0, (_hot < 0 ? cur + 1 : cur) - 1); break;
                case Keys.Down: _hot = Math.Min(n - 1, _hot < 0 ? cur : cur + 1); break;
                case Keys.Home: _hot = 0; break;
                case Keys.End: _hot = n - 1; break;
                case Keys.PageUp: _hot = Math.Max(0, cur - MaxRows); break;
                case Keys.PageDown: _hot = Math.Min(n - 1, cur + MaxRows); break;
                case Keys.Enter: case Keys.Tab: { var c = Commit; if (_hot >= 0 && c != null) c(_hot); else { var x = Cancel; if (x != null) x(); } return true; }
                case Keys.Escape: { var x = Cancel; if (x != null) x(); return true; }
                default: return false;
            }
            EnsureVisible(_hot);
            return true;
        }

        public void TypeChar(char ch)
        {
            if ((DateTime.UtcNow - _typedAt).TotalMilliseconds > 900) _typed = "";
            _typedAt = DateTime.UtcNow; _typed += ch;
            int i = TypeAhead.Find(_items, _typed, _typed.Length == 1 ? _hot + 1 : Math.Max(0, _hot));
            if (i >= 0) { _hot = i; EnsureVisible(i); }
        }

        protected override bool IsInputKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            return kc == Keys.Up || kc == Keys.Down || kc == Keys.Home || kc == Keys.End || kc == Keys.PageUp || kc == Keys.PageDown || kc == Keys.Enter || kc == Keys.Escape || base.IsInputKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (HandleKey(e.KeyData)) e.Handled = true; base.OnKeyDown(e); }
        protected override void OnKeyPress(KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar)) { TypeChar(e.KeyChar); e.Handled = true; } base.OnKeyPress(e); }

        private int IndexAt(int y) { if (y < Pad || y >= Height - Pad) return -1; int i = (y - Pad + _top) / RowH; return i >= 0 && i < _items.Count ? i : -1; }

        private Rectangle Thumb()
        {
            if (MaxTop <= 0) return Rectangle.Empty;
            int track = ViewH - Theme.D(4);
            int h = Math.Max(Theme.D(24), (int)(track * (ViewH / (double)ContentH)));
            int y = Pad + Theme.D(2) + (int)((track - h) * (_top / (double)MaxTop));
            return new Rectangle(Width - Theme.D(10), y, Theme.D(8), h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag)
            {
                var t = Thumb(); int track = ViewH - Theme.D(4) - t.Height;
                if (track > 0) { _top = Math.Max(0, Math.Min(MaxTop, _dragTop + (int)((e.Y - _dragY) * (MaxTop / (double)track)))); Invalidate(); }
            }
            else
            {
                bool bar = MaxTop > 0 && e.X >= Width - Theme.D(14);
                int i = bar ? _hot : IndexAt(e.Y);
                if (i != _hot || bar != _barHot || !_mouseHot) { _hot = i; _barHot = bar; _mouseHot = true; Invalidate(); }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _barHot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && MaxTop > 0 && e.X >= Width - Theme.D(14))
            {
                var t = Thumb();
                if (t.Contains(new Point(t.X + 1, e.Y))) { _drag = true; _dragY = e.Y; _dragTop = _top; Capture = true; }
                else { _top = Math.Max(0, Math.Min(MaxTop, _top + (e.Y < t.Top ? -ViewH : ViewH))); Invalidate(); }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drag) { _drag = false; Capture = false; Invalidate(); }
            else if (e.Button == MouseButtons.Left && !(MaxTop > 0 && e.X >= Width - Theme.D(14)))
            {
                int i = IndexAt(e.Y);
                if (i >= 0) { var c = Commit; if (c != null) c(i); }
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
            var clip = new Rectangle(0, Pad, Width, ViewH);
            g.SetClip(clip);
            int first = Math.Max(0, _top / RowH), last = Math.Min(_items.Count - 1, (_top + ViewH) / RowH);
            int textR = Width - Theme.D(34);
            for (int i = first; i <= last; i++)
            {
                var r = new Rectangle(0, Pad + i * RowH - _top, Width, RowH);
                if (i == _hot)
                    using (var path = Theme.Round(new RectangleF(Theme.D(4), r.Y + Theme.D(1), Width - Theme.D(8) - (MaxTop > 0 ? Theme.D(8) : 0), RowH - Theme.D(2)), Theme.Df(5)))
                    using (var b = new SolidBrush(Theme.CardHi)) g.FillPath(b, path);
                bool sel = i == _sel;
                TextRenderer.DrawText(g, _items[i], Font, new Rectangle(Theme.D(12), r.Y, textR - Theme.D(12), RowH), sel ? Color.White : Theme.Fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (sel)
                {
                    float cx = textR + Theme.Df(12), cy = r.Y + RowH / 2f, u = Theme.Df(4.5f);
                    using (var p = new Pen(Theme.Acc, Theme.Df(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(p, new[] { new PointF(cx - u, cy), new PointF(cx - u * 0.3f, cy + u * 0.7f), new PointF(cx + u, cy - u * 0.7f) });
                }
            }
            g.ResetClip();
            var t = Thumb();
            if (!t.IsEmpty)
            {
                float w = _barHot || _drag ? Theme.Df(6) : Theme.Df(4);
                ThinScrollBar.PaintThumb(g, new RectangleF(t.Right - Theme.Df(2) - w, t.Y, w, t.Height), _barHot || _drag ? 1f : 0f, _drag ? 1f : 0f);
            }
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.Df(8)))
            using (var p = new Pen(Theme.Line)) g.DrawPath(p, path);
        }
    }

    // ================================================================== stepper (NumericUpDown replacement)
    // Box + two chevron buttons. Hold a button to repeat; the wheel steps only while the stepper has focus.
    public sealed class Stepper : Control, IWheel
    {
        private readonly TextBox _box;
        private decimal _v, _min, _max = 100;
        private readonly Timer _rep = new Timer();
        private int _press, _hotBtn;
        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Font = Theme.F(9.5f); Size = new Size(Theme.D(90), Theme.D(32));
            _box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Fg, Font = Font, Text = "0" };
            _box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.PageUp) { Step(10); e.Handled = true; }
                else if (e.KeyCode == Keys.PageDown) { Step(-10); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            _box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && _min < 0)) e.Handled = true; };
            _box.Leave += (s, e) => Commit();
            _box.GotFocus += (s, e) => Invalidate();
            _box.LostFocus += (s, e) => Invalidate();
            Controls.Add(_box);
            _rep.Tick += (s, e) => { _rep.Interval = 60; if (_press != 0) Step(_press); };
        }

        public decimal Minimum { get { return _min; } set { _min = value; if (_max < _min) _max = _min; Value = _v; } }
        public decimal Maximum { get { return _max; } set { _max = value; if (_min > _max) _min = _max; Value = _v; } }
        public decimal Value
        {
            get { return _v; }
            set
            {
                decimal v = Math.Max(_min, Math.Min(_max, value));
                _box.Text = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (v == _v) return;
                _v = v;
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        private void Commit()
        {
            decimal v;
            if (decimal.TryParse(_box.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v)) Value = v;
            else _box.Text = _v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void Step(int n) { Commit(); Value = _v + n; _box.SelectionStart = _box.Text.Length; }

        public bool Wheel(int delta)
        {
            if (!ContainsFocus || !Enabled) return false;
            Step(delta > 0 ? 1 : -1);
            return true;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { if (Wheel(e.Delta) && e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true; base.OnMouseWheel(e); }

        private int BtnW { get { return Theme.D(26); } }
        private int BtnAt(Point p) { if (p.X < Width - BtnW) return 0; return p.Y < Height / 2 ? 1 : -1; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_box == null) return;
            int ph = _box.PreferredHeight;
            _box.SetBounds(Theme.D(10), (Height - ph) / 2 + 1, Math.Max(10, Width - BtnW - Theme.D(14)), ph);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            int b = BtnAt(e.Location);
            _box.Focus();
            if (b == 0) return;
            _press = b; Step(b); _rep.Interval = 400; _rep.Start(); Capture = true; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { _press = 0; _rep.Stop(); Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseMove(MouseEventArgs e) { int b = BtnAt(e.Location); if (b != _hotBtn) { _hotBtn = b; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hotBtn = 0; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { _box.Enabled = Enabled; _box.BackColor = Enabled ? Theme.Input : Theme.Mix(Theme.Card, Theme.Input, 0.5f); Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill = Enabled ? Theme.Input : Theme.Mix(Theme.Card, Theme.Input, 0.5f);
            Color edge = !Enabled ? Theme.Line : _box.Focused ? Theme.EdgeDn : _hotBtn != 0 ? Theme.EdgeHi : Theme.Edge;
            using (var path = Theme.Round(r, Theme.Df(6)))
            using (var b = new SolidBrush(fill)) using (var p = new Pen(edge)) { g.FillPath(b, path); g.DrawPath(p, path); }
            int bx = Width - BtnW;
            if (_press != 0 || _hotBtn != 0)
            {
                int which = _press != 0 ? _press : _hotBtn;
                var hr = which > 0 ? new RectangleF(bx + 1, Theme.D(3), BtnW - Theme.D(4), Height / 2f - Theme.D(3)) : new RectangleF(bx + 1, Height / 2f, BtnW - Theme.D(4), Height / 2f - Theme.D(3));
                using (var path = Theme.Round(hr, Theme.Df(3))) using (var b = new SolidBrush(_press != 0 ? Theme.EdgeHi : Theme.CardHi)) g.FillPath(b, path);
            }
            using (var p = new Pen(Theme.Line)) { g.DrawLine(p, bx, Theme.D(5), bx, Height - Theme.D(5)); g.DrawLine(p, bx + Theme.D(5), Height / 2f, Width - Theme.D(6), Height / 2f); }
            float cx = bx + BtnW / 2f - Theme.Df(1);
            Color up = !Enabled ? Theme.Faint : _hotBtn > 0 || _press > 0 ? Theme.Fg : Theme.Dim;
            Color dn = !Enabled ? Theme.Faint : _hotBtn < 0 || _press < 0 ? Theme.Fg : Theme.Dim;
            float u = Theme.Df(3.2f), v = Theme.Df(1.7f), yu = Height / 4f + Theme.Df(0.5f), yd = Height * 3f / 4f - Theme.Df(0.5f);
            using (var p = new Pen(up, Theme.Df(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - u, yu + v), new PointF(cx, yu - v), new PointF(cx + u, yu + v) });
            using (var p = new Pen(dn, Theme.Df(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - u, yd - v), new PointF(cx, yd + v), new PointF(cx + u, yd - v) });
        }

        protected override void Dispose(bool disposing) { if (disposing) _rep.Dispose(); base.Dispose(disposing); }
    }

    // ================================================================== text box border
    // FixedSingle is a light system border on a dark box: repaint the non-client frame 1 px Edge, EdgeDn with focus.
    internal sealed class BoxBorder : NativeWindow
    {
        private readonly TextBox _t;
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr h, IntPtr rc, IntPtr rgn, uint flags);

        public BoxBorder(TextBox t)
        {
            _t = t;
            if (t.IsHandleCreated) AssignHandle(t.Handle);
            t.HandleCreated += (s, e) => { try { AssignHandle(t.Handle); } catch { } };
            t.HandleDestroyed += (s, e) => { try { ReleaseHandle(); } catch { } };
            t.GotFocus += (s, e) => Frame();
            t.LostFocus += (s, e) => Frame();
            t.EnabledChanged += (s, e) => Frame();
        }

        private void Frame() { try { if (_t.IsHandleCreated) RedrawWindow(_t.Handle, IntPtr.Zero, IntPtr.Zero, 0x0400 | 0x0001 | 0x0100); } catch { } }   // RDW_FRAME | RDW_INVALIDATE | RDW_UPDATENOW

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0085 || m.Msg == 0x000F) Paint();      // WM_NCPAINT, WM_PAINT
        }

        private void Paint()
        {
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                using (var g = Graphics.FromHdc(dc))
                using (var p = new Pen(!_t.Enabled ? Theme.Line : _t.Focused ? Theme.EdgeDn : Theme.Edge))
                    g.DrawRectangle(p, 0, 0, _t.Width - 1, _t.Height - 1);
            }
            catch { }
            finally { if (dc != IntPtr.Zero) { try { ReleaseDC(Handle, dc); } catch { } } }
        }
    }

    // ================================================================== grid
    // Native scroll bars off; a thin thumb painted over the right edge mirrors FirstDisplayedScrollingRowIndex.
    // Drop-down cells (DropCell) open the same list as DropBox.
    public sealed class DarkGrid : DataGridView, IWheel
    {
        private bool _barHot, _drag;
        private int _dragY, _dragFirst;

        public DarkGrid() { ScrollBars = ScrollBars.None; }

        private List<int> VisibleRows()
        {
            var l = new List<int>(Rows.Count);
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Visible) l.Add(i);
            return l;
        }

        private int FirstOrdinal(List<int> vis)
        {
            int f = FirstDisplayedScrollingRowIndex;
            if (f < 0) return 0;
            int k = vis.BinarySearch(f);
            return k < 0 ? ~k : k;
        }

        private int Shown { get { return Math.Max(1, DisplayedRowCount(false)); } }

        private Rectangle Thumb(List<int> vis)
        {
            int total = vis.Count, shown = Shown;
            if (total <= shown || Height < Theme.D(60)) return Rectangle.Empty;
            int top = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
            int track = Height - top - Theme.D(8);
            int h = Math.Max(Theme.D(28), (int)(track * (shown / (double)total)));
            int y = top + Theme.D(4) + (int)((track - h) * (FirstOrdinal(vis) / (double)Math.Max(1, total - shown)));
            return new Rectangle(Width - Theme.D(10), y, Theme.D(8), h);
        }

        public void SetFirst(int ordinal)
        {
            var vis = VisibleRows();
            if (vis.Count == 0) return;
            ordinal = Math.Max(0, Math.Min(vis.Count - 1, ordinal));
            try { if (FirstDisplayedScrollingRowIndex != vis[ordinal]) FirstDisplayedScrollingRowIndex = vis[ordinal]; } catch { }
            InvalidateBar();
        }

        private void InvalidateBar() { Invalidate(new Rectangle(Width - Theme.D(14), 0, Theme.D(14), Height)); }

        public bool Wheel(int delta)
        {
            var vis = VisibleRows();
            int total = vis.Count, shown = Shown;
            if (total <= shown) return false;
            int first = FirstOrdinal(vis), lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int want = first - Math.Sign(delta) * lines * Math.Max(1, Math.Abs(delta) / 120);
            want = Math.Max(0, Math.Min(total - shown, want));
            if (want == first) return false;       // at the top / bottom: the page takes the wheel
            SetFirst(want);
            return true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Wheel(e.Delta)) { if (e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true; }
        }

        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); InvalidateBar(); }
        protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e) { base.OnRowsAdded(e); InvalidateBar(); }
        protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e) { base.OnRowsRemoved(e); InvalidateBar(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); InvalidateBar(); }

        private bool OverBar(Point p, List<int> vis) { return !Thumb(vis).IsEmpty && p.X >= Width - Theme.D(14) && p.Y >= (ColumnHeadersVisible ? ColumnHeadersHeight : 0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var vis = VisibleRows();
            if (e.Button == MouseButtons.Left && OverBar(e.Location, vis))
            {
                var t = Thumb(vis);
                if (e.Y >= t.Top && e.Y < t.Bottom) { _drag = true; _dragY = e.Y; _dragFirst = FirstOrdinal(vis); Capture = true; InvalidateBar(); }
                else SetFirst(FirstOrdinal(vis) + (e.Y < t.Top ? -Shown : Shown));
                return;                                  // the bar strip never selects the cell under it
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag)
            {
                var vis = VisibleRows(); var t = Thumb(vis);
                int top = ColumnHeadersVisible ? ColumnHeadersHeight : 0, track = Height - top - Theme.D(8) - t.Height, range = vis.Count - Shown;
                if (track > 0 && range > 0) SetFirst(_dragFirst + (int)Math.Round((e.Y - _dragY) * (range / (double)track)));
                return;
            }
            bool hot = OverBar(e.Location, VisibleRows());
            if (hot != _barHot) { _barHot = hot; InvalidateBar(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drag) { _drag = false; Capture = false; InvalidateBar(); return; }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (_barHot) { _barHot = false; InvalidateBar(); } base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var t = Thumb(VisibleRows());
            if (t.IsEmpty) return;
            float w = _barHot || _drag ? Theme.Df(6) : Theme.Df(4);
            ThinScrollBar.PaintThumb(e.Graphics, new RectangleF(t.Right - Theme.Df(3) - w, t.Y, w, t.Height), _barHot || _drag ? 1f : 0f, _drag ? 1f : 0f);
        }

        // ---- drop-down cells ----
        protected override void OnCellMouseClick(DataGridViewCellMouseEventArgs e)
        {
            base.OnCellMouseClick(e);
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0) OpenCell(e.RowIndex, e.ColumnIndex);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var c = CurrentCell;
            if (c != null && c is DropCell && (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode == Keys.Down)))
            { OpenCell(c.RowIndex, c.ColumnIndex); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        public void OpenCell(int row, int col)
        {
            var cell = Rows[row].Cells[col] as DropCell;
            if (cell == null || ReadOnly || cell.ReadOnly || Columns[col].ReadOnly) return;
            var items = cell.Choices;
            if (items.Count == 0) return;
            var texts = items.Select(o => Convert.ToString(o)).ToList();
            int sel = texts.IndexOf(Convert.ToString(cell.Value));
            var rect = GetCellDisplayRectangle(col, row, false);
            var p = new DropPopup(texts, sel, rect.Width, Font);
            p.Picked += i =>
            {
                if (cell.DataGridView == null) return;
                if (!Equals(cell.Value, items[i])) cell.Value = items[i];      // raises CellValueChanged like a committed combo edit
            };
            p.Closed += (s, e) =>
            {
                if ((e.CloseReason == ToolStripDropDownCloseReason.ItemClicked || e.CloseReason == ToolStripDropDownCloseReason.Keyboard) && CanFocus) Focus();
                if (IsHandleCreated) BeginInvoke((Action)(() => p.Dispose()));
            };
            p.ShowFor(this, rect);
        }
    }

    public sealed class DropColumn : DataGridViewColumn
    {
        public readonly List<object> Items = new List<object>();
        public DropColumn() : base(new DropCell()) { SortMode = DataGridViewColumnSortMode.NotSortable; }
        public override object Clone() { var c = (DropColumn)base.Clone(); c.Items.AddRange(Items); return c; }
    }

    // A text cell whose value is picked from a list (no editing control: the DropPopup list sets Value).
    public sealed class DropCell : DataGridViewTextBoxCell
    {
        public readonly List<object> Items = new List<object>();        // own items (Theme.AddRow); empty = the column's
        public override Type EditType { get { return null; } }
        public List<object> Choices
        {
            get
            {
                if (Items.Count > 0) return Items;
                var col = OwningColumn as DropColumn;
                return col != null ? col.Items : Items;
            }
        }
        public override object Clone() { var c = (DropCell)base.Clone(); c.Items.AddRange(Items); return c; }
    }

    // ================================================================== wheel router
    // One rule for the whole app (IMessageFilter on WM_MOUSEWHEEL): the wheel goes to the scrollable under the pointer.
    // Closed drop-downs and unfocused steppers never take it; grids, the log and text viewers pass it on at their
    // ends; an open list owns it. Page bodies scroll through their native range (no layout pass per tick).
    internal sealed class WheelRouter : IMessageFilter
    {
        private static bool _on;
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        private static bool _noUser32;

        public static void Install()
        {
            if (_on) return;
            _on = true;
            try { WindowFromPoint(Point.Empty); } catch { _noUser32 = true; }
            Application.AddMessageFilter(new WheelRouter());
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;                 // WM_MOUSEWHEEL
            try
            {
                long wp = m.WParam.ToInt64(), lp = m.LParam.ToInt64();
                int delta = (short)((wp >> 16) & 0xFFFF);
                // lParam is the screen point on Windows; mono passes client coordinates, so read the cursor itself
                var pt = _noUser32 ? Control.MousePosition : new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                if ((Control.ModifierKeys & (Keys.Control | Keys.Shift)) != 0) return false;   // zoom / horizontal: native

                var pop = DropPopup.Current;
                if (pop != null && !pop.IsDisposed && pop.Visible)
                {
                    if (pop.Bounds.Contains(pt)) { pop.View.Wheel(delta); return true; }
                    pop.Close(ToolStripDropDownCloseReason.AppClicked);
                }

                Control c = Under(pt);
                if (c == null) return false;
                if (c is ToolStrip || c is ToolStripDropDown) return false;
                for (; c != null; c = c.Parent)
                {
                    var w = c as IWheel;
                    if (w != null) { if (w.Wheel(delta)) return true; continue; }
                    var tb = c as TextBoxBase;
                    if (tb != null && tb.Multiline) return false;                   // native multi-line text keeps its wheel
                    var sc = c as ScrollableControl;
                    if (sc != null && sc.AutoScroll && sc.VerticalScroll.Visible)
                    {
                        Smooth.By(sc, -delta * Theme.K);
                        return true;
                    }
                    if (c is Form) break;
                }
                return true;                                      // nothing scrolls here: no inner control takes it either
            }
            catch { return false; }
        }

        private static Control Under(Point pt)
        {
            if (!_noUser32)
            {
                try
                {
                    IntPtr h = WindowFromPoint(pt);
                    if (h == IntPtr.Zero) return null;
                    return Control.FromChildHandle(h);
                }
                catch (DllNotFoundException) { _noUser32 = true; }
                catch (EntryPointNotFoundException) { _noUser32 = true; }
            }
            // portable path (mono / tests): top-most of our forms under the pointer, then down the child tree
            Form f = null;
            var act = Form.ActiveForm;
            if (act != null && act.Visible && act.Bounds.Contains(pt)) f = act;
            else for (int i = Application.OpenForms.Count - 1; i >= 0; i--) { var o = Application.OpenForms[i]; if (o.Visible && o.Bounds.Contains(pt)) { f = o; break; } }
            if (f == null) return null;
            Control c = f;
            while (true)
            {
                var ch = c.GetChildAtPoint(c.PointToClient(pt), GetChildAtPointSkip.Invisible);
                if (ch == null) break;
                c = ch;
            }
            return c;
        }
    }

    // Eased wheel scrolling for page bodies: the target accumulates, a 10 ms timer moves 35 % of the rest per tick.
    // Only the native scroll value changes (ScrollWindowEx inside WinForms): no layout pass.
    internal static class Smooth
    {
        private static readonly Dictionary<ScrollableControl, float> _target = new Dictionary<ScrollableControl, float>();
        private static Timer _t;

        public static void By(ScrollableControl sc, float dy)
        {
            var vs = sc.VerticalScroll;
            int max = Math.Max(0, vs.Maximum - vs.LargeChange + 1);
            float cur;
            if (!_target.TryGetValue(sc, out cur)) cur = vs.Value;
            cur = Math.Max(0, Math.Min(max, cur + dy));
            _target[sc] = cur;
            if (_t == null) { _t = new Timer { Interval = 10 }; _t.Tick += (s, e) => Tick(); }
            if (!_t.Enabled) _t.Start();
        }

        private static void Tick()
        {
            foreach (var sc in _target.Keys.ToList())
            {
                if (sc.IsDisposed || !sc.VerticalScroll.Visible) { _target.Remove(sc); continue; }
                float want = _target[sc];
                int now = sc.VerticalScroll.Value;
                float step = (want - now) * 0.35f;
                int next = Math.Abs(want - now) < 1.5f ? (int)Math.Round(want) : now + (int)Math.Round(step == 0 ? Math.Sign(want - now) : step);
                if (next == now && Math.Abs(want - now) < 1.5f) { _target.Remove(sc); continue; }
                ThinScrollBar.SetV(sc, next);
                if (sc.VerticalScroll.Value == now) _target.Remove(sc);      // clamped: stop
            }
            if (_target.Count == 0) _t.Stop();
        }
    }
}
