using System;

namespace Grobyq
{
    // Every option in one place: what it does, in plain words, with a tier.
    // T1 = documented mechanism, T2 = engineering practice (measure it), T3 = folklore.
    public static class Explain
    {
        public static string Mask(string key, Topo t)
        {
            string spec = Masks.ToSpec(key) ?? key;
            bool hy = t != null && t.Hybrid;
            switch (key)
            {
                case "AllCores": return L.T("The game may run on every logical processor. The A/B baseline - always compare against it.");
                case "NoCpu0": return L.T("Drops the first physical core (both SMT threads). Windows and many drivers land interrupts and DPCs on CPU 0 by default. T2.");
                case "SmtOff": return hy ? L.T("P-cores only, one logical processor per core. E-cores and SMT siblings go to background. T2.")
                                         : L.T("One logical processor per physical core: no SMT sibling shares a core with a game thread. Background moves to the siblings. T2.");
                case "SmtOffNoCpu0": return L.T("SMT off and no CPU 0: most isolation, fewest LPs. Needs 8+ physical cores, otherwise CS2's worker pool is short of LPs. T2.");
            }
            switch (spec)
            {
                case "GameSet": return L.T("The whole fast set: the 3D V-Cache CCD (dual-CCD X3D) or every P-core (hybrid), both SMT threads of each core. Windows and background keep the other set. T1 topology / T2 effect - judge it by frame-time, not average FPS.");
                case "P": return L.T("Only performance cores (Intel hybrid). E-cores are left to background. T1 topology / T2 effect.");
                case "P+NoCore0": return L.T("P-cores without the first core. T2.");
                case "VCache": return L.T("Only the CCD with 3D V-Cache (the big L3). The game stays on one L3: no cross-CCD cache misses. Compare with Freq. T2.");
                case "VCache+NoCore0": return L.T("V-Cache CCD without the first core. T2.");
                case "VCache+NoSMT": return L.T("V-Cache CCD, one LP per core. T2.");
                case "VCache+NoSMT+NoCore0": return L.T("V-Cache CCD, one LP per core, without the first core. T2.");
                case "Freq": return L.T("Only the frequency CCD (no V-Cache, higher boost clock). The A/B partner of VCache. T2.");
            }
            if (spec.StartsWith("CCD")) return L.T("Only this L3 domain (one CCD on Zen 3/4/5, one CCX on Zen 2). Keeps the game's threads on one L3. T2.");
            return L.F("Custom rule {0}. Resolved on each PC from the CPU topology.", spec);
        }

        // Short "when to use it" line for the mask cards on the Threads page (Your processor / All masks).
        public static string MaskWhen(string key, Topo t)
        {
            string spec = Masks.ToSpec(key) ?? key;
            switch (key)
            {
                case "AllCores": return L.T("Start here and keep it if a narrower mask does not win in your own frame-time test.");
                case "NoCpu0": return L.T("When DPC/ISR time piles up on CPU 0 in WPA and the game shares it.");
                case "SmtOff": return L.T("When SMT siblings of the game's cores are busy with other work; costs half the logical processors.");
                case "SmtOffNoCpu0": return L.T("Only on 8+ core CPUs where both isolation steps measurably help.");
            }
            switch (spec)
            {
                case "GameSet": return L.T("Dual-CCD X3D and Intel hybrid: game on the fast set, Windows and background on the rest. Compare with All cores.");
                case "P": case "P+NoCore0": return L.T("Intel hybrid: keeps the game off E-cores, which have lower clocks and smaller caches.");
                case "VCache": case "VCache+NoCore0": case "VCache+NoSMT": case "VCache+NoSMT+NoCore0": return L.T("Dual-CCD X3D: cache-hungry games like CS2 usually prefer the V-Cache CCD. Compare with Freq.");
                case "Freq": return L.T("Dual-CCD X3D: the A/B partner of VCache for games that prefer clock speed.");
            }
            if (spec.StartsWith("CCD")) return L.T("Multi-CCD CPUs without V-Cache: one L3 avoids cross-CCD latency for the game's threads.");
            return "";
        }

