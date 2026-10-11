using System;
using System.Collections.Generic;
using System.Linq;

namespace Grobyq
{
    // What the optimizer really gives the game (3.6.5 adaptive mask), in three layers:
    //   1. the base rule - Settings "Mask": a portable rule (AllCores, SmtOffNoCpu0, GameSet, VCache+NoSMT ...) or a hex;
    //   2. the user's own edits on the Cores tab - LPs added to / taken off the base (Settings "MaskAdd" / "MaskDrop");
    //   3. adaptive - the LPs device interrupts are pinned to (Affinity Policy = Specified processors +
    //      AssignmentSetOverride, present PCI devices only), taken off as that thread or as the whole core (Settings "MaskIrq").
    //      Only a DEDICATED pin counts (all its LPs inside one physical core): "every CPU but 0" or a whole CCD is interrupt
    //      steering, not a core to keep the game away from. And the interrupt layer never takes more than half of the game's
    //      LPs - past that the interrupt LPs stay in the mask and the card says why.
    // The ReservedCpuSets guard stays in the engine, and a per-process rule for the game (Processes tab) still wins.
    // Adjust is pure (the tests call it); Info / Rule read Settings and the registry around it.
    public sealed class GameMaskInfo
    {
        public string BaseRule = "";
        public ulong Base, Add, Drop, Manual, Irq, IrqDrop, Final;
        public bool IrqIgnored;                                  // taking the interrupt LPs off would leave the game less than half
        public bool EditsStale;                                  // saved grid edits belong to another CPU layout: ignored
        public string Mode = GameMask.IrqThread;
        public List<KeyValuePair<string, ulong>> IrqDevices = new List<KeyValuePair<string, ulong>>();   // dedicated pins (count)
        public List<KeyValuePair<string, ulong>> IrqWide = new List<KeyValuePair<string, ulong>>();      // wider pins (ignored)
        public bool Adjusted { get { return Final != Base; } }
    }

    public static class GameMask
    {
        public const string IrqOff = "Off", IrqThread = "Thread", IrqCore = "Core";

        // ---------------- pure ----------------

        public static int Count(ulong m) { int n = 0; while (m != 0) { m &= m - 1; n++; } return n; }

        // Every LP of each physical core that holds at least one LP of lps.
        public static ulong WholeCores(Topo t, ulong lps)
        {
            ulong m = 0;
            if (t == null || lps == 0) return 0;
            foreach (var g in t.Sets.Where(s => s.Group == 0).GroupBy(s => s.Core))
                if (g.Any(s => ((lps >> s.LP) & 1) != 0)) foreach (var s in g) m |= 1UL << s.LP;
            return m;
        }

        // A dedicated interrupt pin: every LP of it inside ONE physical core.
        public static bool Dedicated(Topo t, ulong pin)
        {
            if (t == null || pin == 0) return false;
            var cores = t.Sets.Where(s => s.Group == 0 && ((pin >> s.LP) & 1) != 0).Select(s => s.Core).Distinct().Count();
            return cores == 1;
        }

        // base | add, minus drop, minus the interrupt LPs (as threads or whole cores). Never an empty mask: edits that
        // would leave nothing fall back to the base. The interrupt layer may take at most half of what the edits leave;
        // past that nothing is taken and irqIgnored says so.
        public static ulong Adjust(Topo t, ulong baseMask, ulong add, ulong drop, ulong irq, string mode, out ulong irqDrop, out bool irqIgnored)
        {
            irqDrop = 0; irqIgnored = false;
            ulong all = t.FullMask();
            ulong m = ((baseMask | add) & ~drop) & all;
            if (m == 0) m = baseMask & all;
            if (m == 0) m = all;
            if (irq != 0 && mode != IrqOff)
            {
                ulong d = (mode == IrqCore ? WholeCores(t, irq) : irq) & m;
                if (d != 0)
                {
                    ulong left = m & ~d;
                    if (left == 0 || Count(left) * 2 < Count(m)) irqIgnored = true;
                    else { m = left; irqDrop = d; }
                }
            }
            return m;
        }

