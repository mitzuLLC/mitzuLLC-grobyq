using System.Collections.Generic;
using System.Linq;

namespace Grobyq
{
    // Core-layout templates: orientation, not magic presets. Each one resolves on THIS CPU through the existing
    // mask / split helpers (no topology logic of its own) and is a hypothesis until a trace confirms it (T2):
    // Contour A (WPR/WPA CPU + DPC/ISR) shows whether background and interrupts actually left the game cores.
    // Pure: the tests feed synthetic family A / B / C / D topologies.
    public sealed class LayoutTemplate
    {
        public string Key = "", Name = "", Summary = "", Irq = "";
        public string Mask = "";            // stored in Settings "Mask" when the template is applied
        public ulong Game, Background;      // resolved on this CPU
        public bool Available;
        public string Why = "";             // why it is not available here (English format; the UI translates)
        public string WhyArg;               // {0} of Why, when it has one
        public bool Reserve;                // the Reservation companion (ReservedCpuSets) exists for this CPU
    }

    public static class Layouts
    {
        public static List<LayoutTemplate> For(Topo t)
        {
            var list = new List<LayoutTemplate>();
            if (t == null) return list;
            ulong all = t.FullMask();
            var sp = t.Split;

            // 1. baseline: Windows scheduling on every core (the A side of every A/B)
            list.Add(new LayoutTemplate
            {
                Key = "baseline", Name = "Baseline (all cores)", Mask = "AllCores", Game = all, Background = all, Available = true,
                Summary = "The game may run on every core; background is not pinned. Measure this first, then compare a template against it.",
                Irq = "Device interrupts stay where Windows or the driver put them.",
            });

            // 2. SMT isolation: one LP per physical core for the game (core 0 left out), the SMT siblings and core 0 for
            //    everything else. On a hybrid CPU the same rule on the P-cores; E-cores stay background.
            var smt = new LayoutTemplate
            {
                Key = "smt-isolation", Name = "SMT isolation", Mask = "SmtOffNoCpu0",
                Summary = t.Hybrid ? "Game on one thread of every P-core except core 0; the SMT siblings, core 0 and the E-cores take background work."
                                   : "Game on one thread of every physical core except core 0; the SMT siblings and core 0 take background work.",
                Irq = "GPU interrupts on core 0 (LP 0 / 1), mouse / NIC on a free SMT sibling. LP 0 already carries kernel DPC load: co-locating the GPU there can help or contend - decide by WPA, not by preset.",
            };
            if (!t.SMT) smt.Why = "no SMT on this CPU";
            else if (t.PhysicalCount < 4) smt.Why = "fewer than 4 physical cores";
            else
            {
                smt.Available = true;
                smt.Game = Masks.Build(t, smt.Mask).Mask;
                smt.Background = all & ~smt.Game;
            }
            list.Add(smt);

            // 3. V-Cache split (family A dual-CCD X3D): game on the V-Cache CCD, everything else on the other one.
            var vc = new LayoutTemplate
            {
                Key = "vcache-split", Name = "V-Cache split", Mask = "GameSet",
                Summary = "Game on the V-Cache CCD (both threads of each core); background on the frequency CCD.",
                Irq = "GPU / NIC / audio interrupts on the other CCD; the mouse controller may stay on the game CCD.",
            };
            if (sp.Family == "A" && sp.CanSplit) { vc.Available = true; vc.Game = sp.Game; vc.Background = sp.NonGame; vc.Reserve = true; }
            else if (sp.Family == "A") { vc.Why = "the split is not verified: {0}"; vc.WhyArg = sp.Why(); }
            else { vc.Why = "needs a dual-CCD X3D CPU (family A); this CPU is family {0}"; vc.WhyArg = sp.Family; }
            list.Add(vc);

            // 4. P / E split (family C hybrid): game on the P-cores, background on the E-cores.
            var pe = new LayoutTemplate
            {
                Key = "pe-split", Name = "P-core / E-core split", Mask = "GameSet",
                Summary = "Game on every P-core thread; background on the E-cores.",
                Irq = "Device interrupts on E-cores, or on a P-core the game does not use.",
            };
            if (sp.Family == "C" && sp.CanSplit) { pe.Available = true; pe.Game = sp.Game; pe.Background = sp.NonGame; pe.Reserve = true; }
            else if (sp.Family == "C") { pe.Why = "the split is not verified: {0}"; pe.WhyArg = sp.Why(); }
            else { pe.Why = "needs a hybrid CPU with P- and E-cores (family C); this CPU is family {0}"; pe.WhyArg = sp.Family; }
            list.Add(pe);

            return list;
        }

        public static LayoutTemplate Find(Topo t, string key)
        {
            return For(t).FirstOrDefault(x => x.Key == key);
        }
    }
}
