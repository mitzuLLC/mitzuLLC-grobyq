using System;
using System.Collections.Generic;
using System.Linq;

namespace Grobyq
{
    // Game / non-game sets as the Affinity page and the interrupt helpers see them.
    // With a verified split (family A / C) the sets are the split; otherwise the current game mask and the
    // background cores it leaves free (Masks.BgMask). Pure: the tests feed synthetic topologies.
    public static class CoreSets
    {
        public sealed class View
        {
            public bool Split;          // true = family A / C sets, false = current mask + free cores
            public ulong Game, NonGame;
        }

        public static View Effective(Topo t, string maskName)
        {
            var v = new View();
            if (t == null) return v;
            var p = t.Split;
            if (p.CanSplit) { v.Split = true; v.Game = p.Game; v.NonGame = p.NonGame; return v; }
            v.Game = Masks.Build(t, maskName).Mask;
            v.NonGame = Masks.BgMask(t, v.Game);
            return v;
        }

        // Interrupt order over the non-game cores: NIC first, then USB (mouse / keyboard controller), then GPU, audio,
        // storage, the rest. Each class starts on its own physical core, so NIC / USB / GPU land on different cores
        // when there are enough (7950X3D: NIC 16, USB 18, GPU 20). T2 engineering order, measured in WPA.
        public static int ClassSlot(string cls)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "NET": return 0;
                case "USB": return 1;
                case "DISPLAY": return 2;
                case "MEDIA": return 3;
                case "SCSIADAPTER": case "HDC": return 4;
                default: return 5;
            }
        }

        // Physical cores entirely inside the set, never the core of LP 0, ordered by their first LP.
        public static List<List<Native.CpuSet>> CoresIn(Topo t, ulong set)
        {
            var lp0 = t.Sets.FirstOrDefault(s => s.Group == 0 && s.LP == 0);
            bool has0 = t.Sets.Any(s => s.Group == 0 && s.LP == 0);
            return t.Sets.Where(s => s.Group == 0).GroupBy(s => s.Core)
                    .Where(g => g.All(s => ((set >> s.LP) & 1) != 0) && !(has0 && g.Key == lp0.Core))
                    .Select(g => g.OrderBy(s => s.LP).ToList())
                    .OrderBy(g => g[0].LP).ToList();
        }

        // ONE LP of the non-game set for a device of this class; -1 if the set has no usable core.
        // others = (class, target mask) of the OTHER devices: a core another class already uses is skipped when possible.
        public static int PickIrqLp(Topo t, ulong nonGame, string cls, IEnumerable<KeyValuePair<string, ulong>> others)
        {
            if (t == null || nonGame == 0) return -1;
            var cores = CoresIn(t, nonGame);
            if (cores.Count == 0) return -1;
            ulong busy = 0;
            if (others != null)
                foreach (var o in others)
                    if (!string.Equals(o.Key ?? "", cls ?? "", StringComparison.OrdinalIgnoreCase)) busy |= o.Value;
            int start = ClassSlot(cls) % cores.Count;
            for (int i = 0; i < cores.Count; i++)
            {
                var c = cores[(start + i) % cores.Count];
                if ((Masks.Mask(c) & busy) == 0) return c[0].LP;
            }
            return cores[start][0].LP;
        }

        // "Off the game cores": one LP outside the effective game set, never LP 0 and never an LP of the game
        // CCD / P-core set when a split exists. Highest first (away from the game's low LPs). -1 = none.
        public static int OffGameLp(Topo t, string maskName)
        {
            if (t == null) return -1;
            ulong game = Masks.Build(t, maskName).Mask;
            ulong bg = Masks.BgMask(t, game);
            var p = t.Split;
            if (p.CanSplit) bg = (bg | (p.NonGame & ~game)) & ~p.Game;
            bg &= ~game;
            bg &= ~1UL;
            for (int i = 63; i >= 1; i--) if (((bg >> i) & 1) != 0) return i;
            return -1;
        }

        // ---------------- topology-aware interrupt target (3.6.6) ----------------
        public sealed class IrqPick
        {
            public int Lp = -1;
            public int Tier;            // 1 = a physical core the game does not use, 2 = the free thread of a game core, 3 = a game LP
            public int CoreNo = -1;     // physical core number in LP order, as the grids show it ("Core N")
            public bool ECore;          // the pick is an efficiency core (hybrid, E-cores allowed)
            public bool ECoresSkipped;  // hybrid with "P-cores only": E-cores were left out
            public bool MultiGroup;     // more than one processor group: only group 0 is addressed
            public bool Shared;         // no core free of other devices' interrupts was left
        }

        // Where ONE device's interrupts should go on THIS CPU. Topology from CPU Sets (T1: core, SMT sibling, L3 domain,
        // EfficiencyClass, group) - never guessed from the CPU name or from even / odd numbering. Order (T2, confirmed per
        // PC in WPA DPC/ISR by CPU):
        //   never the core of LP 0 (clock owner and default interrupt target) and never a reserved LP; group 0 only;
        //   1. a physical core the game mask does not touch (the other CCD, E-cores, a freed core), highest first;
        //   2. else the free SMT thread of a game core, highest core first. grobyq gives hot game threads the FIRST LP of
        //      each core from the low end (Masks.IdealCandidates), so the second thread of the last core is never one of them;
        //   3. else (the game has every LP) the second thread of the last core - the adaptive game mask takes it off the game.
        // pOnly (hybrid): P-cores only. busy = LPs other devices are pinned to: their cores are skipped while possible.
        public static IrqPick RecommendIrq(Topo t, ulong game, bool pOnly, ulong busy, ulong reserved)
        {
            var r = new IrqPick();
            if (t == null) return r;
            r.MultiGroup = t.Groups > 1;
            var sets = t.Sets.Where(s => s.Group == 0 && s.LP < 64).ToList();
            if (sets.Count == 0) return r;
            byte top = sets.Max(s => s.Eff);
            bool hybrid = sets.Select(s => s.Eff).Distinct().Count() > 1;
            bool has0 = sets.Any(s => s.LP == 0);
            byte core0 = has0 ? sets.First(s => s.LP == 0).Core : (byte)255;
            var order = sets.GroupBy(s => s.Core).OrderBy(g => g.Min(s => s.LP)).Select(g => g.Key).ToList();
            var cores = sets.GroupBy(s => s.Core).Where(g => !(has0 && g.Key == core0))
                            .Select(g => g.OrderBy(s => s.LP).ToList()).OrderByDescending(g => g[0].LP).ToList();
            if (hybrid && pOnly && cores.Any(c => c[0].Eff == top))
            {
                r.ECoresSkipped = cores.Any(c => c[0].Eff != top);
                cores = cores.Where(c => c[0].Eff == top).ToList();
            }
            Func<int, bool> inGame = lp => ((game >> lp) & 1) != 0;
            Func<int, bool> usable = lp => ((reserved >> lp) & 1) == 0;
            Func<List<Native.CpuSet>, bool> idle = c => c.All(s => ((busy >> s.LP) & 1) == 0);

            for (int tier = 1; tier <= 3; tier++)
                foreach (bool needIdle in new[] { true, false })
                    foreach (var c in cores)
                    {
                        if (needIdle && !idle(c)) continue;
                        bool free = c.All(s => !inGame(s.LP));
                        int lp = -1;
                        if (tier == 1 && free) { foreach (var s in c) if (usable(s.LP)) { lp = s.LP; break; } }
                        else if (tier == 2 && !free) { for (int k = c.Count - 1; k >= 0; k--) if (!inGame(c[k].LP) && usable(c[k].LP)) { lp = c[k].LP; break; } }
                        else if (tier == 3) { for (int k = c.Count - 1; k >= 0; k--) if (usable(c[k].LP)) { lp = c[k].LP; break; } }
                        if (lp < 0) continue;
                        r.Lp = lp; r.Tier = tier; r.Shared = !needIdle;
                        r.CoreNo = order.IndexOf(c[0].Core);
                        r.ECore = hybrid && c[0].Eff != top;
                        return r;
                    }
            return r;
        }

        // An E-core of a hybrid CPU (for the manual-pick warning).
        public static bool IsECore(Topo t, int lp)
        {
            if (t == null || !t.Hybrid) return false;
            byte top = t.Sets.Max(s => s.Eff);
            return t.Sets.Any(s => s.Group == 0 && s.LP == lp && s.Eff != top);
        }
    }
}