        // Grid ticks -> edits relative to the base: what is ticked but not in the base is added, what the base has but
        // is not ticked is dropped.
        public static void EditsFrom(ulong baseMask, ulong manual, out ulong add, out ulong drop)
        {
            add = manual & ~baseMask;
            drop = baseMask & ~manual;
        }

        public static string NormMode(string v)
        {
            if (string.Equals(v, IrqOff, StringComparison.OrdinalIgnoreCase)) return IrqOff;
            if (string.Equals(v, IrqCore, StringComparison.OrdinalIgnoreCase)) return IrqCore;
            return IrqThread;
        }

        // ---------------- settings ----------------

        public static string Mode { get { return NormMode(Settings.Get("MaskIrq")); } }
        public static ulong SavedAdd { get { return Masks.Parse(Settings.Get("MaskAdd")); } }
        public static ulong SavedDrop { get { return Masks.Parse(Settings.Get("MaskDrop")); } }

        // A new base (dropdown, template, custom rule, advice, profile): the edits were made against the old base, so
        // they are cleared - an old "LP 11 off" never silently rides on a different rule. True = edits were cleared.
        public static bool SetBase(string rule)
        {
            rule = rule ?? "";
            bool same = string.Equals(Settings.Get("Mask"), rule, StringComparison.OrdinalIgnoreCase);
            bool had = SavedAdd != 0 || SavedDrop != 0;
            Settings.Set("Mask", rule);
            if (same) return false;
            ClearEdits();
            return had;
        }

        public static void SetEdits(ulong add, ulong drop)
        {
            Settings.Set("MaskAdd", add == 0 ? "" : Masks.Hex(add));
            Settings.Set("MaskDrop", drop == 0 ? "" : Masks.Hex(drop));
            string sig = "";
            if (add != 0 || drop != 0) { try { sig = CpuSig(Topo.Live()); } catch { } }
            Settings.Set("MaskCpu", sig);                 // the layout these LP numbers belong to
        }

        public static void ClearEdits() { SetEdits(0, 0); }

        public static void SetMode(string mode) { Settings.Set("MaskIrq", NormMode(mode)); }

        // Grid edits are fixed LP numbers: a profile carries this and its edits are applied only on the same layout.
        public static string CpuSig(Topo t)
        {
            return t == null ? "" : t.LogicalCount + "LP/" + t.PhysicalCount + "C/" + t.LLCGroups.Count + "L3" + (t.Hybrid ? "/hybrid" : "");
        }

        // The 3.6.5 update keeps a running install's game on the cores it had (same rule as Settings.PinMaskDefault):
        // when taking interrupt LPs off would change the mask right now, the mode starts at Off and the card offers it.
        // A first run, or an install whose pins do not touch the game mask, starts at Thread.
        public static string PinModeDefault(bool existing)
        {
            try
            {
                if (Settings.Stored("MaskIrq")) return "";
                string mode = IrqThread, note = "";
                if (existing)
                {
                    var t = Topo.Live();
                    var i = Info(t, true, IrqThread);
                    if (i.IrqDrop != 0)
                    {
                        mode = IrqOff;
                        note = "interrupt cores stay in the game mask for now (LP " + Masks.Ranges(i.IrqDrop) + " would leave it); turn it on at Affinity > Cores > Interrupt cores";
                    }
                }
                Settings.Set("MaskIrq", mode);
                return note;
            }
            catch { return ""; }
            finally { Invalidate(); }
        }

        // ---------------- interrupt pins (cached: the engine asks on every re-apply) ----------------

        public static Func<List<KeyValuePair<string, ulong>>> IrqHook;       // tests / UI demo
        private static readonly object _lk = new object();
        private static List<KeyValuePair<string, ulong>> _irq;
        private static DateTime _irqAt;

        public static void Invalidate() { lock (_lk) _irq = null; }

        public static List<KeyValuePair<string, ulong>> IrqPins()
        {
            lock (_lk)
            {
                if (_irq != null && (DateTime.UtcNow - _irqAt).TotalSeconds < 30) return _irq;
                List<KeyValuePair<string, ulong>> l;
                try { l = IrqHook != null ? IrqHook() : Devices.PinnedIrqs(); } catch { l = null; }
                _irq = l ?? new List<KeyValuePair<string, ulong>>();
                _irqAt = DateTime.UtcNow;
                return _irq;
            }
        }

