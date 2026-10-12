using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Grobyq
{
    // "For AerialOS users only": grobyq starts on AerialOS, or anywhere with a license signed with "anyos".
    // Detection reads the markers the AerialOS build (or AerialOS-marker.reg) writes. Rules (as in 3.6.3+):
    //  - the marker key counts only with Product = "AerialOS" (REG_SZ) and, when present, a Version >= 6.0 - an empty or
    //    foreign key does not pass; it is read from the 64-bit view and, if a 32-bit regedit imported it, from WOW6432Node;
    //  - OEM / registration strings count only as the exact product name "AerialOS" (a word, not the substring "aerial":
    //    "Aerial Photography LLC" as the registered organization does not pass), the support URL only with the AerialOS site.
    // Any offline marker can be copied by hand, so this is a distribution gate; the signed HWID license stays the lock.
    // Engine-side only (registry + rules): the windows that show the verdict live in Ui/Gate.cs.
    public static class AerialOs
    {
        public const int MinMajor = 6;                        // the oldest AerialOS a marker may name
        public sealed class Check { public string Where = "", Value = "", Note = ""; public bool Hit; }

        private static readonly Regex Word = new Regex(@"(?<![A-Za-z0-9])AerialOS(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly object _gate = new object();
        private static bool? _cached;

        public static bool Detected()
        {
            lock (_gate)
            {
                if (!_cached.HasValue)
                {
                    bool hit = false;
                    foreach (var c in Checks()) if (c.Hit) { hit = true; break; }
                    _cached = hit;
                }
                return _cached.Value;
            }
        }

        // ---- pure rules (the selftest calls these) ----
        public static bool MarkerOk(object product, object version, out string note)
        {
            note = "";
            var ps = product as string;
            if (ps == null) { note = product == null ? "no Product value" : "Product is not a text value"; return false; }
            if (!string.Equals(ps.Trim(), "AerialOS", StringComparison.OrdinalIgnoreCase)) { note = "Product must be AerialOS"; return false; }
            if (version == null) return true;                 // older builds wrote Product only
            var vs = version as string;
            Version v;
            if (vs == null || !Version.TryParse(vs.Trim(), out v)) { note = "Version is not a version number"; return false; }
            if (v.Major < MinMajor) { note = "AerialOS {0}.0 or newer is required"; return false; }
            return true;
        }

        public static bool NameOk(string v) { return !string.IsNullOrEmpty(v) && Word.IsMatch(v); }

        public static bool UrlOk(string v)
        {
            return !string.IsNullOrEmpty(v) && (v.IndexOf("aerialos.vercel.app", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("dsc.gg/aerialos", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static List<Check> Checks()
        {
            var res = new List<Check>();
            // 1. The AerialOS marker key: 64-bit view first, then the 32-bit one (a 32-bit regedit writes WOW6432Node).
            var k = new Check { Where = @"HKLM\SOFTWARE\AerialOS" };
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var a = hk.OpenSubKey(@"SOFTWARE\AerialOS", false))
                    {
                        if (a == null) continue;
                        object prod = a.GetValue("Product"), ver = a.GetValue("Version");
                        string note;
                        bool ok = MarkerOk(prod, ver, out note);
                        k.Value = Convert.ToString(prod ?? "") + (ver != null ? " " + Convert.ToString(ver) : "") + (view == RegistryView.Registry32 ? "  (WOW6432Node)" : "");
                        k.Note = note;
                        if (ok) { k.Hit = true; break; }
                    }
                }
                catch { }
            }
            res.Add(k);

            RegistryKey hklm = null;
            try { hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); } catch { }
            if (hklm == null) return res;
            using (hklm)
            {
                // 2. OEM information (System > About), registration and edition name: the exact word AerialOS only.
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "Manufacturer", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "Model", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "SupportURL", true);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOrganization", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOwner", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", false);
            }
            return res;
        }

        private static void Add(List<Check> res, RegistryKey hklm, string path, string name, bool url)
        {
            var c = new Check { Where = @"HKLM\" + path + @"\" + name };
            try
            {
                using (var k = hklm.OpenSubKey(path, false))
                    if (k != null) c.Value = Convert.ToString(k.GetValue(name) ?? "");
            }
            catch { }
            c.Hit = url ? UrlOk(c.Value) : NameOk(c.Value);
            res.Add(c);
        }

        // grobyq.exe /aerial : what the check sees on this PC, and what makes it pass.
        public static string Report()
        {
            bool det = Detected();
            var lines = new List<string>();
            lines.Add(det ? L.T("AerialOS detected.") : L.T("AerialOS not detected."));
            lines.Add("");
            foreach (var c in Checks())
                lines.Add((c.Hit ? "✔  " : "·  ") + c.Where + " = " + (c.Value.Length == 0 ? "—" : c.Value) + (c.Note.Length > 0 ? "   (" + L.F(c.Note, MinMajor) + ")" : ""));
            if (!det)
            {
                lines.Add("");
                lines.Add(L.F("Needed: HKLM\\SOFTWARE\\AerialOS with Product = AerialOS and Version {0}.0 or newer (AerialOS-marker.reg, run as administrator), or a license issued for any Windows.", MinMajor));
            }
            return string.Join("\n", lines);
        }

        // One rule for every launch mode that uses grobyq (panel, tray): AerialOS, or a valid license signed with "anyos".
        public static bool Allowed(LicResult r) { return !Brand.AerialOnly || Detected() || (r != null && r.Ok && r.AnyOs); }
    }
}
