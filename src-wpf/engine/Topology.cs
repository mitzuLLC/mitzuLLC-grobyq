using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Grobyq
{
    // Live CPU topology from GetSystemCpuSetInformation (+ L3 sizes from
    // GetLogicalProcessorInformationEx), and the mask math built on top of it.
    // Pure: every method takes the CpuSet list, so the tests can feed synthetic
    // topologies with no hardware.
    public sealed class Topo
    {
        public List<Native.CpuSet> Sets;
        public int LogicalCount;
        public int PhysicalCount;
        public bool SMT;
        public int Groups;
        public List<byte> EffClasses;
        public List<byte> LLCGroups;                                            // L3 domains, ascending LastLevelCacheIndex
        public Dictionary<byte, ulong> L3ByLlc = new Dictionary<byte, ulong>();   // L3 bytes per domain (empty if unknown)
        public byte? VCacheLlc;                                                 // the X3D domain, null if none
        public string Name = "";                                                // ProcessorNameString: only CHECKS the split, never picks a side
        public int Reported;                                                    // LPs Windows reports (GetActiveProcessorCount); 0 = unknown

        public bool Hybrid { get { return EffClasses.Count > 1; } }

        // Game / non-game split of this CPU (families A/B/C/D), computed once per Topo.
        private SplitPlan _split;
        public SplitPlan Split { get { return _split ?? (_split = CoreSplit.Detect(this)); } }

        public static Topo From(List<Native.CpuSet> sets, Dictionary<byte, ulong> l3ByLlc = null, string name = null, int reported = 0)
        {
            if (sets == null || sets.Count == 0)
                throw new Exception("GetSystemCpuSetInformation returned nothing (needs Win10 1607+).");
            var t = new Topo();
            t.Sets = sets;
            t.LogicalCount = sets.Count;
            t.PhysicalCount = sets.Select(s => s.Core).Distinct().Count();
            t.SMT = t.LogicalCount > t.PhysicalCount;
            t.Groups = sets.Select(s => s.Group).Distinct().Count();
            t.EffClasses = sets.Select(s => s.Eff).Distinct().OrderBy(x => x).ToList();
            t.LLCGroups = sets.Select(s => s.LLC).Distinct().OrderBy(x => x).ToList();
            if (l3ByLlc != null) t.L3ByLlc = new Dictionary<byte, ulong>(l3ByLlc);
            t.VCacheLlc = DetectVCache(t);
            t.Name = (name ?? "").Trim();
            t.Reported = reported;
            return t;
        }

        public static Func<Topo> LiveHook;   // tests / UI demo only

        public static Topo Live()
        {
            if (LiveHook != null) return LiveHook();
            var sets = Native.GetCpuSets();
            Dictionary<byte, ulong> l3 = null;
            try { l3 = L3Map(sets, Native.GetL3Caches()); } catch { }
            return From(sets, l3, Native.CpuName(), Native.ActiveLpCount());
        }

        // LastLevelCacheIndex -> size of the L3 that contains that domain's LPs.
        public static Dictionary<byte, ulong> L3Map(List<Native.CpuSet> sets, List<Native.L3Cache> caches)
        {
            var d = new Dictionary<byte, ulong>();
            if (sets == null || caches == null) return d;
            foreach (var s in sets)
                foreach (var c in caches)
                    if (c.Masks.Any(m => m.Key == s.Group && ((m.Value >> s.LP) & 1) != 0))
                    {
                        ulong cur;
                        if (!d.TryGetValue(s.LLC, out cur) || c.Size > cur) d[s.LLC] = c.Size;
                        break;
                    }
            return d;
        }

        // X3D rule: 2+ L3 domains, exactly one is the biggest, and it is at least 2x
        // the smallest (7950X3D/9950X3D: 96 MB vs 32 MB). Equal CCX/CCD caches
        // (Zen 2/3/4/5 non-X3D, Intel) -> no V-Cache domain.
        private static byte? DetectVCache(Topo t)
        {
            if (t.LLCGroups.Count < 2) return null;
            if (t.LLCGroups.Any(l => !t.L3ByLlc.ContainsKey(l))) return null;
            var sizes = t.LLCGroups.Select(l => t.L3ByLlc[l]).ToList();
            ulong max = sizes.Max(), min = sizes.Min();
            if (min == 0 || max < 2 * min) return null;
            var big = t.LLCGroups.Where(l => t.L3ByLlc[l] == max).ToList();
            return big.Count == 1 ? big[0] : (byte?)null;
        }

        public ulong FullMask()
        {
            if (LogicalCount >= 64) return ulong.MaxValue;
            return ((ulong)1 << LogicalCount) - 1;
        }

        public byte FirstCore() { return Sets.OrderBy(s => s.Core).First().Core; }

        public string L3Text(byte llc)
        {
            ulong b;
            if (!L3ByLlc.TryGetValue(llc, out b) || b == 0) return "L3 ?";
            return (b / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB";
        }
    }

    public sealed class MaskResult
    {
        public string Name;
        public ulong Mask;
        public string Hex;
        public string LPs;
        public uint[] SetIds;
        public int Count;
        // portable-mask extras
        public string Spec = "";                            // canonical rule, e.g. "VCache+NoSMT"; hex for LP masks
        public bool Portable = true;                        // false = raw LP mask, means other cores on another CPU
        public bool Satisfied = true;                       // false = a selector does not exist here and was dropped
        public int Domains;                                 // how many L3 domains the selection spans
        public List<string> Notes = new List<string>();
    }

    // A mask stored as INTENT, resolved on the machine it runs on.
    //   base      All | P | E | CCD<n> | VCache | Freq      (one, default All)
    //   modifiers NoSMT (one LP per physical core) | NoCore0 (drop the first physical core)
    //   examples  "VCache+NoSMT", "P+NoCore0", "CCD1", "E"
    public sealed class MaskSpec
    {
        public string Base = "All";
        public int Ccd = -1;
        public bool NoSmt, NoCore0;

        public override string ToString()
        {
            string s = Base == "CCD" ? "CCD" + Ccd.ToString(CultureInfo.InvariantCulture) : Base;
            if (NoSmt) s += "+NoSMT";
            if (NoCore0) s += "+NoCore0";
            return s;
        }
    }

    public sealed class MaskOption
    {
        public string Key;     // stored in settings (legacy name or spec)
        public string Label;   // shown in the UI
        public override string ToString() { return Label; }
    }

    public static class Masks
    {
        public static readonly string[] Names = { "AllCores", "NoCpu0", "SmtOff", "SmtOffNoCpu0" };

        // v2.0 names are specs too, so saved settings and the F1 recommender keep working.
        private static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AllCores",     "All" },
            { "NoCpu0",       "All+NoCore0" },
            { "SmtOff",       "P+NoSMT" },
            { "SmtOffNoCpu0", "P+NoSMT+NoCore0" },
        };

        // "0-15", "2,4,6,8" or "2,4,6 … 28,30" for long lists (UI titles).
        public static string LpsShort(ulong mask)
        {
            var l = new List<int>();
            for (int i = 0; i < 64; i++) if (((mask >> i) & 1) != 0) l.Add(i);
            if (l.Count == 0) return "";
            bool contiguous = l[l.Count - 1] - l[0] == l.Count - 1;
            if (contiguous && l.Count > 2) return l[0] + "-" + l[l.Count - 1];
            if (l.Count <= 8) return string.Join(",", l);
            return string.Join(",", l.Take(3)) + " … " + string.Join(",", l.Skip(l.Count - 2));
        }

        // "0-15", "0-1,10-15", "2-9,16-19": the form the log and the core-set strip use.
        public static string Ranges(ulong mask)
        {
            var parts = new List<string>();
            int i = 0;
            while (i < 64)
            {
                if (((mask >> i) & 1) == 0) { i++; continue; }
                int a = i;
                while (i + 1 < 64 && ((mask >> (i + 1)) & 1) != 0) i++;
                parts.Add(a == i ? a.ToString(CultureInfo.InvariantCulture) : a.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture));
                i++;
            }
            return string.Join(",", parts);
        }

        public static string Hex(ulong m) { return "0x" + m.ToString("X"); }

        public static string MaskLps(ulong mask)
        {
            var l = new List<int>();
            for (int i = 0; i < 64; i++) if (((mask >> i) & 1) != 0) l.Add(i);
            return string.Join(",", l);
        }

        // "0x5554", "21844" or a list "2,4,6,8,10,12,14".
        public static ulong Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string t = text.Trim();
            try
            {
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToUInt64(t.Substring(2), 16);
                if (t.All(char.IsDigit)) return ulong.Parse(t, CultureInfo.InvariantCulture);
                if (t.All(c => char.IsDigit(c) || c == ',' || char.IsWhiteSpace(c)))
                {
                    ulong m = 0;
                    foreach (var p in t.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int lp = int.Parse(p, CultureInfo.InvariantCulture);
                        if (lp < 0 || lp > 63) return 0;             // a 64-bit mask has LP 0..63; 1<<64 would wrap to LP 0
                        m |= (ulong)1 << lp;
                    }
                    return m;
                }
                return Convert.ToUInt64(t, 16);
            }
            catch { return 0; }
        }

        // ---------------- portable specs ----------------

        // null = not a spec (then the text may still be a hex / decimal / LP list).
        public static MaskSpec TryParseSpec(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string legacy;
            if (Legacy.TryGetValue(text.Trim(), out legacy)) text = legacy;

            var s = new MaskSpec();
            bool haveBase = false;
            foreach (var raw in text.Split(new[] { '+', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string u = raw.Trim().ToLowerInvariant();
                string b = null;
                switch (u)
                {
                    case "all": case "allcores": b = "All"; break;
                    case "p": case "pcores": case "p-cores": b = "P"; break;
                    case "e": case "ecores": case "e-cores": b = "E"; break;
                    case "vcache": case "v-cache": case "x3d": b = "VCache"; break;
                    case "freq": case "frequency": b = "Freq"; break;
                    case "gameset": case "game": b = "GameSet"; break;
                    case "nosmt": case "smtoff": s.NoSmt = true; continue;
                    case "nocore0": case "nocpu0": s.NoCore0 = true; continue;
                    default:
                        int n;
                        if (u.StartsWith("ccd") && int.TryParse(u.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 0 && n < 64)
                        { b = "CCD"; s.Ccd = n; break; }
                        return null;
                }
                if (haveBase) return null;   // two bases ("P+E") is not a rule
                haveBase = true;
                s.Base = b;
            }
            return s;
        }

        // Canonical spec for a legacy name or a spec text; null if it is neither.
        public static string ToSpec(string nameOrSpec)
        {
            var s = TryParseSpec(nameOrSpec);
            return s == null ? null : s.ToString();
        }

        public static MaskResult BuildSpec(Topo t, MaskSpec spec)
        {
            var notes = new List<string>();
            bool sat = true, noSmt = spec.NoSmt;
            IEnumerable<Native.CpuSet> pool = t.Sets;

            switch (spec.Base)
            {
                case "P":
                    // F12: on hybrid the performance layer only; on a non-hybrid CPU every core is "P".
                    if (t.Hybrid) { byte maxEff = t.Sets.Max(s => s.Eff); pool = t.Sets.Where(s => s.Eff == maxEff); }
                    break;
                case "E":
                    // everything below the top class: E-cores, and LP E-cores on SoC-tile parts
                    if (t.Hybrid) { byte maxEff = t.Sets.Max(s => s.Eff); pool = t.Sets.Where(s => s.Eff < maxEff); }
                    else { sat = false; notes.Add("E: this CPU has no efficiency cores - selector ignored"); }
                    break;
                case "CCD":
                    if (spec.Ccd >= 0 && spec.Ccd < t.LLCGroups.Count) { byte llc = t.LLCGroups[spec.Ccd]; pool = t.Sets.Where(s => s.LLC == llc); }
                    else { sat = false; notes.Add(string.Format("CCD{0}: this CPU has {1} L3 domain(s) - selector ignored", spec.Ccd, t.LLCGroups.Count)); }
                    break;
                case "VCache":
                    if (t.VCacheLlc.HasValue) { byte v = t.VCacheLlc.Value; pool = t.Sets.Where(s => s.LLC == v); }
                    else { sat = false; notes.Add("VCache: no L3 domain with a larger cache (not a dual-CCD X3D) - selector ignored"); }
                    break;
                case "Freq":
                    if (t.VCacheLlc.HasValue) { byte v = t.VCacheLlc.Value; pool = t.Sets.Where(s => s.LLC != v); }
                    else { sat = false; notes.Add("Freq: no V-Cache domain on this CPU, so no 'frequency' CCD - selector ignored"); }
                    break;
                case "GameSet":
                    // Thin alias: family A = the V-Cache domain ("VCache"), family C = the top EfficiencyClass ("P").
                    // The split plan only decides WHETHER that is a valid game set here (known SKU, LP count, SMT pairs whole).
                    var plan = t.Split;
                    if (plan.CanSplit)
                    {
                        byte maxEff = t.Sets.Max(s => s.Eff);
                        if (plan.Family == "C") pool = t.Sets.Where(s => s.Eff == maxEff);
                        else if (t.VCacheLlc.HasValue && Mask(t.Sets.Where(s => s.LLC == t.VCacheLlc.Value)) == plan.Game) { byte v = t.VCacheLlc.Value; pool = t.Sets.Where(s => s.LLC == v); }
                        else pool = t.Sets.Where(s => ((plan.Game >> s.LP) & 1) != 0);
                        if (noSmt) { noSmt = false; notes.Add("GameSet keeps both SMT threads of every core - NoSMT ignored"); }
                    }
                    else { sat = false; notes.Add("GameSet: no game / non-game split on this CPU (" + plan.Why() + ") - selector ignored"); }
                    break;
            }

            byte firstCore = t.FirstCore();
            if (spec.NoCore0) pool = pool.Where(s => s.Core != firstCore);
            if (noSmt) pool = pool.GroupBy(s => s.Core).Select(g => g.OrderBy(s => s.LP).First());

            var list = pool.ToList();
            if (list.Count == 0)
            {
                sat = false;
                notes.Add(spec + ": selects no LP on this CPU - all cores used");
                list = t.Sets.ToList();
            }

            ulong m = 0;
            foreach (var s in list) m |= (ulong)1 << s.LP;
            var ordered = list.OrderBy(s => s.LP).ToList();
            return new MaskResult
            {
                Name = spec.ToString(),
                Spec = spec.ToString(),
                Portable = true,
                Satisfied = sat,
                Mask = m,
                Hex = "0x" + m.ToString("X"),
                LPs = string.Join(",", ordered.Select(s => (int)s.LP)),
                SetIds = list.Select(s => s.Id).ToArray(),
                Count = list.Count,
                Domains = list.Select(s => s.LLC).Distinct().Count(),
                Notes = notes
            };
        }

        // Rule text -> mask on THIS machine. Spec first ("E", "CCD0" are also valid hex;
        // the spec meaning wins, a raw LP mask should be written with 0x).
        public static MaskResult Resolve(Topo t, string text, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(text)) { error = "is empty"; return null; }
            string name = text.Trim();

            var spec = TryParseSpec(name);
            if (spec != null)
            {
                var r = BuildSpec(t, spec);
                r.Name = name;
                return r;
            }

            ulong raw = Parse(name);
            if (raw == 0) { error = "is not a rule or a mask"; return null; }
            var lim = Limit(t, raw);
            if (!lim.Ok) { error = string.Format("0x{0:X} has no LP that exists on this CPU", raw); return null; }
            var res = FromValue(t, lim.Mask, name);
            res.Portable = false;
            res.Spec = res.Hex;
            if (lim.Note.Length > 0) res.Notes.Add(lim.Note);
            return res;
        }

        public static MaskResult Build(Topo t, string name)
        {
            string err;
            var r = Resolve(t, name, out err);
            if (r != null) return r;
            var d = BuildSpec(t, new MaskSpec());
            d.Name = name ?? "";
            d.Notes.Add(string.Format("mask '{0}' {1} - all cores used", name, err));
            return d;
        }

        public static ulong Mask(IEnumerable<Native.CpuSet> sets) { ulong m = 0; foreach (var s in sets) m |= (ulong)1 << s.LP; return m; }

        // Build an arbitrary mask value into a MaskResult (for per-process rules).
        public static MaskResult FromValue(Topo t, ulong m, string name)
        {
            var inSets = t.Sets.Where(s => ((m >> s.LP) & 1) == 1).ToList();
            return new MaskResult
            {
                Name = name,
                Mask = m,
                Hex = "0x" + m.ToString("X"),
                LPs = MaskLps(m),
                SetIds = inSets.Select(s => s.Id).ToArray(),
                Count = inSets.Count,
                Spec = "0x" + m.ToString("X"),
                Portable = false,
                Domains = inSets.Select(s => s.LLC).Distinct().Count()
            };
        }

        // What the mask dropdown offers on THIS CPU. v2.0 keys stay first and unchanged.
        public static List<MaskOption> Options(Topo t)
        {
            var o = new List<MaskOption>
            {
                new MaskOption { Key = "AllCores",     Label = "All cores" },
                new MaskOption { Key = "NoCpu0",       Label = "No CPU0" },
                new MaskOption { Key = "SmtOff",       Label = t.Hybrid ? "P-cores, SMT off" : "SMT off" },
                new MaskOption { Key = "SmtOffNoCpu0", Label = t.Hybrid ? "P-cores, SMT off, no CPU0" : "SMT off, no CPU0" },
            };
            // families A / C only: the whole fast CCD or every P-core, both SMT threads
            if (t.Split.CanSplit) o.Add(new MaskOption { Key = "GameSet", Label = "Game set (whole fast CCD / P-cores)" });
            if (t.Hybrid)
            {
                o.Add(new MaskOption { Key = "P",           Label = "P-cores only" });
                o.Add(new MaskOption { Key = "P+NoCore0",   Label = "P-cores, no CPU0" });
            }
            if (t.VCacheLlc.HasValue)
            {
                o.Add(new MaskOption { Key = "VCache",               Label = "V-Cache CCD" });
                o.Add(new MaskOption { Key = "VCache+NoCore0",       Label = "V-Cache CCD, no CPU0" });
                o.Add(new MaskOption { Key = "VCache+NoSMT",         Label = "V-Cache CCD, SMT off" });
                o.Add(new MaskOption { Key = "VCache+NoSMT+NoCore0", Label = "V-Cache CCD, SMT off, no CPU0" });
                o.Add(new MaskOption { Key = "Freq",                 Label = "Frequency CCD" });
            }
            else if (t.LLCGroups.Count > 1)
            {
                for (int i = 0; i < t.LLCGroups.Count; i++)
                {
                    o.Add(new MaskOption { Key = "CCD" + i,          Label = "L3 domain " + i + " (CCD/CCX)" });
                    o.Add(new MaskOption { Key = "CCD" + i + "+NoSMT", Label = "L3 domain " + i + ", SMT off" });
                }
            }
            return o;
        }

        public sealed class Clamp { public ulong Mask; public string Hex; public ulong Dropped; public bool Ok; public string Note = ""; }

        // A user-typed mask only means the LPs this machine has. SetProcessAffinityMask
        // fails the WHOLE call if any bit is outside the system mask, so a value copied
        // from an 8-core rig applies nothing on a 6-core. Clamp and report what dropped.
        public static Clamp Limit(Topo t, ulong mask)
        {
            ulong all = t.FullMask();
            ulong kept = mask & all;
            ulong lost = mask & ~all;
            var c = new Clamp { Mask = kept, Hex = "0x" + kept.ToString("X"), Dropped = lost, Ok = kept != 0 };
            if (lost != 0) c.Note = "LP " + MaskLps(lost) + " do not exist on this CPU and were dropped";
            return c;
        }

        // Background must not land on the SMT siblings of the cores the game just took.
        // Prefer physical cores the game does not touch; on SmtOff, concentrate on core 0.
        public static ulong BgMask(Topo t, ulong gameMask)
        {
            var free = t.Sets.GroupBy(s => s.Core)
                             .Where(g => g.All(s => ((gameMask >> s.LP) & 1) == 0))
                             .SelectMany(g => g).ToList();
            ulong m = 0;
            foreach (var s in free) m |= (ulong)1 << s.LP;
            if (m != 0) return m;

            byte fc = t.FirstCore();
            foreach (var s in t.Sets.Where(s => s.Core == fc))
                if (((gameMask >> s.LP) & 1) == 0) m |= (ulong)1 << s.LP;
            if (m != 0) return m;

            ulong all = t.FullMask();
            m = all ^ gameMask;
            if (m != 0) return m;
            foreach (var s in t.Sets.Where(s => s.Core == fc)) m |= (ulong)1 << s.LP;
            if (m == 0) m = 1;
            return m;
        }

        // One LP per physical core; hottest threads never share a physical core.
        // On hybrid, performance class only. AllowMask keeps picks inside the
        // process affinity, and (F12) excludes E-cores from the pool.
        public static List<Native.CpuSet> IdealCandidates(Topo t, bool skipCpu0, ulong allowMask)
        {
            IEnumerable<Native.CpuSet> pool = t.Sets;
            if (t.EffClasses.Count > 1)
            {
                byte maxEff = t.Sets.Max(s => s.Eff);
                pool = t.Sets.Where(s => s.Eff == maxEff);
            }
            if (allowMask != 0)
            {
                var inMask = pool.Where(s => ((allowMask >> s.LP) & 1) == 1).ToList();
                if (inMask.Count > 0) pool = inMask;
            }
            var cand = pool.GroupBy(s => s.Core).Select(g => g.OrderBy(s => s.LP).First())
                           .OrderBy(s => s.LP).ToList();
            if (skipCpu0 && cand.Count >= 6) cand = cand.Where(s => s.LP != 0).ToList();
            if (cand.Count == 0) cand = t.Sets.OrderBy(s => s.LP).ToList();
            return cand;
        }

        // Which mask is a sane STARTING point for this CPU (spec F1 recommender). First-run default only:
        // Settings.PinMaskDefault keeps an existing install on the value it was already running.
        // Dual-CCD X3D with a verified split (family A) -> the game set; everything else -> all cores (v4.0).
        // No blanket "no CPU 0" any more: CPU 0 is not special by itself - the LPs that actually carry a dedicated device
        // interrupt pin are, and the interrupt layer (GameMask, Affinity > Cores > Interrupt cores) already takes exactly
        // those off the game, from the registry, not from folklore. NoCpu0 / SmtOffNoCpu0 stay as opt-in A/B steps.
        public static string Recommended(Topo t)
        {
            if (t.Split.Family == "A" && t.Split.CanSplit) return "GameSet";
            return "AllCores";
        }

        // The v2.0 - v3.0 recommender, X3D-agnostic.
        public static string RecommendedLegacy(Topo t)
        {
            if (t.PhysicalCount >= 8 && t.SMT) return "SmtOffNoCpu0";
            if (t.PhysicalCount >= 6) return "NoCpu0";
            return "AllCores";
        }

        // Orientation lines, printed once per session by the engine, never forced.
        public static List<string> Advice(Topo t, MaskResult mask)
        {
            var msgs = new List<string>();
            foreach (var n in mask.Notes) msgs.Add("[i ] mask " + mask.Name + ": " + n);
            int left = mask.Count, total = t.LogicalCount;
            if (left < 6 && total >= 8)
                msgs.Add(string.Format("[i ] mask {0} leaves the game {1} LP of {2}. CS2 keeps a worker pool; below 6 LP it oversubscribes.", mask.Name, left, total));
            if (t.PhysicalCount <= 6 && mask.Name == "SmtOffNoCpu0")
            {
                var alt = Build(t, "NoCpu0");
                msgs.Add(string.Format("[i ] {0} physical cores: SmtOffNoCpu0 gives away {1} of {2} LP. Try NoCpu0 first ({3} LP {4}).",
                    t.PhysicalCount, total - left, total, alt.Hex, alt.LPs));
            }
            if (t.PhysicalCount <= 4 && mask.Name != "AllCores")
                msgs.Add("[i ] 4 physical cores or fewer: any mask narrower than AllCores costs more than it gives. Measure A/B.");
            if (t.LLCGroups.Count > 1 && mask.Domains > 1)
                msgs.Add(string.Format("[i ] mask {0} spans {1} L3 domains (CCD/CCX): game threads that migrate between them lose L3 locality. " +
                    "A single-domain rule ({2}) is the A/B candidate - judge it by frame-time / Contour A, not by avg FPS.",
                    mask.Name, mask.Domains, t.VCacheLlc.HasValue ? "VCache" : "CCD0"));
            return msgs;
        }
    }

    // ---------------- game / non-game split ("CCD split") ----------------
    // Port of the families in grobPC_ReservedCpuSets.ps1 (Get-Plan / Test-Split). Inputs are T1 only:
    // CPU Sets (core, SMT, EfficiencyClass, L3 domain) + L3 sizes (GetLogicalProcessorInformationEx).
    // The CPU name only CHECKS (known dual-CCD X3D SKU + LP count, "X3D" for family B, "Core Ultra"
    // refusal); it never picks the game side.
    //   A  dual-CCD X3D: two L3 domains of different size -> game = the whole bigger-L3 domain
    //   B  single-CCD X3D: one L3 -> nothing to split
    //   C  hybrid: game = the HIGHEST EfficiencyClass (MS Learn: higher = faster, less efficient)
    //   D  homogeneous / equal L3 domains -> no split
    //   ABORT  anything that does not add up (reason code + args)
    public sealed class SplitPlan
    {
        public string Family = "ABORT";
        public string Name = "", Sku = "";
        public ulong Game, NonGame, All;
        public int Probe, Reported, Expected, Cores;
        public bool SmtOn, UpperHalf;
        public ulong GameL3, NonGameL3;            // bytes, family A
        public int PCores, ECores;                 // family C
        public byte TopEff;
        public string Code = "";                   // why there is no split (B / D / ABORT), see CoreSplit.Text
        public object[] Args = new object[0];
        public List<string> Problems = new List<string>();   // Test-Split failures (English, internal)

        public bool IsSplitFamily { get { return Family == "A" || Family == "C"; } }
        public bool CanSplit { get { return IsSplitFamily && Problems.Count == 0 && Game != 0 && NonGame != 0; } }

        // English reason for logs / notes (the UI translates the code itself).
        public string Why()
        {
            if (Problems.Count > 0) return string.Join("; ", Problems);
            return CoreSplit.Text(Code, Args);
        }

        public string Label { get { return Family == "A" ? Sku : Family == "C" ? "hybrid P/E" : Family; } }
    }

    public static class CoreSplit
    {
        // Orientation table for dual-CCD X3D (physical cores). Used to CHECK the probe, never instead of it.
        public static readonly Dictionary<string, int> X3dDual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "7900X3D", 12 }, { "9900X3D", 12 }, { "7950X3D", 16 }, { "9950X3D", 16 },
        };

        public static string SkuOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var m = Regex.Match(name, @"(\d{4}X3D)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
        }

        public static bool IsCoreUltra(string name)
        {
            return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, @"Core(\(TM\))?\s+Ultra", RegexOptions.IgnoreCase);
        }

        private static SplitPlan Stop(SplitPlan p, string code, params object[] args)
        {
            p.Family = "ABORT"; p.Code = code; p.Args = args ?? new object[0];
            return p;
        }

        private static ulong Bits(int count) { return count >= 64 ? ulong.MaxValue : ((ulong)1 << count) - 1; }

        public static SplitPlan Detect(Topo t)
        {
            var p = new SplitPlan { Name = t.Name ?? "", Sku = SkuOf(t.Name) };
            p.Probe = t.LogicalCount;
            p.Reported = t.Reported > 0 ? t.Reported : t.LogicalCount;
            p.Cores = t.PhysicalCount;
            var g0 = t.Sets.Where(s => s.Group == 0).ToList();
            var cores = g0.GroupBy(s => s.Core).ToList();
            p.All = Masks.Mask(g0);
            p.SmtOn = cores.Any(c => c.Count() > 1);

            if (t.Groups > 1 || p.Reported > 64 || p.Probe > 64) return Stop(p, "groups");
            if (p.Probe != p.Reported) return Stop(p, "probe", p.Probe, p.Reported);

            bool mixedSmt = cores.Any(c => c.Count() > 1) && cores.Any(c => c.Count() == 1);

            // ---- C) hybrid: game = the highest EfficiencyClass
            if (t.EffClasses.Count > 1)
            {
                byte top = t.EffClasses.Max();
                p.Family = "C"; p.TopEff = top;
                p.Game = Masks.Mask(g0.Where(s => s.Eff == top));
                p.NonGame = p.All & ~p.Game;
                p.PCores = cores.Count(c => c.First().Eff == top);
                p.ECores = cores.Count - p.PCores;
                p.Expected = p.Reported;
                return Validate(t, p);
            }

            var doms = t.LLCGroups;
            bool sizesKnown = doms.All(l => t.L3ByLlc.ContainsKey(l) && t.L3ByLlc[l] > 0);

            // ---- A) dual-CCD X3D: two L3 domains of different size, the bigger one is the 3D V-Cache CCD
            if (doms.Count == 2)
            {
                if (!sizesKnown) return Stop(p, "l3unknown");
                ulong s0 = t.L3ByLlc[doms[0]], s1 = t.L3ByLlc[doms[1]];
                if (s0 != s1)
                {
                    int nCores;
                    if (p.Sku.Length == 0 || !X3dDual.TryGetValue(p.Sku, out nCores))
                        return Stop(p, "unknownX3d", Mb(s0), Mb(s1), p.Name.Length > 0 ? p.Name : "?");
                    byte big = s0 > s1 ? doms[0] : doms[1];
                    p.GameL3 = Math.Max(s0, s1); p.NonGameL3 = Math.Min(s0, s1);
                    p.Game = Masks.Mask(g0.Where(s => s.LLC == big)) & p.All;
                    p.NonGame = p.All & ~p.Game;
                    int perCore = p.SmtOn ? 2 : 1;
                    p.Expected = nCores * perCore;
                    if (p.Reported != p.Expected) return Stop(p, "lpcount", p.Sku, p.Expected, p.SmtOn ? "on" : "off", p.Reported);
                    ulong low = Bits(nCores / 2 * perCore);
                    p.Family = "A";
                    if (p.Game == low) { }
                    else if (p.NonGame == low) p.UpperHalf = true;          // follow the probe, warn in the UI
                    else return Stop(p, "neither", Masks.Hex(p.Game));
                    return Validate(t, p);
                }
            }
            else if (doms.Count > 2 && sizesKnown && doms.Select(l => t.L3ByLlc[l]).Distinct().Count() > 1)
                return Stop(p, "l3layout", doms.Count);

            // ---- B) single-CCD X3D: one L3, nothing to split
            if (doms.Count == 1 && p.Sku.Length > 0) { p.Family = "B"; p.Code = "single"; return p; }

            // ---- hybrid-looking topology without EfficiencyClass: refuse to guess
            if (mixedSmt || IsCoreUltra(p.Name)) return Stop(p, "noclass");

            // ---- D) homogeneous
            p.Family = "D"; p.Code = doms.Count > 1 ? "equalL3" : "oneL3";
            return p;
        }

        private static object Mb(ulong b) { return (b / (1024 * 1024)).ToString(CultureInfo.InvariantCulture); }

        // Every physical core entirely inside or entirely outside a mask (SMT pairs stay together).
        public static bool SmtWhole(Topo t, ulong mask)
        {
            foreach (var c in t.Sets.Where(s => s.Group == 0).GroupBy(s => s.Core))
            {
                ulong cm = Masks.Mask(c), inside = cm & mask;
                if (inside != 0 && inside != cm) return false;
            }
            return true;
        }

        // Test-Split: the masks must be non-empty, disjoint, cover every LP and keep SMT pairs whole.
        private static SplitPlan Validate(Topo t, SplitPlan p)
        {
            if (p.Game == 0 || p.NonGame == 0) p.Problems.Add("game or non-game set is empty");
            if ((p.Game & p.NonGame) != 0) p.Problems.Add("game and non-game sets overlap");
            if ((p.Game | p.NonGame) != p.All) p.Problems.Add("game + non-game do not cover every LP");
            if (!SmtWhole(t, p.Game)) p.Problems.Add("the game set would split an SMT pair");
            if (!SmtWhole(t, p.NonGame)) p.Problems.Add("the non-game set would split an SMT pair");
            return p;
        }

        // English text per reason code. The UI has the same templates through L.F (Explain.SplitWhy).
        public static string Text(string code, object[] a)
        {
            a = a ?? new object[0];
            switch (code)
            {
                case "groups": return "more than 64 logical processors / several processor groups: a group-0 mask cannot describe this CPU";
                case "probe": return string.Format("the topology probe sees {0} LP, Windows reports {1} - not guessing", a);
                case "l3unknown": return "L3 cache sizes are not readable on this PC - not guessing";
                case "unknownX3d": return string.Format("two L3 domains of different size ({0} / {1} MB), but '{2}' is not a known dual-CCD X3D (7900X3D, 7950X3D, 9900X3D, 9950X3D)", a);
                case "lpcount": return string.Format("{0} should have {1} LP with SMT {2}; Windows reports {3}", a);
                case "neither": return string.Format("the cache domain {0} is neither half of the CPU - not guessing", a);
                case "l3layout": return string.Format("{0} L3 domains of different size: not a known layout - not guessing", a);
                case "noclass": return "looks hybrid (cores with and without SMT, or a Core Ultra) but Windows reports one EfficiencyClass for all cores - cannot tell P from E safely";
                case "single": return "one CCD with the 3D V-Cache: every core already shares the big L3 - nothing to split";
                case "equalL3": return "every L3 domain is the same size: there is no cache half to prefer";
                case "oneL3": return "all cores are the same kind and share one L3: there is no second set";
            }
            return code;
        }
    }
}
