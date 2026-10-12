using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;
using WF = System.Windows.Forms;

namespace Grobyq
{
    // Tray icon: the logo tile + a state dot (red = off, amber = armed, green = active). Right click opens TrayMenu, a
    // small WPF card in the app's own style (the WinForms context menu drew its check mark outside the menu and could
    // not hold switches). WPF has no tray control; the WinForms NotifyIcon runs on the WPF dispatcher's message pump,
    // so its events arrive on the UI thread.
    public sealed class Tray : IDisposable
    {
        private readonly WF.NotifyIcon _icon;
        private readonly Dictionary<int, SD.Icon> _cache = new Dictionary<int, SD.Icon>();
        private TrayMenu _menu;
        private string _state = "OFF", _live = "";
        private bool _game, _sys;

        public event Action OpenRequested, AboutRequested, ExitRequested, DoubleClicked;
        public event Action<bool> GameToggled, SystemToggled;

        private static readonly SD.Color COk = SD.Color.FromArgb(76, 214, 138);
        private static readonly SD.Color CWarn = SD.Color.FromArgb(232, 180, 72);
        private static readonly SD.Color CBad = SD.Color.FromArgb(224, 90, 90);

        public Tray()
        {
            _icon = new WF.NotifyIcon { Text = Brand.Name };
            _icon.Icon = Make(CBad);
            _icon.DoubleClick += (s, e) => { if (_menu != null) _menu.Hide(); var h = DoubleClicked; if (h != null) h(); };
            _icon.MouseUp += (s, e) => { if (e.Button == WF.MouseButtons.Right) ShowMenu(); };
            _icon.Visible = true;
        }

        // Rebuilt after a language switch (the next right click builds it in the new language).
        public void BuildMenu()
        {
            var old = _menu;
            _menu = null;
            if (old != null) { try { old.Close(); } catch { } }
        }

        private void ShowMenu()
        {
            if (_menu == null)
            {
                _menu = new TrayMenu();
                _menu.OpenClicked += () => Fire(OpenRequested);
                _menu.AboutClicked += () => Fire(AboutRequested);
                _menu.ExitClicked += () => Fire(ExitRequested);
                _menu.GameChanged += v => { var h = GameToggled; if (h != null) h(v); };
                _menu.SystemChanged += v => { var h = SystemToggled; if (h != null) h(v); };
            }
            _menu.Sync(_state, _live, _game, _sys);
            _menu.ShowAt(WF.Control.MousePosition);
        }

        private void Fire(Action a)
        {
            if (_menu != null) _menu.Hide();
            if (a != null) a();
        }

        // state: OFF / ARMED / ACTIVE; game / sys = the two optimizer switches
        public void SetState(string state, string live, bool game, bool sys)
        {
            _state = state ?? "OFF"; _live = live ?? ""; _game = game; _sys = sys;
            SD.Color c = _state == "ACTIVE" ? COk : _state == "ARMED" ? CWarn : CBad;
            try { _icon.Icon = Make(c); } catch { }
            string text = Brand.Name + " - " + TrayMenu.StateText(_state) + (_live.Length > 0 ? ": " + _live : "");
            try { _icon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text; } catch { }
            if (_menu != null && _menu.IsVisible) _menu.Sync(_state, _live, _game, _sys);
        }

        public void Balloon(string text)
        {
            try { _icon.ShowBalloonTip(3000, Brand.Name, text, WF.ToolTipIcon.Info); } catch { }
        }

        public void Dispose()
        {
            try { if (_menu != null) _menu.Close(); } catch { }
            try { _icon.Visible = false; } catch { }
            try { _icon.Dispose(); } catch { }
        }

