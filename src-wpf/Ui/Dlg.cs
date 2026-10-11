using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Grobyq
{
    // Native dark chrome for every window (Win10 1809+ / Win11): dark caption, caption = #060606, text #EDEDED,
    // border = hairline. Silently a no-op on older Windows.
    public static class WinChrome
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public static void Dark(Window w)
        {
            if (w == null) return;
            EventHandler apply = null;
            apply = (s, e) =>
            {
                w.SourceInitialized -= apply;
                Apply(new WindowInteropHelper(w).Handle);
            };
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero) Apply(h); else w.SourceInitialized += apply;
        }

        public static void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);   // immersive dark
                int cap = 0x00060606; DwmSetWindowAttribute(hwnd, 35, ref cap, 4);                                // caption color
                int txt = 0x00EDEDED; DwmSetWindowAttribute(hwnd, 36, ref txt, 4);                                // caption text
                int brd = 0x00221C1C; DwmSetWindowAttribute(hwnd, 34, ref brd, 4);                                // border (#1C1C22 as BGR)
            }
            catch { }
        }
    }

    // Dark replacement for MessageBox (the system one is always light). Enter = first button, Esc = last.
    public static class Dlg
    {
        public static bool Ask(Window owner, string text, bool danger = false)
        {
            return Show(owner, text, danger, L.T("Yes"), L.T("No")) == 0;
        }

        public static void Info(Window owner, string text)
        {
            Show(owner, text, false, "OK");
        }

        // Returns the index of the pressed button, -1 when the window was closed.
        public static int Show(Window owner, string text, bool danger, params string[] buttons)
        {
            if (owner == null)
            {
                var mw = MainWindow.Current;
                if (mw != null && mw.IsVisible) owner = mw;
            }
            var w = new Window
            {
                Title = Brand.Name,
                Width = 540,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = Ui.B("Bg"),
                Foreground = Ui.B("Fg"),
                FontFamily = new System.Windows.Media.FontFamily(L.Family),
                UseLayoutRounding = true
            };
            System.Windows.Media.TextOptions.SetTextFormattingMode(w, System.Windows.Media.TextFormattingMode.Display);
            if (owner != null) w.Owner = owner;
            WinChrome.Dark(w);

            var dock = new DockPanel();
            var bar = new Border { Height = 4, Background = Ui.B(danger ? "Bad" : "Acc") };
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);

            var body = new StackPanel { Margin = new Thickness(22, 22, 22, 22) };
            body.Children.Add(Ui.Text(text ?? "", 10, "Fg"));
            var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            int result = -1;
            Button first = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = Ui.Btn(buttons[i], i == 0 ? (danger ? "BtnDanger" : "BtnAccent") : "BtnNormal", 96);
                b.Height = 34;
                b.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
                b.Click += (s, e) => { result = idx; w.Close(); };
                if (i == 0) first = b;
                strip.Children.Add(b);
            }
            body.Children.Add(strip);
            dock.Children.Add(body);
            w.Content = dock;

            w.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { result = buttons.Length > 1 ? buttons.Length - 1 : 0; e.Handled = true; w.Close(); }
                else if (e.Key == Key.Enter) { result = 0; e.Handled = true; w.Close(); }
            };
            w.Loaded += (s, e) => { if (first != null) first.Focus(); };
            try { w.ShowDialog(); } catch { }
            return result;
        }
    }
}
