using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Grobyq
{
    // The two windows that stand before the app (v3.6.8 LicenseForm / AerialOnlyForm, in the v4 style):
    //   LicenseWindow    - checks the signed license list, shows the HWID to send to the developer, Retry / Exit;
    //                      HWID-only mode for  grobyq.exe /hwid .
    //   AerialOnlyWindow - shown instead of the app on a PC without AerialOS (and without an "anyos" license).
    // Both run before MainWindow exists (ShutdownMode = explicit): the caller decides what happens after Close.
    internal static class GateUi
    {
        // 540 px dialog with the dark chrome and the brand header (glyph, name, accent subtitle, "by mitzu LLC").
        public static Window Frame(string title, string subtitle, out StackPanel body)
        {
            var w = new Window
            {
                Title = title,
                Width = 540,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
                Background = Ui.B("Bg"),
                Foreground = Ui.B("Fg"),
                FontFamily = new FontFamily(L.Family),
                UseLayoutRounding = true
            };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            WinChrome.Dark(w);

            var dock = new DockPanel { LastChildFill = true };
            var head = new Border { Background = Ui.B("Card"), BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Height = 70 };
            var hg = new Grid { Margin = new Thickness(16, 0, 18, 0) };
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = Ui.Picture("glyph_40.png", 40);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 10, 0);
            hg.Children.Add(glyph);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.Bold(Brand.Name, 15, "Fg"));
            var sub = Ui.Bold(subtitle, 7.5, "Acc");
            sub.Margin = new Thickness(1, 1, 0, 0);
            names.Children.Add(sub);
            Grid.SetColumn(names, 1);
            hg.Children.Add(names);
            var by = Ui.Text("by mitzu LLC", 8.25, "Dim", false);
            by.VerticalAlignment = VerticalAlignment.Bottom;
            by.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetColumn(by, 2);
            hg.Children.Add(by);
            head.Child = hg;
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            body = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            dock.Children.Add(body);
            w.Content = dock;
            w.Loaded += (s, e) => { try { w.Activate(); } catch { } };
            return w;
        }

        // The HWID in a static box (no caret, no typing): copying goes through the Copy HWID button.
        public static Border HwidBox(string hwid)
        {
            return new Border
            {
                Background = Ui.B("Input"),
                BorderBrush = Ui.B("Edge"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Height = 34,
                Margin = new Thickness(0, 5, 0, 0),
                Child = new TextBlock
                {
                    Text = hwid ?? "",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = Ui.Pt(12),
                    Foreground = Ui.B("Fg"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
        }

        public static TextBlock Caption(string text)
        {
            var t = Ui.Bold(text, 7.5, "Dim");
            t.Margin = new Thickness(0, 14, 0, 0);
            return t;
        }

        public static TextBlock Link(string text, string url, string brush = "Acc")
        {
            var t = Ui.Text(text, 9, brush, false);
            t.Cursor = Cursors.Hand;
            t.Margin = new Thickness(0, 0, 18, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.MouseEnter += (s, e) => t.TextDecorations = TextDecorations.Underline;
            t.MouseLeave += (s, e) => t.TextDecorations = null;
            t.MouseLeftButtonUp += (s, e) => Ui.Open(url);
            return t;
        }

        public static Button ActionBtn(string text, string style, double minWidth)
        {
            var b = Ui.Btn(text, style, minWidth);
            b.Height = 34;
            b.Margin = new Thickness(0, 0, 10, 0);
            return b;
        }

        public static void CopyHwid(TextBlock hint)
        {
            try { Clipboard.SetText(Licensing.Hwid()); hint.Foreground = Ui.B("Ok"); hint.Text = L.T("Copied to the clipboard."); }
            catch { hint.Foreground = Ui.B("Bad"); hint.Text = L.T("Clipboard is busy, try again."); }
        }
    }

    internal sealed class LicenseWindow
    {
        private readonly Window _w;
        private TextBlock _status, _info, _hint;
        private Button _copy, _retry, _exit;
        private DispatcherTimer _closeTimer;
        private bool _licensed, _busy;

        // Shows the window, checks the list at once, closes itself 0.9 s after "License valid.". true = licensed.
        public static bool Gate()
        {
            var f = new LicenseWindow(false);
            try { f._w.ShowDialog(); } catch { }
            return f._licensed;
        }

        // grobyq.exe /hwid : the HWID to send to the developer, nothing checked.
        public static void ShowHwidOnly()
        {
            var f = new LicenseWindow(true);
            try { f._w.ShowDialog(); } catch { }
        }

        // the selftest builds both windows without showing them
        internal static bool Builds()
        {
            return new LicenseWindow(false)._w.Content != null && new LicenseWindow(true)._w.Content != null;
        }

        private LicenseWindow(bool hwidOnly)
        {
            StackPanel body;
            _w = GateUi.Frame(hwidOnly ? Brand.Name + " - HWID" : Brand.Name + " - " + L.T("license"),
                              hwidOnly ? L.T("THIS PC - HWID") : L.T("LICENSE CHECK - HWID"), out body);

            if (!hwidOnly)
            {
                _status = Ui.Semi(L.T("Checking the license list..."), 12, "Dim", true);
                body.Children.Add(_status);
                _info = Ui.Text("", 9, "Dim");
                _info.Margin = new Thickness(1, 6, 0, 0);
                _info.MinHeight = 38;
                body.Children.Add(_info);
            }
            else
            {
                var t = Ui.Text(L.F("Send this HWID to the developer to activate {0} on this PC. After activation press Retry in the license window.", Brand.Name), 9, "Dim");
                body.Children.Add(t);
            }

            body.Children.Add(GateUi.Caption(L.T("YOUR HWID")));
            body.Children.Add(GateUi.HwidBox(Licensing.Hwid()));
            _hint = Ui.Text("", 8.75, "Ok", false);
            _hint.Margin = new Thickness(1, 6, 0, 0);
            _hint.MinHeight = 18;
            body.Children.Add(_hint);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            _copy = GateUi.ActionBtn(L.T("Copy HWID"), "BtnAccent", 140);
            _copy.Click += (s, e) => GateUi.CopyHwid(_hint);
            row.Children.Add(_copy);
            if (hwidOnly)
            {
                _exit = GateUi.ActionBtn(L.T("Close"), "BtnGhost", 100);
                _exit.Click += (s, e) => _w.Close();
                row.Children.Add(_exit);
            }
            else
            {
                _retry = GateUi.ActionBtn(L.T("Retry"), "BtnNormal", 104);
                _retry.Click += (s, e) => Check();
                row.Children.Add(_retry);
                _exit = GateUi.ActionBtn(L.T("Exit"), "BtnGhost", 96);
                _exit.Click += (s, e) => { _licensed = false; _w.Close(); };
                row.Children.Add(_exit);
                _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _closeTimer.Tick += (s, e) => { _closeTimer.Stop(); _w.Close(); };
                _w.ContentRendered += (s, e) => Check();
            }
            row.Children.Add(GateUi.Link("x.com/grobPC", Brand.XUrl, "Dim"));
            body.Children.Add(row);

            _w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (!_licensed) _w.Close(); } };
            _w.Loaded += (s, e) => _copy.Focus();
        }

        private async void Check()
        {
            if (_busy || _licensed) return;
            _busy = true;
            _retry.IsEnabled = false;
            _status.Foreground = Ui.B("Dim");
            _status.Text = L.T("Checking the license list...");
            _info.Text = "";
            LicResult r;
            try { r = await Task.Run(() => Licensing.Validate()); }
            catch (Exception ex) { r = new LicResult { Ok = false, Message = L.T("License check error:") + " " + ex.Message }; }
            _busy = false;
            ShowResult(r ?? new LicResult());
        }

        private void ShowResult(LicResult r)
        {
            if (r.Ok)
            {
                _status.Foreground = Ui.B("Ok");
                _status.Text = L.T("License valid.");
                _info.Foreground = Ui.B("Dim");
                _info.Text = L.T(r.Message) + Licensing.Dates(r);
                _copy.IsEnabled = false; _retry.IsEnabled = false; _exit.IsEnabled = false;
                _licensed = true;                       // closing the window now (X) still opens the app
                _closeTimer.Start();
            }
            else
            {
                _retry.IsEnabled = true;
                _status.Foreground = Ui.B("Bad");
                _status.Text = L.T("License check failed.");
                _info.Foreground = Ui.B("Fg");
                _info.Text = L.T(r.Message);
            }
        }
    }

    internal static class AerialOnlyWindow
    {
        public static void ShowGate()
        {
            var w = Build();
            try { w.ShowDialog(); } catch { }
        }

        internal static bool Builds() { return Build().Content != null; }

        private static Window Build()
        {
            StackPanel body;
            var w = GateUi.Frame(Brand.Name + " - AerialOS", "AerialOS EDITION", out body);

            body.Children.Add(Ui.Bold("for AerialOS users only", 15, "Fg"));
            var t = Ui.Text(L.T("grobyq runs only on AerialOS - the gaming Windows build by zero_unflesh & mitzu LLC. This PC is not running AerialOS.") + "\n" +
                            L.T("Install AerialOS to use grobyq. Questions: the AerialOS Discord."), 9, "Dim");
            t.Margin = new Thickness(1, 8, 0, 0);
            body.Children.Add(t);

            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 12, 0, 0) };
            links.Children.Add(GateUi.Link("aerialos.vercel.app", Brand.AerialUrl));
            links.Children.Add(GateUi.Link("dsc.gg/aerialos", Brand.AerialDiscord));
            body.Children.Add(links);

            var hint = Ui.Text("", 8.75, "Ok", false);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var close = GateUi.ActionBtn(L.T("Close"), "BtnAccent", 110);
            close.Click += (s, e) => w.Close();
            row.Children.Add(close);
            var copy = GateUi.ActionBtn(L.T("Copy HWID"), "BtnNormal", 120);
            copy.Click += (s, e) => GateUi.CopyHwid(hint);
            row.Children.Add(copy);
            hint.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(hint);
            body.Children.Add(row);

            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; w.Close(); } };
            w.Loaded += (s, e) => close.Focus();
            return w;
        }
    }
}
