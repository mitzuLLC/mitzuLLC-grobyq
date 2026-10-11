using System.Windows.Controls;

namespace Grobyq.Pages
{
    public partial class HomePage : UserControl
    {
        public HomePage()
        {
            InitializeComponent();
            VersionText.Text = "v" + Brand.Version + "   ·   " + Brand.Credit;
            try
            {
                var t = Topo.Live();
                string smt = t.SMT ? ", SMT" : "";
                string name = string.IsNullOrEmpty(t.Name) ? "CPU" : t.Name;
                CpuText.Text = name + "   —   " + t.LogicalCount + " LP / " + t.PhysicalCount + " cores" + smt;
            }
            catch
            {
                CpuText.Text = "—";
            }
        }
    }
}
