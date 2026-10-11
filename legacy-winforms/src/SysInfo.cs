using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Grobyq
{
    // The four "System Overview" tiles. WMI, read once on a worker thread.
    public sealed class SysInfo
    {
        public string Cpu = "", Gpu = "", Ram = "", Board = "", Os = "", Disk = "";

        public static Func<SysInfo> Hook;      // demo data off-Windows

        public static SysInfo Read()
        {
            if (Hook != null) return Hook();
            var s = new SysInfo();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    foreach (ManagementObject mo in q.Get()) { s.Cpu = Clean(mo["Name"]); break; }
            }
            catch { }
            try
            {
                var names = new List<string>();
                using (var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject mo in q.Get())
                    {
                        string pnp = Clean(mo["PNPDeviceID"]);
                        if (pnp.StartsWith("PCI", StringComparison.OrdinalIgnoreCase)) names.Add(Clean(mo["Name"]));
                    }
                s.Gpu = string.Join(" + ", names.Distinct());
            }
            catch { }
            try
            {
                ulong total = 0; int n = 0; uint speed = 0;
                using (var q = new ManagementObjectSearcher("SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject mo in q.Get())
                    {
                        total += mo["Capacity"] == null ? 0 : Convert.ToUInt64(mo["Capacity"]); n++;
                        uint cs = mo["ConfiguredClockSpeed"] == null ? 0 : Convert.ToUInt32(mo["ConfiguredClockSpeed"]);
                        if (cs == 0 && mo["Speed"] != null) cs = Convert.ToUInt32(mo["Speed"]);
                        speed = Math.Max(speed, cs);
                    }
                if (n > 0) s.Ram = L.F("{0} GB ({1}x modules) @ {2} MT/s", total / (1024UL * 1024 * 1024), n, speed);
            }
            catch { }
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                    foreach (ManagementObject mo in q.Get()) { s.Board = (Clean(mo["Manufacturer"]) + " " + Clean(mo["Product"])).Trim(); break; }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    string prod = Convert.ToString(k.GetValue("ProductName"));
                    int build = 0; int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                    if (build >= 22000 && prod.StartsWith("Windows 10")) prod = "Windows 11" + prod.Substring(10);   // ProductName still says 10 on 11
                    s.Os = string.Format("{0} {1} ({2}.{3})", prod, k.GetValue("DisplayVersion"), k.GetValue("CurrentBuild"), k.GetValue("UBR"));
                }
            }
            catch { }
            try
            {
                string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
                string model = "";
                using (var q = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + sysDrive + "'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                    foreach (ManagementObject part in q.Get())
                    {
                        using (var q2 = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                            foreach (ManagementObject dd in q2.Get()) { model = Clean(dd["Model"]); break; }
                        break;
                    }
                var di = new System.IO.DriveInfo(sysDrive);
                s.Disk = (model.Length > 0 ? model + "  ·  " : "") + sysDrive + " " + L.F("{0} / {1} GB free", Math.Round(di.AvailableFreeSpace / 1073741824.0), Math.Round(di.TotalSize / 1073741824.0));
            }
            catch { }
            return s;
        }

        private static string Clean(object o) { return o == null ? "" : System.Text.RegularExpressions.Regex.Replace(o.ToString(), @"\s+", " ").Trim(); }
    }
}
