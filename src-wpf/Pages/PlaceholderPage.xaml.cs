using System.Windows.Controls;

namespace Grobyq.Pages
{
    public partial class PlaceholderPage : UserControl
    {
        public PlaceholderPage(string title)
        {
            InitializeComponent();
            TitleText.Text = title;
        }
    }
}
