using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

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
        public ThreadRule Clone() { return (ThreadRule)MemberwiseClone(); }
    }

    // Everything grobyq sets on one target process.
    public sealed class Preset
    {
        public string Exe = "";
        public string Class = "High";      // Idle BelowNormal Normal AboveNormal High   (never Realtime)
        public string Io = "Normal";       // VeryLow Low Normal High
        public string Mem = "Normal";      // VeryLow Low Medium BelowNormal Normal
        public string Eco = "Off";         // Off = EcoQoS off (HighQoS) | Keep = leave Windows default
        public string Boost = "Keep";      // process priority boost: Keep | On | Off
        public string HotFirst = "AboveNormal";   // hottest unnamed thread (when no named rule hit)
        public string HotOthers = "";             // the other hot threads
        public bool Enabled = true;               // include this process in the "programs & services" bulk pass
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
                var s2 = new Preset { Exe = exe, Class = "High", Io = "Normal", Mem = "Normal" };
                s2.Threads.Add(new ThreadRule { Match = "desc", Pattern = "*MainThrd*", Ideal = "auto", Prio = "AboveNormal" });
                s2.Threads.Add(new ThreadRule { Match = "desc", Pattern = "*D3D11RenderThread*", Ideal = "auto" });
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
            return d;
        }

        public static Dictionary<string, string> DefaultBg()
        {
            // steam.exe steps aside while the game has focus (Idle, as in the AerialOS profile); released on alt-tab / exit
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "ctfmon.exe", "Idle" }, { "textinputhost.exe", "Idle" }, { "discord.exe", "Normal" }, { "steam.exe", "Idle" } };
        }

        // One-time update of an existing install (3.6.2): Steam joins the background list (Idle), and the old special
        // "steamclient stays Normal" rule of the built-in cs2 / dota2 presets is removed. Runs once; returns log lines.
        public static List<string> Migrate362(Engine eng)
        {
            var log = new List<string>();
            if (Settings.Get("Migrated362") == "1") return log;
            var list = new List<string>(Settings.BgProcs());
            if (!list.Any(x => string.Equals(x, "steam.exe", StringComparison.OrdinalIgnoreCase)))
            {
                list.Add("steam.exe");
                Settings.Set("BgProcs", string.Join(" ", list));
                log.Add("[i] steam.exe added to the background list (Idle while the game has focus)");
            }
            if (!eng.BgClasses.ContainsKey("steam.exe")) eng.BgClasses["steam.exe"] = "Idle";
            foreach (var exe in new[] { "cs2.exe", "dota2.exe" })
            {
                Preset p;
                if (!eng.Presets.TryGetValue(exe, out p)) continue;
                int n = p.Threads.RemoveAll(r => string.Equals(r.Match, "module", StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(r.Pattern, "steamclient*.dll", StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(r.Prio, "Normal", StringComparison.OrdinalIgnoreCase));
                if (n > 0) log.Add("[i] " + exe + ": special steamclient rule removed");
            }
            string err = Save(eng.Presets, eng.BgClasses);
            if (err.Length > 0) { log.Add("[!!] presets.json: " + err); return log; }
            Settings.Set("Migrated362", "1");
            return log;
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
                { "topN", Settings.GetInt("TopN", 4) },
                { "period", Settings.GetInt("Period", 2000) },
                { "skipCpu0", Settings.GetBool("SkipCpu0") },
                { "backgroundList", string.Join(" ", Settings.BgProcs()) },
                { "affinity", eng.AffRules.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => (object)k.Value) },
                { "background", eng.BgClasses.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => (object)k.Value) },
                { "presets", eng.Presets.Values.OrderBy(p => p.Exe, StringComparer.OrdinalIgnoreCase).Select(PresetObj).ToList() },
                { "layout", Settings.Get("LayoutTemplate") },
                { "flags", FlagKeys.ToDictionary(k => k, k => (object)Settings.GetBool(k)) },
            };
            return Json(root);
        }

        // Switches a profile carries. The two gated ones (protected priority, TimeCritical) are never switched on by a
        // load: they come back in LastImportWants and the page asks the user (per-session acknowledgment).
        public static readonly string[] FlagKeys = { "DoThreads", "SkipCpu0", "DoAffRules", "ApplyAllPresets", "SystemThreadOpt", "DoBackground",
                                                     "BgAffinity", "BgForeground", "PeriodicReapply", "AllowProtectedPriority", "AdvancedThreadMode" };
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
            var bg = ReadMap(root, "background");
            if (bg != null)
            {
                if (replace) eng.BgClasses.Clear();
                foreach (var kv in bg)
                {
                    string exe = ExeName(kv.Key);
                    if (exe.Length == 0 || Engine.Gate(exe, Engine.Op.Process, false) != null) continue;
                    eng.BgClasses[exe] = kv.Value;
                }
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
            if (root.ContainsKey("topN")) Settings.Set("TopN", S(root, "topN"));
            int per; if (root.ContainsKey("period") && int.TryParse(S(root, "period"), NumberStyles.Integer, CultureInfo.InvariantCulture, out per) && per >= 1000 && per <= 60000) Settings.Set("Period", per.ToString(CultureInfo.InvariantCulture));
            if (root.ContainsKey("skipCpu0")) { string sc = S(root, "skipCpu0").Trim().ToLowerInvariant(); Settings.SetBool("SkipCpu0", sc == "true" || sc == "1"); }
            if (root.ContainsKey("backgroundList"))
                Settings.Set("BgProcs", string.Join(" ", S(root, "backgroundList").Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                       .Select(ExeName).Where(x => x.Length > 0 && Engine.Gate(x, Engine.Op.Process, false) == null)));
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
                { "hotFirst", p.HotFirst }, { "hotOthers", p.HotOthers }, { "on", p.Enabled },
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
                    Exe = S(m, "exe"), Class = S(m, "class", "High"), Io = S(m, "io", "Normal"), Mem = S(m, "mem", "Normal"),
                    Eco = S(m, "eco", "Off"), Boost = S(m, "boost", "Keep"), HotFirst = S(m, "hotFirst", "AboveNormal"), HotOthers = S(m, "hotOthers", ""),
                };
                p.Enabled = S(m, "on", "true").ToLowerInvariant() != "false";
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
