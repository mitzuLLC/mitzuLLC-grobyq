using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grobyq.Pages;

namespace Grobyq
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, Func<UserControl>> _factories;
        private readonly Dictionary<string, UserControl> _cache = new Dictionary<string, UserControl>();
        private string _current;

        public MainWindow()
        {
            InitializeComponent();

            // Pages are created on first navigation and cached. Real pages are ported one at a time; the rest are
            // placeholders so the whole nav works today and lights up page by page.
            _factories = new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
            {
                { "home",     () => new HomePage() },
                { "threads",  () => new PlaceholderPage("Threads") },
                { "affinity", () => new PlaceholderPage("Affinity") },
                { "clean",    () => new PlaceholderPage("System cleanup") },
                { "log",      () => new PlaceholderPage("Log") },
                { "settings", () => new PlaceholderPage("Settings") },
                { "about",    () => new PlaceholderPage("About") },
            };

            Navigate("home");
        }

        private void Nav(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(tag)) Navigate(tag);
        }

        private void Navigate(string tag)
        {
            if (tag == null || !_factories.ContainsKey(tag)) return;
            if (!_cache.TryGetValue(tag, out var page)) { page = _factories[tag](); _cache[tag] = page; }
            PageHost.Content = page;
            _current = tag;
            Highlight(tag);
        }

        private void Highlight(string tag)
        {
            foreach (var b in NavButtons())
            {
                bool sel = string.Equals(b.Tag as string, tag, StringComparison.OrdinalIgnoreCase);
                b.Foreground = (Brush)FindResource(sel ? "Fg" : "Dim");
                b.Background = sel ? (Brush)FindResource("Line") : Brushes.Transparent;
            }
        }

        private IEnumerable<Button> NavButtons()
        {
            foreach (var panel in new[] { NavTop, NavFoot })
                foreach (var child in panel.Children)
                    if (child is Button b) yield return b;
        }
    }
}
