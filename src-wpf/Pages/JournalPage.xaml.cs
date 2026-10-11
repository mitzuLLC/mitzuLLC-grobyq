using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    // Log tab: every Apply / Revert / IRQ / cleanup line. Follows the tail while the view is at the bottom (scroll up to
    // read; it stays put until you come back down), Copy puts the whole log on the clipboard.
    public partial class JournalPage : UserControl
    {
        private ScrollViewer _sv;

        public JournalPage()
        {
            InitializeComponent();
            Caption.Text = L.T("LOG  ·  every Apply is written here (A/B evidence)");
            CopyBtn.Content = L.T("Copy");
            ClearBtn.Content = L.T("Clear");
            HideBtn.Content = L.T("Hide");
            List.ItemsSource = AppLog.Entries;
            Loaded += (s, e) =>
            {
                AppLog.Entries.CollectionChanged += OnLogChanged;
                _sv = FindScroll(List);
                ScrollToEnd();
            };
            Unloaded += (s, e) => AppLog.Entries.CollectionChanged -= OnLogChanged;
        }

        private static ScrollViewer FindScroll(DependencyObject d)
        {
            if (d == null) return null;
            if (d is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var r = FindScroll(VisualTreeHelper.GetChild(d, i));
                if (r != null) return r;
            }
            return null;
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (_sv == null) _sv = FindScroll(List);
            // the item is already in the list: "at the end" means the view was at the end before this line arrived
            if (_sv == null || _sv.VerticalOffset >= _sv.ScrollableHeight - 40) ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            int n = List.Items.Count;
            if (n > 0) List.ScrollIntoView(List.Items[n - 1]);
        }

        private void OnClear(object sender, RoutedEventArgs e) { AppLog.Clear(); }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (LogEntry en in AppLog.Entries) sb.AppendLine(en.Time + "  " + en.Raw);
            try { Clipboard.SetText(sb.ToString()); } catch { }
            Ui.Flash(CopyBtn, L.T("Copied"));
        }

        // leave the log tab, back to the main screen (as in v3)
        private void OnHide(object sender, RoutedEventArgs e)
        {
            var w = MainWindow.Current;
            if (w != null) w.Navigate("threads");
        }
    }
}
