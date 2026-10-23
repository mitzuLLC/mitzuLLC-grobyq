using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Grobyq
{
    // Persistence in HKCU\Software\grobyq. String values; callers parse.
    // First run after the rename copies HKCU\Software\grobPC over, so clients keep
    // their mask, watch list and affinity rules.
    public static class Settings
    {
        private const string Root = @"Software\grobyq";
        private const string AffSub = @"Software\grobyq\Affinity";
        private const string OldRoot = @"Software\grobPC";

        // 4.0.2: the user background list is removed (owner decision); kept empty so an old stored
        // list or a settings reset brings nothing back. ctfmon / TextInputHost live in the system threads optimizer.
        public static readonly string DefaultBgProcs = "";

        private static bool _migrated;
        public static string MigrationNote = "";
        public static string MaskPinNote = "";
        public static string MaskIrqNote = "";
        public static bool ExistedBefore;           // grobyq (or grobPC v2) settings were there when this run started

        // Copy the v2 (grobPC) key once. Never deletes the old key: rolling back to v2 keeps working.
        public static void MigrateOnce()
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                using (var now = Registry.CurrentUser.OpenSubKey(Root, false))
                {
                    ExistedBefore = now != null && now.GetValueNames().Length > 0;
                    if (now != null) return;
                }
                using (var old = Registry.CurrentUser.OpenSubKey(OldRoot, false))
                {
                    if (old == null) return;
                    using (var dst = Registry.CurrentUser.CreateSubKey(Root))
                        CopyKey(old, dst);
                    MigrationNote = "settings imported from grobPC v2 (HKCU\\Software\\grobPC)";
                    ExistedBefore = true;
                }
            }
            catch { }
        }

        private static void CopyKey(RegistryKey src, RegistryKey dst)
        {
            foreach (var n in src.GetValueNames())
                dst.SetValue(n, src.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), src.GetValueKind(n));
            foreach (var s in src.GetSubKeyNames())
                using (var a = src.OpenSubKey(s, false))
                using (var b = dst.CreateSubKey(s))
                    if (a != null && b != null) CopyKey(a, b);
        }

        // v3.1 recommends the game set on a verified dual-CCD X3D. An install that never picked a mask ran the old
        // recommendation (defaults are computed, not stored): write that value down once, so an update never moves
        // someone's game to other cores silently. The Threads page offers the game set instead ("Use game set").
        // A true first run (no grobyq / grobPC values yet) keeps the new recommendation.
        public static string PinMaskDefault()
        {
            MigrateOnce();
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Root))
                {
                    if (k == null || k.GetValue("MaskRec") != null) return "";
                    var t = Topo.Live();
                    string note = "";
                    bool existing = k.GetValueNames().Length > 0;
                    if (existing && string.IsNullOrEmpty(k.GetValue("Mask") as string))
                    {
                        string legacy = Masks.RecommendedLegacy(t), now = Masks.Recommended(t);
                        if (!string.Equals(legacy, now, StringComparison.OrdinalIgnoreCase))
                        {
                            k.SetValue("Mask", legacy, RegistryValueKind.String);
                            note = "core mask kept at " + legacy + " (the value this PC was running); the game set is offered on Affinity > Cores";
                        }
                    }
                    k.SetValue("MaskRec", "2", RegistryValueKind.String);
                    return note;
                }
            }
            catch { return ""; }
        }

        // First-run defaults. Mask default is filled from the live recommender (F1).
        private static Dictionary<string, string> Defaults()
        {
            string recMask = "AllCores";
            try { recMask = Masks.Recommended(Topo.Live()); } catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "WatchList", "cs2.exe" },      // spec F2: cs2.exe only by default
                { "Mask", recMask },
                { "MaskIrq", "Thread" },          // adaptive: LPs device interrupts are pinned to leave the game mask (Off / Thread / Core)
                { "MaskAdd", "" },                // Cores tab edits against the base rule (hex)
                { "MaskDrop", "" },
                { "MaskCpu", "" },                // CPU layout signature the edits were made on (GameMask.CpuSig)
                { "IrqPOnly", "1" },              // interrupt recommender on a hybrid CPU: P-cores only (untick to allow E-cores)
                { "Period", "2000" },            // 2 s: 1 s is re-scan overhead, 5 s can miss threads spawned on map load
                { "BgAffinity", "0" },            // 4.0.2: background feature removed (key kept so old values read as off)
                { "BgProcs", DefaultBgProcs },
                // v4 manual mode: a first run changes NOTHING. Both optimizers start off and the core parts are off; the
                // user picks what to apply and presses Save. 4.0.3: the thread rules start ON - they act only once the game
                // threads optimizer is switched on, which then applies the whole game preset (owner decision). The session always follows the game process (applied at
                // launch, caught up every 2 s, restored when the game exits); the mechanism setting (period) keeps its
                // default - it only acts on a chosen part.
                { "AutoEnable", "0" },
                { "CloseToTray", "1" },
                { "DoMask", "0" },                // game core mask (affinity + CPU sets)
                { "DoThreads", "1" },             // 4.0.3: on by default (Threads > Thread rules > Apply thread rules)
                { "DoBackground", "0" },          // 4.0.2: background list removed (key kept so old values read as off)
                { "CloseBackground", "0" },       // 4.0.2: game mode removed (Optimizer.CloseBgOn is always false)
                { "DoAffRules", "0" },
                { "ApplyAllPresets", "0" },       // thread-priority presets on programs / services (non-targets)
                { "SystemThreadOpt", "0" },       // built-in system thread profile (embedded), one switch
                { "AllowProtectedPriority", "0" }, // Advanced: priority on protected system processes (off by default)
                { "RestoreOnExit", "1" },
                { "AdvancedThreadMode", "0" },    // TimeCritical (15) selectable in thread rules (Advanced, warned)
                { "LogOpen", "0" },            // the log starts folded: in a normal window the pages keep their full height
            };
        }

        private static Dictionary<string, string> _defaults;
        private static Dictionary<string, string> Def { get { return _defaults ?? (_defaults = Defaults()); } }

        // Interface language: saved choice, else the Windows display language (if grobyq has it), else English.
        public static string Language()
        {
            string v = null;
            try { using (var k = Registry.CurrentUser.OpenSubKey(Root, false)) v = k == null ? null : k.GetValue("Language") as string; } catch { }
            return string.IsNullOrEmpty(v) || L.Find(v) == null ? L.Detect() : v;
        }

        public static string Get(string name, string fallback = null)
        {
            MigrateOnce();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root, false))
                {
                    if (k != null)
                    {
                        var v = k.GetValue(name) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
            catch { }
            if (fallback != null) return fallback;
            string d; return Def.TryGetValue(name, out d) ? d : "";
        }

        public static int GetInt(string name, int fallback)
        {
            int v; return int.TryParse(Get(name), out v) ? v : fallback;
        }
        public static bool GetBool(string name)
        {
            return Get(name) == "1";
        }
        public static void SetBool(string name, bool v) { Set(name, v ? "1" : "0"); }

        public static void Set(string name, string value)
        {
            MigrateOnce();
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Root))
                    if (k != null) k.SetValue(name, value ?? "", RegistryValueKind.String);
            }
            catch { }
        }

        // Back to the first-run default: the stored value is removed, so Get answers the default again.
        public static void Reset(params string[] names)
        {
            MigrateOnce();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root, true))
                    if (k != null) foreach (var n in names) { try { k.DeleteValue(n, false); } catch { } }
            }
            catch { }
        }

        // Watch list, always "<name>.exe" (presets, affinity rules and the foreground check key on it), no duplicates.
        public static string[] WatchList()
        {
            var l = new List<string>();
            foreach (var raw in Get("WatchList").Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
                if (!l.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) l.Add(n);
            }
            return l.ToArray();
        }

        // Background list. A stored EMPTY list stays empty (the user cleared it); only a missing value means defaults.
        public static string[] BgProcs()
        {
            string v;
            if (!TryStored("BgProcs", out v)) v = Get("BgProcs");
            return v.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool Stored(string name) { string v; return TryStored(name, out v) && v.Length > 0; }

        private static bool TryStored(string name, out string value)
        {
            value = null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root, false))
                    value = k == null ? null : k.GetValue(name) as string;
            }
            catch { }
            return value != null;
        }

        // ---- affinity rules ----
        public static Dictionary<string, string> LoadAffRules()
        {
            MigrateOnce();
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(AffSub, false))
                {
                    if (k != null)
                        foreach (var n in k.GetValueNames())
                            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                d[n] = k.GetValue(n) as string ?? "";
                }
            }
            catch { }
            return d;
        }

        public static void SaveAffRules(Dictionary<string, string> rules)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(AffSub))
                {
                    if (k == null) return;
                    foreach (var n in k.GetValueNames())
                        if (!rules.ContainsKey(n)) k.DeleteValue(n, false);
                    foreach (var kv in rules) k.SetValue(kv.Key, kv.Value ?? "", RegistryValueKind.String);
                }
            }
            catch { }
        }
    }
}