        public static string MaskLabel(MaskOption o)
        {
            if (o == null) return "";
            switch (o.Label)
            {
                case "All cores": return L.T("All cores");
                case "Game set (whole fast CCD / P-cores)": return L.T("Game set (whole fast CCD / P-cores)");
                case "No CPU0": return L.T("No CPU0");
                case "SMT off": return L.T("SMT off");
                case "SMT off, no CPU0": return L.T("SMT off, no CPU0");
                case "P-cores, SMT off": return L.T("P-cores, SMT off");
                case "P-cores, SMT off, no CPU0": return L.T("P-cores, SMT off, no CPU0");
                case "P-cores only": return L.T("P-cores only");
                case "P-cores, no CPU0": return L.T("P-cores, no CPU0");
                case "V-Cache CCD": return L.T("V-Cache CCD");
                case "V-Cache CCD, no CPU0": return L.T("V-Cache CCD, no CPU0");
                case "V-Cache CCD, SMT off": return L.T("V-Cache CCD, SMT off");
                case "V-Cache CCD, SMT off, no CPU0": return L.T("V-Cache CCD, SMT off, no CPU0");
                case "Frequency CCD": return L.T("Frequency CCD");
            }
            if (o.Label.StartsWith("L3 domain "))
            {
                string rest = o.Label.Substring(10);
                int sp = 0; while (sp < rest.Length && char.IsDigit(rest[sp])) sp++;
                string n = rest.Substring(0, sp), tail = rest.Substring(sp);
                if (tail == " (CCD/CCX)") return L.F("L3 domain {0} (CCD/CCX)", n);
                if (tail == ", SMT off") return L.F("L3 domain {0}, SMT off", n);
            }
            return o.Label;
        }

        public static string MaskGrammar
        {
            get
            {
                return L.T("Rules are stored as intent and resolved on the PC they run on:  base  All | P | E | CCD0..n | VCache | Freq | GameSet  +  modifiers  NoSMT (one LP per core), NoCore0 (drop the first core). Example: VCache+NoSMT. A raw mask (0x5554 or 2,4,6) also works but means different cores on another CPU.");
            }
        }

        public static string ThreadPriority
        {
            get
            {
                return L.T("Thread priority = process class base + thread offset. With class High (base 13): Normal 13, Above normal 14, High (Highest) 15 - the top of the non-realtime range. TimeCritical would also land on 15, so grobyq stops at High. A thread at 15 wins every contest on its core: give it to 1-2 threads (main / render) and judge by frame-time, not average FPS.");
            }
        }

        public static string IdealCpu
        {
            get { return L.T("Ideal CPU is a scheduler hint, not a pin: Windows prefers that LP but may run the thread elsewhere. auto spreads the matched and hottest threads over separate physical cores inside the game mask."); }
        }

        public static string Boost
        {
            get { return L.T("Priority boost = short dynamic boosts after waits (I/O completion, input, GUI). Off keeps the priority fixed; keep lets Windows decide."); }
        }

        public static string Hot
        {
            get { return L.T("Hot threads: after the named rules, the busiest unnamed threads (by QueryThreadCycleTime) get an Ideal CPU on free cores. The first one may also get a priority."); }
        }

        public static string Class(string c)
        {
            switch (c)
            {
                case "High": return L.T("Base priority 13. Wins over Normal (8) programs on the same core - including the ones the game itself waits on (DWM, Steam). Raise it only if a trace shows the game losing the CPU. Never Realtime (24) - it can starve the input and audio stack.");
                case "AboveNormal": return L.T("Base priority 10. A softer step above normal programs.");
                case "Normal": return L.T("Base priority 8. Windows default, and grobyq's default for the game.");
                case "BelowNormal": return L.T("Base priority 6. For background tools.");
                case "Idle": return L.T("Base priority 4. Runs only when nothing else wants the core.");
            }
            return "";
        }

        public static string Io(string v)
        {
            switch (v)
            {
                case "High": return L.T("Disk requests go ahead of normal ones. Only matters while the game streams from disk (map load). T1 mechanism.");
                case "Normal": return L.T("Windows default I/O priority.");
                case "Low": return L.T("Background-level I/O.");
                case "VeryLow": return L.T("Lowest I/O priority, used for background processes.");
            }
            return "";
        }

