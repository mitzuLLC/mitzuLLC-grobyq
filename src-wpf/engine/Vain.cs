using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Grobyq
{
    // Decode Vain Governor rules from HKCU\Software\VainGovernor\Rules and flag
    // illegal Win32 values. Diagnostic only - it never executes the rules (spec).
    public static class Vain
    {
        private static readonly Dictionary<long, string> CLS = new Dictionary<long, string>
        { {64,"Idle"},{16384,"BelowNormal"},{32,"Normal"},{32768,"AboveNormal"},{128,"High"},{256,"Realtime"} };
        private static readonly Dictionary<long, string> TPR = new Dictionary<long, string>
        { {-15,"Idle"},{-2,"Lowest"},{-1,"BelowNormal"},{0,"Normal"},{1,"AboveNormal"},{2,"Highest"},{15,"TimeCritical"} };

        public static List<string> Decode()
        {
            var o = new List<string>();
            o.Add("");
            o.Add("  Decoding Vain Governor rules from HKCU\\Software\\VainGovernor\\Rules");
            o.Add("");
            using (var root = Registry.CurrentUser.OpenSubKey(@"Software\VainGovernor\Rules", false))
            {
                if (root == null) { o.Add("  Key not present - no rules found."); return o; }
                foreach (var name in root.GetSubKeyNames())
                {
                    o.Add("  == " + name);
                    using (var k = root.OpenSubKey(name, false))
                    {
                        if (k == null) continue;
                        object prio = k.GetValue("prio");
                        if (prio != null)
                        {
                            long n = Convert.ToInt64(prio);
                            o.Add(CLS.ContainsKey(n)
                                ? string.Format("       prio = {0,-12} -> PRIORITY_CLASS {1}", n, CLS[n])
                                : string.Format("       prio = {0,-12} -> NOT a valid PRIORITY_CLASS (32 64 128 256 16384 32768)", n));
                        }
                        foreach (var f in new[] { "IOPrio", "aff_t_cores", "aff_t_ccd", "aff_t_smt", "set_t_cores", "set_t_ccd", "set_t_smt", "cpusets" })
                        {
                            object fv = k.GetValue(f);
                            if (fv != null) o.Add(string.Format("       {0,-12} = {1}", f, fv));
                        }
                        using (var th = k.OpenSubKey("Threads", false))
                        {
                            if (th == null) continue;
                            foreach (var tn in th.GetSubKeyNames())
                                using (var tk = th.OpenSubKey(tn, false))
                                {
                                    if (tk == null) continue;
                                    var parts = new List<string>();
                                    object tp = tk.GetValue("prio");
                                    if (tp != null)
                                    {
                                        long raw = Convert.ToInt64(tp);
                                        long sv = raw > 2147483647 ? raw - 4294967296 : raw;
                                        parts.Add(TPR.ContainsKey(sv)
                                            ? string.Format("prio={0} [{1}]", sv, TPR[sv])
                                            : string.Format("prio={0} [OUT OF RANGE]", sv));
                                    }
                                    object tidl = tk.GetValue("tidl"); if (tidl != null) parts.Add("idealCPU=" + tidl);
                                    object tbst = tk.GetValue("tbst"); if (tbst != null) parts.Add("boost=" + tbst);
                                    object tdesc = tk.GetValue("tdesc"); if (tdesc != null && tdesc.ToString().Length > 0) parts.Add("desc=" + tdesc);
                                    o.Add(string.Format("         thread {0,-26} {1}", tn, string.Join("  ", parts)));
                                }
                        }
                    }
                }
            }
            return o;
        }
    }
}