        // The logo tile + a status dot, drawn at the size the shell asks for.
        private SD.Icon Make(SD.Color dot)
        {
            int key = dot.ToArgb();
            SD.Icon ic;
            if (_cache.TryGetValue(key, out ic)) return ic;
            int sz = 16;
            try { sz = WF.SystemInformation.SmallIconSize.Width; } catch { }
            string asset = sz >= 30 ? "ico_32.png" : sz >= 23 ? "ico_24.png" : sz >= 19 ? "ico_20.png" : "ico_16.png";
            using (var bmp = new SD.Bitmap(sz, sz))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                    g.InterpolationMode = SD2.InterpolationMode.HighQualityBicubic;
                    g.Clear(SD.Color.Transparent);
                    using (var src = LoadPng(asset)) if (src != null) g.DrawImage(src, 0, 0, sz, sz);
                    float d = sz * 0.46f, x = sz - d, y = sz - d;
                    using (var ring = new SD.SolidBrush(SD.Color.FromArgb(255, 10, 10, 13))) g.FillEllipse(ring, x - 1, y - 1, d + 1, d + 1);
                    using (var br = new SD.SolidBrush(dot)) g.FillEllipse(br, x + 0.5f, y + 0.5f, d - 2, d - 2);
                }
                ic = SD.Icon.FromHandle(bmp.GetHicon());
            }
            _cache[key] = ic;
            return ic;
        }

        private static SD.Bitmap LoadPng(string name)
        {
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name, UriKind.Absolute));
                if (info == null) return null;
                var ms = new System.IO.MemoryStream();
                using (var s = info.Stream) s.CopyTo(ms);
                ms.Position = 0;
                return new SD.Bitmap(ms);
            }
            catch { return null; }
        }
    }

    // The tray menu card: a header (state; click = open the panel), the two optimizer switches (the Settings switch, the
    // whole row toggles), About and Exit. Equal rows, the app font, closes on a click elsewhere or Esc.
    internal sealed class TrayMenu : Window
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
        private const double Shadow = 10;              // transparent margin around the card for its shadow (DIP)

        private readonly Ellipse _dot;
        private readonly TextBlock _state, _sub;
        private readonly CheckBox _game, _sys;
        private SD.Point _anchor;

        public event Action OpenClicked, AboutClicked, ExitClicked;
        public event Action<bool> GameChanged, SystemChanged;

        public static string StateText(string st)
        {
            return st == "ACTIVE" ? L.T("ACTIVE") : st == "ARMED" ? L.T("ARMED") : L.T("OFF");
        }

        public TrayMenu()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = Brand.Name;
            FontFamily = new FontFamily(L.Family);
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var card = new Border
            {
                Width = 284,
                Margin = new Thickness(Shadow),
                Padding = new Thickness(5),
                Background = Ui.B("Card"),
                BorderBrush = Ui.B("Edge"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.6, Color = Colors.Black }
            };
            var stack = new StackPanel();

            // header: logo, name, state - the whole block opens the panel
            var hg = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = Ui.Picture("glyph_40.png", 26);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 10, 0);
            hg.Children.Add(glyph);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.Semi(Brand.Name, 10, "Fg"));
            _sub = Ui.Text("", 8.25, "Dim", false);
            _sub.Margin = new Thickness(0, 1, 0, 0);
            names.Children.Add(_sub);
            Grid.SetColumn(names, 1);
            hg.Children.Add(names);
            var st = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            _dot = new Ellipse { Width = 7, Height = 7, Fill = Ui.B("Bad"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) };
            _state = Ui.Semi("", 7.75, "Dim");
            _state.VerticalAlignment = VerticalAlignment.Center;
            st.Children.Add(_dot); st.Children.Add(_state);
            Grid.SetColumn(st, 2);
            hg.Children.Add(st);
            stack.Children.Add(Row(hg, 50, () => { var h = OpenClicked; if (h != null) h(); }, L.T("Open grobyq")));
            stack.Children.Add(Sep());

            _game = Ui.Switch(false);
            stack.Children.Add(SwitchRow(L.T("Game threads optimizer"), _game, v => { var h = GameChanged; if (h != null) h(v); }));
            _sys = Ui.Switch(false);
            stack.Children.Add(SwitchRow(L.T("System threads optimizer"), _sys, v => { var h = SystemChanged; if (h != null) h(v); }));
            stack.Children.Add(Sep());

            stack.Children.Add(ActionRow("about", L.T("About"), () => { var h = AboutClicked; if (h != null) h(); }));
            stack.Children.Add(ActionRow("power", L.T("Exit"), () => { var h = ExitClicked; if (h != null) h(); }));

            card.Child = stack;
            Content = card;

            // a tool window: never listed in Alt+Tab while it is open
            SourceInitialized += (s, e) =>
            {
                try
                {
                    var h = new WindowInteropHelper(this).Handle;
                    long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                    SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
                }
                catch { }
            };
            Deactivated += (s, e) => Hide();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Hide(); } };
            DpiChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke((Action)(() => Place())); };
        }

        // one even row: hover = the card highlight, click = the action
        private static Border Row(UIElement content, double height, Action click, string tip = null)
        {
            var b = new Border
            {
                MinHeight = height,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = content
            };
            if (content is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrEmpty(tip)) b.ToolTip = tip;
            b.MouseEnter += (s, e) => b.Background = Ui.B("CardHi");
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        private static Border SwitchRow(string text, CheckBox sw, Action<bool> changed)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Ui.Text(text, 9.5, "Fg", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            sw.IsHitTestVisible = false;               // the row takes the click: one toggle per click
            sw.Focusable = false;
            sw.VerticalAlignment = VerticalAlignment.Center;
            sw.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            return Row(g, 38, () => { bool v = sw.IsChecked != true; sw.IsChecked = v; changed(v); });
        }

        private static Border ActionRow(string icon, string text, Action click)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new Ico { Kind = icon, Width = 15, Height = 15, Stroke = Ui.B("Dim"), StrokeWidth = 1.4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 11, 0) };
            p.Children.Add(ic);
            var t = Ui.Text(text, 9.5, "Fg", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(t);
            return Row(p, 34, click);
        }

        private static Border Sep()
        {
            return new Border { Height = 1, Background = Ui.B("Line"), Margin = new Thickness(8, 4, 8, 4), SnapsToDevicePixels = true };
        }

        public void Sync(string state, string live, bool game, bool sys)
        {
            _game.IsChecked = game;
            _sys.IsChecked = sys;
            _state.Text = StateText(state);
            var brush = Ui.B(state == "ACTIVE" ? "Ok" : state == "ARMED" ? "Warn" : "Bad");
            _dot.Fill = brush;
            _state.Foreground = brush;
            var o = Optimizer.Instance;
            string targets = o != null && o.Eng.Targets != null ? string.Join(", ", o.Eng.Targets) : "cs2.exe";
            _sub.Text = state == "OFF" ? L.T("Optimizers are off")
                      : !string.IsNullOrEmpty(live) && game ? L.F("optimizing {0}", live)
                      : game ? L.F("waiting for {0}", targets)
                      : L.F("system threads: {0}", o != null ? o.Eng.SysThreadCount : 0);
        }

        // At the cursor (physical pixels): the card's bottom-right corner at the pointer, kept inside the work area.
        public void ShowAt(SD.Point px)
        {
            _anchor = px;
            if (!IsVisible)
            {
                Opacity = 0;
                Left = -32000; Top = -32000;
                Show();
            }
            Place();
            Opacity = 1;
            try { Activate(); } catch { }
        }

        private void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            // onto the pointer's monitor first (per-monitor DPI rescales the card there), then measure and align
            SetWindowPos(h, HWND_TOPMOST, _anchor.X, _anchor.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            UpdateLayout();
            RECT r;
            if (!GetWindowRect(h, out r)) return;
            int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            double k = 1.0;
            try { k = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
            int m = (int)Math.Round(Shadow * k);
            var wa = WF.Screen.FromPoint(_anchor).WorkingArea;
            int x = _anchor.X - w + m, y = _anchor.Y - ht + m;
            x = Math.Max(wa.Left - m, Math.Min(x, wa.Right - w + m));
            y = Math.Max(wa.Top - m, Math.Min(y, wa.Bottom - ht + m));
            SetWindowPos(h, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
