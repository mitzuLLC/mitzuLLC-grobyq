using System;
using System.Collections.Generic;
using System.Linq;

namespace Grobyq
{
    public sealed class CheckLine
    {
        public int Level;              // IrqCheck.Ok / Warn / Bad / Info
        public string Text;
        public string Tag = "";        // "pending" / "live-ok": lets the full check reconcile the config with the live result
        public CheckLine(int level, string text) { Level = level; Text = text ?? ""; }
    }

    // "Check status" for one device's interrupts: plain lines, each a fact with its source.
    //   Config: the registry (DevicePolicy / AssignmentSetOverride / MSISupported, MS Learn T1), what the device has allocated
    //   now (CM_Get_First_Log_Conf), what Windows assigned at its last start (resource map, T2), whether a grobyq change has
    //   been followed by a device restart or a reboot, and how the target sits with the game mask.
    //   Live: IrqTrace counts - the device's ISRs by interrupt vector (exact, from the resource map) or by driver module
    //   (ndis.sys for network adapters), its DPCs by module, per CPU, against the target CPUs.
    // Config / Live are pure (the tests feed them facts); Gather reads the machine.
    public static class IrqCheck
    {
        public const int Ok = 0, Warn = 1, Bad = 2, Info = 3;

        public sealed class Facts
        {
            public PciDevice Dev = new PciDevice();
            public IrqConfig Cfg = new IrqConfig();
            public int AllocState = -1, Msi;                       // Devices.IrqAlloc
            public List<int> Lines = new List<int>();
            public Topo Topo;
            public GameMaskInfo Game;
            public ulong Reserved;
            public bool POnly = true;
            public DateTime? Applied, Restarted;
            public DateTime Boot;
            public List<Devices.IrqResource> Resources;            // null = not known
            public List<KeyValuePair<string, ulong>> Others = new List<KeyValuePair<string, ulong>>();   // other devices' pins (name, LPs)
            public int NetDevices = 1;
            public string DriverFile = "";
        }

        public static Facts Gather(PciDevice dev, List<KeyValuePair<string, ulong>> others, int netDevices)
        {
            var f = new Facts { Dev = dev ?? new PciDevice(), NetDevices = Math.Max(1, netDevices) };
            if (dev == null) return f;
            f.Cfg = Devices.ReadIrq(dev.InstanceId);
            f.AllocState = Devices.IrqAlloc(dev.DevInst, out f.Msi, out f.Lines);
            try { f.Topo = Topo.Live(); } catch { f.Topo = null; }   // was ThreadsPage.SafeTopo() (UI); engine reads topology directly
            try { if (f.Topo != null) f.Game = GameMask.Info(f.Topo); } catch { }
            f.Reserved = Reservation.Reserved();
            f.POnly = Settings.Get("IrqPOnly") != "0";
            f.Applied = Devices.StateTime(dev.InstanceId, "applied");
            f.Restarted = Devices.StateTime(dev.InstanceId, "restarted");
            try { f.Boot = Reservation.BootUtc(); } catch { f.Boot = DateTime.MinValue; }
            f.Resources = Devices.TranslatedIrqs(dev);
            f.Others = others ?? new List<KeyValuePair<string, ulong>>();
            f.DriverFile = Devices.DriverFile(dev);
            return f;
        }

        private static string R(ulong m) { return Masks.Ranges(m); }

        private static bool IsNet(Facts f) { return string.Equals(f.Dev.Class, "Net", StringComparison.OrdinalIgnoreCase); }

        private static ulong Target(Facts f) { return f.Cfg.Policy == 4 && f.Cfg.Assign.HasValue ? f.Cfg.Assign.Value : 0; }

