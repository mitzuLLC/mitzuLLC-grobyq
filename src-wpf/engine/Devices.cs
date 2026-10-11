using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Grobyq
{
    // One present PCI device, as SetupAPI sees it.
    public sealed class PciDevice
    {
        public string InstanceId = "";
        public string Name = "";
        public string Class = "";        // Display, Net, USB, MEDIA, SCSIAdapter, HDC, System ...
        public string Service = "";
        public string DriverKey = "";    // {class-guid}\0001
        public string Mfg = "";
        public string Location = "";
        public string Pdo = "";          // \Device\NTPNP_PCI00xx: names its resource map entry
        public uint DevInst;
        public int LinkSpeed, LinkWidth, MaxLinkSpeed, MaxLinkWidth;   // PCIe gen / lanes, 0 = unknown
        public int IrqSupport = -1;      // bit1 line, bit2 MSI, bit4 MSI-X (DEVPKEY_PciDevice_InterruptSupport)
        public int MsgMax = -1;          // DEVPKEY_PciDevice_InterruptMessageMaximum

        public bool IsDisplay { get { return string.Equals(Class, "Display", StringComparison.OrdinalIgnoreCase); } }
        public bool IsNet { get { return string.Equals(Class, "Net", StringComparison.OrdinalIgnoreCase); } }
        public string Short { get { return Name.Length > 34 ? Name.Substring(0, 33) + "…" : Name; } }

        public string LinkText
        {
            get
            {
                if (LinkSpeed <= 0 && MaxLinkSpeed <= 0) return "n/a";
                return string.Format("Gen{0} x{1}  ({2} Gen{3} x{4})", LinkSpeed, LinkWidth, L.T("max"), MaxLinkSpeed, MaxLinkWidth);
            }
        }
        public string IrqSupportText
        {
            get
            {
                if (IrqSupport < 0) return "n/a";
                var l = new List<string>();
                if ((IrqSupport & 1) != 0) l.Add(L.T("line"));
                if ((IrqSupport & 2) != 0) l.Add("MSI");
                if ((IrqSupport & 4) != 0) l.Add("MSI-X");
                return string.Join(" / ", l) + (MsgMax > 0 ? "  (" + L.F("max {0} messages", MsgMax) + ")" : "");
            }
        }
        public bool MsiCapable { get { return IrqSupport > 0 && (IrqSupport & 6) != 0; } }
    }

    // Interrupt settings stored per device instance (MS Learn: "Interrupt Affinity and
    // Priority", "Enabling Message-Signaled Interrupts in the Registry"). null = not set.
    public sealed class IrqConfig
    {
        public int? MsiSupported;       // MessageSignaledInterruptProperties\MSISupported
        public int? MsgLimit;           // ...\MessageNumberLimit
        public int? Policy;             // Affinity Policy\DevicePolicy
        public int? Priority;           // Affinity Policy\DevicePriority
        public ulong? Assign;           // Affinity Policy\AssignmentSetOverride (KAFFINITY)

        public IrqConfig Clone() { return (IrqConfig)MemberwiseClone(); }

        public static readonly string[] PolicyNames =
        { "Machine default", "All close processors", "One close processor", "All processors", "Specified processors", "Spread messages", "All processors when steered" };
        public static readonly string[] PriorityNames = { "Undefined", "Low", "Normal", "High" };

        public string PolicyText { get { return PolicyLabel(Policy.HasValue ? Policy.Value : 0); } }
        public string PriorityText { get { return PriorityLabel(Priority.HasValue ? Priority.Value : 0); } }
        public string MsiText { get { return !MsiSupported.HasValue ? L.T("driver default") : MsiSupported.Value != 0 ? L.T("on") : L.T("off"); } }

        public static string PolicyLabel(int i)
        {
            switch (i)
            {
                case 1: return L.T("All close processors");
                case 2: return L.T("One close processor");
                case 3: return L.T("All processors");
                case 4: return L.T("Specified processors");
                case 5: return L.T("Spread messages");
                case 6: return L.T("All processors when steered");
                default: return L.T("Machine default");
            }
        }

        public static string PriorityLabel(int i)
        {
            switch (i)
            {
                case 1: return L.T("Low");
                case 2: return L.T("Normal");
                case 3: return L.T("High");
                default: return L.T("Undefined");
            }
        }
    }

    public static class Devices
    {
        // ------------------------------------------------------------------ P/Invoke
        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [StructLayout(LayoutKind.Sequential)]
        private struct SP_CLASSINSTALL_HEADER { public uint cbSize; public uint InstallFunction; }
        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS { public SP_CLASSINSTALL_HEADER Header; public uint StateChange; public uint Scope; public uint HwProfile; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SP_DEVINSTALL_PARAMS
        {
            public uint cbSize; public uint Flags; public uint FlagsEx; public IntPtr hwndParent;
            public IntPtr InstallMsgHandler; public IntPtr InstallMsgHandlerContext; public IntPtr FileQueue;
            public IntPtr ClassInstallReserved; public uint Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint idx, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder id, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA d, uint prop, out uint regType, byte[] buf, uint size, out uint req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA d, ref DEVPROPKEY key, out uint type, byte[] buf, uint size, out uint req, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiSetClassInstallParams(IntPtr set, ref SP_DEVINFO_DATA d, ref SP_PROPCHANGE_PARAMS p, int size);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiCallClassInstaller(uint fn, IntPtr set, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr guid, IntPtr hwnd);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiOpenDeviceInfo(IntPtr set, string inst, IntPtr hwnd, uint flags, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstallParams(IntPtr set, ref SP_DEVINFO_DATA d, ref SP_DEVINSTALL_PARAMS p);

        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_First_Log_Conf(out IntPtr lc, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Next_Res_Des(out IntPtr rd, IntPtr cur, uint forRes, out uint resId, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Res_Des_Data_Size(out uint size, IntPtr rd, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Res_Des_Data(IntPtr rd, byte[] buf, uint len, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Free_Res_Des_Handle(IntPtr rd);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Free_Log_Conf_Handle(IntPtr lc);

        private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        private const uint SPDRP_DEVICEDESC = 0, SPDRP_SERVICE = 4, SPDRP_CLASS = 7, SPDRP_DRIVER = 9, SPDRP_MFG = 11, SPDRP_FRIENDLYNAME = 12, SPDRP_LOCATION = 13, SPDRP_PHYSICAL_DEVICE_OBJECT_NAME = 14;
        private static readonly Guid PciProps = new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
        private static readonly IntPtr INVALID = new IntPtr(-1);

        // ------------------------------------------------------------------ enumerate
        public static Func<List<PciDevice>> ListHook;   // UI demo only

        public static List<PciDevice> ListPci()
        {
            if (ListHook != null) return ListHook();
            var res = new List<PciDevice>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "PCI", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == INVALID || set == IntPtr.Zero) return res;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var d = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    var dev = new PciDevice { DevInst = d.DevInst };
                    var sb = new StringBuilder(512); int req;
                    if (SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) dev.InstanceId = sb.ToString();
                    dev.Name = RegStr(set, ref d, SPDRP_FRIENDLYNAME);
                    if (dev.Name.Length == 0) dev.Name = RegStr(set, ref d, SPDRP_DEVICEDESC);
                    dev.Class = RegStr(set, ref d, SPDRP_CLASS);
                    dev.Service = RegStr(set, ref d, SPDRP_SERVICE);
                    dev.DriverKey = RegStr(set, ref d, SPDRP_DRIVER);
                    dev.Mfg = RegStr(set, ref d, SPDRP_MFG);
                    dev.Location = RegStr(set, ref d, SPDRP_LOCATION);
                    dev.Pdo = RegStr(set, ref d, SPDRP_PHYSICAL_DEVICE_OBJECT_NAME);
                    dev.LinkSpeed = PropU32(set, ref d, 9); dev.LinkWidth = PropU32(set, ref d, 10);
                    dev.MaxLinkSpeed = PropU32(set, ref d, 11); dev.MaxLinkWidth = PropU32(set, ref d, 12);
                    int sup = PropU32(set, ref d, 14, -1); dev.IrqSupport = sup;
                    dev.MsgMax = PropU32(set, ref d, 15, -1);
                    if (dev.InstanceId.Length > 0) res.Add(dev);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return res;
        }

        private static string RegStr(IntPtr set, ref SP_DEVINFO_DATA d, uint prop)
        {
            try
            {
                var buf = new byte[1024]; uint t, req;
                if (!SetupDiGetDeviceRegistryProperty(set, ref d, prop, out t, buf, (uint)buf.Length, out req)) return "";
                string s = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(req, (uint)buf.Length));
                int z = s.IndexOf('\0'); if (z >= 0) s = s.Substring(0, z);
                return s.Trim();
            }
            catch { return ""; }
        }

        private static int PropU32(IntPtr set, ref SP_DEVINFO_DATA d, uint pid, int def = 0)
        {
            try
            {
                var key = new DEVPROPKEY { fmtid = PciProps, pid = pid };
                var buf = new byte[8]; uint t, req;
                if (!SetupDiGetDeviceProperty(set, ref d, ref key, out t, buf, (uint)buf.Length, out req, 0)) return def;
                return BitConverter.ToInt32(buf, 0);
            }
            catch { return def; }
        }

        // ------------------------------------------------------------------ driver info
        public static Dictionary<string, string> DriverInfo(PciDevice dev)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(dev.DriverKey)) return d;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + dev.DriverKey, false))
                {
                    if (k == null) return d;
                    foreach (var n in new[] { "DriverVersion", "DriverDate", "ProviderName", "DriverDesc", "InfPath", "MatchingDeviceId" })
                    {
                        var v = k.GetValue(n); if (v != null) d[n] = v.ToString();
                    }
                }
            }
            catch { }
            return d;
        }

        // ------------------------------------------------------------------ interrupts: registry
        private static string MgmtPath(string inst) { return @"SYSTEM\CurrentControlSet\Enum\" + inst + @"\Device Parameters\Interrupt Management"; }

        public static IrqConfig ReadIrq(string inst)
        {
            var c = new IrqConfig();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(MgmtPath(inst) + @"\MessageSignaledInterruptProperties", false))
                    if (k != null) { c.MsiSupported = Int(k.GetValue("MSISupported")); c.MsgLimit = Int(k.GetValue("MessageNumberLimit")); }
                using (var k = Registry.LocalMachine.OpenSubKey(MgmtPath(inst) + @"\Affinity Policy", false))
                    if (k != null)
                    {
                        c.Policy = Int(k.GetValue("DevicePolicy")); c.Priority = Int(k.GetValue("DevicePriority"));
                        var b = k.GetValue("AssignmentSetOverride") as byte[];
                        if (b != null && b.Length > 0) { var b8 = new byte[8]; Array.Copy(b, b8, Math.Min(8, b.Length)); c.Assign = BitConverter.ToUInt64(b8, 0); }
                    }
            }
            catch { }
            return c;
        }

        private static int? Int(object v) { if (v == null) return null; try { return Convert.ToInt32(v); } catch { return null; } }

        // Present PCI devices whose interrupts are pinned: DevicePolicy = Specified processors (4) with a non-empty
        // AssignmentSetOverride - whoever wrote it (grobyq, GoInterruptPolicy, MSI utility). Name -> LP mask. Only
        // present devices count: the Enum entry of a removed GPU never takes a core from the game.
        public static List<KeyValuePair<string, ulong>> PinnedIrqs()
        {
            var res = new List<KeyValuePair<string, ulong>>();
            foreach (var d in ListPciNames())
            {
                var c = ReadIrq(d.Key);
                if (c.Policy == 4 && c.Assign.HasValue && c.Assign.Value != 0)
                    res.Add(new KeyValuePair<string, ulong>(d.Value.Length > 0 ? d.Value : d.Key, c.Assign.Value));
            }
            return res;
        }

        // instance id -> display name of every present PCI device; two queries per device (the game mask asks every 30 s)
        public static List<KeyValuePair<string, string>> ListPciNames()
        {
            var res = new List<KeyValuePair<string, string>>();
            if (ListHook != null) { foreach (var d in ListHook()) res.Add(new KeyValuePair<string, string>(d.InstanceId, d.Name)); return res; }
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "PCI", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == INVALID || set == IntPtr.Zero) return res;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var d = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    var sb = new StringBuilder(512); int req;
                    if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
                    string name = RegStr(set, ref d, SPDRP_FRIENDLYNAME);
                    if (name.Length == 0) name = RegStr(set, ref d, SPDRP_DEVICEDESC);
                    res.Add(new KeyValuePair<string, string>(sb.ToString(), name));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return res;
        }

        // Writes only what differs from "not set". First write per device keeps the original in backup-irq.json.
        public static string WriteIrq(string inst, IrqConfig want)
        {
            try
            {
                BackupOnce(inst);
                using (var k = Registry.LocalMachine.CreateSubKey(MgmtPath(inst) + @"\MessageSignaledInterruptProperties"))
                {
                    SetOrDelete(k, "MSISupported", want.MsiSupported);
                    SetOrDelete(k, "MessageNumberLimit", want.MsgLimit);
                }
                using (var k = Registry.LocalMachine.CreateSubKey(MgmtPath(inst) + @"\Affinity Policy"))
                {
                    SetOrDelete(k, "DevicePolicy", want.Policy);
                    SetOrDelete(k, "DevicePriority", want.Priority);
                    if (want.Assign.HasValue && want.Assign.Value != 0) k.SetValue("AssignmentSetOverride", BitConverter.GetBytes(want.Assign.Value), RegistryValueKind.Binary);
                    else k.DeleteValue("AssignmentSetOverride", false);
                }
                return "";
            }
            catch (UnauthorizedAccessException) { return "access denied - run grobyq as administrator"; }
            catch (Exception ex) { return ex.Message; }
        }

        private static void SetOrDelete(RegistryKey k, string name, int? v)
        {
            if (v.HasValue) k.SetValue(name, v.Value, RegistryValueKind.DWord); else k.DeleteValue(name, false);
        }

        private static string BackupPath { get { return Path.Combine(Store.Dir, "backup-irq.json"); } }

        private static Dictionary<string, object> LoadBackup()
        {
            try { if (File.Exists(BackupPath)) return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(BackupPath)) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
            catch { }
            return new Dictionary<string, object>();
        }

        private static void BackupOnce(string inst)
        {
            var all = LoadBackup();
            if (all.ContainsKey(inst)) return;
            var c = ReadIrq(inst);
            all[inst] = new Dictionary<string, object>
            {
                { "MSISupported", c.MsiSupported }, { "MessageNumberLimit", c.MsgLimit },
                { "DevicePolicy", c.Policy }, { "DevicePriority", c.Priority },
                { "AssignmentSetOverride", c.Assign.HasValue ? "0x" + c.Assign.Value.ToString("X") : null },
            };
            Directory.CreateDirectory(Store.Dir);
            File.WriteAllText(BackupPath, Store.Json(all));
        }

        public static bool HasBackup(string inst) { return LoadBackup().ContainsKey(inst); }

        public static string RestoreIrq(string inst)
        {
            var all = LoadBackup();
            object o;
            if (!all.TryGetValue(inst, out o)) return "no backup for this device (grobyq never changed it)";
            var m = o as Dictionary<string, object>;
            if (m == null) return "backup entry unreadable";
            Func<string, int?> gi = n => { object v; return m.TryGetValue(n, out v) && v != null ? (int?)Convert.ToInt32(v) : null; };
            var c = new IrqConfig { MsiSupported = gi("MSISupported"), MsgLimit = gi("MessageNumberLimit"), Policy = gi("DevicePolicy"), Priority = gi("DevicePriority") };
            object a; if (m.TryGetValue("AssignmentSetOverride", out a) && a != null) c.Assign = Masks.Parse(a.ToString());
            string err = WriteIrq(inst, c);
            if (err.Length == 0) { all.Remove(inst); try { File.WriteAllText(BackupPath, Store.Json(all)); } catch { } }
            return err;
        }

        // ------------------------------------------------------------------ interrupts: what is in use now
        // Allocated IRQ descriptors. MSI/MSI-X messages show up as negative IRQ numbers
        // (Device Manager prints them as 42949672xx). Returns e.g. "MSI x3" or "line IRQ 16".
        public static string IrqInUse(uint devInst)
        {
            int msi; List<int> lines;
            int st = IrqAlloc(devInst, out msi, out lines);
            if (st < 0) return "n/a";
            if (st == 0) return L.T("not allocated");
            if (msi > 0) return "MSI x" + msi;
            if (lines.Count > 0) return L.T("line IRQ") + " " + string.Join(",", lines);
            return L.T("none");
        }

        public static Func<uint, Tuple<int, int, List<int>>> AllocHook;     // UI demo: (state, msi, lines)

        // 1 = allocated (msi = MSI / MSI-X messages, lines = line IRQs), 0 = no allocated configuration, -1 = unknown
        public static int IrqAlloc(uint devInst, out int msi, out List<int> lines)
        {
            msi = 0; lines = new List<int>();
            if (AllocHook != null) { var h = AllocHook(devInst); msi = h.Item2; lines = h.Item3; return h.Item1; }
            try
            {
                IntPtr lc;
                if (CM_Get_First_Log_Conf(out lc, devInst, 2 /*ALLOC_LOG_CONF*/) != 0) return 0;
                try
                {
                    IntPtr cur = lc, rd;
                    uint rid;
                    while (CM_Get_Next_Res_Des(out rd, cur, 4 /*ResType_IRQ*/, out rid, 0) == 0)
                    {
                        if (cur != lc) CM_Free_Res_Des_Handle(cur);
                        cur = rd;
                        uint size;
                        if (CM_Get_Res_Des_Data_Size(out size, rd, 0) != 0 || size < 16) continue;
                        var buf = new byte[size];
                        if (CM_Get_Res_Des_Data(rd, buf, size, 0) != 0) continue;
                        int alloc = BitConverter.ToInt32(buf, 12);      // IRQ_DES_64.IRQD_Alloc_Num
                        if (alloc < 0) msi++; else lines.Add(alloc);
                    }
                    if (cur != lc) CM_Free_Res_Des_Handle(cur);
                }
                finally { CM_Free_Log_Conf_Handle(lc); }
                return 1;
            }
            catch { return -1; }
        }

        // ------------------------------------------------------------------ interrupts: what Windows assigned (resource map)
        // The PnP manager records every started device's resources under HKLM\HARDWARE\RESOURCEMAP\PnP Manager\PnpManager,
        // value "<PDO name>.Translated", REG_RESOURCE_LIST (CM_RESOURCE_LIST, wdm.h). A translated interrupt descriptor carries
        // the IDT vector and the processor affinity assigned at the device's last start: the live check attributes ISR events
        // by that vector, and a changed policy that is not active yet shows up as a different affinity. Parsed defensively:
        // anything that does not look like a sane resource list returns null and the check falls back (T2 data).
        public sealed class IrqResource { public int Vector; public ulong Affinity; public int Group; public bool Message; }

        public static Func<string, byte[]> ResourceHook;        // tests / UI demo

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegGetValueW(IntPtr hkey, string subKey, string value, uint flags, out uint type, byte[] data, ref uint size);

        public static List<IrqResource> TranslatedIrqs(PciDevice dev)
        {
            try
            {
                if (dev == null || string.IsNullOrEmpty(dev.Pdo)) return null;
                byte[] b;
                if (ResourceHook != null) b = ResourceHook(dev.Pdo);
                else
                {
                    var hklm = new IntPtr(unchecked((int)0x80000002));
                    uint type, size = 0;
                    const string key = @"HARDWARE\RESOURCEMAP\PnP Manager\PnpManager";
                    string name = dev.Pdo + ".Translated";
                    if (RegGetValueW(hklm, key, name, 0x0000ffff, out type, null, ref size) != 0 || size == 0 || size > 65536) return null;
                    b = new byte[size];
                    if (RegGetValueW(hklm, key, name, 0x0000ffff, out type, b, ref size) != 0 || type != 8 /*REG_RESOURCE_LIST*/) return null;
                }
                return ParseResourceList(b);
            }
            catch { return null; }
        }

        // CM_RESOURCE_LIST (x64): ULONG Count; then per CM_FULL_RESOURCE_DESCRIPTOR: InterfaceType(4) BusNumber(4) Version(2)
        // Revision(2) Count(4) = 16 bytes, then CM_PARTIAL_RESOURCE_DESCRIPTORs of 20 bytes each (pshpack4: Type(1)
        // ShareDisposition(1) Flags(2) + a 16-byte union). Interrupt (Type 2), translated: Level(2) Group(2) Vector(4) Affinity(8).
        // DeviceSpecific (Type 5) is followed by DataSize extra bytes. Only interrupts are returned; null = not a sane list.
        public static List<IrqResource> ParseResourceList(byte[] b)
        {
            if (b == null || b.Length < 4) return null;
            var list = new List<IrqResource>();
            int count = BitConverter.ToInt32(b, 0), off = 4;
            if (count < 1 || count > 8) return null;
            for (int f = 0; f < count; f++)
            {
                if (off + 16 > b.Length) return null;
                int iface = BitConverter.ToInt32(b, off), n = BitConverter.ToInt32(b, off + 12);
                if (iface < -1 || iface > 32 || n < 0 || n > 128) return null;
                off += 16;
                for (int i = 0; i < n; i++)
                {
                    if (off + 20 > b.Length) return null;
                    byte type = b[off];
                    ushort flags = BitConverter.ToUInt16(b, off + 2);
                    if (type == 2)
                    {
                        int group = BitConverter.ToUInt16(b, off + 6), vector = BitConverter.ToInt32(b, off + 8);
                        ulong aff = BitConverter.ToUInt64(b, off + 12);
                        if (vector < 0x10 || vector > 0x1FF || aff == 0 || group > 32) return null;
                        list.Add(new IrqResource { Vector = vector, Affinity = aff, Group = group, Message = (flags & 0x2) != 0 });
                    }
                    else if (type > 7 && type < 128) return null;           // no such resource type
                    int extra = type == 5 ? BitConverter.ToInt32(b, off + 4) : 0;
                    if (extra < 0 || extra > 4096) return null;
                    off += 20 + extra;
                }
            }
            return list;
        }

        // The driver image of a device (service ImagePath), e.g. "rt640x64.sys"; "" if unknown.
        public static string DriverFile(PciDevice dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.Service)) return "";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + dev.Service, false))
                {
                    string p = k == null ? null : Convert.ToString(k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                    if (!string.IsNullOrEmpty(p)) return Path.GetFileName(p.Trim().Trim('"'));
                }
            }
            catch { }
            return dev.Service + ".sys";
        }

        // ------------------------------------------------------------------ interrupts: when grobyq changed / restarted a device
        // irq-state.json: instance id -> { applied, restarted } (UTC). Lets the check say "waits for a restart" honestly.
        private static string StatePath { get { return Path.Combine(Store.Dir, "irq-state.json"); } }

        private static Dictionary<string, object> LoadState()
        {
            try { if (File.Exists(StatePath)) return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(StatePath)) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
            catch { }
            return new Dictionary<string, object>();
        }

        private static void Mark(string inst, string what)
        {
            try
            {
                var all = LoadState();
                object o; var m = all.TryGetValue(inst, out o) ? o as Dictionary<string, object> : null;
                if (m == null) m = new Dictionary<string, object>();
                m[what] = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                all[inst] = m;
                Directory.CreateDirectory(Store.Dir);
                File.WriteAllText(StatePath, Store.Json(all));
            }
            catch { }
        }

        public static void MarkApplied(string inst) { Mark(inst, "applied"); }
        public static void MarkRestarted(string inst) { Mark(inst, "restarted"); }

        public static DateTime? StateTime(string inst, string what)
        {
            object o; var all = LoadState();
            var m = all.TryGetValue(inst ?? "", out o) ? o as Dictionary<string, object> : null;
            object v; DateTime t;
            if (m != null && m.TryGetValue(what, out v) && DateTime.TryParse(Convert.ToString(v), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out t))
                return t.ToUniversalTime();
            return null;
        }

        // ------------------------------------------------------------------ restart (DIF_PROPERTYCHANGE)
        // Same as disable+enable in Device Manager. Returns "" ok, "reboot" if Windows wants a reboot, else error.
        public static string Restart(string inst)
        {
            IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == INVALID || set == IntPtr.Zero) return "SetupDiCreateDeviceInfoList failed";
            try
            {
                var d = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                if (!SetupDiOpenDeviceInfo(set, inst, IntPtr.Zero, 0, ref d)) return "device not found (" + Marshal.GetLastWin32Error() + ")";
                var p = new SP_PROPCHANGE_PARAMS
                {
                    Header = new SP_CLASSINSTALL_HEADER { cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER)), InstallFunction = 0x12 /*DIF_PROPERTYCHANGE*/ },
                    StateChange = 3 /*DICS_PROPCHANGE*/, Scope = 1 /*DICS_FLAG_GLOBAL*/, HwProfile = 0
                };
                if (!SetupDiSetClassInstallParams(set, ref d, ref p, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS))))
                    return "SetupDiSetClassInstallParams error " + Marshal.GetLastWin32Error();
                if (!SetupDiCallClassInstaller(0x12, set, ref d))
                    return "restart failed, error " + Marshal.GetLastWin32Error() + " (run as administrator)";
                var ip = new SP_DEVINSTALL_PARAMS { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINSTALL_PARAMS)) };
                if (SetupDiGetDeviceInstallParams(set, ref d, ref ip) && (ip.Flags & (0x80 | 0x100)) != 0) return "reboot";
                return "";
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