        public static string Mem(string v)
        {
            return v == "Normal" ? L.T("Memory priority 5 (default): the game's pages are the last to be trimmed to standby.")
                                 : L.T("Lower memory priority: these pages leave RAM first under pressure. Use for background only.");
        }

        public static string Eco
        {
            get { return L.T("EcoQoS (power throttling) lets Windows run a process at efficient clocks / on E-cores. Off = the game always gets HighQoS. T1."); }
        }

        public static string ProcBoost
        {
            get { return L.T("Process-wide priority boost switch (SetProcessPriorityBoost). Keep = Windows default (boost enabled)."); }
        }

        // ---------------- CCD split (families A / B / C / D) ----------------
        public static string SplitFamily(SplitPlan p)
        {
            switch (p.Family)
            {
                case "A": return L.F("A · dual-CCD X3D ({0})", p.Sku);
                case "B": return L.F("B · single-CCD X3D ({0})", p.Sku);
                case "C": return L.T("C · hybrid P / E cores");
                case "D": return p.Code == "equalL3" ? L.T("D · several L3 domains of equal size") : L.T("D · homogeneous, one L3");
            }
            return L.T("not detected");
        }

        public static string SplitWhy(SplitPlan p)
        {
            var a = p.Args ?? new object[0];
            Func<int, object> A = i => i < a.Length ? a[i] : "?";
            if (p.IsSplitFamily && p.Problems.Count > 0) return L.T("The detected sets fail the split check (overlap, gap or a split SMT pair). No split.");
            switch (p.Code)
            {
                case "groups": return L.T("More than 64 logical processors / several processor groups: a group-0 mask cannot describe this CPU.");
                case "probe": return L.F("The topology probe sees {0} LP, Windows reports {1}. Not guessing.", A(0), A(1));
                case "l3unknown": return L.T("L3 cache sizes are not readable on this PC. Not guessing.");
                case "unknownX3d": return L.F("Two L3 domains of different size ({0} / {1} MB), but '{2}' is not a known dual-CCD X3D (7900X3D, 7950X3D, 9900X3D, 9950X3D). No split.", A(0), A(1), A(2));
                case "lpcount": return L.F("{0} should have {1} LP with SMT {2}; Windows reports {3}. No split.", A(0), A(1), Convert.ToString(A(2)) == "on" ? L.T("on") : L.T("off"), A(3));
                case "neither": return L.F("The cache domain {0} is neither half of the CPU. Not guessing.", A(0));
                case "l3layout": return L.F("{0} L3 domains of different size: not a known layout. Not guessing.", A(0));
                case "noclass": return L.T("Looks hybrid (cores with and without SMT, or a Core Ultra), but Windows reports one EfficiencyClass for all cores. P and E cores cannot be told apart safely, so there is no split.");
                case "single": return L.T("One CCD with the 3D V-Cache: every core already shares the big L3. Nothing to split.");
                case "equalL3": return L.T("Every L3 domain is the same size: there is no cache half to prefer. Nothing to split.");
                case "oneL3": return L.T("All cores are the same kind and share one L3: there is no second set to move the game away from. Nothing to split.");
            }
            return p.Why();
        }

        // ReservedCpuSets refusal codes (Reservation.Refusals)
        public static string Refusal(string code)
        {
            switch (code)
            {
                case "groups": return L.T("More than 64 logical processors: the 8-byte value describes processor group 0 only. Disabled here.");
                case "family": return L.T("No game / non-game split on this CPU (family B / D or not detected). Nothing to reserve.");
                case "nosplit": return L.T("The split is not verified on this PC. Nothing to reserve.");
                case "empty": return L.T("The set is empty.");
                case "lp0": return L.T("The non-game set contains LP 0 (boot processor, default interrupt target). Not reserved.");
                case "smt": return L.T("The set would split an SMT pair. Not reserved.");
                case "game": return L.T("The set overlaps the game set. The game set is never reserved.");
                case "notnongame": return L.T("Only the non-game set can be reserved.");
            }
            return code;
        }
    }
}
