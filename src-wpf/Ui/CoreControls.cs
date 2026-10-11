using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Grobyq
{
    // Combo item: a fixed stored value with translated text.
    public sealed class Opt
    {
        public string Value, Text;
        public Opt(string value, string text) { Value = value; Text = text; }
        public override string ToString() { return Text; }
    }

    public static class Combo
    {
        public static ComboBox New(double width, IEnumerable<Opt> items = null)
        {
            var c = new ComboBox { Style = Ui.S("Combo"), Width = width };
            if (items != null) foreach (var o in items) c.Items.Add(o);
            return c;
        }

        public static string Val(ComboBox c) { var o = c.SelectedItem as Opt; return o != null ? o.Value : c.SelectedItem == null ? "" : c.SelectedItem.ToString(); }

        public static void Select(ComboBox c, string value)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                var o = c.Items[i] as Opt;
                if (o != null ? string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase) : string.Equals(Convert.ToString(c.Items[i]), value, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; return; }
            }
            if (c.Items.Count > 0 && c.SelectedIndex < 0) c.SelectedIndex = 0;
        }
    }

    // ---------------------------------------------------------------- checkbox (BoxCheck)
    // 20 px rounded square, Edge border; checked = accent fill + white tick; the whole control (box + caption) toggles.
    // Mark = small dot after the caption (a hint: a core of the current game mask, or amber = interrupt LP).
    // Locked = drawn disabled and never toggled, but still shows its tooltip.
    public sealed class BoxCheck : Border
    {
        private readonly Border _box;
        private readonly Path _tick;
        private readonly TextBlock _text;
        private readonly Ellipse _dot;
        private bool _checked, _hot, _locked, _mark;
        private Brush _markBrush;
        public event Action CheckedChanged;

        public BoxCheck(string text)
        {
            Height = 28;
            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(7);
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _box = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.2), Margin = new Thickness(4, 0, 0, 0), SnapsToDevicePixels = true };
            _tick = new Path
            {
                Data = Geometry.Parse("M 4.2,9.2 L 7.6,12.6 L 14,5.4"),
                Stroke = Brushes.White, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Visibility = Visibility.Collapsed
            };
            _box.Child = _tick;
            row.Children.Add(_box);
            _text = Ui.Text(text ?? "", 9.5, "Dim", false);
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.Margin = new Thickness(8, 0, 0, 0);
            if (string.IsNullOrEmpty(text)) _text.Visibility = Visibility.Collapsed;
            row.Children.Add(_text);
            _dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 1, 4, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            row.Children.Add(_dot);
            Child = row;
            MouseEnter += (s, e) => { _hot = true; Paint(); };
            MouseLeave += (s, e) => { _hot = false; Paint(); };
            MouseLeftButtonUp += (s, e) => { if (IsEnabled && !_locked) Checked = !Checked; };
            IsEnabledChanged += (s, e) => Paint();
            Paint();
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Paint(); var h = CheckedChanged; if (h != null) h(); }
        }
        public void SetSilently(bool v) { _checked = v; Paint(); }
        public bool Locked { get { return _locked; } set { _locked = value; Cursor = value ? Cursors.Arrow : Cursors.Hand; Paint(); } }
        public bool Mark { get { return _mark; } set { _mark = value; Paint(); } }
        public Brush MarkBrush { get { return _markBrush; } set { _markBrush = value; Paint(); } }

        private void Paint()
        {
            bool live = IsEnabled && !_locked;
            Brush fill = _checked ? (live ? Ui.B("Acc") : Ui.B("AccDim")) : Ui.B("Input");
            _box.Background = fill;
            _box.BorderBrush = _checked ? fill : (_hot && !_locked) ? Ui.B("EdgeHi") : Ui.B("Edge");
            _tick.Visibility = _checked ? Visibility.Visible : Visibility.Collapsed;
            _tick.Stroke = live ? Brushes.White : Ui.B("Dim");
            _text.Foreground = !live ? Ui.B("Faint") : _checked ? Ui.B("Fg") : Ui.B("Dim");
            Background = _hot && !_locked && _text.Visibility == Visibility.Visible ? Ui.B("CardHi") : Brushes.Transparent;
            _dot.Visibility = _mark ? Visibility.Visible : Visibility.Collapsed;
            _dot.Fill = _markBrush ?? (live ? Ui.B("Acc") : Ui.B("Faint"));
        }
    }

    // ---------------------------------------------------------------- core grid
    // Checkbox grid of logical processors, one column per physical core: "Core N" on top, then the first LP of the core
    // and its SMT sibling below. Cores are grouped by L3 domain (the V-Cache CCD marked) or, on a hybrid CPU, by P-cores /
    // E-cores; each group wraps to new bands when the card is narrow. Mask get/set works on group 0.
    public sealed class CoreGrid : StackPanel
    {
        private readonly Dictionary<int, BoxCheck> _byLp = new Dictionary<int, BoxCheck>();
        private bool _setting;
        public event Action Changed;
        public const double ColW = 104;

        public void Build(Topo t)
        {
            Children.Clear(); _byLp.Clear();
            if (t == null) return;
            var g0 = t.Sets.Where(s => s.Group == 0).ToList();
            var coreNo = new Dictionary<byte, int>();
            foreach (var c in g0.GroupBy(s => s.Core).OrderBy(c => c.Min(s => s.LP))) coreNo[c.Key] = coreNo.Count;
            IEnumerable<IGrouping<int, Native.CpuSet>> parts;
            if (t.Hybrid) parts = g0.GroupBy(s => -(int)s.Eff).OrderBy(g => g.Key);                // P-cores (higher class) first
            else if (t.LLCGroups.Count > 1) parts = g0.GroupBy(s => (int)s.LLC).OrderBy(g => g.Key);
            else parts = g0.GroupBy(s => 0);
            int idx = 0;
            foreach (var part in parts)
            {
                string head = "";
                bool accent = false;
                if (t.Hybrid) head = idx == 0 ? L.T("P-cores") : idx == 1 ? L.T("E-cores") : L.T("LP E-cores");
                else if (t.LLCGroups.Count > 1)
                {
                    byte llc = (byte)part.Key;
                    bool vc = t.VCacheLlc.HasValue && t.VCacheLlc.Value == llc;
                    head = L.F("L3 domain {0}", idx) + "  ·  " + t.L3Text(llc) + (vc ? "  ·  V-Cache" : "");
                    accent = vc;
                }
                if (head.Length > 0)
                {
                    var hl = Ui.Semi(head, 9, accent ? "Acc" : "Dim");
                    hl.Margin = new Thickness(0, idx == 0 ? 0 : 8, 0, 2);
                    Children.Add(hl);
                }
                var wrap = new WrapPanel { ItemWidth = ColW, Orientation = Orientation.Horizontal };
                foreach (var core in part.GroupBy(s => s.Core).OrderBy(c => c.Min(s => s.LP)))
                {
                    var col = new StackPanel { Margin = new Thickness(0, 0, 6, 4) };
                    var ch = Ui.Text(L.F("Core {0}", coreNo[core.Key]), 8.5, "Faint", false);
                    ch.Height = 18; ch.Padding = new Thickness(4, 2, 0, 0);
                    col.Children.Add(ch);
                    foreach (int lp in core.Select(s => (int)s.LP).OrderBy(x => x))
                    {
                        var cb = new BoxCheck("CPU " + lp) { Margin = new Thickness(0, 0, 0, 2) };
                        cb.CheckedChanged += () => { if (!_setting) Raise(); };
                        _byLp[lp] = cb;
                        col.Children.Add(cb);
                    }
                    wrap.Children.Add(col);
                }
                Children.Add(wrap);
                idx++;
            }
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

        // set without raising Changed (loading the saved state)
        public void Load(ulong value)
        {
            _setting = true;
            try { foreach (var kv in _byLp) kv.Value.Checked = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; }
            finally { _setting = false; }
        }

        public bool IsEmpty { get { return _byLp.Count == 0; } }
        public ulong AllMask { get { ulong m = 0; foreach (var k in _byLp.Keys) if (k < 64) m |= 1UL << k; return m; } }

        // accent dot on the LPs of this mask (the current game cores), as a picking hint
        public ulong MarkMask { set { foreach (var kv in _byLp) { kv.Value.MarkBrush = null; kv.Value.Mark = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; } } }

        // dot in a colour of its own on these LPs (the game-mask grid: interrupt LPs, amber); overrides MarkMask there
        public void MarkWith(ulong mask, Brush b)
        {
            foreach (var kv in _byLp)
            {
                bool on = kv.Key < 64 && ((mask >> kv.Key) & 1) != 0;
                if (on) { kv.Value.MarkBrush = b; kv.Value.Mark = true; }
            }
        }

        // LPs that cannot be ticked here (taken by an automatic rule); the rest stay editable
        public ulong LockedMask { set { foreach (var kv in _byLp) kv.Value.Locked = kv.Key < 64 && ((value >> kv.Key) & 1) != 0; } }

        public void TipFor(int lp, string text)
        {
            BoxCheck cb;
            if (_byLp.TryGetValue(lp, out cb)) cb.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        public void SetEnabled(bool on) { foreach (var cb in _byLp.Values) cb.IsEnabled = on; }

        public static string Sig(Topo t) { return t == null ? "" : string.Join(",", t.Sets.Select(x => x.Group + ":" + x.LP + ":" + x.Core + ":" + x.LLC + ":" + x.Eff)); }
    }

    // ---------------------------------------------------------------- tabs
    // A tab strip: hover lights the tab, the selected one is white with an accent bar under it, a hairline below all.
    public sealed class Tabs : Border
    {
        private sealed class Tab { public string Key; public Border Head; public TextBlock Text; public Border Bar; }
        private readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly List<Tab> _tabs = new List<Tab>();
        public string Selected { get; private set; }
        public event Action<string> Changed;

        public Tabs()
        {
            Height = 42;
            BorderBrush = Ui.B("Line");
            BorderThickness = new Thickness(0, 0, 0, 1);
            SnapsToDevicePixels = true;
            Child = _row;
        }

        public void AddTab(string key, string title)
        {
            var t = new Tab { Key = key };
            var g = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 4, 0) };
            t.Head = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(1, 4, 1, 6), Background = Brushes.Transparent };
            t.Text = Ui.Semi(title, 10, "Dim");
            t.Text.Margin = new Thickness(14, 0, 14, 4);
            t.Text.VerticalAlignment = VerticalAlignment.Center;
            t.Bar = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ui.B("Acc"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 0), Visibility = Visibility.Collapsed };
            g.Children.Add(t.Head);
            g.Children.Add(t.Text);
            g.Children.Add(t.Bar);
            g.MouseEnter += (s, e) => { if (Selected != key) t.Head.Background = Ui.B("CardHi"); };
            g.MouseLeave += (s, e) => t.Head.Background = Brushes.Transparent;
            g.MouseLeftButtonUp += (s, e) => Select(key);
            _row.Children.Add(g);
            _tabs.Add(t);
            if (Selected == null) Selected = key;
            Paint();
        }

        public void Select(string key)
        {
            if (!_tabs.Any(x => x.Key == key) || Selected == key) return;
            Selected = key;
            Paint();
            var h = Changed; if (h != null) h(key);
        }

        private void Paint()
        {
            foreach (var t in _tabs)
            {
                bool sel = t.Key == Selected;
                t.Text.Foreground = sel ? Brushes.White : Ui.B("Dim");
                t.Bar.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
                if (sel) t.Head.Background = Brushes.Transparent;
            }
        }
    }

    // ---------------------------------------------------------------- key / value grid
    public sealed class InfoGrid : Grid
    {
        public double KeyWidth = 180;
        private readonly List<TextBlock> _vals = new List<TextBlock>();

        public void Set(IEnumerable<KeyValuePair<string, string>> rows)
        {
            Children.Clear(); RowDefinitions.Clear(); ColumnDefinitions.Clear(); _vals.Clear();
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = KeyWidth });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (var kv in rows)
            {
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = Ui.Text(kv.Key, 9, "Dim", false);
                k.Margin = new Thickness(0, 3, 18, 3); k.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(k, i);
                Children.Add(k);
                var v = Ui.Text(kv.Value ?? "", 9.5, "Fg", true);
                v.Margin = new Thickness(0, 3, 0, 3);
                Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                Children.Add(v);
                _vals.Add(v);
                i++;
            }
        }

        public void Tint(int row, Brush b) { if (row >= 0 && row < _vals.Count) _vals[row].Foreground = b; }
    }

    // ---------------------------------------------------------------- CPU map
    // One tile per logical processor, grouped by physical core and L3 domain. Accent = game mask, amber outline =
    // background, hatched = ReservedCpuSets, violet outline = interrupt target, E = efficiency core.
    public sealed class CpuMap : FrameworkElement
    {
        public Topo Topo;
        public ulong Game, Bgm, Highlight, SplitGame, SplitNon, Reserved;
        public static readonly Color TargetC = Color.FromRgb(200, 120, 255);
        public static readonly Color NonGameC = Color.FromRgb(52, 190, 170);
        private const double Tile = 30;

        public void Set(Topo t, ulong game, ulong bg) { Topo = t; Game = game; Bgm = bg; InvalidateMeasure(); InvalidateVisual(); }
        public void Overlay(ulong splitGame, ulong splitNon, ulong reserved) { SplitGame = splitGame; SplitNon = splitNon; Reserved = reserved; InvalidateMeasure(); InvalidateVisual(); }

        private double Strip { get { return SplitGame != 0 || SplitNon != 0 ? 4 : 0; } }

        private List<KeyValuePair<byte, List<List<Native.CpuSet>>>> Domains()
        {
            var res = new List<KeyValuePair<byte, List<List<Native.CpuSet>>>>();
            foreach (var llc in Topo.LLCGroups)
            {
                var cores = Topo.Sets.Where(s => s.LLC == llc).GroupBy(s => s.Core).OrderBy(g => g.Min(s => s.LP)).Select(g => g.OrderBy(s => s.LP).ToList()).ToList();
                res.Add(new KeyValuePair<byte, List<List<Native.CpuSet>>>(llc, cores));
            }
            return res;
        }

        private sealed class Leg { public Color C; public int Look; public string Text; }   // 0 fill, 1 outline, 2 strip, 3 hatch, 4 text

        private List<Leg> Legend()
        {
            var l = new List<Leg>();
            l.Add(new Leg { C = C("Acc"), Look = 0, Text = L.T("game mask") });
            l.Add(new Leg { C = C("Warn"), Look = 1, Text = L.T("background") });
            l.Add(new Leg { C = C("Input"), Look = 0, Text = L.T("unused") });
            if (SplitGame != 0) l.Add(new Leg { C = C("Acc"), Look = 2, Text = L.T("game set") });
            if (SplitNon != 0) l.Add(new Leg { C = NonGameC, Look = 2, Text = L.T("non-game set") });
            if (Reserved != 0) l.Add(new Leg { C = C("Warn"), Look = 3, Text = L.T("reserved (ReservedCpuSets)") });
            if (Highlight != 0) l.Add(new Leg { C = TargetC, Look = 1, Text = L.T("interrupt target") });
            if (Topo != null && Topo.Hybrid) l.Add(new Leg { Look = 4, Text = L.T("E = efficiency core") });
            return l;
        }

        private static Color C(string key) { var b = Ui.B(key) as SolidColorBrush; return b != null ? b.Color : Colors.Gray; }
        private static Color Mix(Color a, Color b, double t) { return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t)); }

        private FormattedText Ft(string s, double pt, Color c, bool semi = false)
        {
            double dip = 1.0;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            var tf = new Typeface(new FontFamily(L.Family), FontStyles.Normal, semi ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
            return new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, Ui.Pt(pt), new SolidColorBrush(c), dip);
        }

        private double Layout(double width, DrawingContext dc)
        {
            if (Topo == null || width < 50) return 40;
            byte maxEff = Topo.Sets.Max(s => s.Eff);
            byte firstCore = Topo.FirstCore();
            var bg = new SolidColorBrush(C("Bg")); var input = new SolidColorBrush(C("Input")); var acc = new SolidColorBrush(C("Acc"));
            var line = new Pen(new SolidColorBrush(C("Line")), 1); var faint = new Pen(new SolidColorBrush(C("Faint")), 1);
            var warnPen = new Pen(new SolidColorBrush(C("Warn")), 1.6); var targetPen = new Pen(new SolidColorBrush(TargetC), 2.4);
            Brush hatch = Hatch();
            double y = 0; int idx = 0;
            foreach (var dom in Domains())
            {
                string head = Topo.LLCGroups.Count > 1 ? L.F("L3 domain {0}", idx) + "  ·  " + Topo.L3Text(dom.Key) : "L3  ·  " + Topo.L3Text(dom.Key);
                if (Topo.VCacheLlc.HasValue && Topo.VCacheLlc.Value == dom.Key) head += "  ·  V-Cache";
                ulong dm = Masks.Mask(dom.Value.SelectMany(c => c));
                Color hc = C("Dim");
                if (SplitGame != 0 && (dm & ~SplitGame) == 0) { head += "  ·  " + L.T("GAME SET"); hc = Mix(C("Dim"), C("Acc"), 0.6); }
                else if (SplitNon != 0 && (dm & ~SplitNon) == 0) { head += "  ·  " + L.T("NON-GAME SET"); hc = Mix(C("Dim"), NonGameC, 0.6); }
                if (dc != null) dc.DrawText(Ft(head, 8.75, hc, true), new Point(0, y));
                y += 20;
                double x = 0;
                foreach (var core in dom.Value)
                {
                    double cw = core.Count * Tile + (core.Count - 1) * 3 + 8;
                    if (x + cw > width) { x = 0; y += Tile + Strip + 14; }
                    if (dc != null)
                    {
                        bool eCore = Topo.Hybrid && core[0].Eff < maxEff;
                        ulong cm = Masks.Mask(core);
                        dc.DrawRoundedRectangle(bg, core[0].Core == firstCore ? faint : line, new Rect(x + 0.5, y + 0.5, cw - 1, Tile + 8 + Strip - 1), 6, 6);
                        if (Strip > 0)
                        {
                            Color? sc = (cm & ~SplitGame) == 0 ? C("Acc") : (cm & ~SplitNon) == 0 ? NonGameC : (Color?)null;
                            if (sc.HasValue) dc.DrawRoundedRectangle(new SolidColorBrush(sc.Value), null, new Rect(x + 5, y + Tile + 6, cw - 10, 3), 1.5, 1.5);
                        }
                        double lx = x + 4;
                        foreach (var s in core)
                        {
                            bool inGame = ((Game >> s.LP) & 1) != 0, inBg = ((Bgm >> s.LP) & 1) != 0, hi = ((Highlight >> s.LP) & 1) != 0, res = ((Reserved >> s.LP) & 1) != 0;
                            var tr = new Rect(lx, y + 4, Tile, Tile);
                            dc.DrawRoundedRectangle(inGame ? acc : res ? hatch : input, null, tr, 4, 4);
                            if (inBg) dc.DrawRoundedRectangle(null, warnPen, tr, 4, 4);
                            if (hi) dc.DrawRoundedRectangle(null, targetPen, tr, 4, 4);
                            var ft = Ft(s.LP.ToString(CultureInfo.InvariantCulture), 8.5, inGame ? Colors.White : hi ? TargetC : C("Dim"), true);
                            dc.DrawText(ft, new Point(tr.X + (Tile - ft.Width) / 2, tr.Y + (Tile - ft.Height) / 2));
                            if (eCore) dc.DrawText(Ft("E", 6.5, inGame ? Colors.White : C("Warn")), new Point(tr.Right - 9, tr.Y));
                            lx += Tile + 3;
                        }
                    }
                    x += cw + 8;
                }
                y += Tile + Strip + 22;
                idx++;
            }
            // legend, wrapped to the width
            double lx2 = 0, ly = y;
            foreach (var it in Legend())
            {
                var ft = Ft(it.Text, 8.5, C("Dim"));
                double w = (it.Look == 4 ? 0 : 16) + ft.Width + 18;
                if (lx2 > 0 && lx2 + w > width) { lx2 = 0; ly += 20; }
                if (dc != null)
                {
                    if (it.Look != 4)
                    {
                        var r = new Rect(lx2, ly + 3, 12, 12);
                        var cb = new SolidColorBrush(it.C);
                        switch (it.Look)
                        {
                            case 0: dc.DrawRoundedRectangle(cb, null, r, 3, 3); break;
                            case 1: dc.DrawRoundedRectangle(input, new Pen(cb, 1.6), r, 3, 3); break;
                            case 2: dc.DrawRoundedRectangle(bg, null, r, 3, 3); dc.DrawRoundedRectangle(cb, null, new Rect(r.X + 1, r.Bottom - 4, r.Width - 2, 3), 1.5, 1.5); break;
                            case 3: dc.DrawRoundedRectangle(hatch, null, r, 3, 3); break;
                        }
                        dc.DrawText(ft, new Point(lx2 + 16, ly));
                    }
                    else dc.DrawText(ft, new Point(lx2, ly));
                }
                lx2 += w;
            }
            return ly + 20 + 6;
        }

        private Brush Hatch()
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(C("Input")), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            dg.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Mix(C("Input"), C("Warn"), 0.45)), 2), new LineGeometry(new Point(0, 8), new Point(8, 0))));
            return new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
        }

        protected override Size MeasureOverride(Size avail)
        {
            double w = double.IsInfinity(avail.Width) ? 700 : avail.Width;
            return new Size(w, Layout(w, null));
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Topo == null) { dc.DrawText(Ft(L.T("CPU topology unavailable"), 9, C("Dim")), new Point(0, 0)); return; }
            Layout(ActualWidth, dc);
        }
    }
}

