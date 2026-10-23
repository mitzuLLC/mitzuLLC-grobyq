using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    // 4.0.3: Affinity is closed for rework (Brand.AffinityOpen = false). Its sidebar item opens this page instead: a
    // "technical work" notice, what that means (the game core mask and the per-process core rules are not applied - the
    // optimizer ignores those switches while the tab is closed) and, only when the Affinity tabs left something in the
    // registry, one button each to put the original back: interrupt settings (backup-irq.json) and ReservedCpuSets
    // (backup-reservedcpusets.json). Nothing else on this page writes anything.
    public sealed class MaintenancePage : PageBase
    {
        private readonly TextBlock _status;

        public MaintenancePage() : base(1100)
        {
            Title(L.T("Affinity"));
            var c = Card();
            var head = c.Add(Ui.Semi(L.T("Technical work in progress"), 12, "Fg"));
            head.Margin = new Thickness(0, 8, 0, 4);
            var body = c.Add(Ui.Text(L.T("Affinity is closed while it is being reworked and will return in a later version. Until then the game core mask and the per-process core rules are not applied; thread priorities on Threads work as usual."), 9.5, "Dim"));
            body.Margin = new Thickness(0, 0, 0, 10);

            List<string> irq;
            try { irq = Devices.BackedUp(); } catch { irq = new List<string>(); }
            Reservation.State st = null;
            try { st = Reservation.Read(); } catch { }
            bool resv = st != null && st.HasBackup;
            if (irq.Count > 0 || resv)
            {
                Heading(L.T("Changes made in Affinity"), false);
                var rc = Card();
                var intro = rc.Add(Ui.Text(L.T("These registry changes stay until you restore them. Restoring puts back the values saved before grobyq's first write."), 8.75, "Dim"));
                intro.Margin = new Thickness(0, 6, 0, 2);
                if (irq.Count > 0)
                {
                    var b = Ui.Btn(L.T("Restore original"), "BtnNormal");
                    b.Click += (s, e) => RestoreIrq(b);
                    rc.Add(Ui.Row(L.T("Interrupt settings"), L.F("grobyq changed the interrupt settings of {0} device(s); the originals are saved. A restore takes effect after a device restart or a reboot.", irq.Count), b, resv));
                }
                if (resv)
                {
                    var b = Ui.Btn(L.T("Restore original"), "BtnNormal");
                    b.Click += (s, e) => RestoreResv(b);
                    string d = st.Original == "none"
                        ? L.T("Written by grobyq; it did not exist before. Restoring deletes it (takes effect after a restart).")
                        : L.F("Written by grobyq; the value before was {0}. Restoring puts it back (takes effect after a restart).", st.Original);
                    rc.Add(Ui.Row("ReservedCpuSets", d, b, false));
                }
            }
            _status = Add(Ui.Text("", 9.5, "Dim"));
        }

        private void RestoreIrq(Button b)
        {
            List<string> list;
            try { list = Devices.BackedUp(); } catch { list = new List<string>(); }
            if (list.Count == 0) { b.IsEnabled = false; return; }
            if (!Dlg.Ask(Window.GetWindow(this), L.F("Restore the original interrupt settings of {0} device(s)?", list.Count))) return;
            int ok = 0;
            foreach (var inst in list)
            {
                string err;
                try { err = Devices.RestoreIrq(inst); } catch (Exception ex) { err = ex.Message; }
                if (err.Length == 0) { ok++; try { Devices.MarkApplied(inst); } catch { } }   // the restored values need a device restart too
                AppLog.Add("[IRQ] " + inst + ": " + (err.Length == 0 ? "original interrupt settings restored" : err));
            }
            try { b.IsEnabled = Devices.BackedUp().Count > 0; } catch { }
            Done(L.F("Interrupt settings restored: {0} of {1}. Restart the devices or Windows to apply.", ok, list.Count), ok < list.Count);
        }

        private void RestoreResv(Button b)
        {
            var st = Reservation.Read();
            string target = st.HasBackup ? st.Original : "none";
            string q = target == "none"
                ? L.T("Delete ReservedCpuSets? (It did not exist before the first grobyq write, or there is no grobyq backup.)")
                : L.F("Restore ReservedCpuSets = {0} (the value before the first grobyq write)?", target);
            if (!Dlg.Ask(Window.GetWindow(this), q)) return;
            var r = Reservation.Revert();
            AppLog.Add("[RS] revert: " + (r.Ok ? r.Info : r.Error));
            if (r.Ok) b.IsEnabled = false;
            Done(r.Ok ? L.T("Reverted and read back. Restart Windows to apply.") : "! " + r.Error, !r.Ok);
        }

        private void Done(string text, bool bad)
        {
            _status.Text = text;
            _status.Foreground = Ui.B(bad ? "Warn" : "Ok");
        }
    }
}
