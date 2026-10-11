using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    public partial class CleanupPage : UserControl
    {
        private sealed class Row { public CleanItem Item; public CheckBox Check; public TextBlock Size; }
        private readonly List<Row> _rows = new List<Row>();

        public CleanupPage()
        {
            InitializeComponent();
            Build();
        }

        private void Build()
        {
            List<CleanItem> items;
            try { items = Cleaner.Items(); } catch { items = new List<CleanItem>(); }

            var fg = (Brush)FindResource("Fg");
            var dim = (Brush)FindResource("Dim");

            foreach (var it in items)
            {
                var title = new TextBlock { Text = it.Title, Foreground = fg, FontSize = 13.5 };
                var desc = new TextBlock { Text = it.Desc, Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                var left = new StackPanel();
                left.Children.Add(title);
                if (!string.IsNullOrEmpty(it.Desc)) left.Children.Add(desc);

                var check = new CheckBox { IsChecked = it.Recommended, Content = left, VerticalAlignment = VerticalAlignment.Center };
                var size = new TextBlock { Foreground = dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, TextAlignment = TextAlignment.Right };

                var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(check, 0);
                Grid.SetColumn(size, 1);
                grid.Children.Add(check);
                grid.Children.Add(size);
                Rows.Children.Add(grid);

                _rows.Add(new Row { Item = it, Check = check, Size = size });
            }
        }

        private void OnScan(object sender, RoutedEventArgs e)
        {
            ScanBtn.IsEnabled = false; CleanBtn.IsEnabled = false; StatusText.Text = "Сканирование…";
            var rows = _rows.ToArray();
            var th = new Thread(() =>
            {
                long total = 0;
                foreach (var r in rows)
                {
                    try { Cleaner.Scan(r.Item); } catch { }
                    total += r.Item.Size;
                    var rr = r;
                    Dispatch(() => rr.Size.Text = Cleaner.Human(rr.Item.Size));
                }
                long t = total;
                Dispatch(() => { StatusText.Text = "Найдено: " + Cleaner.Human(t); ScanBtn.IsEnabled = true; CleanBtn.IsEnabled = true; });
            })
            { IsBackground = true };
            th.Start();
        }

        private void OnClean(object sender, RoutedEventArgs e)
        {
            ScanBtn.IsEnabled = false; CleanBtn.IsEnabled = false; StatusText.Text = "Очистка…";
            var arr = new List<Row>();
            foreach (var r in _rows) if (r.Check.IsChecked == true) arr.Add(r);
            var rows = arr.ToArray();
            var th = new Thread(() =>
            {
                long freed = 0;
                foreach (var r in rows)
                {
                    try { freed += Cleaner.Clean(r.Item, s => AppLog.Add(s)); }
                    catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
                    var rr = r;
                    Dispatch(() => rr.Size.Text = "—");
                }
                long f = freed;
                AppLog.Add("[i] очистка: освобождено " + Cleaner.Human(f));
                Dispatch(() => { StatusText.Text = "Освобождено: " + Cleaner.Human(f); ScanBtn.IsEnabled = true; CleanBtn.IsEnabled = true; });
            })
            { IsBackground = true };
            th.Start();
        }

        private void Dispatch(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }
    }
}
