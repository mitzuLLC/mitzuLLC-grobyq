using System.Windows;
using System.Windows.Controls;

namespace Grobyq
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // Placeholder navigation for the v4.0 shell bring-up. Real pages are ported in one at a time.
        private void Nav(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag as string ?? "";
            Content.Text = "grobyq v4.0 — " + tag;
        }
    }
}