        // "core 5, second thread" / "core 3, E-core" for one LP of group 0
        public static string Describe(Topo t, int lp)
        {
            if (t == null) return "";
            var g0 = t.Sets.Where(s => s.Group == 0).ToList();
            if (!g0.Any(s => s.LP == lp)) return "";
            var me = g0.First(s => s.LP == lp);
            var order = g0.GroupBy(s => s.Core).OrderBy(g => g.Min(s => s.LP)).Select(g => g.Key).ToList();
            var sib = g0.Where(s => s.Core == me.Core).OrderBy(s => s.LP).ToList();
            string pos = sib.Count < 2 ? "" : sib[0].LP == lp ? L.T("first thread") : L.T("second thread");
            string kind = CoreSets.IsECore(t, lp) ? L.T("E-core") : "";
            var parts = new List<string> { L.F("core {0}", order.IndexOf(me.Core)) };
            if (pos.Length > 0) parts.Add(pos);
            if (kind.Length > 0) parts.Add(kind);
            return string.Join(", ", parts);
        }

        // ---------------- config ----------------
        public static List<CheckLine> Config(Facts f)
        {
            var l = new List<CheckLine>();
            var t = f.Topo;
            ulong all = t != null ? t.FullMask() : ulong.MaxValue;

            // 1. policy
            int pol = f.Cfg.Policy ?? 0;
            if (pol == 4) l.Add(new CheckLine(Ok, L.T("Affinity policy: Specified processors (DevicePolicy = 4)")));
            else if (pol == 0) l.Add(new CheckLine(Info, L.T("Affinity policy: not set - Windows and the driver choose the CPU")));
            else l.Add(new CheckLine(Info, L.F("Affinity policy: {0} (DevicePolicy = {1})", IrqConfig.PolicyLabel(pol), pol)));

            // 2. target
            ulong tgt = Target(f);
            if (pol == 4)
            {
                if (tgt == 0) l.Add(new CheckLine(Bad, L.T("Specified processors without target CPUs: the setting does nothing")));
                else
                {
                    ulong missing = tgt & ~all;
                    var lps = Enumerable.Range(0, 64).Where(i => ((tgt >> i) & 1) != 0 && ((all >> i) & 1) != 0).ToList();
                    if (lps.Count == 1) l.Add(new CheckLine(Ok, L.F("Target in the registry: CPU {0} - {1}", lps[0], Describe(t, lps[0]))));
                    else if (lps.Count > 1) l.Add(new CheckLine(Info, L.F("Target in the registry: CPU {0} - Windows spreads the interrupts over them", R(tgt & all))));
                    if (missing != 0) l.Add(new CheckLine(Bad, L.F("CPU {0} does not exist on this PC", R(missing))));
                    if (t != null)
                    {
                        byte core0 = t.Sets.Any(s => s.Group == 0 && s.LP == 0) ? t.Sets.First(s => s.Group == 0 && s.LP == 0).Core : (byte)255;
                        var on0 = lps.Where(lp => t.Sets.Any(s => s.Group == 0 && s.LP == lp && s.Core == core0)).ToList();
                        if (on0.Count > 0) l.Add(new CheckLine(Warn, L.F("CPU {0} is on core 0: clock owner and default interrupt target", string.Join(",", on0))));
                        var e = lps.Where(lp => CoreSets.IsECore(t, lp)).ToList();
                        if (e.Count > 0 && f.POnly) l.Add(new CheckLine(Info, L.F("CPU {0} is an E-core: allowed, its DPCs run longer than on a P-core (T2)", string.Join(",", e))));
                        if (t.Groups > 1) l.Add(new CheckLine(Warn, L.F("This PC has {0} processor groups: the target addresses group 0 only", t.Groups)));
                    }
                    if ((tgt & f.Reserved) != 0) l.Add(new CheckLine(Warn, L.F("CPU {0} is reserved (ReservedCpuSets): interrupt routing there is unverified (T3)", R(tgt & f.Reserved))));
                }
            }

            // 3. MSI: the registry setting and what the device uses now
            string reg = !f.Cfg.MsiSupported.HasValue ? L.T("driver default") : f.Cfg.MsiSupported.Value != 0 ? L.T("on") : L.T("off");
            if (f.AllocState == 0) l.Add(new CheckLine(Warn, L.T("No interrupt allocated now: the device is stopped or disabled")));
            else if (f.AllocState > 0 && f.Msi > 0) l.Add(new CheckLine(Ok, L.F("MSI: the device uses message-signaled interrupts now ({0} message(s)); registry: {1}", f.Msi, reg)));
            else if (f.AllocState > 0 && f.Lines.Count > 0)
            {
                if (f.Cfg.MsiSupported == 1) l.Add(new CheckLine(Warn, L.F("MSI is on in the registry, but the device still uses line IRQ {0}: restart the device", string.Join(",", f.Lines))));
                else if (f.Dev.IrqSupport >= 0 && !f.Dev.MsiCapable) l.Add(new CheckLine(Info, L.F("Line IRQ {0}: the device supports line interrupts only", string.Join(",", f.Lines))));
                else l.Add(new CheckLine(Info, L.F("Line IRQ {0} (shared line); MSI in the registry: {1}", string.Join(",", f.Lines), reg)));
            }

            // 4. is the setting active? The resource map says what Windows assigned at the last device start; otherwise the
            //    record of when grobyq changed / restarted the device.
            if (pol == 4 && tgt != 0)
            {
                ulong assigned = 0;
                if (f.Resources != null) foreach (var r in f.Resources) if (r.Group == 0) assigned |= r.Affinity;
                assigned &= all;
                if (assigned != 0)
                {
                    if ((assigned & ~tgt) == 0) l.Add(new CheckLine(Ok, L.F("Active: Windows assigned the interrupts to CPU {0} at the last device start", R(assigned))));
                    else l.Add(new CheckLine(Warn, L.F("Not active yet: Windows still targets CPU {0}. Restart device or reboot", R(assigned))) { Tag = "pending" });
                }
                else if (f.Applied.HasValue)
                {
                    bool restarted = (f.Restarted.HasValue && f.Restarted.Value >= f.Applied.Value) || f.Boot > f.Applied.Value;
                    if (restarted) l.Add(new CheckLine(Ok, L.T("Active: the device restarted (or Windows rebooted) after the change")));
                    else l.Add(new CheckLine(Warn, L.T("The change waits for Restart device or a reboot")) { Tag = "pending" });
                }
                else l.Add(new CheckLine(Info, L.T("No record of when this was set (outside grobyq, or before 3.6.7): the live check shows where the interrupts really go")));
            }

            // 5. the game mask
            if (pol == 4 && tgt != 0 && f.Game != null && t != null)
            {
                var gi = f.Game;
                ulong inGame = tgt & gi.Final;
                bool dedicated = GameMask.Dedicated(t, tgt & all);
                if (inGame == 0)
                {
                    if ((tgt & gi.IrqDrop) != 0) l.Add(new CheckLine(Ok, L.F("Game mask: CPU {0} taken off automatically (Interrupt cores); the game gets LP {1}", R(tgt & gi.IrqDrop), R(gi.Final))));
                    else l.Add(new CheckLine(Ok, L.F("Game mask: CPU {0} is outside it; the game gets LP {1}", R(tgt & all), R(gi.Final))));
                }
                else if (!dedicated) l.Add(new CheckLine(Info, L.F("Game mask: CPU {0} stays in it - the target covers more than one core, the adaptive mask counts dedicated pins only", R(inGame))));
                else if (gi.Mode == GameMask.IrqOff) l.Add(new CheckLine(Warn, L.F("Game mask: CPU {0} is inside it - Interrupt cores are kept in the mask (Affinity > Cores)", R(inGame))));
                else if (gi.IrqIgnored) l.Add(new CheckLine(Warn, L.F("Game mask: CPU {0} stays in it - taking it would leave the game less than half of its cores", R(inGame))));
                else l.Add(new CheckLine(Info, L.F("Game mask: CPU {0} is inside it", R(inGame))));
            }

            // 6. another device on the same core
            if (pol == 4 && tgt != 0 && t != null)
            {
                ulong cores = GameMask.WholeCores(t, tgt);
                var share = f.Others.Where(o => (o.Value & cores) != 0).Select(o => o.Key).Distinct().ToList();
                if (share.Count > 0) l.Add(new CheckLine(Info, L.F("Shares its core with: {0}", string.Join(", ", share))));
            }
            return l;
        }

