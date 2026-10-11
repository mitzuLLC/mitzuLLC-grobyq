using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Grobyq
{
    // Sidebar item of the WinForms build: line icon, label, rounded highlight (CardHi when selected, #111115 on hover)
    // and a 3 x 16 accent bar on the selected item; an accent dot (badge) when something new waits behind it.
    public sealed class NavItem : Border
    {
        public readonly string Key;
        private readonly Border _bg, _bar;
        private readonly TextBlock _text;
        private readonly Ellipse _badge;
        private bool _sel, _hot;
        public event Action Clicked;

        public NavItem(string key, string text, string icon)
        {
            Key = key;
            Height = 36;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;

            var g = new Grid();
            _bg = new Border { Margin = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
            _bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = Ui.B("Acc"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), Visibility = Visibility.Collapsed };
            var ico = new Ico { Kind = icon, Width = 16, Height = 16, Stroke = Ui.B("Fg"), StrokeWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(50, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _text = Ui.Text(text, 10, "Fg", false);
            _text.VerticalAlignment = VerticalAlignment.Center;
            _badge = new Ellipse { Width = 7, Height = 7, Fill = Ui.B("Acc"), Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            row.Children.Add(_text);
            row.Children.Add(_badge);

            g.Children.Add(_bg);
            g.Children.Add(_bar);
            g.Children.Add(ico);
            g.Children.Add(row);
            Child = g;
            if (text != null && text.Length > 30) ToolTip = text;

            MouseEnter += (s, e) => { _hot = true; Refresh(); };
            MouseLeave += (s, e) => { _hot = false; Refresh(); };
            MouseLeftButtonUp += (s, e) => { var h = Clicked; if (h != null) h(); };
        }

        public bool Selected { get { return _sel; } set { _sel = value; Refresh(); } }
        public bool Badge { set { _badge.Visibility = value ? Visibility.Visible : Visibility.Collapsed; } }

        private void Refresh()
        {
            _bg.Background = _sel ? Ui.B("CardHi") : _hot ? Ui.B("Hot") : Brushes.Transparent;
            _bar.Visibility = _sel ? Visibility.Visible : Visibility.Collapsed;
            _text.Foreground = _sel ? Brushes.White : Ui.B("Fg");
        }
    }

    // Group caption in the sidebar: hairline on top, Semi 10 pt label.
    public sealed class NavHeader : Grid
    {
        public NavHeader(string text)
        {
            Height = 46;
            var line = new Border { Height = 1, Background = Ui.B("Line"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), SnapsToDevicePixels = true };
            var t = Ui.Semi(text, 10, "Fg");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(16, 10, 0, 0);
            Children.Add(line);
            Children.Add(t);
        }
    }
}
