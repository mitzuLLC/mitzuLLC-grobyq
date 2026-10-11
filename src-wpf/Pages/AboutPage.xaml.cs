using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Grobyq.Pages
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            VersionText.Text = "v" + Brand.Version + "   ·   " + Brand.Credit;
        }

        private void OnNav(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true }); } catch { }
            e.Handled = true;
        }
    }
}