        // Config + live, reconciled: a live result that sees the interrupts on the target (enough events) overrides a
        // "waits for a restart" guess (the record can miss a restart done outside grobyq; the trace cannot).
        public static List<CheckLine> Full(Facts f, IrqTrace.Result r)
        {
            var cfg = Config(f);
            var live = Live(f, r);
            if (live.Any(x => x.Tag == "live-ok"))
                for (int i = 0; i < cfg.Count; i++)
                    if (cfg[i].Tag == "pending") cfg[i] = new CheckLine(Ok, L.T("Active: confirmed by the live check below"));
            return cfg.Concat(live).ToList();
        }

        // ---------------- live ----------------
        public static string Split(Dictionary<int, int> byCpu)
        {
            int total = byCpu.Values.Sum();
            if (total == 0) return "";
            var top = byCpu.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(3)
                           .Select(kv => { double p = 100.0 * kv.Value / total; return "CPU " + kv.Key + " " + (p < 1 ? "<1" : Math.Round(p).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " %"; }).ToList();
            int rest = byCpu.Count(kv => kv.Value > 0) - top.Count;
            return string.Join(", ", top) + (rest > 0 ? ", …" : "");
        }

        private static Dictionary<int, int> Sum(IEnumerable<Dictionary<int, int>> parts)
        {
            var d = new Dictionary<int, int>();
            foreach (var p in parts) foreach (var kv in p) { int c; d.TryGetValue(kv.Key, out c); d[kv.Key] = c + kv.Value; }
            return d;
        }

        public static List<CheckLine> Live(Facts f, IrqTrace.Result r)
        {
            var l = new List<CheckLine>();
            if (r == null) return l;
            if (r.Error.Length > 0)
            {
                if (r.Error == "busy") l.Add(new CheckLine(Warn, L.T("Live check: another check is running - try again in a few seconds")));
                else if (r.Error == "admin") l.Add(new CheckLine(Bad, L.T("Live check needs administrator rights (run grobyq as administrator)")));
                else if (r.Error == "x64") l.Add(new CheckLine(Bad, L.T("Live check needs the 64-bit grobyq")));
                else if (r.Error.StartsWith("StartTrace 1450")) l.Add(new CheckLine(Bad, L.T("Live check: too many system trace sessions are running (WPR, xperf, other tools) - stop them and retry")));
                else if (r.Error == "StartTrace 87") l.Add(new CheckLine(Bad, L.T("Live check needs Windows 8 or newer (private kernel trace sessions)")));
                else if (r.Error == "timeout") l.Add(new CheckLine(Warn, L.T("Live check: the trace did not finish in time - try again in a few seconds")));
                else l.Add(new CheckLine(Bad, L.F("Live check failed: {0}", r.Error)));
                return l;
            }

            // ISRs: by vector on the CPUs Windows assigned it to (exact), else by module; DPCs by module
            // the driver itself, plus the framework that registers the ISR for it: NDIS (network), DXGKRNL (WDDM display),
            // Storport (NVMe / SAS); those serve several devices, so without the vector the count can mix them
            var mods = new List<string>();
            if (f.DriverFile.Length > 0) mods.Add(f.DriverFile.ToLowerInvariant());
            string shared = IsNet(f) ? "ndis.sys" : string.Equals(f.Dev.Class, "Display", StringComparison.OrdinalIgnoreCase) ? "dxgkrnl.sys"
                          : string.Equals(f.Dev.Class, "SCSIAdapter", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Dev.Class, "HDC", StringComparison.OrdinalIgnoreCase) ? "storport.sys" : "";
            if (shared.Length > 0 && !mods.Contains(shared)) mods.Add(shared);
            int g0 = f.Topo != null ? f.Topo.Sets.Count(s => s.Group == 0) : 64;      // ProcessorIndex is flat: group 1 starts after group 0
            var isr = new Dictionary<int, int>();
            bool byVector = false;
            if (f.Resources != null && f.Resources.Count > 0)
            {
                foreach (var res in f.Resources.Where(x => x.Group == 0))
                {
                    Dictionary<int, int> d;
                    if (!r.IsrVector.TryGetValue(res.Vector & 0xFF, out d)) continue;
                    foreach (var kv in d)
                        if (kv.Key < 64 && ((res.Affinity >> kv.Key) & 1) != 0) { int c; isr.TryGetValue(kv.Key, out c); isr[kv.Key] = c + kv.Value; }
                }
                byVector = isr.Values.Sum() > 0;
            }
            if (!byVector) isr = Sum(mods.Where(m => r.Isr.ContainsKey(m)).Select(m => r.Isr[m]));
            var dpc = Sum(mods.Where(m => r.Dpc.ContainsKey(m)).Select(m => r.Dpc[m]));
            isr = isr.Where(kv => kv.Key < g0).ToDictionary(kv => kv.Key, kv => kv.Value);
            dpc = dpc.Where(kv => kv.Key < g0).ToDictionary(kv => kv.Key, kv => kv.Value);
            int ni = isr.Values.Sum(), nd = dpc.Values.Sum();

            if (ni + nd == 0)
            {
                l.Add(new CheckLine(Warn, L.F("Live check ({0} s): no interrupts from this device - let it work (a download, a game) and check again", r.Seconds)));
                if (!r.ModulesResolved && !byVector) l.Add(new CheckLine(Info, L.T("Kernel modules could not be resolved: run grobyq as administrator")));
                else if (!IsNet(f)) l.Add(new CheckLine(Info, L.T("Some drivers take interrupts through a shared framework (WDF, Storport): without the vector their events cannot be told apart - check WPA")));
                return l;
            }

            // verdict on the ISRs (the interrupt itself), DPCs when no ISR could be attributed
            var basis = ni > 0 ? isr : dpc;
            int nb = basis.Values.Sum();
            ulong tgt = Target(f);
            if (tgt != 0)
            {
                int on = basis.Where(kv => kv.Key < 64 && ((tgt >> kv.Key) & 1) != 0).Sum(kv => kv.Value);
                double share = 100.0 * on / nb;
                var top = basis.OrderByDescending(kv => kv.Value).First().Key;
                if (share >= 90) l.Add(new CheckLine(nb < 20 ? Warn : Ok, L.F("Live check ({0} s): {1} % of the interrupts ran on CPU {2} - as set", r.Seconds, Math.Round(share), R(tgt))) { Tag = nb < 20 ? "" : "live-ok" });
                else if (share <= 50) l.Add(new CheckLine(Bad, L.F("Live check ({0} s): the interrupts ran mostly on CPU {1}, not on CPU {2} - restart the device (or reboot) and check again", r.Seconds, top, R(tgt))));
                else l.Add(new CheckLine(Warn, L.F("Live check ({0} s): only {1} % of the interrupts ran on CPU {2}", r.Seconds, Math.Round(share), R(tgt))));
            }
            else l.Add(new CheckLine(Info, L.F("Live check ({0} s): interrupts ran on {1} (no fixed target: Windows chooses)", r.Seconds, Split(basis))));

            if (ni > 0) l.Add(new CheckLine(Info, L.F("ISR {0}: {1}   ·   {2}", ni, Split(isr), byVector ? L.T("by interrupt vector (exact)") : L.F("by driver module ({0})", string.Join(", ", mods)))));
            if (nd > 0) l.Add(new CheckLine(Info, L.F("DPC {0}: {1}   ·   {2}", nd, Split(dpc), L.F("by driver module ({0})", string.Join(", ", mods)))));
            if (nb < 20) l.Add(new CheckLine(Warn, L.F("Only {0} events: the result is rough - check again under load (a download, a game)", nb)));
            if (IsNet(f) && !byVector && f.NetDevices > 1) l.Add(new CheckLine(Info, L.T("ndis.sys serves every network adapter: with Wi-Fi and Ethernet both busy the count mixes them")));
            else if (!byVector && shared.Length > 0 && !IsNet(f)) l.Add(new CheckLine(Info, L.F("{0} serves several devices of this kind: without the vector the count can mix them", shared)));
            if (r.EventsLost > 0 || r.BuffersLost > 0) l.Add(new CheckLine(Info, L.F("ETW dropped {0} events and {1} buffers under load: the counts are a sample", r.EventsLost, r.BuffersLost)));
            return l;
        }
    }
}
