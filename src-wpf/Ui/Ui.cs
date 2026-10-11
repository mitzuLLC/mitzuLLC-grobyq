using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Grobyq
{
    // UI building blocks of the WinForms build (Card, SettingRow, Btn, Toggle, TextBlock, LinkCard, ...) as WPF elements,
    // so the pages are built in code the same way the v3 pages were. Sizes are the v3 point sizes (pt -> px = * 96/72).
    public static class Ui
    {
        public static Brush B(string key) { return (Brush)Application.Current.Resources[key]; }
        public static Style S(string key) { return (Style)Application.Current.Resources[key]; }
        public static double Pt(double pt) { return Math.Round(pt * 96.0 / 72.0, 2); }

        public static readonly Brush White = Brushes.White;

        // ---------------------------------------------------------------- text
        public static TextBlock Text(string text, double pt, string brush = "Fg", bool wrap = true)
        {
            var t = new TextBlock { Text = text ?? "", FontSize = Pt(pt), Foreground = B(brush) };
            if (wrap) t.TextWrapping = TextWrapping.Wrap;
            else { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; }
            return t;
        }
        public static TextBlock Semi(string text, double pt, string brush = "Fg", bool wrap = false)
        {
            var t = Text(text, pt, brush, wrap); t.FontWeight = FontWeights.SemiBold; return t;
        }
        public static TextBlock Bold(string text, double pt, string brush = "Fg", bool wrap = false)
        {
            var t = Text(text, pt, brush, wrap); t.FontWeight = FontWeights.Bold; return t;
        }

        // ---------------------------------------------------------------- images (Assets, pack URI)
        private static readonly Dictionary<string, BitmapImage> _img = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        public static BitmapImage Img(string name)
        {
            BitmapImage b;
            if (_img.TryGetValue(name, out b)) return b;
            try
            {
                b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri("pack://application:,,,/Assets/" + name, UriKind.Absolute);
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();
            }
            catch { b = null; }
            _img[name] = b;
            return b;
        }

        public static Image Picture(string name, double height)
        {
            var i = new Image { Source = Img(name), Height = height, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
            return i;
        }

        // ---------------------------------------------------------------- controls
        public static Button Btn(string text, string style = "BtnNormal", double minWidth = 0)
        {
            var b = new Button { Content = text, Style = S(style) };
            if (minWidth > 0) b.MinWidth = minWidth;
            return b;
        }

        public static CheckBox Switch(bool on)
        {
            return new CheckBox { Style = S("Switch"), IsChecked = on };
        }

        public static TextBox Box(string text, double width)
        {
            return new TextBox { Style = S("Box"), Text = text ?? "", Width = width };
        }

        // Button caption confirms the action for a moment, then returns ("Copied").
        private static readonly HashSet<Button> _flashing = new HashSet<Button>();
        public static void Flash(Button b, string text)
        {
            if (b == null || !_flashing.Add(b)) return;
            object was = b.Content;
            b.Content = text;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            t.Tick += (s, e) => { t.Stop(); _flashing.Remove(b); b.Content = was; };
            t.Start();
        }

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
        }

        // ---------------------------------------------------------------- setting row
        // Title + explanation on the left, the control on the right (on the title line), hairline below.
        public static Border Row(string title, string desc, FrameworkElement right, bool divider = true, bool alignTop = true)
        {
            var g = new Grid { MinHeight = string.IsNullOrEmpty(desc) ? 42 : 48 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Margin = new Thickness(0, 9, 24, 9) };
            var t = Semi(title, 10f, "Fg");
            left.Children.Add(t);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = Text(desc, 8.75, "Dim");
                d.Margin = new Thickness(0, 1, 0, 0);
                left.Children.Add(d);
            }
            else left.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(left);

            if (right != null)
            {
                Grid.SetColumn(right, 1);
                if (alignTop && !string.IsNullOrEmpty(desc))
                {
                    // centered on the title line (title line: top 9, ~18 px tall); never closer than 6 px to the top
                    double h = double.IsNaN(right.Height) ? 32 : right.Height;
                    right.VerticalAlignment = VerticalAlignment.Top;
                    right.Margin = new Thickness(0, Math.Max(6, 9 + (18 - h) / 2), 0, 0);
                }
                else right.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(right);
            }

            return new Border
            {
                Child = g,
                BorderBrush = B("Line"),
                BorderThickness = new Thickness(0, 0, 0, divider ? 1 : 0),
                SnapsToDevicePixels = true
            };
        }

        // A strip of controls left to right (buttons), 8 px apart.
        public static StackPanel Strip(double height, params FrameworkElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Height = height, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var c in items) { c.Margin = new Thickness(0, 0, 8, 0); c.VerticalAlignment = VerticalAlignment.Top; p.Children.Add(c); }
            return p;
        }

        public static Border Hairline()
        {
            return new Border { Height = 1, Background = B("Line"), Margin = new Thickness(0, 10, 0, 10), SnapsToDevicePixels = true };
        }

        // 0 = unknown (grey), 1 = ok (green), 2 = offline cache (amber), 3 = bad (red).
        public static Brush DotBrush(int kind)
        {
            return B(kind == 1 ? "Ok" : kind == 2 ? "Warn" : kind == 3 ? "Bad" : "Faint");
        }
    }

    // A rounded card with a vertical stack inside (Card / Box of the WinForms build).
    public sealed class CardBox : Border
    {
        public readonly StackPanel Stack = new StackPanel();
        public CardBox(double radius = 10, double padH = 16, double padV = 8)
        {
            Background = Ui.B("Card");
            BorderBrush = Ui.B("Line");
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(radius);
            Padding = new Thickness(padH, padV, padH, padV);
            SnapsToDevicePixels = true;
            Child = Stack;
        }
        public T Add<T>(T e) where T : UIElement { Stack.Children.Add(e); return e; }
    }

    // ---------------------------------------------------------------- page base
    // The v3 Page: a scrolling column on the canvas (26 px left, 18 px right, 12 px top), optionally a centered column
    // at most ColumnMax wide (Home / About), with the title / section / heading helpers.
    public abstract class PageBase : UserControl
    {
        protected readonly StackPanel Body;
        protected readonly ScrollViewer Scroll;

        protected PageBase(double columnMax = 0)
        {
            Background = Ui.B("Bg");
            Focusable = false;
            Body = new StackPanel { Margin = new Thickness(26, 12, 18, 18) };
            // Stretch + MaxWidth: WPF centers an element whose MaxWidth is smaller than its slot, and it still takes the
            // full width below that (Center alone would shrink the column to its content).
            if (columnMax > 0) Body.MaxWidth = columnMax;
            Scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = Body
            };
            _root = new DockPanel { LastChildFill = true };
            _root.Children.Add(Scroll);
            Content = _root;
        }

        private readonly DockPanel _root;

        // A bar pinned under the scrolling column (outside the scroll area): the Save / Cancel bar of the optimizer pages.
        protected Border Footer(UIElement content)
        {
            var bar = new Border
            {
                Background = Ui.B("Side"),
                BorderBrush = Ui.B("Line"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(26, 10, 18, 10),
                Child = content,
                SnapsToDevicePixels = true
            };
            DockPanel.SetDock(bar, Dock.Bottom);
            _root.Children.Insert(0, bar);
            return bar;
        }

        protected T Add<T>(T e, double bottom = 10) where T : FrameworkElement
        {
            var m = e.Margin;
            e.Margin = new Thickness(m.Left, m.Top, m.Right, bottom);
            Body.Children.Add(e);
            return e;
        }

        // Page title (Semi 13 pt, 30 px).
        protected TextBlock Title(string text)
        {
            var l = Ui.Semi(text, 13f, "Fg");
            l.Height = 30; l.VerticalAlignment = VerticalAlignment.Bottom;
            l.Padding = new Thickness(0, 6, 0, 0);
            l.Margin = new Thickness(0, 4, 0, 6);
            Body.Children.Add(l);
            return l;
        }

        // Settings-style section title (Semi 13 pt; 12 px above, 6 px to the card).
        protected TextBlock Section(string text, bool first)
        {
            var l = Ui.Semi(text, 13f, "Fg");
            l.Padding = new Thickness(0, 6, 0, 0);
            l.Margin = new Thickness(0, first ? 2 : 12, 0, 6);
            Body.Children.Add(l);
            return l;
        }

        // Quiet heading of the column pages (Semi 12 pt).
        protected TextBlock Heading(string text, bool first)
        {
            var l = Ui.Semi(text, 12f, "Fg");
            l.Padding = new Thickness(0, 4, 0, 0);
            l.Margin = new Thickness(0, first ? 2 : 12, 0, 6);
            Body.Children.Add(l);
            return l;
        }

        // Settings card (radius 10, padding 16/8) and column card (radius 12, padding 18/8).
        protected CardBox Card(double bottom = 12) { return Add(new CardBox(10, 16, 8), bottom); }
        protected CardBox Box(double bottom = 8) { return Add(new CardBox(12, 18, 8), bottom); }

        public virtual void OnEnter() { }
        public virtual void OnLeave() { }
        public virtual void OnTick(int tick) { }
    }

    // ---------------------------------------------------------------- link card
    // Community card: icon tile, title + one line, external-link tick; hover lifts the card.
    public sealed class LinkCard : Border
    {
        private readonly Ico _ext;
        public LinkCard(string icon, string title, string sub, string url)
        {
            Height = 58;
            CornerRadius = new CornerRadius(12);
            Background = Ui.B("Card");
            BorderBrush = Ui.B("Line");
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            ToolTip = url;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Ui.B("Input"), Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            if (icon.StartsWith("img:", StringComparison.Ordinal))
            {
                var pic = Ui.Picture(icon.Substring(4), 22);
                pic.Width = 22; pic.HorizontalAlignment = HorizontalAlignment.Center; pic.VerticalAlignment = VerticalAlignment.Center;
                tile.Child = pic;
            }
            else tile.Child = new Ico { Kind = icon, Width = 16, Height = 16, Stroke = Ui.B("Fg"), StrokeWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(tile);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-2, 0, 0, 0) };
            text.Children.Add(Ui.Semi(title, 10.5, "Fg"));
            var s = Ui.Text(sub, 9, "Dim", false); s.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(s);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            _ext = new Ico { Kind = "ext", Width = 14, Height = 14, Stroke = Ui.B("Faint"), StrokeWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetColumn(_ext, 2);
            g.Children.Add(_ext);
            Child = g;

            MouseEnter += (a, b) => { Background = Ui.B("CardHi"); BorderBrush = Ui.B("Edge"); _ext.Stroke = Ui.B("Fg"); };
            MouseLeave += (a, b) => { Background = Ui.B("Card"); BorderBrush = Ui.B("Line"); _ext.Stroke = Ui.B("Faint"); };
            MouseLeftButtonUp += (a, b) => Ui.Open(url);
        }
    }

    // Equal cards in rows of MaxCols (fewer when a card would get narrower than MinCard); an incomplete last row is
    // centered under the full rows.
    public sealed class CardGridPanel : Panel
    {
        public int MaxCols = 4;
        public double MinCard = 176, Gap = 10;

        private int Cols(double width)
        {
            int cols = Math.Max(1, Math.Min(MaxCols, InternalChildren.Count));
            while (cols > 1 && (width - Gap * (cols - 1)) / cols < MinCard) cols = cols > 2 ? cols / 2 : 1;
            return cols;
        }

        protected override Size MeasureOverride(Size avail)
        {
            int n = InternalChildren.Count;
            if (n == 0) return new Size(0, 0);
            double w = double.IsInfinity(avail.Width) ? 800 : avail.Width;
            int cols = Cols(w);
            double cw = Math.Max(0, (w - Gap * (cols - 1)) / cols), y = 0;
            for (int i = 0; i < n; i += cols)
            {
                double rowH = 0;
                for (int k = 0; k < cols && i + k < n; k++)
                {
                    var c = InternalChildren[i + k];
                    c.Measure(new Size(cw, double.PositiveInfinity));
                    rowH = Math.Max(rowH, c.DesiredSize.Height);
                }
                y += rowH + Gap;
            }
            return new Size(w, Math.Max(0, y - Gap));
        }

        protected override Size ArrangeOverride(Size final)
        {
            int n = InternalChildren.Count;
            if (n == 0) return final;
            int cols = Cols(final.Width);
            double cw = Math.Max(0, (final.Width - Gap * (cols - 1)) / cols), y = 0;
            for (int i = 0; i < n; i += cols)
            {
                int cnt = Math.Min(cols, n - i);
                double x0 = (final.Width - (cnt * cw + (cnt - 1) * Gap)) / 2;   // full rows start at 0, the last is centered
                double rowH = 0;
                for (int k = 0; k < cnt; k++) rowH = Math.Max(rowH, InternalChildren[i + k].DesiredSize.Height);
                for (int k = 0; k < cnt; k++) InternalChildren[i + k].Arrange(new Rect(x0 + k * (cw + Gap), y, cw, rowH));
                y += rowH + Gap;
            }
            return final;
        }
    }

    // Known system facts: icon + muted caption + value, two columns (one when narrow); empty values are left out.
    public sealed class FactGrid : UniformGrid
    {
        public FactGrid() { Columns = 2; }

        public void Set(IEnumerable<string[]> rows)
        {
            Children.Clear();
            var list = new List<string[]>();
            foreach (var r in rows) if (r != null && r.Length >= 3 && !string.IsNullOrWhiteSpace(r[2])) list.Add(r);
            if (list.Count == 0)
            {
                Columns = 1;
                var t = Ui.Text(L.T("reading…"), 9.5, "Faint", false);
                t.Height = 48; t.Padding = new Thickness(0, 15, 0, 0);
                Children.Add(t);
                return;
            }
            Columns = 2;
            foreach (var r in list)
            {
                var cell = new Grid { Height = 48 };
                var ic = new Ico { Kind = r[0], Width = 16, Height = 16, Stroke = Ui.B("Dim"), StrokeWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
                cell.Children.Add(ic);
                var cap = Ui.Text(r[1], 8.75, "Dim", false);
                cap.Margin = new Thickness(28, 4, 16, 0); cap.VerticalAlignment = VerticalAlignment.Top;
                cell.Children.Add(cap);
                var val = Ui.Text(r[2], 10, "Fg", false);
                val.Margin = new Thickness(28, 23, 16, 0); val.VerticalAlignment = VerticalAlignment.Top;
                val.ToolTip = r[2];
                ToolTipService.SetInitialShowDelay(val, 600);
                cell.Children.Add(val);
                Children.Add(cell);
            }
        }
    }

    // Label on the left (muted), value on the right; optional status dot before the value. Display-only (no caret).
    public sealed class SheetRow : Grid
    {
        private readonly TextBlock _v;
        private readonly Ellipse _dot;
        public SheetRow(string key, bool dot = false, double keyWidth = 100)
        {
            Height = 30;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var k = Ui.Text(key, 9.5, "Dim", false); k.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(k);
            if (dot)
            {
                _dot = new Ellipse { Width = 8, Height = 8, Fill = Ui.B("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 9, 0) };
                Grid.SetColumn(_dot, 1);
                Children.Add(_dot);
            }
            _v = Ui.Text("", 10, "Fg", false); _v.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_v, 2);
            Children.Add(_v);
        }
        public string Value { get { return _v.Text; } set { _v.Text = value ?? ""; _v.ToolTip = string.IsNullOrEmpty(value) ? null : value; } }
        public int Dot { set { if (_dot != null) _dot.Fill = Ui.DotBrush(value); } }
        public string Tip { set { ToolTip = value; } }
    }
}
