using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Grobyq
{
    // MSI + affinity policy + priority + target CPUs for ONE device. Expanded under a row of the Affinity table.
    internal sealed class IrqEditor : StackBox
    {
        private readonly MainForm M;
        private PciDevice _dev;
        private Label _head;
        private DropBox _msi, _prio, _pol;
        private Stepper _limit;
        private TextBox _cpus;
        private CoreGrid _pick;                  // Vain cores checkbox grid for the target CPUs
        private TextBlock _status;                // sizes itself: a three-line status (reserved-set warning) is never clipped
        private Btn _apply, _restore, _restart, _clear, _all, _recommend, _check;
        private CheckList _checks;                // "Check status": plain lines, config at once, the live part after 5 s
        private bool _checking;
        private string _checkInst = "";            // the device a running live check belongs to
        private System.Windows.Forms.Timer _countdown;
        private BoxCheck _pOnly;
        private TextBlock _pickInfo;              // wraps: the recommendation says where and why in one or two lines
        private bool _sync;
        public event Action Changed;
        public Func<List<KeyValuePair<string, ulong>>> Others;     // (class, target LPs) of the other devices
        public Func<List<KeyValuePair<string, ulong>>> OtherNames; // (name, target LPs) of the other devices pinned to CPUs
        public Func<int> NetCount;                                 // present network adapters (ndis.sys attribution note)

        private string _pickSig = "";
        private static string Legend { get { return L.T("Blue dot = a core of the current game mask."); } }

        public IrqEditor(MainForm m)
        {
            M = m;
            _head = Theme.Lbl("", Theme.Semi(10.5f), Theme.Fg, false); _head.Height = Theme.D(30); _head.AutoEllipsis = true; _head.BackColor = Theme.Card;
            Stack(_head);
            _msi = Theme.Combo(Theme.D(200), new Opt("", L.T("driver default")), new Opt("1", L.T("On")), new Opt("0", L.T("Off")));
            Stack(new SettingRow(L.T("Message-signaled interrupts (MSI)"), L.T("MSI replaces the shared line interrupt with in-band messages: no sharing, per-message CPU targeting. Registry: MessageSignaledInterruptProperties\\MSISupported (MS Learn, T1). Only if the device supports MSI."), _msi));
            _limit = Theme.Num(Theme.D(90), 0, 2048, 0);
            Stack(new SettingRow(L.T("MSI message limit"), L.T("MessageNumberLimit. 0 = driver default. Fewer messages = fewer vectors to steer; the device's maximum is shown in the status line."), _limit));
            _prio = Theme.Combo(Theme.D(200), new Opt("0", IrqConfig.PriorityLabel(0)), new Opt("1", IrqConfig.PriorityLabel(1)), new Opt("2", IrqConfig.PriorityLabel(2)), new Opt("3", IrqConfig.PriorityLabel(3)));
            Stack(new SettingRow(L.T("Interrupt priority"), L.T("DevicePriority hint for the interrupt controller (Undefined = default). T1 key, effect T2."), _prio));
            var pols = Enumerable.Range(0, 7).Select(i => new Opt(i.ToString(), IrqConfig.PolicyLabel(i))).ToArray();
            _pol = Theme.Combo(Theme.D(250), pols[0], pols.Skip(1).ToArray());
            Stack(new SettingRow(L.T("Affinity policy"), L.T("DevicePolicy: which CPUs may receive this device's interrupts (and usually run its DPCs). 'Specified processors' uses the target CPUs below."), _pol));

            _cpus = Theme.Box(Theme.D(230));
            _recommend = new Btn(L.T("Recommend a core"), Btn.Style.Outline);
            _all = new Btn(L.T("Select all"), Btn.Style.Normal);
            _clear = new Btn(L.T("Clear all"), Btn.Style.Ghost);
            _pOnly = new BoxCheck(L.T("P-cores only")) { Checked = Settings.Get("IrqPOnly") != "0" };
            Stack(new SettingRow(L.T("Target CPUs"), L.T("AssignmentSetOverride. Tick cores in the grid below, or type a rule (Freq, CCD1, E) or LPs (2,3 / 0xC). Put GPU / NIC / USB interrupts on a core the game does not use, then check DPC/ISR per CPU in WPA (Contour A)."), _cpus));
            var helpers = new Strip();
            helpers.Add(_recommend);
            helpers.Add(_pOnly);
            helpers.Add(_all);
            helpers.Add(_clear);
            Stack(helpers);
            _pick = new CoreGrid();
            Stack(_pick);
            _pickInfo = new TextBlock("", Theme.Dim, 9f) { BackColor = Theme.Card };
            Stack(_pickInfo);
            _pickInfo.SizeChanged += (s, e) => PerformLayout();

            var strip = new Strip();
            _apply = strip.Add(new Btn(L.T("Apply"), Btn.Style.Accent, Theme.D(90)));
            _restore = strip.Add(new Btn(L.T("Restore original"), Btn.Style.Normal));
            _restart = strip.Add(new Btn(L.T("Restart device"), Btn.Style.Normal));
            _check = strip.Add(new Btn(L.T("Check status"), Btn.Style.Outline));
            Stack(strip);
            _status = new TextBlock("", Theme.Dim, 9f) { BackColor = Theme.Card };
            Stack(_status);
            _status.SizeChanged += (s, e) => PerformLayout();
            _checks = new CheckList();
            Stack(_checks);
            _checks.SizeChanged += (s, e) => PerformLayout();
            var note = new TextBlock(L.T("Check status reads the registry and the device's resources, then records 5 s of kernel interrupt events (ETW - the same events WPR / WPA show). For the live part let the device work: a download, a game."), Theme.Faint, 8.75f) { BackColor = Theme.Card };
            Stack(note);
            note.SizeChanged += (s, e) => PerformLayout();
            _check.Click += (s, e) => RunCheck();

            _apply.Click += (s, e) => DoApply();
            _restore.Click += (s, e) => DoRestore();
            _restart.Click += (s, e) => DoRestart();
            _recommend.Click += (s, e) => Recommend();
            _pOnly.CheckedChanged += (s, e) => Settings.SetBool("IrqPOnly", _pOnly.Checked);
            _clear.Click += (s, e) => { _cpus.Text = ""; _pickInfo.Text = Legend; _pickInfo.ForeColor = Theme.Dim; };
            _all.Click += (s, e) => { _pick.Mask = _pick.AllMask; };
            _cpus.TextChanged += (s, e) => { if (_sync) return; _sync = true; try { _pick.Mask = ResolveCpus(); } finally { _sync = false; } };
            _pick.Changed += () =>
            {
                if (_sync) return;
                _sync = true;
                try { ulong mask = _pick.Mask; _cpus.Text = mask == 0 ? "" : Masks.MaskLps(mask); if (mask != 0 && Theme.Val(_pol) != "4") Theme.SelectVal(_pol, "4"); }
                finally { _sync = false; }
                WarnECores();
            };
            _pol.SelectedIndexChanged += (s, e) => { if (Theme.Val(_pol) == "4" && _cpus.Text.Trim().Length == 0 && !_sync) Recommend(); };
            Bind(null);
        }

        public void Bind(PciDevice dev)
        {
            _dev = dev;
            bool on = dev != null;
            foreach (Control c in new Control[] { _msi, _limit, _prio, _pol, _cpus, _apply, _restore, _restart, _recommend, _pOnly, _clear, _all, _pick }) c.Enabled = on;
            _check.Enabled = on && !_checking;
            _pickInfo.Text = Legend; _pickInfo.ForeColor = Theme.Dim;
            var topo = ThreadsPage.SafeTopo();
            ulong game = 0; try { if (topo != null) game = GameMask.Effective(topo).Mask; } catch { }
            string sig = AffinityPage.TopoSig(topo);
            if (topo != null && sig != _pickSig) { _pickSig = sig; _pick.Build(topo); }
            _pick.MarkMask = game;
            _pOnly.Visible = topo != null && topo.Hybrid;
            if (!on) { _head.Text = L.T("Select a device in the list above."); _status.Text = ""; _checks.Set(null); _sync = true; try { _pick.Mask = 0; } finally { _sync = false; } return; }
            _head.Text = dev.Name + "   ·   " + dev.Class;
            var c0 = Devices.ReadIrq(dev.InstanceId);
            _sync = true;
            try
            {
                Theme.SelectVal(_msi, !c0.MsiSupported.HasValue ? "" : c0.MsiSupported.Value != 0 ? "1" : "0");
                _limit.Value = Math.Max(0, Math.Min(2048, c0.MsgLimit ?? 0));
                Theme.SelectVal(_prio, Math.Max(0, Math.Min(3, c0.Priority ?? 0)).ToString());
                Theme.SelectVal(_pol, Math.Max(0, Math.Min(6, c0.Policy ?? 0)).ToString());
                _cpus.Text = c0.Assign.HasValue ? Masks.MaskLps(c0.Assign.Value) : "";
                _pick.Mask = c0.Assign ?? 0;
            }
            finally { _sync = false; }
            _restore.Enabled = Devices.HasBackup(dev.InstanceId);
            if (!dev.MsiCapable && dev.IrqSupport >= 0) _msi.Enabled = false;
            ShowStatus(null);
            ShowConfig();
        }

        // ------------------------------------------------------------------ check status
        private IrqCheck.Facts Facts()
        {
            var names = OtherNames != null ? OtherNames() : null;
            return IrqCheck.Gather(_dev, names, NetCount != null ? NetCount() : 1);
        }

        // the config part: instant, shown when a device opens and after Apply / Restore / Restart
        private void ShowConfig()
        {
            if (_dev == null || (_checking && _dev.InstanceId == _checkInst)) return;     // its own check is drawing the lines
            try { _checks.Set(IrqCheck.Config(Facts())); }
            catch (Exception ex) { _checks.Set(new[] { new CheckLine(IrqCheck.Bad, ex.Message) }); }
        }

        // config now, then 5 s of ETW on a background thread; the result is shown only if the same device is still open
        private void RunCheck()
        {
            if (_dev == null || _checking) return;
            _checking = true; _check.Enabled = false;
            var dev = _dev;
            _checkInst = dev.InstanceId;
            List<CheckLine> cfg;
            try { cfg = IrqCheck.Config(Facts()); } catch (Exception ex) { cfg = new List<CheckLine> { new CheckLine(IrqCheck.Bad, ex.Message) }; }
            int left = 5;
            Action show = () => { if (_dev != null && _dev.InstanceId == dev.InstanceId) _checks.Set(cfg.Concat(new[] { new CheckLine(IrqCheck.Info, L.F("Live check: recording kernel interrupt events, {0} s left…", left)) })); };
            show();
            _countdown = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdown.Tick += (s, e) => { if (left > 1) { left--; show(); } };
            _countdown.Start();
            M.Log("[IRQ] " + dev.Name + ": live check (5 s of ISR / DPC events)");
            var th = new System.Threading.Thread(() =>
            {
                IrqTrace.Result r;
                try { r = IrqTrace.Capture(5); } catch (Exception ex) { r = new IrqTrace.Result { Error = ex.Message }; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        try { _countdown.Stop(); _countdown.Dispose(); } catch { }
                        _checking = false; _checkInst = "";
                        _check.Enabled = _dev != null;
                        if (_dev == null || _dev.InstanceId != dev.InstanceId) { ShowConfig(); return; }     // another device opened meanwhile
                        IrqCheck.Facts f;
                        try { f = Facts(); } catch { f = null; }
                        var all = f != null ? IrqCheck.Full(f, r) : cfg;
                        _checks.Set(all);
                        M.Log("[IRQ] " + dev.Name + ": " + string.Join(" | ", all.Where(x => x.Level != IrqCheck.Info).Select(x => x.Text)));
                    }));
                }
                catch { _checking = false; }
            }) { IsBackground = true, Name = "grobyq check status" };
            th.Start();
        }

        private void ShowStatus(string extra)
        {
            if (_dev == null) return;
            string now = Devices.IrqInUse(_dev.DevInst);
            _status.ForeColor = extra != null && extra.StartsWith("!") ? Theme.Warn : Theme.Dim;
            string res = "";
            ulong target = ResolveCpus(), reserved = Reservation.Reserved();
            if ((target & reserved) != 0) { res = "\n" + L.T("Target LPs inside the reserved set: interrupt routing to reserved LPs is unverified (T3)."); _status.ForeColor = Theme.Warn; }
            var tg = ThreadsPage.SafeTopo();
            if (tg != null && tg.Groups > 1) { res += "\n" + L.F("This PC has {0} processor groups: the target CPUs here are group 0 only. AssignmentSetOverride is one 64-bit mask and MS Learn documents no group value for it, so grobyq writes none.", tg.Groups); _status.ForeColor = Theme.Warn; }
            _status.Text = L.T("In use now:") + " " + now + "   ·   " + L.T("supports:") + " " + _dev.IrqSupportText + "\n" + (extra != null ? extra.TrimStart('!') : L.T("Changes take effect after Restart device or a reboot.")) + res;
        }

        private ulong ResolveCpus()
        {
            string t = _cpus.Text.Trim();
            if (t.Length == 0) return 0;
            var topo = ThreadsPage.SafeTopo(); if (topo == null) return 0;
            // A single number here is one logical processor ("11" = LP 11), not a decimal mask (11 = LP 0,1,3).
            // The map and "Off the game cores" write single LPs this way.
            int one;
            if (int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out one) && one >= 0 && one < 64)
                return topo.Sets.Any(s => s.LP == one) ? 1UL << one : 0;
            string err; var r = Masks.Resolve(topo, t, out err);
            return r == null ? 0 : r.Mask;
        }

        // Topology-aware pick for THIS device (CoreSets.RecommendIrq): a core the game does not use, else the free thread of a
        // game core, else the second thread of the last core (the adaptive game mask then leaves it). Never core 0, never a
        // reserved LP, P-cores only on a hybrid CPU unless unticked; other devices' cores are skipped while possible.
        // Shows the pick and why; Apply and Restart device stay explicit.
        private void Recommend()
        {
            if (_dev == null) return;
            var topo = ThreadsPage.SafeTopo(); if (topo == null) return;
            ulong game = Masks.Build(topo, GameMask.ManualRule(topo)).Mask;       // base + edits: never shaped by this device's own pin
            ulong busy = 0;
            if (Others != null) foreach (var o in Others()) busy |= o.Value;
            var r = CoreSets.RecommendIrq(topo, game, topo.Hybrid && _pOnly.Checked, busy, Reservation.Reserved());
            if (r.Lp < 0) { ShowStatus("!" + L.T("No core fits: only core 0 or reserved LPs are left. Nothing picked.")); return; }
            _sync = true;
            try { _cpus.Text = r.Lp.ToString(); _pick.Mask = 1UL << r.Lp; Theme.SelectVal(_pol, "4"); }
            finally { _sync = false; }
            string why = r.Tier == 1 ? L.T("a core the game does not use")
                       : r.Tier == 2 ? L.T("the free second thread of a game core; hot game threads never get it")
                       : L.T("every LP is in the game mask: the second thread of the last core. With Interrupt cores on (Affinity > Cores) the game mask leaves it by itself");
            string extra = (r.ECore ? "  ·  " + L.T("E-core: its DPCs run longer than on a P-core - check DPC duration in WPA") : "")
                         + (r.Shared ? "  ·  " + L.T("shares its core with another device's interrupts") : "");
            _pickInfo.ForeColor = Theme.Dim;
            _pickInfo.Text = L.F("LP {0} (core {1}) - {2}.", r.Lp, r.CoreNo, why) + extra + "  " + L.T("Apply, then Restart device.");
            ShowStatus(null);
        }

        // a hand-ticked E-core with "P-cores only": allowed, said once in the info line
        private void WarnECores()
        {
            var topo = ThreadsPage.SafeTopo();
            if (topo == null || !topo.Hybrid || !_pOnly.Checked) return;
            ulong m = _pick.Mask;
            var e = Enumerable.Range(0, 64).Where(lp => ((m >> lp) & 1) != 0 && CoreSets.IsECore(topo, lp)).ToList();
            if (e.Count == 0) return;
            _pickInfo.ForeColor = Theme.Warn;
            _pickInfo.Text = L.F("E-core LP {0} ticked: allowed, but its DPCs run longer than on a P-core (T2). Check DPC duration per CPU in WPA.", string.Join(",", e));
        }

        private void DoApply()
        {
            if (_dev == null) return;
            bool storage = new[] { "SCSIAdapter", "HDC" }.Contains(_dev.Class, StringComparer.OrdinalIgnoreCase);
            if (storage && Theme.Val(_msi) == "0" && !Dlg.Ask(this, L.T("This is a storage controller. Turning MSI off on the boot drive's controller can stop Windows from booting. Continue?"), true)) return;
            string msi = Theme.Val(_msi), prio = Theme.Val(_prio), pol = Theme.Val(_pol);
            var c = new IrqConfig
            {
                MsiSupported = msi == "" ? (int?)null : msi == "1" ? 1 : 0,
                MsgLimit = _limit.Value == 0 ? (int?)null : (int)_limit.Value,
                Priority = prio == "0" ? (int?)null : int.Parse(prio),
                Policy = pol == "0" ? (int?)null : int.Parse(pol),
            };
            ulong cpus = ResolveCpus();
            if (pol == "4")
            {
                if (cpus == 0) { ShowStatus("!" + L.T("'Specified processors' needs target CPUs.")); return; }
                c.Assign = cpus;
            }
            string err = Devices.WriteIrq(_dev.InstanceId, c);
            if (err.Length > 0) { M.Log("[IRQ] " + _dev.Name + ": " + err); ShowStatus("!" + err); return; }
            M.Log(string.Format("[IRQ] {0}: MSI {1}, limit {2}, priority {3}, policy {4}{5}  (original saved to backup-irq.json)",
                _dev.Name, msi == "" ? "default" : msi == "1" ? "on" : "off", c.MsgLimit.HasValue ? c.MsgLimit.Value.ToString() : "default", prio, pol, c.Assign.HasValue ? ", CPUs " + Masks.MaskLps(c.Assign.Value) : ""));
            _restore.Enabled = true;
            Devices.MarkApplied(_dev.InstanceId);
            ShowStatus("!" + L.T("Saved. Restart the device (or reboot) to apply; then compare DPC/ISR per CPU."));
            var h = Changed; if (h != null) h();
            ShowConfig();
        }

        private void DoRestore()
        {
            if (_dev == null) return;
            string err = Devices.RestoreIrq(_dev.InstanceId);
            if (err.Length == 0) Devices.MarkApplied(_dev.InstanceId);         // the restored values also need a device restart
            M.Log("[IRQ] " + _dev.Name + ": " + (err.Length == 0 ? "original interrupt settings restored" : err));
            Bind(_dev);
            var h = Changed; if (h != null) h();
        }

        private void DoRestart()
        {
            if (_dev == null) return;
            string warn = _dev.IsDisplay ? L.T("The screen will go black for a few seconds.") : _dev.IsNet ? L.T("The network drops for a few seconds.") : L.T("The device stops for a few seconds.");
            if (!Dlg.Ask(this, L.F("Restart {0}?", _dev.Name) + "\n\n" + warn)) return;
            Cursor = Cursors.WaitCursor;
            string r = Devices.Restart(_dev.InstanceId);
            Cursor = Cursors.Default;
            M.Log("[DEV] restart " + _dev.Name + ": " + (r.Length == 0 ? "done" : r == "reboot" ? "Windows needs a reboot to finish" : r));
            if (r.Length == 0) Devices.MarkRestarted(_dev.InstanceId);
            ShowStatus(r.Length == 0 ? L.T("Restarted.") : r == "reboot" ? "!" + L.T("Reboot required to finish.") : "!" + r);
            ShowConfig();
        }
    }
}
