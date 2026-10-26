using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Grobyq
{
    // The built-in system thread profile: Assets/system-threads.txt (embedded as "system-threads.txt"), grobyq's own
    // profile 5.0.0 in the AerialTool_Threads .ini format. The system threads optimizer applies it while a chosen game
    // runs, as written, and restores every thread when the game closes.
    //
    //   [SETTINGS]      ignored (the engine has its own catch-up period)
    //   [DISABLEBOOST]  bare process names: process priority boost off (SetProcessPriorityBoost), for the whole process
    //   [process]       one section per process name (-> "<name>.exe"); rule lines, first match wins per thread:
    //                     module=<file or glob>, <priority>[, disableboost][, tid=first|last|N]
    //                     description=<glob>, <priority>[, disableboost][, tid=...]
    //                   priority: -15 Idle · -2 Lowest · -1 Below normal · 0 Normal · 1 Above normal · 2 Highest
    //                   module = the file the thread starts in; a pattern without an extension ("SHCore") also matches
    //                   the file ("SHCore.dll") - Engine.RuleHits. disableboost alone (no priority) = boost off, priority kept.
    //
    // 5.0.0 (grobyq 4.1.0): the profile sets THREAD priorities and boost, nothing else - no process class, no suspend, no
    // terminate; the old profile and its paths are gone. What the format still allows but grobyq does not do:
    //   - the codes 300 / 200 (the source tool's suspend / terminate) and any other number that is not a priority: the
    //     line is skipped and reported, never mapped to a priority;
    //   - [PROCESSESTOSUSPEND] / [SETPROCESSESTOIDLEPRIORITY] (a process class) and [GAMES]: recognised so they never
    //     become process sections, and ignored - the games are chosen on Threads.
    // Anti-cheat processes and modules are refused everywhere (Engine.ANTICHEAT); protected Windows processes apply only
    // after the one-time confirmation (Engine.Gate).
    public static class ThreadIni
    {
        // Sections that are not a process. SETTINGS is silent; the other three are legacy lists, reported and ignored.
        private static readonly HashSet<string> IgnoredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SETTINGS", "GAMES", "PROCESSESTOSUSPEND", "SETPROCESSESTOIDLEPRIORITY" };
        private const string BoostSection = "DISABLEBOOST";

        // Never create thread rules for these (anti-cheat components), whatever the section or pattern says.
        public static readonly HashSet<string> Excluded = Engine.ANTICHEAT;

        // One preview / log line: what the parser decided for a single source entry.
        public sealed class PreviewRow
        {
            public string Process = "";
            public string Match = "";        // "desc" | "module" | "" (process-level entry)
            public string Pattern = "";
            public string SourceValue = "";
            public string Mapped = "";
            public bool Skipped;
            public bool AntiCheat;           // skipped because it is an anti-cheat process / module
            public bool Protected;           // a protected Windows process: applies only after the one-time confirmation
            public string Note = "";
        }

        public sealed class Result
        {
            public Dictionary<string, Preset> Presets = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
            public List<PreviewRow> Rows = new List<PreviewRow>();
            // Lines the profile holds but grobyq does not act on (legacy lists, values that are not a priority, empty
            // rules), one message each: the journal shows them when the optimizer is switched on.
            public List<string> Ignored = new List<string>();

            public int Rules { get { return Rows.Count(x => !x.Skipped && (x.Match == "desc" || x.Match == "module")); } }
            public int BoostOff { get { return Presets.Values.Count(p => string.Equals(p.Boost, "Off", StringComparison.OrdinalIgnoreCase)); } }
        }

        // Source value -> grobyq Prio. "" = not a thread priority (the line is skipped): that covers the legacy codes
        // 300 / 200 and the source tool's 15 (its "time critical") - the system profile lowers, it never pushes a system
        // thread above Highest.
        public static string MapPrio(string value)
        {
            int v;
            if (!int.TryParse((value ?? "").Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
                return "";
            switch (v)
            {
                case 2: return "Highest";
                case 1: return "AboveNormal";
                case 0: return "Normal";
                case -1: return "BelowNormal";
                case -2: return "Lowest";
                case -15: return "Idle";
                default: return "";
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
            string procKey = "";        // exe key of the current process section ("" = not a process section)
            bool sectionSkipped = false;

            foreach (var raw in iniText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                // inline comment: " ;..." after the content ("module=a.dll, -2 ; why"). Only ';' - a thread description can
                // contain " #" ("Worker #3"); '#' starts a comment only at the line start.
                int cm = -1;
                for (int ci = 1; ci < line.Length; ci++) if (line[ci] == ';' && char.IsWhiteSpace(line[ci - 1])) { cm = ci; break; }
                if (cm > 0) line = line.Substring(0, cm).TrimEnd();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    procKey = "";
                    sectionSkipped = false;
                    if (IgnoredSections.Contains(section) || section.Equals(BoostSection, StringComparison.OrdinalIgnoreCase)) continue;
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
                    continue;
                }

                // ---- legacy lists: reported once per entry, nothing applied ----
                if (IgnoredSections.Contains(section))
                {
                    if (section.Equals("SETTINGS", StringComparison.OrdinalIgnoreCase)) continue;   // the engine has its own period
                    string name = line.Split('=')[0].Trim();
                    if (name.Length == 0) continue;
                    r.Ignored.Add("[" + section + "] " + name + ": legacy list ignored - the system profile sets thread priorities only" +
                                  (section.Equals("GAMES", StringComparison.OrdinalIgnoreCase) ? " (the games are chosen on Threads)" : " (no process class)"));
                    r.Rows.Add(new PreviewRow { Process = ExeKey(name), Skipped = true, Note = "legacy list ignored (" + section + ")" });
                    continue;
                }

                // ---- [DISABLEBOOST]: bare process names -> process priority boost off ----
                if (section.Equals(BoostSection, StringComparison.OrdinalIgnoreCase))
                {
                    string name = line.Split('=')[0].Trim();   // tolerate "name" or "name=..."
                    if (name.Length == 0) continue;
                    if (IsExcluded(name))
                    {
                        r.Rows.Add(new PreviewRow { Process = ExeKey(name), Skipped = true, AntiCheat = true, Note = "skipped: anti-cheat (" + section + ")" });
                        continue;
                    }
                    string key = ExeKey(name);
                    Preset p;
                    if (!r.Presets.TryGetValue(key, out p)) { p = new Preset { Exe = key }; r.Presets[key] = p; }
                    p.Boost = "Off";
                    r.Rows.Add(new PreviewRow { Process = key, Mapped = "boost Off", Protected = Engine.IsProtected(key), Note = "process priority boost off (DISABLEBOOST)" });
                    continue;
                }

                // ---- inside a process section: module= / description= rule lines ----
                if (sectionSkipped) continue;           // whole section was excluded
                if (procKey.Length == 0) continue;      // preamble / unknown section

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
                bool boostOff = false; string tid = "";
                for (int i = 1; i < parts.Length; i++)
                {
                    string f = parts[i].Trim();
                    if (f.Length == 0) continue;
                    int dummy;
                    if (value.Length == 0 && int.TryParse(f, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out dummy)) value = f;
                    else if (f.Equals("disableboost", StringComparison.OrdinalIgnoreCase)) boostOff = true;
                    else if (f.StartsWith("tid=", StringComparison.OrdinalIgnoreCase)) tid = f.Substring(4).Trim();   // tid=last|first|0|<N> -> ThreadRule.Tid ordinal
                }

                if (IsExcluded(pattern))
                {
                    r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, SourceValue = value, Skipped = true, AntiCheat = true, Note = "skipped: anti-cheat" });
                    continue;
                }

                string prio = value.Length == 0 ? "" : MapPrio(value);
                if (value.Length > 0 && prio.Length == 0)
                {
                    // 300 / 200 / 15 / anything else: not a thread priority. Never mapped - skipped and reported.
                    string why = value == "300" ? "300 = suspend in the source tool" : value == "200" ? "200 = terminate in the source tool" : value + " is not a thread priority";
                    r.Ignored.Add("[" + procKey + "] " + kind + "=" + pattern + ", " + value + ": " + why + " - grobyq sets priorities only, line skipped");
                    r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, SourceValue = value, Skipped = true, Note = "not a priority (" + value + "): skipped" });
                    continue;
                }
                if (prio.Length == 0 && !boostOff)
                {
                    // flags only, and none that sets anything ("module=x.exe, tid=0"): nothing to do
                    r.Ignored.Add("[" + procKey + "] " + kind + "=" + pattern + ": no priority and no disableboost - nothing to set, line skipped");
                    r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, Skipped = true, Note = "no priority, no boost flag: skipped" });
                    continue;
                }

                var rule = new ThreadRule { Match = match, Pattern = pattern, Prio = prio, Tid = tid };
                if (boostOff) rule.Boost = "Off";
                r.Presets[procKey].Threads.Add(rule);

                var note = new List<string>();
                if (prio.Length == 0) note.Add("priority kept");
                if (boostOff) note.Add("boost off");
                if (tid.Length > 0) note.Add("tid=" + tid);
                bool protRow = Engine.IsProtected(procKey);
                if (protRow) note.Add("protected process: after the confirmation");
                r.Rows.Add(new PreviewRow { Process = procKey, Match = match, Pattern = pattern, SourceValue = value, Mapped = prio, Protected = protRow, Note = string.Join(", ", note) });
            }

            return r;
        }

        // ---------------- the bundled profile (embedded resource "system-threads.txt") ----------------
        // Parsed with the rules above every time the system threads optimizer is switched on (Engine.LoadSystemProfile).
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
    }
}