        // ---------------- the effective mask ----------------

        public static GameMaskInfo Info(Topo t) { return Info(t, true, null); }

        // withIrq = false: base + edits only (background pinning, interrupt helpers) - no registry read
        public static GameMaskInfo Info(Topo t, bool withIrq, string modeOverride)
        {
            var i = new GameMaskInfo { BaseRule = Settings.Get("Mask"), Mode = modeOverride ?? Mode };
            if (t == null) return i;
            ulong all = t.FullMask();
            i.Base = Masks.Build(t, i.BaseRule).Mask;
            i.Add = SavedAdd & all;
            i.Drop = SavedDrop & all;
            // edits are fixed LP numbers: made on another CPU layout (a CPU swap, a BIOS SMT / core change) they are ignored
            string sig = Settings.Get("MaskCpu");
            if ((i.Add | i.Drop) != 0 && sig.Length > 0 && sig != CpuSig(t)) { i.Add = 0; i.Drop = 0; i.EditsStale = true; }
            i.Manual = ((i.Base | i.Add) & ~i.Drop) & all;
            if (i.Manual == 0) i.Manual = i.Base;
            if (withIrq)
                foreach (var d in IrqPins())
                {
                    ulong m = d.Value & all;
                    if (m == 0) continue;
                    if (Dedicated(t, m)) { i.IrqDevices.Add(new KeyValuePair<string, ulong>(d.Key, m)); i.Irq |= m; }
                    else i.IrqWide.Add(new KeyValuePair<string, ulong>(d.Key, m));
                }
            ulong idrop; bool ign;
            i.Final = Adjust(t, i.Base, i.Add, i.Drop, withIrq ? i.Irq : 0, i.Mode, out idrop, out ign);
            i.IrqDrop = idrop; i.IrqIgnored = ign;
            return i;
        }

        // The rule text every caller resolves: the base rule itself while nothing changes it (portable, the GameSet log
        // keeps working), else the final LP mask as hex.
        public static string Rule(Topo t)
        {
            if (t == null) return Settings.Get("Mask");
            var i = Info(t);
            return i.Final == i.Base ? i.BaseRule : Masks.Hex(i.Final);
        }

        public static MaskResult Effective(Topo t) { return Masks.Build(t, Rule(t)); }

        // A per-process rule for a TARGET (the game, Processes tab) goes through the same interrupt layer: the rule
        // replaces base + edits, interrupt cores still leave it (same mode, dedicated pins only, same half floor).
        // Rules for helpers (voice, VPN, OBS) are never touched.
        public static ulong ForRule(Topo t, ulong ruleMask, out ulong irqDrop)
        {
            irqDrop = 0;
            if (t == null || ruleMask == 0) return ruleMask;
            string mode = Mode;
            if (mode == IrqOff) return ruleMask;
            ulong all = t.FullMask(), irq = 0;
            foreach (var d in IrqPins()) { ulong m = d.Value & all; if (m != 0 && Dedicated(t, m)) irq |= m; }
            bool ign;
            return Adjust(t, ruleMask, 0, 0, irq, mode, out irqDrop, out ign);
        }

        // Base + the user's edits, without the interrupt layer. Background pinning follows this: an interrupt LP leaves
        // the game, it does not become the one core every background process is squeezed onto (All cores + NIC on LP 11
        // would otherwise pin Discord, OBS and the browser to LP 11 alone). The interrupt helpers use it too, so "Off the
        // game cores" never suggests an LP only because another device's pin took it off the game.
        public static string ManualRule(Topo t)
        {
            if (t == null) return Settings.Get("Mask");
            var i = Info(t, false, null);
            return i.Manual == i.Base ? i.BaseRule : Masks.Hex(i.Manual);
        }

        public static ulong Background(Topo t) { return t == null ? 0 : Masks.BgMask(t, Masks.Build(t, ManualRule(t)).Mask); }
    }
}