namespace Grobyq
{
    // Vain-style checkable row (cleanup lists): bold title + grey description (+ amber warning) on the left, a value
    // column (size / "found") and a big checkbox on the right. The whole row toggles; hover lights the row.
    public sealed class CheckRow : System.Windows.Controls.Border
    {
        private bool _checked, _hot, _divider = true;
        private readonly System.Windows.Controls.TextBlock _value;
        private readonly System.Windows.Controls.Border _box, _line;
        private readonly System.Windows.Shapes.Path _tick;
        public event System.Action CheckedChanged;
        public object Data;

        public CheckRow(string title, string desc, string note = null, bool caution = false)
        {
            CornerRadius = new System.Windows.CornerRadius(8);
            Cursor = System.Windows.Input.Cursors.Hand;
            Background = System.Windows.Media.Brushes.Transparent;
            var root = new System.Windows.Controls.Grid();
            var g = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(10, 8, 14, 8) };
            g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(150) });
            g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            var text = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 24, 0) };
            text.Children.Add(Ui.Semi(title, 10, caution ? "Warn" : "Fg"));
            if (!string.IsNullOrEmpty(desc)) { var d = Ui.Text(desc, 8.75, "Dim"); d.Margin = new System.Windows.Thickness(0, 1, 0, 0); text.Children.Add(d); }
            if (!string.IsNullOrEmpty(note)) { var n = Ui.Text("⚠ " + note, 8.75, "Warn"); n.Margin = new System.Windows.Thickness(0, 2, 0, 0); text.Children.Add(n); }
            g.Children.Add(text);
            _value = Ui.Text("", 9.5, "Fg", false);
            _value.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            _value.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            _value.Margin = new System.Windows.Thickness(0, 0, 12, 0);
            System.Windows.Controls.Grid.SetColumn(_value, 1);
            g.Children.Add(_value);
            _box = new System.Windows.Controls.Border { Width = 20, Height = 20, CornerRadius = new System.Windows.CornerRadius(5), BorderThickness = new System.Windows.Thickness(1.2), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            _tick = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M 4.2,9.2 L 7.6,12.6 L 14,5.4"),
                Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 2,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
            };
            _box.Child = _tick;
            System.Windows.Controls.Grid.SetColumn(_box, 2);
            g.Children.Add(_box);
            _line = new System.Windows.Controls.Border { Height = 1, Background = Ui.B("Line"), VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new System.Windows.Thickness(10, 0, 10, 0) };
            root.Children.Add(g);
            root.Children.Add(_line);
            Child = root;
            MouseEnter += (s, e) => { _hot = true; Paint(); };
            MouseLeave += (s, e) => { _hot = false; Paint(); };
            MouseLeftButtonUp += (s, e) => { if (IsEnabled) Checked = !Checked; };
            IsEnabledChanged += (s, e) => Paint();
            Paint();
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Paint(); var h = CheckedChanged; if (h != null) h(); }
        }
        public string Value { get { return _value.Text; } set { _value.Text = value ?? ""; } }
        public System.Windows.Media.Brush ValueBrush { set { _value.Foreground = value; } }
        public bool Divider { set { _divider = value; Paint(); } }

        private void Paint()
        {
            Background = _hot ? Ui.B("CardHi") : System.Windows.Media.Brushes.Transparent;
            System.Windows.Media.Brush fill = _checked ? (IsEnabled ? Ui.B("Acc") : Ui.B("AccDim")) : Ui.B("Input");
            _box.Background = fill;
            _box.BorderBrush = _checked ? fill : _hot ? Ui.B("EdgeHi") : Ui.B("Edge");
            _tick.Visibility = _checked ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _line.Visibility = _divider && !_hot ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
    }
}
