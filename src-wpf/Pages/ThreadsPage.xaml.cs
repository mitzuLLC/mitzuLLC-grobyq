using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    public partial class ThreadsPage : UserControl
    {
        public ThreadsPage()
        {
            InitializeComponent();
            SwAuto.IsChecked    = Settings.GetBool("AutoEnable");
            SwThreads.IsChecked = Settings.GetBool("DoThreads");
            SwBg.IsChecked      = Settings.GetBool("DoBackground");
            SwReapply.IsChecked = Settings.GetBool("PeriodicReapply");

            if (Optimizer.Instance != null) Optimizer.Instance.StateChanged += RefreshState;
            Unloaded += (s, e) => { if (Optimizer.Instance != null) Optimizer.Instance.StateChanged -= RefreshState; };
            RefreshState();
        }

        private void OnToggle(object sender, RoutedEventArgs e)
        {
            var o = Optimizer.Instance;
            if (o != null) o.SetActive(!o.Active);
        }

        private void OnSwitch(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string key)
                Settings.SetBool(key, cb.IsChecked == true);
        }

        private void RefreshState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)RefreshState); return; }
            var o = Optimizer.Instance;
            string st = o != null ? o.StateName : "OFF";
            StateText.Text = st;
            StateDot.Fill = (Brush)FindResource(st == "ACTIVE" ? "Ok" : st == "ARMED" ? "Warn" : "Bad");
            string live = o != null ? o.LiveTarget : "";
            TargetText.Text = string.IsNullOrEmpty(live) ? "Цель не запущена" : "Цель: " + live;
            bool on = o != null && o.Active;
            ToggleBtn.Content = on ? "ВЫКЛЮЧИТЬ" : "ВКЛЮЧИТЬ";
            ToggleBtn.Background = (Brush)FindResource(on ? "Bad" : "Ok");
        }
    }
}
