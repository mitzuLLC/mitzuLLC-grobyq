using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Grobyq
{
    // Pick a program for a list: the running processes (search box, click = select, double click / Enter = add), or an
    // .exe from disk. Anti-cheat and protected system processes are never offered. Returns "name.exe" or null.
    internal static class ProcPicker
    {
        public static string Pick(Window owner, string title, ICollection<string> exclude)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var p in Process.GetProcesses()) { using (p) { if (p.Id > 4) names.Add(p.ProcessName + ".exe"); } } } catch { }
            names.RemoveWhere(n => Engine.IsAntiCheat(n) || Engine.IsProtected(n) || (exclude != null && exclude.Contains(n)));

            var w = new Window
            {
                Title = title,
                Width = 440,
                Height = 540,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = Ui.B("Bg"),
                Foreground = Ui.B("Fg"),
                FontFamily = new FontFamily(L.Family),
                UseLayoutRounding = true
            };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            if (owner != null) w.Owner = owner;
            WinChrome.Dark(w);

            string result = null, sel = null;
            Border selRow = null;
            var dock = new DockPanel { Margin = new Thickness(18, 16, 18, 16) };

            var head = new StackPanel();
            head.Children.Add(Ui.Text(L.T("Running programs. Pick one, or choose an .exe on disk."), 9, "Dim"));
            var filter = Ui.Box("", double.NaN);
            filter.Margin = new Thickness(0, 10, 0, 10);
            filter.ToolTip = L.T("Search");
            head.Children.Add(filter);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            var bar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var browse = Ui.Btn(L.T("Choose .exe..."), "BtnOutline");
            bar.Children.Add(browse);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = Ui.Btn(L.T("Cancel"), "BtnGhost", 90);
            var add = Ui.Btn(L.T("Add"), "BtnAccent", 110);
            add.Margin = new Thickness(8, 0, 0, 0);
            add.IsEnabled = false;
            right.Children.Add(cancel); right.Children.Add(add);
            Grid.SetColumn(right, 2);
            bar.Children.Add(right);
            DockPanel.SetDock(bar, Dock.Bottom);
            dock.Children.Add(bar);

            var list = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var frame = new Border { Background = Ui.B("Card"), BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = scroll };
            dock.Children.Add(frame);
            w.Content = dock;

            Action<string> done = n => { result = n; w.Close(); };
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                selRow = null;
                string q = (filter.Text ?? "").Trim();
                var shown = names.Where(n => q.Length == 0 || n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (!shown.Any(n => string.Equals(n, sel, StringComparison.OrdinalIgnoreCase))) sel = null;
                foreach (var n in shown)
                {
                    string name = n;
                    var t = Ui.Text(name, 9.5, "Fg", false);
                    t.VerticalAlignment = VerticalAlignment.Center;
                    var row = new Border { Height = 32, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = t };
                    if (string.Equals(name, sel, StringComparison.OrdinalIgnoreCase)) { row.Background = Ui.B("AccDim"); selRow = row; }
                    row.MouseEnter += (s, e) => { if (row != selRow) row.Background = Ui.B("CardHi"); };
                    row.MouseLeave += (s, e) => { if (row != selRow) row.Background = Brushes.Transparent; };
                    row.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount >= 2) { done(name); return; }
                        if (selRow != null) selRow.Background = Brushes.Transparent;
                        selRow = row; sel = name; row.Background = Ui.B("AccDim");
                        add.IsEnabled = true;
                    };
                    list.Children.Add(row);
                }
                if (shown.Count == 0)
                {
                    var none = Ui.Text(L.T("Nothing found. Choose the .exe on disk instead."), 9, "Faint");
                    none.Margin = new Thickness(10, 8, 10, 8);
                    list.Children.Add(none);
                }
                add.IsEnabled = sel != null;
            };
            filter.TextChanged += (s, e) => fill();
            add.Click += (s, e) => { if (sel != null) done(sel); };
            cancel.Click += (s, e) => w.Close();
            browse.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = L.T("Programs") + " (*.exe)|*.exe", CheckFileExists = true };
                bool ok = false;
                try { ok = dlg.ShowDialog(w) == true; } catch { }
                if (!ok) return;
                string exe = System.IO.Path.GetFileName(dlg.FileName ?? "");
                if (exe.Length > 0) done(exe);
            };
            w.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; w.Close(); }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    string q = (filter.Text ?? "").Trim();
                    var shown = names.Where(n => q.Length == 0 || n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (sel != null) done(sel);
                    else if (shown.Count == 1) done(shown[0]);
                }
            };
            w.Loaded += (s, e) => filter.Focus();
            fill();
            try { w.ShowDialog(); } catch { }
            return result;
        }
    }
}
