using System;
using System.Collections.Generic;
using System.Linq;

namespace Grobyq
{
    // 4.0.5: the priority test (Threads > Priority test) - an A/B for the three frame threads of the Source 2 games without
    // launch options. A level picked on the page replaces the preset's rule for that thread: the engine puts these rules in
    // front of the game preset's (first match wins), for cs2.exe and dota2.exe only, while the game threads optimizer runs.
    // A pick is stored (Settings "TestMain" / "TestRender" / "TestPool") and stays until Turn the test off or a factory reset.
    //   ""            no test: the preset's rule applies (the reference)
    //   keep          game default: grobyq leaves the thread alone - what the game or a launch option set stays
    //   Normal / AboveNormal / Highest / TimeCritical   that thread priority. Time critical = 15, the top of the normal
    //                 range (THREAD_PRIORITY_TIME_CRITICAL); a thread of a normal-class process has no higher level, and
    //                 grobyq never sets a real-time class or level (T1: Scheduling Priorities, MS Learn).
    public static class PrioTest
    {
        public sealed class Slot { public string Key, Pattern, Name; public string[] Values; }

        // the job pool stops at Highest: five busy workers at 15 would take their cores from every thread below 15
        public static readonly Slot[] Slots =
        {
            new Slot { Key = "TestMain",   Pattern = "*MainThrd*",          Name = "MainThrd",          Values = new[] { "", "keep", "Normal", "AboveNormal", "Highest", "TimeCritical" } },
            new Slot { Key = "TestRender", Pattern = "*D3D11RenderThread*", Name = "D3D11RenderThread", Values = new[] { "", "keep", "Normal", "AboveNormal", "Highest", "TimeCritical" } },
            new Slot { Key = "TestPool",   Pattern = "*GlobPool*",          Name = "GlobPool",          Values = new[] { "", "keep", "Normal", "AboveNormal", "Highest" } },
        };

        public static readonly string[] Games = { "cs2.exe", "dota2.exe" };
        public static bool ForGame(string exe) { return Games.Any(g => string.Equals(g, exe, StringComparison.OrdinalIgnoreCase)); }

        // a value checked against the slot's list: anything else (a hand-edited registry value) is "" - no test
        public static string Norm(Slot s, string v)
        {
            v = (v ?? "").Trim();
            foreach (var x in s.Values) if (string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) return x;
            return "";
        }

        public static string Get(Slot s) { return Norm(s, Settings.Get(s.Key)); }
        public static void Set(Slot s, string v) { string n = Norm(s, v); if (n.Length == 0) Settings.Reset(s.Key); else Settings.Set(s.Key, n); }
        public static void Clear() { Settings.Reset(Slots.Select(s => s.Key).ToArray()); }
        public static bool Active { get { return Slots.Any(s => Get(s).Length > 0); } }
        public static string[] Values() { return Slots.Select(Get).ToArray(); }

        // The test rules for the values (main, render, pool) - pure, the tests call it directly. "keep" is a rule without a
        // priority: it still wins the thread, so the preset's rule behind it does not touch it.
        public static List<ThreadRule> RulesFor(params string[] values)
        {
            var l = new List<ThreadRule>();
            for (int i = 0; i < Slots.Length && values != null && i < values.Length; i++)
            {
                string v = Norm(Slots[i], values[i]);
                if (v.Length == 0) continue;
                l.Add(new ThreadRule { Match = "desc", Pattern = Slots[i].Pattern, Prio = v == "keep" ? "" : v, Test = true });
            }
            return l;
        }

        public static List<ThreadRule> Rules() { return RulesFor(Values()); }

        // "MainThrd TimeCritical, D3D11RenderThread Highest" for the log (English on purpose); "" when no test is picked
        public static string Summary(params string[] values)
        {
            var parts = new List<string>();
            for (int i = 0; i < Slots.Length && values != null && i < values.Length; i++)
            {
                string v = Norm(Slots[i], values[i]);
                if (v.Length > 0) parts.Add(Slots[i].Name + " " + (v == "keep" ? "game default" : v));
            }
            return string.Join(", ", parts);
        }

        public static string Summary() { return Summary(Values()); }
    }
}
