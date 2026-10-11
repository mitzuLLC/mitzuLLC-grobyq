using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    public partial class JournalPage : UserControl
    {
        public JournalPage()
        {
            InitializeComponent();
            List.ItemsSource = AppLog.Entries;
            AppLog.Entries.CollectionChanged += OnLogChanged;
            ScrollToEnd();
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && AutoScroll.IsChecked == true) ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            int n = List.Items.Count;
            if (n > 0) List.ScrollIntoView(List.Items[n - 1]);
        }

        private void OnClear(object sender, RoutedEventArgs e) => AppLog.Clear();

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (LogEntry en in AppLog.Entries) sb.AppendLine(en.Time + "  " + en.Raw);
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }
    }
}
