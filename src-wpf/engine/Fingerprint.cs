using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Grobyq
{
    // System fingerprint (/check, SYSTEM tab). Diagnostic only - reads, never writes.
    public static class Fingerprint
    {
        public static List<string> Lines()
        {
            var o = new List<string>();
            Action<string, object> A = (k, v) => o.Add(string.Format("  {0,-32} {1}", k, v));

            o.Add("[OS]");
            try
            {
                using (var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    A("ProductName", cv.GetValue("ProductName"));
                    A("DisplayVersion", cv.GetValue("DisplayVersion"));
                    A("Build", cv.GetValue("CurrentBuild") + "." + cv.GetValue("UBR"));
                }
            }
            catch { A("OS", "n/a"); }

            o.Add("");
            o.Add("[CPU]");
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    foreach (ManagementObject mo in s.Get()) { A("Name", (mo["Name"] ?? "").ToString().Trim()); break; }
            }
            catch { }
            try
            {
                var t = Topo.Live();
                A("Logical processors", t.LogicalCount);
                A("Physical cores", t.PhysicalCount);
                A("SMT", t.SMT);
                A("Processor groups", t.Groups);
                A("EfficiencyClasses", string.Join(",", t.EffClasses));
                A("LLC groups", string.Join(",", t.LLCGroups));
                A("Recommended mask", Masks.Recommended(t));
                foreach (var mn in Masks.Names)
                {
                    var m = Masks.Build(t, mn);
                    A("mask " + mn, string.Format("{0}  {1} LP  [{2}]", m.Hex, m.Count, m.LPs));
                    ulong bg = Masks.BgMask(t, m.Mask);
                    A("  background would get", string.Format("0x{0:X}  LP {1}", bg, Masks.MaskLps(bg)));
                }
                A("Ideal Processor pool", string.Join(",", Masks.IdealCandidates(t, true, 0).Select(s => (int)s.LP)));
            }
            catch (Exception ex) { A("topology", ex.Message); }

            o.Add("");
            o.Add("[TIMERS]   <- biggest silent difference between installs");
            long qpf = Stopwatch.Frequency;
            string note = "unknown source";
            if (qpf == 10000000) note = "TSC / invariant - GOOD";
            else if (qpf == 14318180) note = "HPET forced - BAD for frametime";
            else if (qpf == 3579545) note = "ACPI PM timer - BAD";
            A("QueryPerformanceFrequency", string.Format("{0}  [{1}]", qpf, note));
            try
            {
                uint mn, mx, cu;
                Native.NtQueryTimerResolution(out mn, out mx, out cu);
                A("Timer resolution now (ms)", string.Format("{0:N4}   min {1:N4}  max {2:N4}", cu / 10000.0, mn / 10000.0, mx / 10000.0));
            }
            catch { }

            o.Add("");
            o.Add("[BCD]");
            string bcd = RunCapture("bcdedit", "/enum {current}");
            foreach (var k in new[] { "useplatformclock", "disabledynamictick", "tscsyncpolicy", "hypervisorlaunchtype" })
            {
                var m = Regex.Match(bcd, @"(?im)^\s*" + k + @"\s+(\S+)");
                A(k, m.Success ? m.Groups[1].Value : "<not set>");
            }

            o.Add("");
            o.Add("[VBS / HVCI]   <- fresh Win11 turns this ON by default");
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT * FROM Win32_DeviceGuard"))
                    foreach (ManagementObject dg in s.Get())
                    {
                        int vbs = Convert.ToInt32(dg["VirtualizationBasedSecurityStatus"]);
                        string vs = vbs == 0 ? "off" : vbs == 1 ? "enabled, not running" : vbs == 2 ? "RUNNING" : "?";
                        A("VirtualizationBasedSecurity", vs);
                        var svc = (dg["SecurityServicesRunning"] as System.Collections.IEnumerable);
                        bool hvci = false; if (svc != null) foreach (var x in svc) if (Convert.ToInt32(x) == 2) hvci = true;
                        A("HVCI / Memory integrity", hvci ? "RUNNING  <- adds kernel transition cost" : "off");
                        break;
                    }
            }
            catch { A("DeviceGuard", "n/a"); }

            o.Add("");
            o.Add("[POWER]");
            A("Active scheme", RunCapture("powercfg", "/getactivescheme").Trim());
            A("CPMINCORES (core parking)", PowerVal("CPMINCORES"));
            A("PROCTHROTTLEMIN", PowerVal("PROCTHROTTLEMIN"));
            A("IDLEDISABLE", PowerVal("IDLEDISABLE"));

            o.Add("");
            o.Add("[SCHEDULER / MMCSS]");
            A("Win32PrioritySeparation", RegVal(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"));
            A("SystemResponsiveness", RegVal(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"));
            A("GameDVR_Enabled", RegVal(@"HKCU\System\GameConfigStore", "GameDVR_Enabled"));
            A("HwSchMode (HAGS)", RegVal(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));

            o.Add("");
            o.Add("[LOAD]");
            try
            {
                var ps = Process.GetProcesses();
                int th = 0; foreach (var x in ps) { try { th += x.Threads.Count; } catch { } }
                A("Processes", ps.Length);
                A("Total threads", th);
            }
            catch { }
            return o;
        }

        private static string PowerVal(string alias)
        {
            try
            {
                string outp = RunCapture("powercfg", "/query SCHEME_CURRENT SUB_PROCESSOR " + alias);
                var m = Regex.Match(outp, @"Current AC Power Setting Index:\s*(\S+)");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            catch { }
            return "n/a";
        }

        private static string RegVal(string path, string name)
        {
            try
            {
                RegistryKey root = path.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
                string sub = path.Substring(path.IndexOf('\\') + 1);
                using (var k = root.OpenSubKey(sub, false))
                {
                    if (k == null) return "<not set>";
                    var v = k.GetValue(name);
                    return v == null ? "<not set>" : v.ToString();
                }
            }
            catch { return "<not set>"; }
        }

        private static string RunCapture(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    return s ?? "";
                }
            }
            catch { return ""; }
        }
    }
}
