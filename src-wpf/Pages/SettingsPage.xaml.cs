using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            WatchBox.Text  = Settings.Get("WatchList", "cs2.exe");
            PeriodBox.Text = Settings.GetInt("Period", 2000).ToString(CultureInfo.InvariantCulture);

            SwSystem.IsChecked = Settings.GetBool("SystemThreadOpt");
            SwBgAff.IsChecked  = Settings.GetBool("BgAffinity");
            SwBgFg.IsChecked   = Settings.GetBool("BgForeground");
            SwAff.IsChecked    = Settings.GetBool("DoAffRules");
            SwSkip0.IsChecked  = Settings.GetBool("SkipCpu0");
            SwTray.IsChecked   = Settings.GetBool("CloseToTray");
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Settings.Set("WatchList", (WatchBox.Text ?? "").Trim());

            int period;
            if (int.TryParse((PeriodBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out period))
            {
                if (period < 1000) period = 1000;
                Settings.Set("Period", period.ToString(CultureInfo.InvariantCulture));
                PeriodBox.Text = period.ToString(CultureInfo.InvariantCulture);
            }

            // retarget the running engine so the new watch list takes effect without a restart
            var o = Optimizer.Instance;
            if (o != null)
            {
                var wl = Settings.WatchList();
                o.Eng.Targets = wl.Length > 0 ? wl : new[] { "cs2.exe" };
                AppLog.Add("[i] watch list: " + string.Join(" ", o.Eng.Targets));
            }

            SavedText.Text = "Сохранено ✓";
        }

        private void OnSwitch(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox cb) || !(cb.Tag is string key)) return;
            bool val = cb.IsChecked == true;
            Settings.SetBool(key, val);
            if (key == "SystemThreadOpt" && Optimizer.Instance != null)
            {
                try { Optimizer.Instance.Eng.LoadSystemProfile(val); } catch { }
            }
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try { Directory_CreateAndOpen(); } catch { }
        }

        private static void Directory_CreateAndOpen()
        {
            System.IO.Directory.CreateDirectory(Store.Dir);
            Process.Start(new ProcessStartInfo { FileName = Store.Dir, UseShellExecute = true });
        }
    }
}
