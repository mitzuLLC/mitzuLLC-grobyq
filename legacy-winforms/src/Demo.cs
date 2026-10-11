#if DEMO
using System.Collections.Generic;

namespace Grobyq
{
    // UI demo data for layout checks off-Windows (build with -p:DefineConstants=DEMO, run with GROBYQ_DEMO=1).
    internal static class Demo
    {
        public static void Install()
        {
            SysInfo.Hook = () => new SysInfo { Cpu = "AMD Ryzen 9 7950X3D 16-Core Processor", Gpu = "NVIDIA GeForce RTX 3050", Ram = "32 GB (2x modules) @ 6000 MT/s",
                                               Board = "ASUSTeK COMPUTER INC. ROG STRIX X670E-E GAMING WIFI", Os = "Windows 11 Pro 24H2 (26100.4652)", Disk = "Samsung SSD 990 PRO 2TB  ·  C: 1204 / 1863 GB free" };
            string cpu = System.Environment.GetEnvironmentVariable("GROBYQ_DEMO_CPU") ?? "";   // 7950x3d (default) | 13600k | 7800x3d | 7950x
            Topo.LiveHook = () =>
            {
                var l = new List<Native.CpuSet>();
                if (cpu == "13600k")
                {
                    for (int lp = 0; lp < 12; lp++) l.Add(new Native.CpuSet { Id = (uint)(0x100 + lp), LP = (byte)lp, Core = (byte)(lp & ~1), LLC = 0, Eff = 1 });
                    for (int lp = 12; lp < 20; lp++) l.Add(new Native.CpuSet { Id = (uint)(0x100 + lp), LP = (byte)lp, Core = (byte)lp, LLC = 0, Eff = 0 });
                    return Topo.From(l, new Dictionary<byte, ulong> { { 0, 24UL << 20 } }, "13th Gen Intel(R) Core(TM) i5-13600K", 20);
                }
                if (cpu == "12400f")
                {
                    for (int lp = 0; lp < 12; lp++) l.Add(new Native.CpuSet { Id = (uint)(0x100 + lp), LP = (byte)lp, Core = (byte)(lp & ~1), LLC = 0 });
                    return Topo.From(l, new Dictionary<byte, ulong> { { 0, 18UL << 20 } }, "12th Gen Intel(R) Core(TM) i5-12400F", 12);
                }
                int cores = cpu == "7800x3d" ? 8 : 16;
                for (int c = 0; c < cores; c++)
                    for (int t = 0; t < 2; t++)
                    {
                        int lp = c * 2 + t;
                        l.Add(new Native.CpuSet { Id = (uint)(0x100 + lp), LP = (byte)lp, Core = (byte)(c * 2), LLC = (byte)(c < 8 || cores == 8 ? 0 : 16) });
                    }
                if (cpu == "7800x3d") return Topo.From(l, new Dictionary<byte, ulong> { { 0, 96UL << 20 } }, "AMD Ryzen 7 7800X3D 8-Core Processor", 16);
                if (cpu == "7950x") return Topo.From(l, new Dictionary<byte, ulong> { { 0, 32UL << 20 }, { 16, 32UL << 20 } }, "AMD Ryzen 9 7950X 16-Core Processor", 32);
                return Topo.From(l, new Dictionary<byte, ulong> { { 0, 96UL << 20 }, { 16, 32UL << 20 } }, "AMD Ryzen 9 7950X3D 16-Core Processor", 32);
            };
            // adaptive game mask: GROBYQ_DEMO_IRQ=11 pins the demo NIC's interrupts to LP 11
            string irq = System.Environment.GetEnvironmentVariable("GROBYQ_DEMO_IRQ") ?? "";
            if (irq.Length > 0)
            {
                ulong m = 0;
                foreach (var p in irq.Split(',')) { int lp; if (int.TryParse(p.Trim(), out lp) && lp >= 0 && lp < 64) m |= 1UL << lp; }
                GameMask.IrqHook = () => new List<KeyValuePair<string, ulong>> { new KeyValuePair<string, ulong>("Realtek PCIe GbE Family Controller", m) };
            }
            // check status: the device uses MSI, and the live check sees ndis.sys / the Realtek driver on the pinned LP
            Devices.AllocHook = di => System.Tuple.Create(1, 1, new List<int>());
            IrqTrace.Hook = sec =>
            {
                System.Threading.Thread.Sleep(sec * 1000);
                int lp; if (!int.TryParse(irq.Split(',')[0], out lp)) lp = 11;
                var r = new IrqTrace.Result { Seconds = sec, ModulesResolved = true };
                r.Isr["ndis.sys"] = new Dictionary<int, int> { { lp, 1843 } };
                r.Dpc["ndis.sys"] = new Dictionary<int, int> { { lp, 1797 }, { 4, 6 } };
                r.Dpc["rt640x64.sys"] = new Dictionary<int, int> { { lp, 21 } };
                return r;
            };
            // ReservedCpuSets card against a harmless per-user key
            Reservation.Hive = Microsoft.Win32.Registry.CurrentUser;
            Reservation.KeyPath = @"Software\grobyq-demo\kernel";
            var boot = System.DateTime.UtcNow.AddMinutes(-5);
            Reservation.BootUtc = () => boot;
            // GPU (NvDrs) and NIC (NetAdapters) demo hooks removed with the Devices & Drivers tabs.
            Devices.ListHook = () => new List<PciDevice>
            {
                new PciDevice { InstanceId = @"PCI\VEN_10DE&DEV_2507&SUBSYS_00000000&REV_A1\4&1", Name = "NVIDIA GeForce RTX 3050", Class = "Display", Service = "nvlddmkm", Mfg = "NVIDIA", LinkSpeed = 4, LinkWidth = 8, MaxLinkSpeed = 4, MaxLinkWidth = 8, IrqSupport = 6, MsgMax = 1 },
                new PciDevice { InstanceId = @"PCI\VEN_10EC&DEV_8168&SUBSYS_00000000&REV_15\4&2", Name = "Realtek PCIe GbE Family Controller", Class = "Net", Service = "rt640x64", Mfg = "Realtek", LinkSpeed = 1, LinkWidth = 1, MaxLinkSpeed = 1, MaxLinkWidth = 1, IrqSupport = 7, MsgMax = 4 },
                new PciDevice { InstanceId = @"PCI\VEN_1022&DEV_43F7\4&3", Name = "AMD USB 3.20 eXtensible Host Controller", Class = "USB", Service = "USBXHCI", Mfg = "AMD", IrqSupport = 6, MsgMax = 8 },
            };
        }
    }
}
#endif
