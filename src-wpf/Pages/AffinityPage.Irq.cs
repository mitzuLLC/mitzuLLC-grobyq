using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Grobyq.Pages
{
    // Interrupts tab: one row per PCI device (MSI limit, target CPUs / policy, interrupt modes, priority); click a row to
    // edit MSI / limit / priority / policy / target CPUs. Every change is an explicit Apply (registry, the original backed
    // up in backup-irq.json, Restore original puts it back) and takes effect after Restart device or a reboot.
    public sealed partial class AffinityPage
    {
        private StackPanel _devList, _chips;
        private TextBox _search;
        private TextBlock _badge, _devEmpty;
        private List<PciDevice> _pci;
        private bool _pciLoading;
        private readonly List<DevRow> _devRows = new List<DevRow>();
        private DevRow _open;
        private CardBox _edCard;

        private void BuildIrq(StackPanel p)
        {
            // header: title, MSI count, search, rescan
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Ui.Semi(L.T("Device interrupts"), 15, "Fg");
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            _badge = Ui.Text("", 9, "Dim", false);
            var badgeBox = new Border { Child = _badge, Background = Ui.B("Card"), BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
            Grid.SetColumn(badgeBox, 2);
            head.Children.Add(badgeBox);
            _search = Ui.Box("", 250);
            _search.Height = 34;
            _search.ToolTip = L.T("Search");
            _search.TextChanged += (s, e) => { DevFilter(); badgeBox.Visibility = _badge.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; };
            Grid.SetColumn(_search, 3);
            head.Children.Add(_search);
            var rescan = Ui.Btn(L.T("Rescan"), "BtnOutline");
            rescan.Height = 34; rescan.Margin = new Thickness(10, 0, 0, 0);
            rescan.Click += (s, e) => { _pci = null; IrqEnter(); };
            Grid.SetColumn(rescan, 4);
            head.Children.Add(rescan);
            p.Children.Add(head);
            Action showBadge = () => badgeBox.Visibility = _badge.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _badgeShow = showBadge;

            var intro = Ui.Text(L.T("Click a device to change MSI, the message limit, the affinity policy and the priority. The originals are backed up on the first change; changes apply after Restart device or a reboot. Measure DPC/ISR per CPU before and after (WPA, Contour A)."), 9, "Dim");
            intro.Margin = new Thickness(0, 0, 0, 10);
            p.Children.Add(intro);

            _chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            p.Children.Add(new ScrollViewer { Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });

            // column header
            var hdr = DevRow.Columns();
            foreach (var cap in new[] { new { c = 1, t = L.T("Device") }, new { c = 2, t = L.T("Limit") }, new { c = 3, t = L.T("Policy") }, new { c = 4, t = L.T("Modes") }, new { c = 5, t = L.T("Priority") } })
            {
                var tb = Ui.Semi(cap.t.ToUpper(CultureInfo.CurrentCulture), 8.25, "Faint");
                tb.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(tb, cap.c);
                hdr.Children.Add(tb);
            }
            hdr.Height = 30;
            p.Children.Add(hdr);

            _devList = new StackPanel();
            p.Children.Add(_devList);
            _devEmpty = Ui.Text(L.T("reading…"), 9.5, "Faint", false);
            _devEmpty.HorizontalAlignment = HorizontalAlignment.Center;
            _devEmpty.Margin = new Thickness(0, 12, 0, 12);
            p.Children.Add(_devEmpty);

            _edCard = new CardBox(12, 20, 8) { Margin = new Thickness(12, 0, 12, 10) };
            _edCard.Padding = new Thickness(20, 8, 20, 12);
            BuildEditor(_edCard);
        }

        private Action _badgeShow;

        private void IrqEnter()
        {
            if (_devList == null) return;
            ChipsRefresh();
            if (_pci != null) { foreach (var r in _devRows) r.Read(); EdRefreshMarks(); return; }
            if (_pciLoading) return;
            _pciLoading = true;
            _devEmpty.Text = L.T("reading…"); _devEmpty.Visibility = Visibility.Visible;
            var th = new Thread(() =>
            {
                List<PciDevice> list;
                try { list = Devices.ListPci(); } catch { list = new List<PciDevice>(); }
                try { Dispatcher.BeginInvoke((Action)(() => { _pciLoading = false; _pci = list; DevLoad(); })); } catch { }
            }) { IsBackground = true, Name = "grobyq-pci" };
            th.Start();
        }

        // GPU, USB, network, storage, audio, then everything else by name. No device is hidden.
        private static int Rank(PciDevice d)
        {
            switch ((d.Class ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return 0;
                case "USB": return 1;
                case "NET": return 2;
                case "SCSIADAPTER": case "HDC": return 3;
                case "MEDIA": return 4;
                default: return 5;
            }
        }

        private void DevLoad()
        {
            string keep = _open != null ? _open.Dev.InstanceId : null;
            _devList.Children.Clear(); _devRows.Clear(); _open = null;
            foreach (var d in (_pci ?? new List<PciDevice>()).OrderBy(Rank).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new DevRow(d);
                row.MouseLeftButtonUp += (s, e) => DevToggle(row);
                _devRows.Add(row);
                _devList.Children.Add(row);
            }
            if (keep != null) { var again = _devRows.FirstOrDefault(r => r.Dev.InstanceId == keep); if (again != null) DevToggle(again); }
            DevFilter();
        }

        private void DevFilter()
        {
            string q = (_search.Text ?? "").Trim();
            foreach (var r in _devRows)
            {
                var d = r.Dev;
                bool vis = q.Length == 0 || new[] { d.Name, d.Class, d.Service, d.InstanceId, d.Mfg }.Any(t => (t ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                r.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_open != null && _open.Visibility != Visibility.Visible) DevToggle(_open);
            int total = _devRows.Count(r => r.Visibility == Visibility.Visible);
            int msi = _devRows.Count(r => r.Visibility == Visibility.Visible && (r.Dev.MsiCapable || r.InUse.StartsWith("MSI", StringComparison.Ordinal)));
            _badge.Text = msi > 0 ? L.F("MSI capable: {0} of {1}", msi, total) : "";
            if (_badgeShow != null) _badgeShow();
            _devEmpty.Text = _devRows.Count == 0 ? L.T("reading…") : L.T("No device matches the search.");
            _devEmpty.Visibility = (_devRows.Count == 0 && _pci == null) || (_devRows.Count > 0 && total == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DevToggle(DevRow row)
        {
            if (_edCard.Parent is Panel pp) pp.Children.Remove(_edCard);
            if (_open == row) { row.Expanded = false; _open = null; return; }
            if (_open != null) _open.Expanded = false;
            _open = row; row.Expanded = true;
            int i = _devList.Children.IndexOf(row);
            _devList.Children.Insert(i + 1, _edCard);
            EdBind(row.Dev);
        }

        // "CORE SETS   ● GAME LP 0-15   ● NON-GAME LP 16-31   ● ReservedCpuSets: not set"
        private void ChipsRefresh()
        {
            _chips.Children.Clear();
            var cap = Ui.Semi(L.T("Core sets").ToUpper(CultureInfo.CurrentCulture), 8.25, "Faint");
            cap.VerticalAlignment = VerticalAlignment.Center; cap.Margin = new Thickness(0, 0, 10, 0);
            _chips.Children.Add(cap);
            var t = T; if (t == null) return;
            var v = CoreSets.Effective(t, GameMask.Rule(t));
            if (!v.Split) v.NonGame = GameMask.Background(t);
            if (v.Split)
            {
                Chip("Acc", L.T("GAME") + "  LP " + Masks.Ranges(v.Game), "Fg");
                Chip(null, L.T("NON-GAME") + "  LP " + Masks.Ranges(v.NonGame), "Fg", CpuMap.NonGameC);
            }
            else
            {
                Chip("Acc", L.T("GAME MASK") + "  LP " + Masks.Ranges(v.Game) + (Settings.GetBool("DoMask") ? "" : "  (" + L.T("not applied") + ")"), "Fg");
                if (v.NonGame != 0) Chip("Warn", L.T("BACKGROUND") + "  LP " + Masks.Ranges(v.NonGame), "Fg");
            }
            var st = Reservation.Read();
            if (st.Kind == Reservation.Kind.NotSet) Chip("Faint", L.T("ReservedCpuSets: not set"), "Dim");
            else if (st.Kind != Reservation.Kind.Binary) Chip("Bad", L.T("ReservedCpuSets: unreadable"), "Warn");
            else
            {
                string when = st.Pending ? L.T("pending reboot") : st.TimeKnown ? L.T("active") : L.T("write time unknown");
                if (v.Split && st.Mask == v.NonGame) Chip(null, L.F("ReservedCpuSets: = NON-GAME ({0})", when), st.Pending ? "Warn" : "Fg", CpuMap.NonGameC);
                else if ((st.Mask & v.Game) != 0) Chip("Bad", L.F("ReservedCpuSets: {0} overlaps the game set ({1})", Masks.Hex(st.Mask), when), "Warn");
                else Chip("Warn", L.F("ReservedCpuSets: other, LP {0} ({1})", Masks.Ranges(st.Mask), when), "Fg");
            }
        }

        private void Chip(string dotKey, string text, string fg, Color? dotColor = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = dotKey != null ? Ui.B(dotKey) : new SolidColorBrush(dotColor ?? Colors.Gray), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var tb = Ui.Text(text, 9, fg, false); tb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(tb);
            _chips.Children.Add(new Border { Child = row, Background = Ui.B("Card"), BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 4, 14, 4), Margin = new Thickness(0, 4, 8, 4) });
        }

        // ================================================================== the editor (one device)
        private PciDevice _dev;
        private TextBlock _edHead, _pickInfo, _edStatus;
        private ComboBox _msi, _prio, _pol;
        private TextBox _limit, _cpus;
        private CoreGrid _pick;
        private BoxCheck _pOnly;
        private Button _apply, _restore, _restart, _check, _recommend, _all, _clear;
        private StackPanel _checks;
        private bool _sync, _checking;
        private string _checkInst = "", _pickSig = "";
        private DispatcherTimer _countdown;

        private static string Legend { get { return L.T("Blue dot = a core of the current game mask."); } }

        private void BuildEditor(CardBox c)
        {
            _edHead = c.Add(Ui.Semi("", 10.5, "Fg"));
            _edHead.Height = 30; _edHead.Padding = new Thickness(0, 6, 0, 0);
            _msi = Combo.New(220, new[] { new Opt("", L.T("driver default")), new Opt("1", L.T("On")), new Opt("0", L.T("Off")) });
            c.Add(Ui.Row(L.T("Message-signaled interrupts (MSI)"), L.T("MSI replaces the shared line interrupt with in-band messages: no sharing, per-message CPU targeting. Registry: MessageSignaledInterruptProperties\\MSISupported (MS Learn, T1). Only if the device supports MSI."), _msi));
            _limit = Ui.Box("0", 90);
            c.Add(Ui.Row(L.T("MSI message limit"), L.T("MessageNumberLimit. 0 = driver default. Fewer messages = fewer vectors to steer; the device's maximum is shown in the status line."), _limit));
            _prio = Combo.New(220, Enumerable.Range(0, 4).Select(i => new Opt(i.ToString(CultureInfo.InvariantCulture), IrqConfig.PriorityLabel(i))));
            c.Add(Ui.Row(L.T("Interrupt priority"), L.T("DevicePriority hint for the interrupt controller (Undefined = default). T1 key, effect T2."), _prio));
            _pol = Combo.New(270, Enumerable.Range(0, 7).Select(i => new Opt(i.ToString(CultureInfo.InvariantCulture), IrqConfig.PolicyLabel(i))));
            _pol.SelectionChanged += (s, e) => { if (!_sync && Combo.Val(_pol) == "4" && (_cpus.Text ?? "").Trim().Length == 0) Recommend(); };
            c.Add(Ui.Row(L.T("Affinity policy"), L.T("DevicePolicy: which CPUs may receive this device's interrupts (and usually run its DPCs). 'Specified processors' uses the target CPUs below."), _pol));
            _cpus = Ui.Box("", 240);
            _cpus.TextChanged += (s, e) => { if (_sync) return; _sync = true; try { _pick.Mask = ResolveCpus(); } finally { _sync = false; } };
            c.Add(Ui.Row(L.T("Target CPUs"), L.T("AssignmentSetOverride. Tick cores in the grid below, or type a rule (Freq, CCD1, E) or LPs (2,3 / 0xC). Put GPU / NIC / USB interrupts on a core the game does not use, then check DPC/ISR per CPU in WPA (Contour A)."), _cpus, false));

            _recommend = Ui.Btn(L.T("Recommend a core"), "BtnOutline");
            _recommend.Click += (s, e) => Recommend();
            _pOnly = new BoxCheck(L.T("P-cores only"));
            _pOnly.SetSilently(Settings.Get("IrqPOnly") != "0");
            _pOnly.CheckedChanged += () => Settings.SetBool("IrqPOnly", _pOnly.Checked);
            _pOnly.VerticalAlignment = VerticalAlignment.Center;
            _all = Ui.Btn(L.T("Select all"), "BtnNormal");
            _all.Click += (s, e) => _pick.Mask = _pick.AllMask;
            _clear = Ui.Btn(L.T("Clear all"), "BtnGhost");
            _clear.Click += (s, e) => { _cpus.Text = ""; _pickInfo.Text = Legend; _pickInfo.Foreground = Ui.B("Dim"); };
            c.Add(Ui.Strip(44, _recommend, _pOnly, _all, _clear));
            _pick = c.Add(new CoreGrid());
            _pick.Changed += () =>
            {
                if (_sync) return;
                _sync = true;
                try { ulong mask = _pick.Mask; _cpus.Text = mask == 0 ? "" : Masks.MaskLps(mask); if (mask != 0 && Combo.Val(_pol) != "4") Combo.Select(_pol, "4"); }
                finally { _sync = false; }
                WarnECores();
            };
            _pickInfo = c.Add(Ui.Text(Legend, 9, "Dim"));
            _pickInfo.Margin = new Thickness(0, 4, 0, 0);

            _apply = Ui.Btn(L.T("Apply"), "BtnAccent", 90);
            _apply.Click += (s, e) => EdApply();
            _restore = Ui.Btn(L.T("Restore original"), "BtnNormal");
            _restore.Click += (s, e) => EdRestore();
            _restart = Ui.Btn(L.T("Restart device"), "BtnNormal");
            _restart.Click += (s, e) => EdRestart();
            _check = Ui.Btn(L.T("Check status"), "BtnOutline");
            _check.Click += (s, e) => RunCheck();
            var strip = Ui.Strip(44, _apply, _restore, _restart, _check);
            strip.Margin = new Thickness(0, 10, 0, 0);
            c.Add(strip);
            _edStatus = c.Add(Ui.Text("", 9, "Dim"));
            _edStatus.Margin = new Thickness(0, 2, 0, 4);
            _checks = c.Add(new StackPanel());
            var note = c.Add(Ui.Text(L.T("Check status reads the registry and the device's resources, then records 5 s of kernel interrupt events (ETW - the same events WPR / WPA show). For the live part let the device work: a download, a game."), 8.75, "Faint"));
            note.Margin = new Thickness(0, 6, 0, 0);
        }

        private void EdRefreshMarks()
        {
            var t = T;
            if (_pick == null || t == null) return;
            try { _pick.MarkMask = GameMask.Effective(t).Mask; } catch { }
        }

        private void EdBind(PciDevice dev)
        {
            _dev = dev;
            bool on = dev != null;
            foreach (UIElement e in new UIElement[] { _msi, _limit, _prio, _pol, _cpus, _apply, _restore, _restart, _recommend, _pOnly, _clear, _all }) e.IsEnabled = on;
            _pick.SetEnabled(on);
            _check.IsEnabled = on && !_checking;
            _pickInfo.Text = Legend; _pickInfo.Foreground = Ui.B("Dim");
            var topo = T;
            string sig = CoreGrid.Sig(topo);
            if (topo != null && sig != _pickSig) { _pickSig = sig; _pick.Build(topo); }
            EdRefreshMarks();
            _pOnly.Visibility = topo != null && topo.Hybrid ? Visibility.Visible : Visibility.Collapsed;
            if (!on) { _edHead.Text = L.T("Select a device in the list above."); _edStatus.Text = ""; _checks.Children.Clear(); return; }
            _edHead.Text = dev.Name + "   ·   " + dev.Class;
            var c0 = Devices.ReadIrq(dev.InstanceId);
            _sync = true;
            try
            {
                Combo.Select(_msi, !c0.MsiSupported.HasValue ? "" : c0.MsiSupported.Value != 0 ? "1" : "0");
                _limit.Text = Math.Max(0, Math.Min(2048, c0.MsgLimit ?? 0)).ToString(CultureInfo.InvariantCulture);
                Combo.Select(_prio, Math.Max(0, Math.Min(3, c0.Priority ?? 0)).ToString(CultureInfo.InvariantCulture));
                Combo.Select(_pol, Math.Max(0, Math.Min(6, c0.Policy ?? 0)).ToString(CultureInfo.InvariantCulture));
                _cpus.Text = c0.Assign.HasValue ? Masks.MaskLps(c0.Assign.Value) : "";
                _pick.Mask = c0.Assign ?? 0;
            }
            finally { _sync = false; }
            _restore.IsEnabled = Devices.HasBackup(dev.InstanceId);
            if (!dev.MsiCapable && dev.IrqSupport >= 0) _msi.IsEnabled = false;
            ShowStatus(null);
            ShowConfig();
        }

        // ---- check status ----
        private IrqCheck.Facts Facts()
        {
            var names = _devRows.Where(r => _open == null || r != _open).Where(r => r.Cfg.Policy == 4 && r.Cfg.Assign.HasValue)
                                .Select(r => new KeyValuePair<string, ulong>(r.Dev.Name, r.Cfg.Assign.Value)).ToList();
            return IrqCheck.Gather(_dev, names, Math.Max(1, _devRows.Count(r => r.Dev.IsNet)));
        }

        private void SetChecks(IEnumerable<CheckLine> lines)
        {
            _checks.Children.Clear();
            if (lines == null) return;
            foreach (var l in lines)
            {
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string mark = l.Level == IrqCheck.Ok ? "✔" : l.Level == IrqCheck.Warn ? "⚠" : l.Level == IrqCheck.Bad ? "✖" : "•";
                string mc = l.Level == IrqCheck.Ok ? "Ok" : l.Level == IrqCheck.Warn ? "Warn" : l.Level == IrqCheck.Bad ? "Bad" : "Faint";
                var m = Ui.Semi(mark, 9.5, mc); m.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(m);
                var t = Ui.Text(l.Text, 9.25, l.Level == IrqCheck.Info ? "Dim" : "Fg");
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                _checks.Children.Add(g);
            }
        }

        private void ShowConfig()
        {
            if (_dev == null || (_checking && _dev.InstanceId == _checkInst)) return;     // its own check is drawing the lines
            try { SetChecks(IrqCheck.Config(Facts())); }
            catch (Exception ex) { SetChecks(new[] { new CheckLine(IrqCheck.Bad, ex.Message) }); }
        }

        // config now, then 5 s of ETW on a background thread; the result is shown only if the same device is still open
        private void RunCheck()
        {
            if (_dev == null || _checking) return;
            _checking = true; _check.IsEnabled = false;
            var dev = _dev;
            _checkInst = dev.InstanceId;
            List<CheckLine> cfg;
            try { cfg = IrqCheck.Config(Facts()); } catch (Exception ex) { cfg = new List<CheckLine> { new CheckLine(IrqCheck.Bad, ex.Message) }; }
            int left = 5;
            Action show = () => { if (_dev != null && _dev.InstanceId == dev.InstanceId) SetChecks(cfg.Concat(new[] { new CheckLine(IrqCheck.Info, L.F("Live check: recording kernel interrupt events, {0} s left…", left)) })); };
            show();
            _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdown.Tick += (s, e) => { if (left > 1) { left--; show(); } };
            _countdown.Start();
            AppLog.Add("[IRQ] " + dev.Name + ": live check (5 s of ISR / DPC events)");
            var th = new Thread(() =>
            {
                IrqTrace.Result r;
                try { r = IrqTrace.Capture(5); } catch (Exception ex) { r = new IrqTrace.Result { Error = ex.Message }; }
                try
                {
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        try { _countdown.Stop(); } catch { }
                        _checking = false; _checkInst = "";
                        _check.IsEnabled = _dev != null;
                        if (_dev == null || _dev.InstanceId != dev.InstanceId) { ShowConfig(); return; }     // another device opened meanwhile
                        IrqCheck.Facts f;
                        try { f = Facts(); } catch { f = null; }
                        var all = f != null ? IrqCheck.Full(f, r) : cfg;
                        SetChecks(all);
                        AppLog.Add("[IRQ] " + dev.Name + ": " + string.Join(" | ", all.Where(x => x.Level != IrqCheck.Info).Select(x => x.Text)));
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
            bool warn = extra != null && extra.StartsWith("!", StringComparison.Ordinal);
            string res = "";
            ulong target = ResolveCpus(), reserved = Reservation.Reserved();
            if ((target & reserved) != 0) { res = "\n" + L.T("Target LPs inside the reserved set: interrupt routing to reserved LPs is unverified (T3)."); warn = true; }
            var tg = T;
            if (tg != null && tg.Groups > 1) { res += "\n" + L.F("This PC has {0} processor groups: the target CPUs here are group 0 only. AssignmentSetOverride is one 64-bit mask and MS Learn documents no group value for it, so grobyq writes none.", tg.Groups); warn = true; }
            _edStatus.Foreground = warn ? Ui.B("Warn") : Ui.B("Dim");
            _edStatus.Text = L.T("In use now:") + " " + now + "   ·   " + L.T("supports:") + " " + _dev.IrqSupportText + "\n" + (extra != null ? extra.TrimStart('!') : L.T("Changes take effect after Restart device or a reboot.")) + res;
        }

        private ulong ResolveCpus()
        {
            string t = (_cpus.Text ?? "").Trim();
            if (t.Length == 0) return 0;
            var topo = T; if (topo == null) return 0;
            // A single number here is one logical processor ("11" = LP 11), not a decimal mask.
            int one;
            if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out one) && one >= 0 && one < 64)
                return topo.Sets.Any(s => s.LP == one) ? 1UL << one : 0;
            string err; var r = Masks.Resolve(topo, t, out err);
            return r == null ? 0 : r.Mask;
        }

        // Topology-aware pick for THIS device (CoreSets.RecommendIrq). Shows the pick and why; Apply and Restart stay explicit.
        private void Recommend()
        {
            if (_dev == null) return;
            var topo = T; if (topo == null) return;
            ulong game = Masks.Build(topo, GameMask.ManualRule(topo)).Mask;       // base + edits: never shaped by this device's own pin
            ulong busy = 0;
            foreach (var r0 in _devRows) if (r0 != _open && r0.Cfg.Assign.HasValue) busy |= r0.Cfg.Assign.Value;
            var r = CoreSets.RecommendIrq(topo, game, topo.Hybrid && _pOnly.Checked, busy, Reservation.Reserved());
            if (r.Lp < 0) { ShowStatus("!" + L.T("No core fits: only core 0 or reserved LPs are left. Nothing picked.")); return; }
            _sync = true;
            try { _cpus.Text = r.Lp.ToString(CultureInfo.InvariantCulture); _pick.Mask = 1UL << r.Lp; Combo.Select(_pol, "4"); }
            finally { _sync = false; }
            string why = r.Tier == 1 ? L.T("a core the game does not use")
                       : r.Tier == 2 ? L.T("the free second thread of a game core; hot game threads never get it")
                       : L.T("every LP is in the game mask: the second thread of the last core. With Interrupt cores on (Affinity > Cores) the game mask leaves it by itself");
            string extra = (r.ECore ? "  ·  " + L.T("E-core: its DPCs run longer than on a P-core - check DPC duration in WPA") : "")
                         + (r.Shared ? "  ·  " + L.T("shares its core with another device's interrupts") : "");
            _pickInfo.Foreground = Ui.B("Dim");
            _pickInfo.Text = L.F("LP {0} (core {1}) - {2}.", r.Lp, r.CoreNo, why) + extra + "  " + L.T("Apply, then Restart device.");
            ShowStatus(null);
        }

        private void WarnECores()
        {
            var topo = T;
            if (topo == null || !topo.Hybrid || !_pOnly.Checked) return;
            ulong m = _pick.Mask;
            var e = Enumerable.Range(0, 64).Where(lp => ((m >> lp) & 1) != 0 && CoreSets.IsECore(topo, lp)).ToList();
            if (e.Count == 0) return;
            _pickInfo.Foreground = Ui.B("Warn");
            _pickInfo.Text = L.F("E-core LP {0} ticked: allowed, but its DPCs run longer than on a P-core (T2). Check DPC duration per CPU in WPA.", string.Join(",", e));
        }

        private void EdApply()
        {
            if (_dev == null) return;
            bool storage = new[] { "SCSIAdapter", "HDC" }.Contains(_dev.Class, StringComparer.OrdinalIgnoreCase);
            if (storage && Combo.Val(_msi) == "0" && !Dlg.Ask(null, L.T("This is a storage controller. Turning MSI off on the boot drive's controller can stop Windows from booting. Continue?"), true)) return;
            string msi = Combo.Val(_msi), prio = Combo.Val(_prio), pol = Combo.Val(_pol);
            int lim; if (!int.TryParse((_limit.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out lim)) lim = 0;
            lim = Math.Max(0, Math.Min(2048, lim));
            var c = new IrqConfig
            {
                MsiSupported = msi == "" ? (int?)null : msi == "1" ? 1 : 0,
                MsgLimit = lim == 0 ? (int?)null : lim,
                Priority = prio == "0" || prio == "" ? (int?)null : int.Parse(prio, CultureInfo.InvariantCulture),
                Policy = pol == "0" || pol == "" ? (int?)null : int.Parse(pol, CultureInfo.InvariantCulture),
            };
            ulong cpus = ResolveCpus();
            if (pol == "4")
            {
                if (cpus == 0) { ShowStatus("!" + L.T("'Specified processors' needs target CPUs.")); return; }
                c.Assign = cpus;
            }
            string err = Devices.WriteIrq(_dev.InstanceId, c);
            if (err.Length > 0) { AppLog.Add("[IRQ] " + _dev.Name + ": " + err); ShowStatus("!" + err); return; }
            AppLog.Add(string.Format("[IRQ] {0}: MSI {1}, limit {2}, priority {3}, policy {4}{5}  (original saved to backup-irq.json)",
                _dev.Name, msi == "" ? "default" : msi == "1" ? "on" : "off", c.MsgLimit.HasValue ? c.MsgLimit.Value.ToString(CultureInfo.InvariantCulture) : "default", prio, pol, c.Assign.HasValue ? ", CPUs " + Masks.MaskLps(c.Assign.Value) : ""));
            _restore.IsEnabled = true;
            Devices.MarkApplied(_dev.InstanceId);
            ShowStatus("!" + L.T("Saved. Restart the device (or reboot) to apply; then compare DPC/ISR per CPU."));
            AfterIrqEdit();
            ShowConfig();
        }

        private void EdRestore()
        {
            if (_dev == null) return;
            string err = Devices.RestoreIrq(_dev.InstanceId);
            if (err.Length == 0) Devices.MarkApplied(_dev.InstanceId);         // the restored values also need a device restart
            AppLog.Add("[IRQ] " + _dev.Name + ": " + (err.Length == 0 ? "original interrupt settings restored" : err));
            EdBind(_dev);
            AfterIrqEdit();
        }

        private void EdRestart()
        {
            if (_dev == null) return;
            string warn = _dev.IsDisplay ? L.T("The screen will go black for a few seconds.") : _dev.IsNet ? L.T("The network drops for a few seconds.") : L.T("The device stops for a few seconds.");
            if (!Dlg.Ask(null, L.F("Restart {0}?", _dev.Name) + "\n\n" + warn)) return;
            Mouse.OverrideCursor = Cursors.Wait;
            string r;
            try { r = Devices.Restart(_dev.InstanceId); } finally { Mouse.OverrideCursor = null; }
            AppLog.Add("[DEV] restart " + _dev.Name + ": " + (r.Length == 0 ? "done" : r == "reboot" ? "Windows needs a reboot to finish" : r));
            if (r.Length == 0) Devices.MarkRestarted(_dev.InstanceId);
            ShowStatus(r.Length == 0 ? L.T("Restarted.") : r == "reboot" ? "!" + L.T("Reboot required to finish.") : "!" + r);
            ShowConfig();
        }

        // interrupt pins feed the adaptive game mask: the row, the chips, the Cores tab and (if running) the optimizer follow
        private void AfterIrqEdit()
        {
            if (_open != null) _open.Read();
            GameMask.Invalidate();
            ChipsRefresh();
            CoresShow(false);
            var o = Optimizer.Instance;
            if (o != null && o.Active && Settings.GetBool("DoMask")) o.ApplyNow();
        }
    }

    // One device row: icon, name + class / service, message limit current | max, target CPUs (or the policy), the
    // interrupt modes the device supports (the one in use filled), the priority, a chevron.
    public sealed class DevRow : Border
    {
        public readonly PciDevice Dev;
        public IrqConfig Cfg = new IrqConfig();
        public string InUse = "";
        private bool _hot, _exp, _backup;
        private readonly Grid _g;
        private readonly TextBlock _limit, _policy, _prio;
        private readonly Ellipse _dot;
        private readonly StackPanel _modes;
        private readonly Path _chev;

        public static Grid Columns()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });                       // icon
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });     // name
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });                       // limit
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(182) });                      // policy
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });                      // modes
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });                       // priority
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });                       // chevron
            return g;
        }

        public DevRow(PciDevice d)
        {
            Dev = d;
            Height = 56;
            Margin = new Thickness(0, 0, 0, 4);
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            _g = Columns();
            var ico = new Ico { Kind = IconKind(d), Width = 18, Height = 18, Stroke = Ui.B("Dim"), StrokeWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            _g.Children.Add(ico);
            _dot = new Ellipse { Width = 6, Height = 6, Fill = Ui.B("Warn"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 0, 24), Visibility = Visibility.Collapsed };
            _g.Children.Add(_dot);
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            name.Children.Add(Ui.Semi(d.Name, 10, "Fg"));
            var sub = Ui.Text(d.Class + (string.IsNullOrEmpty(d.Service) ? "" : "  ·  " + d.Service), 8.5, "Faint", false);
            sub.Margin = new Thickness(0, 1, 0, 0);
            name.Children.Add(sub);
            Grid.SetColumn(name, 1); _g.Children.Add(name);
            _limit = Cell(2); _policy = Cell(3); _prio = Cell(5);
            _modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_modes, 4); _g.Children.Add(_modes);
            _chev = new Path { Stroke = Ui.B("Dim"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_chev, 6); _g.Children.Add(_chev);
            Child = _g;
            MouseEnter += (s, e) => { _hot = true; Paint(); };
            MouseLeave += (s, e) => { _hot = false; Paint(); };
            Read();
        }

        private TextBlock Cell(int col)
        {
            var t = Ui.Text("", 9.25, "Fg", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(t, col);
            _g.Children.Add(t);
            return t;
        }

        private static string IconKind(PciDevice d)
        {
            switch ((d.Class ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return "gpu";
                case "USB": return "usb";
                case "NET": return "nic";
                case "MEDIA": return "audio";
                case "SCSIADAPTER": case "HDC": return "storage";
                default: return "chip";
            }
        }

        public bool Expanded { get { return _exp; } set { _exp = value; Paint(); } }

        public void Read()
        {
            try { Cfg = Devices.ReadIrq(Dev.InstanceId); } catch { Cfg = new IrqConfig(); }
            try { InUse = Devices.IrqInUse(Dev.DevInst) ?? ""; } catch { InUse = ""; }
            try { _backup = Devices.HasBackup(Dev.InstanceId); } catch { _backup = false; }
            ulong res = Reservation.Reserved();
            bool inReserved = Cfg.Assign.HasValue && (Cfg.Assign.Value & res) != 0;
            string tip = Dev.Name + "\n" + Dev.InstanceId + "\n" + L.T("In use now:") + " " + InUse + "   ·   " + L.T("Policy") + ": " + Cfg.PolicyText + (Cfg.Assign.HasValue ? " (" + Masks.MaskLps(Cfg.Assign.Value).Replace(",", ", ") + ")" : "");
            if (inReserved) tip += "\n\n" + L.T("Target LPs inside the reserved set: interrupt routing to reserved LPs is unverified (T3). Prefer the unreserved set or revert the reservation.");
            if (string.Equals(Dev.Class, "USB", StringComparison.OrdinalIgnoreCase))
                tip += "\n\n" + L.T("The mouse interrupt belongs to its USB controller: everything on that controller moves with it (Device Manager > View > Devices by connection).");
            ToolTip = tip;

            bool msi = Dev.MsiCapable || InUse.StartsWith("MSI", StringComparison.Ordinal);
            string max = Dev.MsgMax > 0 ? Dev.MsgMax.ToString(CultureInfo.InvariantCulture) : "–";
            _limit.Text = !msi ? "–" : (Cfg.MsgLimit.HasValue ? Cfg.MsgLimit.Value.ToString(CultureInfo.InvariantCulture) : max) + " | " + max;
            bool spec = Cfg.Assign.HasValue, set = Cfg.Policy.HasValue && Cfg.Policy.Value != 0;
            _policy.Text = (inReserved ? "▲ " : "") + (spec ? Masks.MaskLps(Cfg.Assign.Value).Replace(",", ", ") : set ? Cfg.PolicyText : "—");
            _policy.Foreground = inReserved ? Ui.B("Warn") : spec ? Ui.B("Fg") : Ui.B("Dim");
            bool undef = !Cfg.Priority.HasValue || Cfg.Priority.Value == 0;
            _prio.Text = undef ? "—" : Cfg.PriorityText;
            _prio.Foreground = undef ? Ui.B("Dim") : Ui.B("Fg");
            _dot.Visibility = _backup ? Visibility.Visible : Visibility.Collapsed;

            _modes.Children.Clear();
            foreach (var m in Modes())
            {
                var tb = Ui.Semi(m.Key, 8, m.Value ? "Fg" : "Dim");
                if (m.Value) tb.Foreground = Brushes.White;
                _modes.Children.Add(new Border { Child = tb, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Ui.B(m.Value ? "EdgeDn" : "Edge"), Background = m.Value ? Ui.B("EdgeDn") : Brushes.Transparent, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 5, 0) });
            }
            Paint();
        }

        // Modes the device supports (bit1 line, bit2 MSI, bit4 MSI-X); the one in use is filled.
        private List<KeyValuePair<string, bool>> Modes()
        {
            var l = new List<KeyValuePair<string, bool>>();
            bool msiNow = InUse.StartsWith("MSI", StringComparison.Ordinal), lineNow = InUse.Length > 0 && !msiNow && InUse != "n/a" && InUse != L.T("none") && InUse != L.T("not allocated");
            int n = 0; if (msiNow) int.TryParse(InUse.Replace("MSI x", ""), out n);
            int s = Dev.IrqSupport;
            if (s < 0) s = (msiNow ? 2 : 0) | (lineNow ? 1 : 0);
            bool x = (s & 4) != 0, m = (s & 2) != 0;
            if (m) l.Add(new KeyValuePair<string, bool>("MSI", msiNow && !(x && n > 1)));
            if (x) l.Add(new KeyValuePair<string, bool>("MSI-X", msiNow && (n > 1 || !m)));
            if ((s & 1) != 0) l.Add(new KeyValuePair<string, bool>("Line", lineNow));
            return l;
        }

        private void Paint()
        {
            Background = _exp || _hot ? Ui.B("CardHi") : Ui.B("Card");
            BorderBrush = _exp ? Ui.B("Edge") : Ui.B("Line");
            _chev.Stroke = _hot || _exp ? Ui.B("Fg") : Ui.B("Dim");
            _chev.Data = Geometry.Parse(_exp ? "M 0,4.4 L 4,0 L 8,4.4" : "M 0,0 L 4,4.4 L 8,0");
        }
    }
}
