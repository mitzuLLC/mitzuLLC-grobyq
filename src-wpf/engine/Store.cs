using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Grobyq
{
    // One rule for threads of a process. First matching rule wins per thread.
    public sealed class ThreadRule
    {
        public string Match = "desc";      // desc = GetThreadDescription | module = start-address module
        public string Pattern = "";        // wildcard, e.g. *MainThrd*  or  engine2.dll
        public string Prio = "";           // "" keep | Idle | Lowest | BelowNormal | Normal | AboveNormal | Highest
        public string Ideal = "";          // "" none | auto (spread over free cores) | LP number
        public string Boost = "";          // "" keep | Off | On
        public string Tid = "";            // "" all matches | first | last | N : which matching thread the rule touches (creation order approx by TID)
        public bool Enabled = true;
        public bool Test;                  // 4.0.5: a priority-test rule (PrioTest) - built per pass, never stored in presets.json
        public ThreadRule Clone() { return (ThreadRule)MemberwiseClone(); }

        // 4.0.3: the match kind follows the pattern (Threads > Thread rules has no desc / module choice any more): a file
        // name - .dll .exe .node .sys .drv, wildcards allowed (steamclient*.dll) - is the module the thread starts in;
        // anything else is the thread description (GetThreadDescription). A module pattern without an extension reads as
        // a description, so the editor's rules always carry one.
        private static readonly string[] ModuleExt = { ".dll", ".exe", ".node", ".sys", ".drv" };
        public static string MatchFor(string pattern)
        {
            string s = (pattern ?? "").Trim().TrimEnd('*', '?').ToLowerInvariant();
            foreach (var e in ModuleExt) if (s.EndsWith(e, StringComparison.Ordinal)) return "module";
            return "desc";
        }
    }

    // Everything grobyq sets on one target process.
    public sealed class Preset
    {
        public string Exe = "";
        public string Class = "Normal";    // Idle BelowNormal Normal AboveNormal High   (never Realtime). Normal = Windows default: the game process priority is a lever you raise yourself.
        public string Io = "Normal";       // VeryLow Low Normal High
        public string Mem = "Normal";      // VeryLow Low Medium BelowNormal Normal
        public string Eco = "Off";         // Off = EcoQoS off (HighQoS) | Keep = leave Windows default
        public string Boost = "Keep";      // process priority boost: Keep | On | Off
        public string HotFirst = "AboveNormal";   // 4.0.2: hot threads removed - unused, kept so presets.json still
        public string HotOthers = "";             //        round-trips through 4.0.1
        public bool Enabled = true;               // include this process in the "programs & services" bulk pass
        // 4.0.4: this preset is the built-in reference (presets.json "builtin"). A saved preset marked so is replaced by the
        // reference of the running version when presets.json loads, so an update brings the new reference; an edit saved on
        // Threads clears it, Restore reference / a factory reset set it again.
        public bool Builtin;
        public List<ThreadRule> Threads = new List<ThreadRule>();

        public Preset Clone()
        {
            var p = (Preset)MemberwiseClone();
            p.Threads = Threads.Select(t => t.Clone()).ToList();
            return p;
        }
    }

    // presets.json in %APPDATA%\grobyq + profile export / import.
    public static class Store
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "grobyq"); }
        }
        private static string PresetsPath { get { return Path.Combine(Dir, "presets.json"); } }

        public static Dictionary<string, Preset> Defaults()
        {
            var d = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
            // Source 2 (cs2 / dota2): main + render threads get the free cores; ray-tracing audio (embree3, phonon) and the
            // video decoder step aside. Anti-cheat-free by construction (Engine.ANTICHEAT is refused on every path anyway).
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                // Class = Normal by default: the game keeps the Windows priority. Raising the whole process to High can
                // starve the very threads it waits on (DWM, Steam IPC); any win has to come from the thread rules, not
                // from a blanket class bump. Raise it per game in the preset if a title needs it.
                var s2 = new Preset { Exe = exe, Class = "Normal", Io = "Normal", Mem = "Normal" };
                // The built-in reference: the frame's critical path. With the class at Normal: Highest = 10, Above normal = 9,
                // normal threads 8.
                //   MainThrd           Highest      4.0.3 (owner decision): simulation, every frame starts here. The game
                //                                   sets Highest itself (T0 capture), so this keeps it there.
                //   D3D11RenderThread  Above normal submission to the driver; the game sets Above normal itself too.
                //   GlobPool/0..N      Above normal 4.0.4 (owner decision): the Source 2 job pool. The main thread hands
                //                                   each frame's jobs to it and waits for every worker (fork-join, T3), so
                //                                   one worker preempted by a normal-priority thread holds up the frame. The
                //                                   game leaves them at Normal (T0: 5 workers, ~56% of the game's cycles in
                //                                   online DM); one step up keeps them under the main thread.
                // No Ideal CPU (4.0.1 used one: a frame-time regression suspect, T2) and no boost change - every game
                // thread keeps Windows' priority boost (T0: all on). Judge any change in CapFrameX (avg / 1% low).
                s2.Threads.Add(new ThreadRule { Match = "desc", Pattern = "*MainThrd*", Prio = "Highest" });
                s2.Threads.Add(new ThreadRule { Match = "desc", Pattern = "*D3D11RenderThread*", Prio = "AboveNormal" });
                s2.Threads.Add(new ThreadRule { Match = "desc", Pattern = "*GlobPool*", Prio = "AboveNormal" });
                s2.Threads.Add(new ThreadRule { Match = "module", Pattern = "embree3.dll", Prio = "Lowest" });
                s2.Threads.Add(new ThreadRule { Match = "module", Pattern = "phonon.dll", Prio = "Lowest" });
                s2.Threads.Add(new ThreadRule { Match = "module", Pattern = "video64.dll", Prio = "BelowNormal" });
                d[s2.Exe] = s2;
            }

            var val = new Preset { Exe = "VALORANT-Win64-Shipping.exe", Class = "High", Io = "Normal", Mem = "Normal" };
            foreach (var n in new[] { "*BackgroundThreadPool*", "*FTelemetryWorker*", "*LogWorker*", "*TelemetrySched*", "*ScreenSaverInhibitor*" })
                val.Threads.Add(new ThreadRule { Pattern = n, Prio = "Idle", Boost = "Off" });
            val.Threads.Add(new ThreadRule { Pattern = "*Background Worker*", Boost = "Off" });
            d[val.Exe] = val;

            // Unreal Engine (Fortnite): worker / telemetry threads step aside, the game and render threads stay.
            var fn = new Preset { Exe = "FortniteClient-Win64-Shipping.exe", Class = "High", Io = "Normal", Mem = "Normal" };
            foreach (var n in new[] { "*BackgroundThreadPool*", "*FTelemetryWorker*", "*LogWorker*", "*TelemetrySched*" })
                fn.Threads.Add(new ThreadRule { Pattern = n, Prio = "Idle", Boost = "Off" });
            fn.Threads.Add(new ThreadRule { Pattern = "*Background Worker*", Prio = "BelowNormal" });
            d[fn.Exe] = fn;
            foreach (var p in d.Values) p.Builtin = true;
            return d;
        }

        public static Dictionary<string, string> DefaultBg()
        {
            // 4.0.2: the user background list is gone (owner decision). ctfmon.exe and
            // TextInputHost.exe moved into the system threads optimizer (bundled profile, [SETPROCESSESTOIDLEPRIORITY]).
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // ---------------- the built-in reference ----------------
        // 4.0.4: the Threads page shows whether a game runs the reference built into this version or the user's own rules.
        // Compared is what the engine applies: the process part and every thread rule in order. The retired hot-thread
        // fields and the bulk-pass flag are not part of it; a pattern compares without case (the engine matches it so).
        private static Dictionary<string, Preset> _builtin;
        private static Dictionary<string, Preset> Builtin { get { return _builtin ?? (_builtin = Defaults()); } }

        public static bool HasBuiltin(string exe) { return !string.IsNullOrEmpty(exe) && Builtin.ContainsKey(exe); }

        // true = p is exactly the built-in reference of exe (false for a game without one)
        public static bool IsBuiltin(string exe, Preset p)
        {
            Preset b;
            if (p == null || string.IsNullOrEmpty(exe) || !Builtin.TryGetValue(exe, out b)) return false;
            return RefSig(p) == RefSig(b);
        }

        private static string RefSig(Preset p)
        {
            Func<string, string> n = s => (s ?? "").Trim().ToLowerInvariant();
            var parts = new List<string> { n(p.Class), n(p.Io), n(p.Mem), n(p.Eco) == "keep" ? "keep" : "off", n(p.Boost).Length == 0 ? "keep" : n(p.Boost) };
            foreach (var r in p.Threads)
                parts.Add(string.Join("\u0001", r.Enabled ? "1" : "0", n(r.Match), n(r.Pattern), n(r.Prio), n(r.Ideal), n(r.Boost), n(r.Tid)));
            return string.Join("\u0002", parts);
        }

        // Every start, after the one-time updates: a saved preset that equals the built-in reference is marked as such (so
        // the next update replaces it with the new reference), and one that differs is not. Returns log lines.
        public static List<string> MarkBuiltin(Engine eng)
        {
            var log = new List<string>();
            bool any = false;
            foreach (var kv in eng.Presets)
            {
                if (kv.Value == null || !HasBuiltin(kv.Key)) continue;
                bool b = IsBuiltin(kv.Key, kv.Value);
                if (kv.Value.Builtin == b) continue;
                kv.Value.Builtin = b; any = true;
                log.Add("[i] " + kv.Key + ": " + (b ? "runs the built-in reference (follows grobyq updates)" : "runs your rules (kept through updates)"));
            }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) log.Add("[!!] presets.json: " + err); }
            return log;
        }

        // One-time update (4.0.4, owner decision): a saved cs2 / dota2 preset without a GlobPool rule gets the built-in one
        // (*GlobPool* -> Above normal) right after its main / render rules - a 4.0.3 built-in preset becomes the 4.0.4
        // reference exactly. A preset that already has a rule naming GlobPool keeps it (the user's). Runs once, after
        // MigrateMain403.
        public static List<string> MigrateGlob404(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("Migrated404") == "1") return log;
            bool any = false;
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (!eng.Presets.TryGetValue(exe, out p) || p == null) continue;
                if (p.Threads.Any(r => (r.Pattern ?? "").IndexOf("globpool", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                int at = 0;
                for (int i = 0; i < p.Threads.Count; i++)
                {
                    string pat = p.Threads[i].Pattern ?? "";
                    if (string.Equals(pat, "*MainThrd*", StringComparison.OrdinalIgnoreCase) || string.Equals(pat, "*D3D11RenderThread*", StringComparison.OrdinalIgnoreCase)) at = i + 1;
                }
                p.Threads.Insert(at, new ThreadRule { Match = "desc", Pattern = "*GlobPool*", Prio = "AboveNormal" });
                any = true;
                log.Add("[i] " + exe + ": *GlobPool* -> Above normal added (4.0.4 reference: the Source 2 job pool one step up)");
            }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; } }
            Settings.Set("Migrated404", "1");
            return log;
        }

        // One-time update (4.0.2): safe defaults for the frame-time fix. A saved cs2 / dota2 preset whose render rule still
        // holds an old built-in value (4.0.1 Highest, 4.0.0 keep) goes to Above normal, and the "auto" Ideal CPU of the main
        // and render rules is dropped. The main thread's priority is MigrateMain403's (4.0.3: Highest), so an update from
        // 4.0.0 / 4.0.1 never flips it twice. The game core mask, the user background list and the game mode (close apps)
        // are switched off - the background feature is removed; ctfmon / TextInputHost are handled by the system threads
        // optimizer now. Runs once.
        public static List<string> MigrateSafe402(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("Migrated402") == "1") return log;
            bool any = false;
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (!eng.Presets.TryGetValue(exe, out p) || p == null) continue;
                foreach (var r in p.Threads)
                {
                    if (!string.Equals(r.Match, "desc", StringComparison.OrdinalIgnoreCase)) continue;
                    bool main = string.Equals(r.Pattern, "*MainThrd*", StringComparison.OrdinalIgnoreCase);
                    bool render = string.Equals(r.Pattern, "*D3D11RenderThread*", StringComparison.OrdinalIgnoreCase);
                    if (!main && !render) continue;
                    // render: Highest = the 4.0.1 built-in value; keep (empty) = the 4.0.0 built-in rule. Anything else is the user's.
                    if (render && (string.Equals(r.Prio, "Highest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.Prio)))
                    { string was = string.IsNullOrEmpty(r.Prio) ? "keep" : r.Prio; r.Prio = "AboveNormal"; any = true; log.Add("[i] " + exe + ": " + r.Pattern + " " + was + " -> Above normal (4.0.2 safe default)"); }
                    if (string.Equals(r.Ideal, "auto", StringComparison.OrdinalIgnoreCase)) { r.Ideal = ""; any = true; log.Add("[i] " + exe + ": " + r.Pattern + " Ideal CPU auto -> none (4.0.2 safe default)"); }
                }
            }
            if (Settings.GetBool("DoMask")) { Settings.Set("DoMask", "0"); log.Add("[i] game core mask switched off (4.0.2 safe default)"); }
            if (Settings.GetBool("DoBackground") || Settings.GetBool("CloseBackground") || Settings.BgProcs().Length > 0)
                log.Add("[i] background processes removed (4.0.2): ctfmon.exe and TextInputHost.exe are in the system threads optimizer now");
            Settings.Set("DoBackground", "0");
            Settings.Set("CloseBackground", "0");
            Settings.Set("BgProcs", "");
            if (eng.BgClasses != null && eng.BgClasses.Count > 0) { eng.BgClasses.Clear(); any = true; }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; } }
            Settings.Set("Migrated402", "1");
            return log;
        }

        // One-time update of an existing install (3.6.2): the old special "steamclient stays Normal" rule of the built-in
        // cs2 / dota2 presets is removed. 3.6.2 also put steam.exe on the background list; 4.0.2 removed that list, so this
        // update no longer touches it. Runs once; returns log lines.
        public static List<string> Migrate362(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("Migrated362") == "1") return log;
            bool any = false;
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (!eng.Presets.TryGetValue(exe, out p) || p == null) continue;
                int n = p.Threads.RemoveAll(r => string.Equals(r.Match, "module", StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(r.Pattern, "steamclient*.dll", StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(r.Prio, "Normal", StringComparison.OrdinalIgnoreCase));
                if (n > 0) { any = true; log.Add("[i] " + exe + ": special steamclient rule removed"); }
            }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; } }
            Settings.Set("Migrated362", "1");
            return log;
        }

        // One-time update (v4.0): the default game process priority drops from High to Normal (Windows default). An install
        // whose cs2 / dota2 preset is still at the old High default is moved to Normal, so the game process is no longer
        // blanket-boosted over the threads it waits on. A preset deliberately set to another class is left alone. Runs once.
        public static List<string> MigrateNormalClass(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("MigratedNormalClass") == "1") return log;
            bool any = false;
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (eng.Presets.TryGetValue(exe, out p) && p != null && string.Equals(p.Class, "High", StringComparison.OrdinalIgnoreCase))
                { p.Class = "Normal"; any = true; log.Add("[i] " + exe + ": process priority class High -> Normal (v4.0 default; raise it in the preset if you want it)"); }
            }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; } }
            Settings.Set("MigratedNormalClass", "1");
            return log;
        }

        // One-time update (4.0.3, owner decision): the main thread of a saved cs2 / dota2 preset goes to Highest when its rule
        // still holds a value grobyq shipped as built-in (4.0.0 Above normal, 4.0.2 Above normal, keep); Highest stays. Any
        // other value is the user's and stays. Runs once, after MigrateSafe402.
        public static List<string> MigrateMain403(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("Migrated403") == "1") return log;
            bool any = false;
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (!eng.Presets.TryGetValue(exe, out p) || p == null) continue;
                foreach (var r in p.Threads)
                {
                    if (!string.Equals(r.Match, "desc", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.Pattern, "*MainThrd*", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(r.Prio, "AboveNormal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.Prio))
                    {
                        string was = string.IsNullOrEmpty(r.Prio) ? "keep" : "Above normal";
                        r.Prio = "Highest"; any = true;
                        log.Add("[i] " + exe + ": *MainThrd* " + was + " -> Highest (4.0.3 default; change it in the game preset if you want)");
                    }
                }
            }
            if (any) { string err = Save(eng.Presets, eng.BgClasses); if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; } }
            Settings.Set("Migrated403", "1");
            return log;
        }

        // Retired (4.0.2): 4.0.1 raised the built-in main / render rules to Highest (a frame-time regression suspect);
        // MigrateSafe402 now sets them to Above normal. Kept only to mark itself done, so it never runs again.
        public static List<string> MigrateHighThreads(Engine eng)
        {
            if (Settings.Get("MigratedHighThreads") != "1") Settings.Set("MigratedHighThreads", "1");
            return new List<string>();
        }

        // ---------------- load / save ----------------
        public static void Load(out Dictionary<string, Preset> presets, out Dictionary<string, string> bg)
        {
            presets = Defaults(); bg = DefaultBg();
            try
            {
                if (!File.Exists(PresetsPath)) return;
                var root = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(File.ReadAllText(PresetsPath)) as Dictionary<string, object>;
                if (root == null) return;
                var ps = ReadPresets(root);
                if (ps.Count > 0 || root.ContainsKey("presets")) presets = ps;   // an emptied set stays empty
                // 4.0.4: a preset saved as the built-in reference follows the running version's reference
                var now = Defaults();
                foreach (var exe in presets.Keys.ToList())
                {
                    Preset cur, nb;
                    if (presets.TryGetValue(exe, out cur) && cur != null && cur.Builtin && now.TryGetValue(exe, out nb)) presets[exe] = nb;
                }
                var b = ReadMap(root, "background");
                if (b != null) bg = b;
            }
            catch { }
        }

        public static string Save(Dictionary<string, Preset> presets, Dictionary<string, string> bg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var root = new Dictionary<string, object>
                {
                    { "format", "grobyq-presets" }, { "version", 1 },
                    { "presets", presets.Values.OrderBy(p => p.Exe, StringComparer.OrdinalIgnoreCase).Select(PresetObj).ToList() },
                    { "background", bg.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => (object)k.Value) },
                };
                File.WriteAllText(PresetsPath, Json(root), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------------- profile (everything a client needs, portable) ----------------
        public static string ExportProfile(Engine eng)
        {
            var root = new Dictionary<string, object>
            {
                { "format", "grobyq-profile" }, { "version", 1 },
                { "exported", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "app", Brand.Name + " " + Brand.Version },
                { "watchList", string.Join(" ", Settings.WatchList()) },
                { "mask", Settings.Get("Mask") },
                { "maskAdd", Settings.Get("MaskAdd") },           // Cores-tab edits against the base (hex, fixed LPs of this PC)
                { "maskDrop", Settings.Get("MaskDrop") },
                { "maskIrq", GameMask.Mode },
                { "maskCpu", SafeSig() },
                { "period", Settings.GetInt("Period", 2000) },
                { "affinity", eng.AffRules.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => (object)k.Value) },
                { "presets", eng.Presets.Values.OrderBy(p => p.Exe, StringComparer.OrdinalIgnoreCase).Select(PresetObj).ToList() },
                { "layout", Settings.Get("LayoutTemplate") },
                { "flags", FlagKeys.ToDictionary(k => k, k => (object)Settings.GetBool(k)) },
            };
            return Json(root);
        }

        // Switches a profile carries. The two gated ones (protected priority, TimeCritical) are never switched on by a
        // load: they come back in LastImportWants and the page asks the user (per-session acknowledgment).
        // 4.0.2: the background list, its core pinning and the game mode are removed - old profiles' "DoBackground" /
        // "BgAffinity" / "backgroundList" / "background" / "topN" entries are ignored on import.
        public static readonly string[] FlagKeys = { "DoThreads", "DoAffRules", "ApplyAllPresets", "SystemThreadOpt",
                                                     "AllowProtectedPriority", "AdvancedThreadMode" };
        public static readonly HashSet<string> GatedFlags = new HashSet<string> { "AllowProtectedPriority", "AdvancedThreadMode" };
        public static readonly List<string> LastImportWants = new List<string>();

        // ---------------- named profiles (%APPDATA%\grobyq\profiles\<name>.json) ----------------
        public static string ProfilesDir { get { return Path.Combine(Dir, "profiles"); } }

        public static string SafeName(string name)
        {
            string n = (name ?? "").Trim();
            // the Windows file-name rules, whatever the runtime reports: < > : " / \ | ? * and control characters
            foreach (char c in "<>:\"/\\|?*".ToCharArray().Concat(Path.GetInvalidFileNameChars())) n = n.Replace(c.ToString(), "");
            n = new string(n.Where(ch => ch >= 32).ToArray());
            while (n.Contains("  ")) n = n.Replace("  ", " ");
            n = n.Trim('.', ' ');
            return n.Length > 60 ? n.Substring(0, 60) : n;
        }
        private static string ProfilePath(string name)
        {
            string n = name ?? "";
            if (n.Length > 0 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && n.IndexOfAny("<>:\"/\\|?*".ToCharArray()) < 0)
            {
                string raw = Path.Combine(ProfilesDir, n + ".json");
                if (File.Exists(raw)) return raw;                     // a listed file, whatever its exact name
            }
            return Path.Combine(ProfilesDir, SafeName(n) + ".json");
        }

        public static List<string> ListProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesDir)) return new List<string>();
                return Directory.GetFiles(ProfilesDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }
        public static bool ProfileExists(string name) { return (name ?? "").Trim().Length > 0 && File.Exists(ProfilePath(name)); }

        public static string SaveNamedProfile(string name, Engine eng)
        {
            if (SafeName(name).Length == 0) return "empty name";
            try { Directory.CreateDirectory(ProfilesDir); File.WriteAllText(ProfilePath(name), ExportProfile(eng), new UTF8Encoding(false)); Settings.Set("ActiveProfile", SafeName(name)); return ""; }
            catch (Exception ex) { return ex.Message; }
        }
        public static List<string> LoadNamedProfile(string name, Engine eng)
        {
            var log = ImportProfile(File.ReadAllText(ProfilePath(name)), true, eng);
            Settings.Set("ActiveProfile", Path.GetFileNameWithoutExtension(ProfilePath(name)));
            log.Insert(0, "[PF] profile loaded: " + name);
            return log;
        }
        public static string DuplicateProfile(string from, string to)
        {
            if (SafeName(to).Length == 0) return "empty name";
            try { File.Copy(ProfilePath(from), ProfilePath(to), true); return ""; } catch (Exception ex) { return ex.Message; }
        }
        public static string DeleteProfile(string name)
        {
            try
            {
                string path = ProfilePath(name);
                if (!File.Exists(path)) return "not found: " + Path.GetFileName(path);
                File.Delete(path);
                if (string.Equals(Settings.Get("ActiveProfile"), name, StringComparison.OrdinalIgnoreCase)) Settings.Set("ActiveProfile", "");
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // replace = drop current presets / rules first; otherwise same names are overwritten, the rest added.
        public static List<string> ImportProfile(string json, bool replace, Engine eng)
        {
            var log = new List<string>();
            var root = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new Exception("not a JSON object");
            string fmt = S(root, "format");
            if (fmt != "grobyq-profile" && fmt != "grobyq-presets") throw new Exception("not a grobyq profile (format = '" + fmt + "')");

            // Filter at import, like the .ini importer: anti-cheat names never enter the store; protected processes never
            // become background or affinity entries (the engine refuses both anyway - this keeps the store clean).
            var ps = ReadPresets(root);
            foreach (var p in ps.Values.ToList())
            {
                if (Engine.IsAntiCheat(p.Exe)) { ps.Remove(p.Exe); log.Add("[THR] anti-cheat target skipped: " + p.Exe); continue; }
                int before = p.Threads.Count;
                p.Threads.RemoveAll(t => Engine.IsAntiCheat(t.Pattern));
                if (p.Threads.Count < before) log.Add(string.Format("[THR] anti-cheat target skipped: {0} rule(s) in {1}", before - p.Threads.Count, p.Exe));
            }
            if (replace) eng.Presets.Clear();
            foreach (var p in ps.Values) eng.Presets[p.Exe] = p;
            log.Add(string.Format("[IM] presets: {0} imported{1}", ps.Count, replace ? " (replaced)" : " (merged)"));

            var aff = ReadMap(root, "affinity");
            if (aff != null)
            {
                if (replace) eng.AffRules.Clear();
                int n = 0;
                foreach (var kv in aff)
                {
                    string exe = ExeName(kv.Key);
                    if (exe.Length == 0) continue;
                    if (Engine.Gate(exe, Engine.Op.Affinity, false) != null) { log.Add("[IM] affinity rule skipped (anti-cheat / protected): " + exe); continue; }
                    eng.AffRules[exe] = kv.Value; n++;
                }
                Settings.SaveAffRules(eng.AffRules);
                log.Add(string.Format("[IM] affinity rules: {0}", n));
            }
            if (root.ContainsKey("watchList"))
            {
                string wl = string.Join(" ", S(root, "watchList").Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(ExeName).Where(x => x.Length > 0 && !Engine.IsAntiCheat(x)));
                Settings.Set("WatchList", wl.Length > 0 ? wl : "cs2.exe"); log.Add("[IM] watch list: " + Settings.Get("WatchList"));
            }
            if (root.ContainsKey("mask"))
            {
                // the base always replaces the edits; a profile from 3.6.5+ brings its own (older profiles: none)
                Settings.Set("Mask", S(root, "mask"));
                ulong add = root.ContainsKey("maskAdd") ? Masks.Parse(S(root, "maskAdd")) : 0, drop = root.ContainsKey("maskDrop") ? Masks.Parse(S(root, "maskDrop")) : 0;
                string sig = root.ContainsKey("maskCpu") ? S(root, "maskCpu") : "", here = SafeSig();
                bool foreign = (add != 0 || drop != 0) && sig.Length > 0 && here.Length > 0 && sig != here;
                if (foreign) { add = 0; drop = 0; }                 // LP numbers of another CPU layout mean nothing here
                GameMask.SetEdits(add, drop);
                log.Add("[IM] core mask: " + S(root, "mask") + (add != 0 ? ", +LP " + Masks.Ranges(add) : "") + (drop != 0 ? ", -LP " + Masks.Ranges(drop) : "")
                        + (foreign ? "  (grid edits skipped: made on " + sig + ", this PC is " + here + ")" : ""));
            }
            if (root.ContainsKey("maskIrq")) GameMask.SetMode(S(root, "maskIrq"));
            int per; if (root.ContainsKey("period") && int.TryParse(S(root, "period"), NumberStyles.Integer, CultureInfo.InvariantCulture, out per) && per >= 1000 && per <= 60000) Settings.Set("Period", per.ToString(CultureInfo.InvariantCulture));
            if (root.ContainsKey("layout")) Settings.Set("LayoutTemplate", S(root, "layout"));
            LastImportWants.Clear();
            var flags = root.ContainsKey("flags") ? root["flags"] as Dictionary<string, object> : null;
            if (flags != null)
                foreach (var k in FlagKeys)
                {
                    object v; if (!flags.TryGetValue(k, out v)) continue;
                    string sv = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                    bool on = v is bool ? (bool)v : sv.Equals("true", StringComparison.OrdinalIgnoreCase) || sv == "1";
                    if (GatedFlags.Contains(k)) { if (on && !Settings.GetBool(k)) LastImportWants.Add(k); else if (!on) Settings.SetBool(k, false); continue; }
                    Settings.SetBool(k, on);
                }
            string err = Save(eng.Presets, eng.BgClasses);
            if (err.Length > 0) log.Add("[!!] could not save presets.json: " + err);
            return log;
        }

        private static string ExeName(string n)
        {
            n = (n ?? "").Trim();
            if (n.Length == 0) return "";
            return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe";
        }

        // ---------------- mapping ----------------
        private static Dictionary<string, object> PresetObj(Preset p)
        {
            return new Dictionary<string, object>
            {
                { "exe", p.Exe }, { "class", p.Class }, { "io", p.Io }, { "mem", p.Mem }, { "eco", p.Eco }, { "boost", p.Boost },
                { "hotFirst", p.HotFirst }, { "hotOthers", p.HotOthers }, { "on", p.Enabled }, { "builtin", p.Builtin },
                { "threads", p.Threads.Select(t => (object)new Dictionary<string, object>
                    { { "match", t.Match }, { "pattern", t.Pattern }, { "prio", t.Prio }, { "ideal", t.Ideal }, { "boost", t.Boost }, { "tid", t.Tid ?? "" }, { "enabled", t.Enabled } }).ToList() },
            };
        }

        private static Dictionary<string, Preset> ReadPresets(Dictionary<string, object> root)
        {
            var res = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
            object arrO;
            if (!root.TryGetValue("presets", out arrO)) return res;
            var arr = arrO as object[] ?? (arrO as ArrayList != null ? ((ArrayList)arrO).ToArray() : null);
            if (arr == null) return res;
            foreach (var o in arr)
            {
                var m = o as Dictionary<string, object>;
                if (m == null) continue;
                var p = new Preset
                {
                    Exe = S(m, "exe"), Class = S(m, "class", "Normal"), Io = S(m, "io", "Normal"), Mem = S(m, "mem", "Normal"),
                    Eco = S(m, "eco", "Off"), Boost = S(m, "boost", "Keep"), HotFirst = S(m, "hotFirst", "AboveNormal"), HotOthers = S(m, "hotOthers", ""),
                };
                p.Enabled = S(m, "on", "true").ToLowerInvariant() != "false";
                p.Builtin = S(m, "builtin", "false").ToLowerInvariant() == "true";   // missing (before 4.0.4) = not marked; MarkBuiltin decides
                if (p.Exe.Length == 0) continue;
                if (string.Equals(p.Class, "Realtime", StringComparison.OrdinalIgnoreCase)) p.Class = "High";   // hard boundary
                object thO;
                if (m.TryGetValue("threads", out thO))
                {
                    var th = thO as object[];
                    if (th != null)
                        foreach (var t in th)
                        {
                            var tm = t as Dictionary<string, object>;
                            if (tm == null) continue;
                            var r = new ThreadRule { Match = S(tm, "match", "desc"), Pattern = S(tm, "pattern"), Prio = S(tm, "prio"), Ideal = S(tm, "ideal"), Boost = S(tm, "boost"), Tid = S(tm, "tid") };
                            if (r.Pattern.Length == 0) r.Pattern = S(tm, "desc");   // v2 field name
                            string en = S(tm, "enabled", "true").ToLowerInvariant();
                            r.Enabled = en != "false" && en != "0";
                            // "TimeCritical" is kept as stored: Engine.ThreadPrio runs it as Highest unless Advanced thread mode is on
                            p.Threads.Add(r);
                        }
                }
                res[p.Exe] = p;
            }
            return res;
        }

        private static Dictionary<string, string> ReadMap(Dictionary<string, object> root, string key)
        {
            object o;
            if (!root.TryGetValue(key, out o)) return null;
            var m = o as Dictionary<string, object>;
            if (m == null) return null;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in m) d[kv.Key] = kv.Value == null ? "" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return d;
        }

        private static string SafeSig() { try { return GameMask.CpuSig(Topo.Live()); } catch { return ""; } }

        private static string S(Dictionary<string, object> m, string k, string def = "")
        {
            object v;
            if (m == null || !m.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // ---------------- pretty JSON writer (stable, diff-friendly) ----------------
        public static string Json(object o)
        {
            var sb = new StringBuilder();
            Write(sb, o, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object o, int ind)
        {
            string pad = new string(' ', ind * 2), pad2 = new string(' ', (ind + 1) * 2);
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { sb.Append(Q((string)o)); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is int || o is long || o is double || o is float || o is ulong || o is uint)
            { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (DictionaryEntry kv in dict)
                {
                    sb.Append(pad2).Append(Q(kv.Key.ToString())).Append(": ");
                    Write(sb, kv.Value, ind + 1);
                    sb.Append(++i < dict.Count ? ",\n" : "\n");
                }
                sb.Append(pad).Append('}');
                return;
            }
            var list = o as IEnumerable;
            if (list != null)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad2);
                    Write(sb, items[i], ind + 1);
                    sb.Append(i + 1 < items.Count ? ",\n" : "\n");
                }
                sb.Append(pad).Append(']');
                return;
            }
            sb.Append(Q(o.ToString()));
        }

        private static string Q(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
