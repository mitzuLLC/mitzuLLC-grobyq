using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Grobyq
{
    // Import thread-PRIORITY presets from an AerialTool_Threads .ini file into grobyq's
    // existing rule model. This sets the Windows thread priority level of matching threads
    // and nothing else: there is no thread-suspend / thread-terminate path anywhere here.
    // The two legacy non-priority source codes (200, 300) are NOT suspend/terminate codes -
    // they simply clamp to the lowest priority ("Idle") per MapPrio, and [PROCESSESTOSUSPEND]
    // is treated as a background-deprioritization list (Idle), never as suspension.
    public static class ThreadIni
    {
        // Section names that are global (not a process). A [Section] whose name is not one of
        // these is a process section -> a Preset keyed by "<name>.exe".
        private static readonly HashSet<string> GlobalSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SETTINGS", "GAMES", "PROCESSESTOSUSPEND", "SETPROCESSESTOIDLEPRIORITY", "DISABLEBOOST" };

        // Never create thread rules for these (protected security / anti-cheat components).
        // Matched case-insensitively against BOTH a process-section name and each
        // module=/description= pattern, after stripping .exe/.dll and wildcards.
        // Unconditional: there is no toggle.
        // One list for the whole app: the engine refuses the same names on every apply path (Engine.ANTICHEAT).
        public static readonly HashSet<string> Excluded = Engine.ANTICHEAT;

        // One preview / log line: what the importer decided for a single source entry.
        public sealed class PreviewRow
        {
            public string Process = "";
            public string Match = "";        // "desc" | "module" | "" (process-level / list entry)
            public string Pattern = "";
            public string SourceValue = "";
            public string Mapped = "";
            public bool Skipped;
            public bool AntiCheat;           // skipped because it is an anti-cheat process / module (shown grey)
            public bool Protected;           // a protected system process: applies only with the Advanced gate (amber)
            public string Note = "";
        }

        public sealed class Result
        {
            public Dictionary<string, Preset> Presets = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> BgClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Games = new List<string>();
            public List<PreviewRow> Rows = new List<PreviewRow>();
            public bool HadSuspendCodes;     // the file had 300 / 200 entries: imported as Idle (-15), never suspend / terminate
        }

        // source value -> grobyq Prio. 15/2 -> Highest ; 300/200/-15 -> Idle ; else Normal.
        // 15 -> Highest matches grobyq's existing TimeCritical -> Highest coercion.
        public static string MapPrio(string value)
        {
            int v;
            if (!int.TryParse((value ?? "").Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
                return "Normal";
            switch (v)
            {
                case 15: return "Highest";
                case 2: return "Highest";
                case 1: return "AboveNormal";
                case 0: return "Normal";
                case -1: return "BelowNormal";
                case -2: return "Lowest";
                case -15: return "Idle";
                case 300: return "Idle";   // legacy non-priority code -> clamp to lowest
                case 200: return "Idle";   // legacy non-priority code -> clamp to lowest
                default: return "Normal";
            }
        }

        // Bare comparison name: drop wildcards and a trailing .exe/.dll.
        private static string Bare(string s)
        {
            s = (s ?? "").Trim().Replace("*", "").Replace("?", "");
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            return s.Trim();
        }

        public static bool IsExcluded(string name) { return Engine.IsAntiCheat(Bare(name)); }

        private static string ExeKey(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            return name;
        }

        // ---------------- parse (pure, no I/O) ----------------
        public static Result Parse(string iniText)
        {
            var r = new Result();
            if (string.IsNullOrEmpty(iniText)) return r;

            string section = "";        // current section name ("" = none / preamble)
            string procKey = "";        // exe key of the current process section ("" = global or skipped)
            bool sectionSkipped = false;

            foreach (var raw in iniText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                // inline comment: " ;..." after the content ("[dota2] ; note", "module=a.dll, -2 ; why"). Only ';' - a
                // thread description can contain " #" ("Worker #3"); '#' starts a comment only at the line start.
                int cm = -1;
                for (int ci = 1; ci < line.Length; ci++) if (line[ci] == ';' && char.IsWhiteSpace(line[ci - 1])) { cm = ci; break; }
                if (cm > 0) line = line.Substring(0, cm).TrimEnd();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    procKey = "";
                    sectionSkipped = false;
                    if (!GlobalSections.Contains(section))
                    {
                        // process section
                        if (IsExcluded(section))
                        {
                            sectionSkipped = true;
                            r.Rows.Add(new PreviewRow { Process = ExeKey(section), Skipped = true, AntiCheat = true, Note = "skipped: anti-cheat" });
                        }
                        else
                        {
                            procKey = ExeKey(section);
                            if (!r.Presets.ContainsKey(procKey)) r.Presets[procKey] = new Preset { Exe = procKey };
                        }
                    }
                    continue;
                }

                // ---- global section list entries (bare process names) ----
                if (GlobalSections.Contains(section))
                {
                    if (section.Equals("SETTINGS", StringComparison.OrdinalIgnoreCase)) continue;   // ignored

                    string name = line.Split('=')[0].Trim();   // tolerate "name" or "name=..."
                    if (name.Length == 0) continue;
                    if (IsExcluded(name))
                    {
                        r.Rows.Add(new PreviewRow { Process = ExeKey(name), Skipped = true, AntiCheat = true, Note = "skipped: anti-cheat (" + section + ")" });
                        continue;
                    }

                    if (section.Equals("GAMES", StringComparison.OrdinalIgnoreCase))
                    {
                        string g = ExeKey(name);                  // the watch list, presets and rules all key on "<name>.exe"
                        if (!r.Games.Any(x => string.Equals(x, g, StringComparison.OrdinalIgnoreCase))) r.Games.Add(g);
                        r.Rows.Add(new PreviewRow { Process = g, Note = "watch-list suggestion (GAMES)" });
                    }
                    else if (section.Equals("DISABLEBOOST", StringComparison.OrdinalIgnoreCase))
                    {
                        string key = ExeKey(name);
                        Preset p;
                        if (!r.Presets.TryGetValue(key, out p)) { p = new Preset { Exe = key }; r.Presets[key] = p; }
                        p.Boost = "Off";
                        r.Rows.Add(new PreviewRow { Process = key, Mapped = "boost Off", Protected = Engine.IsProtected(key), Note = "process priority boost off (DISABLEBOOST)" });
                    }
                    else   // SETPROCESSESTOIDLEPRIORITY or PROCESSESTOSUSPEND -> background Idle (never suspended)
                    {
                        string key = ExeKey(name);
                        bool prot = Engine.IsProtected(key);
                        r.Rows.Add(new PreviewRow { Process = key, Mapped = prot ? "" : "Idle", Skipped = prot, Protected = prot,
                            Note = prot ? "protected system process: never lowered as background" : "background Idle (" + section + (section.Equals("PROCESSESTOSUSPEND", StringComparison.OrdinalIgnoreCase) ? " -> Idle, never suspended" : "") + ")" });
                        if (!prot) r.BgClasses[key] = "Idle";
                    }
                    continue;
                }

                // ---- inside a process section: module=/description= rule lines ----
                if (sectionSkipped) continue;           // whole section was excluded
                if (procKey.Length == 0) continue;      // not in a process section (preamble / unknown)

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string kind = line.Substring(0, eq).Trim();
                string rest = line.Substring(eq + 1);
                string match;
                if (kind.Equals("module", StringComparison.OrdinalIgnoreCase)) match = "module";
                else if (kind.Equals("description", StringComparison.OrdinalIgnoreCase)) match = "desc";
                else continue;                          // unknown key inside a process section

                var parts = rest.Split(',');
                string pattern = parts[0].Trim();
                if (pattern.Length == 0) continue;
                // the priority is the first numeric field; flags (disableboost, tid=) may stand anywhere after the pattern
                string value = "";
                for (int i = 1; i < parts.Length; i++)
                {
                    int dummy; string f = parts[i].Trim();
                    if (int.TryParse(f, System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out dummy)) { value = f; break; }
                }

                if (IsExcluded(pattern))
                {
                    r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, SourceValue = value, Skipped = true, AntiCheat = true, Note = "skipped: anti-cheat" });
                    continue;
                }

                bool boostOff = false; string tid = "";
                for (int i = 1; i < parts.Length; i++)
                {
                    string flag = parts[i].Trim();
                    if (flag.Length == 0) continue;
                    if (flag.Equals("disableboost", StringComparison.OrdinalIgnoreCase)) boostOff = true;
                    else if (flag.StartsWith("tid=", StringComparison.OrdinalIgnoreCase)) tid = flag.Substring(4).Trim();   // tid=last|first|0|<N> -> ThreadRule.Tid ordinal
                }

                string prio = value.Length == 0 ? "" : MapPrio(value);   // flags only = keep the priority, change the boost
                string vt = value.Trim();
                if (vt == "300" || vt == "200") r.HadSuspendCodes = true;
                var rule = new ThreadRule { Match = match, Pattern = pattern, Prio = prio, Tid = tid };
                if (boostOff) rule.Boost = "Off";
                r.Presets[procKey].Threads.Add(rule);

                var note = new List<string>();
                if (boostOff) note.Add("boost off");
                if (tid.Length > 0) note.Add("tid=" + tid);
                if (vt == "300") note.Add("Suspend -> Idle");
                if (vt == "200") note.Add("Terminate -> Idle");
                bool protRow = Engine.IsProtected(procKey);
                if (protRow) note.Add("requires Advanced");
                r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, SourceValue = value, Mapped = prio, Protected = protRow, Note = string.Join(", ", note) });
            }

            return r;
        }

        // ---------------- bundled profile (embedded resource "system-threads.txt") ----------------
        // grobyq's built-in system thread-priority profile, parsed with the same priority-only rules as Parse():
        // 300/200 -> Idle, anti-cheat entries dropped. Applied by the "System thread optimization" switch.
        public static Result LoadBundled()
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("system-threads.txt"))
                    if (s != null)
                        using (var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8))
                            return Parse(r.ReadToEnd());
            }
            catch { }
            return new Result();
        }

        public const string SuspendNote = "Suspend/Terminate entries were imported as Idle (-15): grobyq tunes priority, it does not freeze or kill threads. Idle achieves the 'get it off the path' goal reversibly.";

        // ---------------- apply (merges into the engine; mirrors Store.ImportProfile) ----------------
        // replace = drop current presets / background classes first; otherwise same names are
        // overwritten and the rest is kept. Returns log lines.
        public static List<string> Apply(Result r, Engine eng, bool replace)
        {
            var log = new List<string>();
            if (r == null) return log;

            eng.UnderLock(() => {
            if (replace) eng.Presets.Clear();
            foreach (var p in r.Presets.Values) eng.Presets[p.Exe] = p;
            int rules = r.Rows.Count(x => !x.Skipped && (x.Match == "desc" || x.Match == "module"));
            log.Add(string.Format("[IM] .ini presets: {0} presets, {1} thread rules imported{2}", r.Presets.Count, rules, replace ? " (replaced)" : " (merged)"));

            if (r.BgClasses.Count > 0)
            {
                // the background list is space separated: a name with a space ("Riot Client") cannot live in it
                var spaced = r.BgClasses.Keys.Where(n => n.IndexOf(' ') >= 0).ToList();
                foreach (var n in spaced) log.Add("[IM] background entry skipped (a space in the name cannot be stored in the list): " + n);
                var names = r.BgClasses.Keys.Where(n => n.IndexOf(' ') < 0).ToList();

                // Replace = the imported lists replace the current ones (list AND classes, so nothing keeps a list entry
                // without its class); Merge = imported names are added, the rest is kept.
                var set = replace ? new List<string>() : new List<string>(Settings.BgProcs());
                if (replace) eng.BgClasses.Clear();
                foreach (var n in names)
                {
                    eng.BgClasses[n] = r.BgClasses[n];
                    if (!set.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) set.Add(n);
                }
                Settings.Set("BgProcs", string.Join(" ", set));
                log.Add(string.Format("[IM] background (Idle): {0}{1}", string.Join(" ", names), replace ? " (list replaced)" : ""));
            }

            foreach (var x in r.Rows.Where(x => x.AntiCheat))
                log.Add("[THR] anti-cheat target skipped: " + (x.Pattern.Length > 0 ? x.Process + " / " + x.Pattern : x.Process));
            int prot = r.Rows.Count(x => x.Protected && !x.Skipped);
            if (prot > 0) log.Add(string.Format("[IM] {0} rule(s) target protected system processes: they apply only with 'Priority on protected processes (advanced)'", prot));
            if (r.HadSuspendCodes) log.Add("[IM] " + SuspendNote);

            if (r.Games.Count > 0)
            {
                // the watch list is space separated: a game name with a space cannot live in it
                foreach (var g in r.Games.Where(g => g.IndexOf(' ') >= 0)) log.Add("[IM] game skipped (a space in the name cannot be stored in the watch list): " + g);
                var games = r.Games.Where(g => g.IndexOf(' ') < 0).ToList();
                if (replace && games.Count > 0) { Settings.Set("WatchList", string.Join(" ", games)); log.Add("[IM] watch list replaced: " + string.Join(" ", games)); }
                else if (!replace) log.Add("[IM] games (not applied to watch list on merge): " + string.Join(" ", r.Games));
                else log.Add("[IM] watch list kept: no importable game name");
            }

            string err = eng.SavePresets();   // SavePresets() invalidates the Lever-3 apply-once cache -> new rules re-apply next cycle
            if (err.Length > 0) log.Add("[!!] could not save presets.json: " + err);
            });
            return log;
        }
    }
}
