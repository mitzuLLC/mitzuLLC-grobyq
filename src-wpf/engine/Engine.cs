using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Grobyq
{
    // Structured result of one Apply (spec A4). This is the A/B evidence.
    public sealed class ApplyReport
    {
        public string Target = "";
        public int Pid;
        public int HandleLevel = -1;   // L2 / L1 / L0
        public string MaskHex = "";
        public string MaskLps = "";
        public int NamedHits;
        public List<int> CycleFallbackTids = new List<int>();
        public List<string> Failures = new List<string>();
        public bool AccessDenied;
    }

    public sealed class ApplyOptions
    {
        public string MaskName = "AllCores";
        public string BgMaskName;               // the rule background pinning is derived from (null = MaskName): base + edits, no interrupt drops
        public int TopN = 4;
        public bool SkipCpu0 = true;
        public bool DoThreads = true;
        public bool Warmup = false;
        public bool DoBackground = true;
        public bool BgAffinity = true;
        public bool BgForeground = true;
        public bool DoAffRules = true;
        public bool DoProcess = true;           // game process: class / I/O / memory priority / EcoQoS / boost
        public bool DoMask = true;              // game process: core mask (affinity + default CPU sets)
        public bool Game = true;                // the game threads optimizer is on: targets, affinity rules, the user's background list
        public bool Sys = false;                // the system threads optimizer is on: the built-in profile + its background list
    }

    public sealed class Engine
    {
        // priority classes / io / mem / thread priorities
        public static readonly Dictionary<string, uint> PRIO = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        { {"Idle",0x40},{"BelowNormal",0x4000},{"Normal",0x20},{"AboveNormal",0x8000},{"High",0x80},{"Realtime",0x100} };
        public static readonly Dictionary<int, string> PRIONAME = new Dictionary<int, string>
        { {64,"Idle"},{16384,"BelowNormal"},{32,"Normal"},{32768,"AboveNormal"},{128,"High"},{256,"Realtime"} };
        public static readonly Dictionary<string, uint> IOP = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        { {"VeryLow",0},{"Low",1},{"Normal",2},{"High",3},{"Critical",4} };
        public static readonly Dictionary<string, uint> MEM = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        { {"VeryLow",1},{"Low",2},{"Medium",3},{"BelowNormal",4},{"Normal",5} };
        // THREAD_PRIORITY_*. TimeCritical (15) is NOT in this map: it is reachable only through ThreadPrio() with
        // Advanced thread mode on (it usually hurts frame-time: it can starve the scheduler). Off = coerced to Highest.
        public static readonly Dictionary<string, int> TPRIO = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { {"Idle",-15},{"Lowest",-2},{"BelowNormal",-1},{"Normal",0},{"AboveNormal",1},{"Highest",2} };
        public const int TimeCriticalValue = 15;

        public static bool AdvancedThreadMode { get { return Settings.GetBool("AdvancedThreadMode"); } }

        // Thread priority value for a stored key. "TimeCritical" -> 15 only when advanced, otherwise Highest (+2).
        public static bool ThreadPrio(string key, bool advanced, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(key)) return false;
            if (TPRIO.TryGetValue(key, out value)) return true;
            if (string.Equals(key, "TimeCritical", StringComparison.OrdinalIgnoreCase)) { value = advanced ? TimeCriticalValue : TPRIO["Highest"]; return true; }
            return false;
        }

        // ---- safety envelope (keep these comments: grobyq ships to clients) ----
        // Priority mis-set = lag until DISABLE / reboot, always recoverable. Suspending or killing threads is not:
        // csrss / winlogon / dwm = frozen desktop, a suspended game network thread = anti-cheat verification failure (ban). grobyq therefore has NO suspend / resume / terminate path anywhere, and it never touches an
        // anti-cheat process or module at all - not even priority. Both rules are unconditional (no setting).
        public static readonly HashSet<string> ANTICHEAT = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "easyanticheat", "easyanticheat_eos", "easyanticheat_x64", "easyanticheat_x86", "eac", "eaanticheat.gameservice",
            "beservice", "beservice_x64", "bedaisy", "battleye", "beclient", "beclient_x64", "beclient_x86",
            "vgc", "vgk", "vgtray", "vanguard", "sl-ac", "mhyprot", "mhyprot2", "mhyprot3", "mhypbase", "hoyoprotect",
            "gameguard", "gepbl", "xigncode", "xigncode3", "ncsvc", "hshield", "anticheatexpert", "faceit", "faceitac", "faceitservice",
            "ace-base64", "ace-safe", "ace-drv64", "ace-csi64", "ace-ats64", "ace-dfs64", "ace-guard", "sguard64",
        };
        private static readonly string[] AntiCheatPrefix =
        { "easyanticheat", "battleye", "beclient", "beservice", "faceit", "vanguard", "anticheatexpert", "xigncode", "gameguard", "mhyprot", "ace-" };

        // Comparison name of a process / module / pattern: no path, no wildcards, no .exe/.dll/.sys, lower case.
        public static string BareName(string name)
        {
            string s = (name ?? "").Trim().Replace("*", "").Replace("?", "");
            int slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (slash >= 0) s = s.Substring(slash + 1);
            foreach (var ext in new[] { ".exe", ".dll", ".sys" })
                if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - ext.Length); break; }
            return s.Trim().ToLowerInvariant();
        }

        public static bool IsAntiCheat(string name)
        {
            string b = BareName(name);
            if (b.Length == 0) return false;
            if (ANTICHEAT.Contains(b)) return true;
            foreach (var p in AntiCheatPrefix) if (b.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool IsProtected(string exe) { return PROTECTED.Contains(BareName(exe)); }

        // What a change may touch. Thread = thread priority / priority boost (the only thing allowed on a protected
        // process, and only after the Advanced acknowledgment). Process = class / I/O / memory priority / EcoQoS.
        // Affinity = process affinity / CPU sets / Ideal CPU. Anti-cheat: nothing, ever.
        public enum Op { Thread, Process, Affinity }

        // null = allowed; otherwise the reason (a log line / UI advice). Pure: the tests call it directly.
        public static string Gate(string exe, Op op, bool protectedUnlocked)
        {
            if (IsAntiCheat(exe)) return "[THR] anti-cheat target skipped: " + exe;
            if (IsProtected(exe))
            {
                if (op != Op.Thread) return string.Format("[--] {0} is a protected system process: grobyq never changes its class, I/O or cores.", exe);
                if (!protectedUnlocked) return string.Format("[--] {0} is a protected system process: lowering it hurts. Thread priority needs 'Priority on protected processes (advanced)'.", exe);
            }
            return null;
        }

        public static readonly HashSet<string> PROTECTED = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "system","idle","registry","memcompression","smss","csrss","wininit","winlogon",
          "services","lsass","dwm","audiodg","fontdrvhost","sihost","explorer","svchost",
          "powershell","pwsh","cmd","conhost","windowsterminal","grobpc","grobyq" };

        // presets.json (%APPDATA%\grobyq): per-process preset + background classes
        public Dictionary<string, Preset> Presets;
        public Dictionary<string, string> BgClasses;

        public Dictionary<string, string> AffRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Built-in system profile (embedded, see ThreadIni.LoadBundled). Applied by the "System thread optimization"
        // switch, never written to presets.json and not listed in the preset picker. Priority only.
        public Dictionary<string, Preset> SystemPresets = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
        // ...and its background lists ([PROCESSESTOSUSPEND] / [SETPROCESSESTOIDLEPRIORITY] -> Idle while the game has focus).
        public Dictionary<string, string> SystemBg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void LoadSystemProfile(bool on)
        {
            lock (_gate)
            {
                _presetTidCount.Clear();   // rules changed -> next cycle must re-apply (see InvalidatePresetApplyCache)
                if (!on) { SystemPresets.Clear(); SystemBg.Clear(); return; }
                var r = ThreadIni.LoadBundled();
                SystemPresets = r.Presets; SystemBg = r.BgClasses;
            }
        }

        // "before" snapshots keyed by first touch (spec F7)
        // Baseline of a process + what grobyq changed on it. Revert restores ONLY the changed parts, to the captured
        // values: a process whose threads were re-prioritized keeps its class, I/O, cores and EcoQoS untouched.
        private sealed class ProcBefore
        {
            public uint Prio; public ulong Aff; public bool Have; public bool BoostDisabled; public uint Io = 2; public uint Mem = 5;
            public bool EcoKnown; public uint EcoCtl, EcoState;   // EcoQoS before grobyq (when Windows can report it)
            public bool Class, IoSet, MemSet, AffSet, SetsSet, Eco, Boost;
            public bool BgClass, BgIo, BgMem, BgAff;       // set by the background layer only (released on alt-tab)
            public string LastSets = "";                  // the CPU sets written this session (written again only when they change)
        }
        // Thread baseline + what grobyq changed on it (an Ideal-CPU-only thread keeps its priority on revert).
        private sealed class ThrBefore { public int Prio; public bool BoostDisabled; public bool PrioSet, BoostSet; public long Born; }
        private readonly Dictionary<int, ProcBefore> _procBefore = new Dictionary<int, ProcBefore>();
        private readonly Dictionary<string, ThrBefore> _thrBefore = new Dictionary<string, ThrBefore>();

        // Session memory (cleared by Revert): the Ideal CPU each game thread was given - kept for the whole session, so a
        // catch-up pass never moves the busiest threads to other cores - the unnamed hot threads picked, and for how many
        // passes a picked one has been idle (after ~5 its slot is free again; its hint stays, harmless).
        private readonly Dictionary<string, uint> _ideal = new Dictionary<string, uint>();
        private readonly HashSet<string> _hotPick = new HashSet<string>();
        private readonly Dictionary<string, int> _hotIdle = new Dictionary<string, int>();
        private readonly HashSet<int> _gamePids = new HashSet<int>();
        public int GameThreadCount { get; private set; }     // game threads holding a grobyq setting right now

        // live caches
        private class ThrPrevE { public ulong C; public DateTime T; }
        private class ThrMetaE { public string Sym; public string Desc; public long Born; }
        private class ModCacheE { public List<Native.ModRange> M; public DateTime T; }
        private readonly Dictionary<string, ThrPrevE> _thrPrev = new Dictionary<string, ThrPrevE>();
        private readonly Dictionary<string, ThrMetaE> _thrMeta = new Dictionary<string, ThrMetaE>();
        private readonly Dictionary<int, ModCacheE> _modCache = new Dictionary<int, ModCacheE>();
        private readonly HashSet<int> _appliedGame = new HashSet<int>();
        private readonly HashSet<int> _appliedBg = new HashSet<int>();
        private readonly HashSet<string> _adviceOnce = new HashSet<string>();   // F11
        // PID identity: creation time per PID. A reused PID (another process now) drops every baseline and cache of the
        // old one, so nothing is "restored" into, or resolved against, a process grobyq never touched.
        private readonly Dictionary<int, long> _born = new Dictionary<int, long>();

        // Serializes every public mutation/read path (ApplyAll/Apply/Snapshot/Bg*/Revert/Prune). ApplyAll now runs on a
        // background worker (MainForm), while the UI thread still calls Snapshot (live view), BgApply/BgRelease (Tick) and
        // Revert (disable). The lock is reentrant per thread, so ApplyAll's internal Snapshot/Apply nest without deadlock.
        private readonly object _gate = new object();

        // Runs an engine-state mutation (e.g. ThreadIni import replacing Presets/BgClasses from the UI thread) under the
        // same lock ApplyAll holds, so a re-apply cycle on the worker never enumerates those dictionaries mid-rewrite.
        public void UnderLock(Action a) { lock (_gate) a(); }

        // Clears the Lever-3 apply-once cache so the next cycle re-applies every imported-preset rule. Call after the
        // rules themselves change (import / profile load) - otherwise a PID whose thread count is unchanged keeps the
        // priorities from the OLD rules. Reentrant, so it is safe to call from inside an UnderLock block.
        public void InvalidatePresetApplyCache() { lock (_gate) _presetTidCount.Clear(); }

        // Per-cycle thread-id cache: AllThreadIdsByPid() filled once at the top of a re-apply cycle, read by every
        // Snapshot() inside it, cleared when the cycle ends. Null outside a cycle -> Snapshot falls back to a single-PID
        // Native.ThreadIds (live ThreadsPage view). This is Lever 1: N system-wide snapshots per cycle -> one.
        private Dictionary<uint, List<uint>> _tidCache;

        // Lever 3: imported-preset processes (chrome/steam/discord...) get their thread priorities set once per PID, not
        // every cycle. We re-apply only when a PID's thread count changes (a new thread may need the rule). Keyed by PID.
        private readonly Dictionary<int, int> _presetTidCount = new Dictionary<int, int>();

        // Thread ids for a pid: the per-cycle cache when inside ApplyAll, else a fresh single-process snapshot.
        private IEnumerable<uint> TidsFor(uint pid)
        {
            if (_tidCache != null) { List<uint> l; return _tidCache.TryGetValue(pid, out l) ? l : (IEnumerable<uint>)new List<uint>(); }
            return Native.ThreadIds(pid);
        }

        // Mutation paths: record the identity; a changed one forgets the old process first. Returns false if it changed.
        private bool Identity(int pid, IntPtr hp)
        {
            long c = Native.ProcessCreated(hp);
            if (c == 0) return true;
            long old; bool same = !_born.TryGetValue(pid, out old) || old == c;
            if (!same) Forget(pid);
            _born[pid] = c;
            return same;
        }

        // Restore paths: true = still the process whose baseline we hold (or it cannot be told), false = forgotten.
        private bool VerifyPid(int pid)
        {
            int lvl, err;
            IntPtr hp = Native.OpenProcessGraceful((uint)pid, out lvl, out err);
            if (hp == IntPtr.Zero) return true;              // gone / no access: the restore itself will fail harmlessly
            try { return Identity(pid, hp); } finally { Native.CloseHandle(hp); }
        }

        private void Forget(int pid)
        {
            _procBefore.Remove(pid); _appliedGame.Remove(pid); _appliedBg.Remove(pid); _sysPids.Remove(pid); _modCache.Remove(pid); _born.Remove(pid); _presetTidCount.Remove(pid);
            string pre = pid + ":";
            foreach (var k in _thrBefore.Keys.Where(k => k.StartsWith(pre, StringComparison.Ordinal)).ToList()) _thrBefore.Remove(k);
            foreach (var k in _thrMeta.Keys.Where(k => k.StartsWith(pre, StringComparison.Ordinal)).ToList()) _thrMeta.Remove(k);
            foreach (var k in _thrPrev.Keys.Where(k => k.StartsWith(pre, StringComparison.Ordinal)).ToList()) _thrPrev.Remove(k);
            foreach (var k in _ideal.Keys.Where(k => k.StartsWith(pre, StringComparison.Ordinal)).ToList()) _ideal.Remove(k);
            _hotPick.RemoveWhere(k => k.StartsWith(pre, StringComparison.Ordinal));
            foreach (var k in _hotIdle.Keys.Where(k => k.StartsWith(pre, StringComparison.Ordinal)).ToList()) _hotIdle.Remove(k);
            _gamePids.Remove(pid);
        }

        // Baseline record of a thread. A TID reused by a NEW thread of the same process (creation time differs) gets a
        // fresh record - the dead thread's values are never "restored" into it.
        private ThrBefore ThreadBaseline(string key, IntPtr ht)
        {
            long born = Native.ThreadCreated(ht);
            ThrBefore b;
            if (!_thrBefore.TryGetValue(key, out b) || (born != 0 && b.Born != 0 && b.Born != born))
            {
                bool bd; Native.GetThreadPriorityBoost(ht, out bd);
                b = new ThrBefore { Prio = Native.GetThreadPriority(ht), BoostDisabled = bd, Born = born };
                _thrBefore[key] = b;
            }
            return b;
        }

        // The value a field is restored to is read when grobyq FIRST changes that field (an Ideal-CPU-only touch earlier
        // does not freeze a priority the game may still change itself).
        private static void MarkPrio(ThrBefore b, IntPtr ht) { if (!b.PrioSet) { b.Prio = Native.GetThreadPriority(ht); b.PrioSet = true; } }
        private static void MarkBoost(ThrBefore b, IntPtr ht) { if (!b.BoostSet) { bool bd; Native.GetThreadPriorityBoost(ht, out bd); b.BoostDisabled = bd; b.BoostSet = true; } }

        // "All cores" for a restore without a captured mask: never mixes ReservedCpuSets LPs with unreserved ones.
        private static ulong SafeFull()
        {
            ulong full = Topo.Live().FullMask(), res = Reservation.Reserved();
            return Reservation.Mixes(full, res) ? Reservation.Unmix(full, res, false) : full;
        }

        public bool BgOn { get; private set; }
        // something is changed right now (a target, a manual edit, a preset pass, the background layer)
        public bool HasChanges { get { return _appliedGame.Count > 0 || _appliedBg.Count > 0 || _thrBefore.Count > 0 || _procBefore.Count > 0; } }
        public string LiveTarget { get; private set; } = "";
        public int LastThreads { get; private set; }
        public int LastBg { get; private set; }
        public List<ApplyReport> LastReports { get; private set; } = new List<ApplyReport>();

        public string[] Targets;   // watch list; default cs2.exe (spec F2)

        public Engine(string[] targets)
        {
            Targets = (targets != null && targets.Length > 0) ? targets : new[] { "cs2.exe" };
            Dictionary<string, Preset> ps; Dictionary<string, string> bg;
            Store.Load(out ps, out bg);
            Presets = ps; BgClasses = bg;
        }

        // Any save means the rules may have changed (UI rule edits, import, default-list restore): invalidate the
        // Lever-3 apply-once cache so the next cycle re-applies. Cheap (small dict); correctness over micro-optimization.
        public string SavePresets() { lock (_gate) _presetTidCount.Clear(); return Store.Save(Presets, BgClasses); }

        // Module part of a start-address symbol: "engine2.dll+0x1A2B" -> "engine2.dll"
        public static string ModuleOf(string sym)
        {
            if (string.IsNullOrEmpty(sym) || sym.StartsWith("0x") || sym == "n/a") return "";
            int i = sym.IndexOf('+');
            if (i == 0) return "";                           // "+0x1A2B": the module name could not be read = unknown
            return i > 0 ? sym.Substring(0, i) : sym;
        }

        private static bool AcThread(ThreadRow r) { return IsAntiCheat(ModuleOf(r.Sym)); }
        // A thread grobyq may change: its start module is KNOWN and not an anti-cheat one. Fail closed: when the module
        // list is unreadable (VM_READ stripped) or the thread starts outside any module, an injected anti-cheat DLL
        // cannot be ruled out, so the thread is left alone.
        private static bool SafeThread(ThreadRow r) { string m = ModuleOf(r.Sym); return m.Length > 0 && !IsAntiCheat(m); }
        // Protected Windows processes (csrss, lsass, services... are PPL: their module list is never readable) cannot
        // host an injected anti-cheat DLL; there only a KNOWN anti-cheat module is refused. They still need the gate.
        private static bool ThreadOk(ThreadRow r, bool protectedProc) { return protectedProc ? !AcThread(r) : SafeThread(r); }
        private static string ProcName(int pid) { try { return Process.GetProcessById(pid).ProcessName + ".exe"; } catch { return ""; } }

        public static bool RuleHits(ThreadRule rule, string desc, string sym)
        {
            if (rule == null || !rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) return false;
            if (string.Equals(rule.Match, "module", StringComparison.OrdinalIgnoreCase)) return Wild(ModuleOf(sym), rule.Pattern);
            return Wild(desc, rule.Pattern);
        }

        // ---------------- helpers ----------------
        public static bool Wild(string text, string pattern)
        {
            if (text == null) return false;
            string rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private static string Base(string exe) { return exe.ToLowerInvariant().EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe; }
        private static Process[] ByName(string exe) { try { return Process.GetProcessesByName(Base(exe)); } catch { return new Process[0]; } }

        // A process by PID and name, no handle. Inside an apply cycle every lookup reads ONE process snapshot taken at the
        // start of the cycle (a lookup per name was a full system snapshot each - ~90 per pass, heavier with every browser
        // process and thread); outside a cycle it asks Windows directly.
        private struct Pn { public int Id; public string Name; }
        private Dictionary<string, List<Pn>> _pidCache;

        private List<Pn> Procs(string exe)
        {
            string key = Base(exe);
            if (_pidCache != null) { List<Pn> l; return _pidCache.TryGetValue(key, out l) ? l : new List<Pn>(); }
            var res = new List<Pn>();
            foreach (var p in ByName(exe)) { try { res.Add(new Pn { Id = p.Id, Name = p.ProcessName }); } catch { } finally { try { p.Dispose(); } catch { } } }
            return res;
        }

        private void FillPidCache()
        {
            var d = new Dictionary<string, List<Pn>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string n = p.ProcessName;
                        List<Pn> l;
                        if (!d.TryGetValue(n, out l)) { l = new List<Pn>(); d[n] = l; }
                        l.Add(new Pn { Id = p.Id, Name = n });
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { d = null; }
            _pidCache = d;
        }

        // ---- starvation guard (on by default) ----
        // The processes a game waits on - Windows' protected ones (DWM, csrss, services...) and Steam (the game's Steam API
        // calls are IPC round trips to steam.exe) - never go below "Below normal": under the game, above the lowered
        // background apps. A thread at Idle (priority 1) loses every contest to a busy background app (an Idle-class
        // browser runs at 4-6) and gets the CPU only when Windows' starvation boost lifts it after ~4 s: a game waiting
        // on it freezes for seconds.
        public static readonly HashSet<string> Companions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "steam", "steamservice", "steamwebhelper", "gameoverlayui" };
        public static bool StarveGuard { get { return Settings.GetBool("StarveGuard"); } }
        public static bool Guarded(string exe) { return StarveGuard && (IsProtected(exe) || Companions.Contains(BareName(exe))); }

        private void AdviceOnce(List<string> log, string key, string msg)
        {
            if (_adviceOnce.Contains(key)) return;
            _adviceOnce.Add(key);
            log.Add(msg);
        }

        // ---------------- thread scan ----------------
        private List<Native.ModRange> Modules(int pid)
        {
            ModCacheE e;
            if (_modCache.TryGetValue(pid, out e) && (DateTime.UtcNow - e.T).TotalSeconds < 60) return e.M;
            int lvl, err;
            IntPtr hp = Native.OpenProcessGraceful((uint)pid, out lvl, out err);
            var mods = new List<Native.ModRange>();
            if (hp != IntPtr.Zero) { try { if (lvl >= 2) mods = Native.Modules(hp); } catch { } finally { Native.CloseHandle(hp); } }
            _modCache[pid] = new ModCacheE { M = mods, T = DateTime.UtcNow };
            return mods;
        }

        private static string Resolve(ulong sa, List<Native.ModRange> mods)
        {
            if (sa == 0) return "n/a";
            foreach (var m in mods) if (sa >= m.Base && sa < m.End) return m.Name + "+0x" + (sa - m.Base).ToString("X");
            return "0x" + sa.ToString("X");
        }
        private double ModulesAge(int pid) { ModCacheE e; return _modCache.TryGetValue(pid, out e) ? (DateTime.UtcNow - e.T).TotalSeconds : double.MaxValue; }

        public sealed class ThreadRow { public uint Tid; public ulong Rate; public int Prio; public string Sym; public string Desc; }
        public sealed class ThreadSnap { public bool Ok; public int Err; public int Level; public List<ThreadRow> Rows = new List<ThreadRow>(); }

        public ThreadSnap Snapshot(int pid) { lock (_gate) return SnapshotCore(pid); }
        private ThreadSnap SnapshotCore(int pid)
        {
            int lvl, err;
            IntPtr probe = Native.OpenProcessGraceful((uint)pid, out lvl, out err);
            if (probe == IntPtr.Zero) return new ThreadSnap { Ok = false, Err = err, Level = -1 };
            try { Identity(pid, probe); } finally { Native.CloseHandle(probe); }

            List<Native.ModRange> mods = null;
            bool reread = false;
            DateTime now = DateTime.UtcNow;
            var rows = new List<ThreadRow>();

            foreach (uint tid in TidsFor((uint)pid))
            {
                IntPtr ht = Native.OpenThreadOf(pid, tid);
                if (ht == IntPtr.Zero) continue;
                try
                {
                    ulong c;
                    if (!Native.QueryThreadCycleTime(ht, out c)) continue;
                    string key = pid + ":" + tid;
                    ThrMetaE meta;
                    long tborn = Native.ThreadCreated(ht);
                    if (_thrMeta.TryGetValue(key, out meta) && tborn != 0 && meta.Born != 0 && meta.Born != tborn)
                    { _thrMeta.Remove(key); _thrPrev.Remove(key); meta = null; }        // a new thread with a reused TID
                    if (meta == null)
                    {
                        if (mods == null) mods = Modules(pid);
                        ulong sa = Native.StartAddress(ht);
                        string sym = Resolve(sa, mods);
                        // a start address outside the cached list: a module loaded after the list was read - re-read once
                        if (sa != 0 && ModuleOf(sym).Length == 0 && !reread && ModulesAge(pid) > 5)
                        {
                            reread = true; _modCache.Remove(pid); mods = Modules(pid); sym = Resolve(sa, mods);
                        }
                        meta = new ThrMetaE { Sym = sym, Desc = Native.ThreadDesc(ht), Born = tborn };
                        if (ModuleOf(sym).Length > 0) _thrMeta[key] = meta;   // only a resolved symbol is cached
                    }
                    else if (string.IsNullOrEmpty(meta.Desc)) meta.Desc = Native.ThreadDesc(ht);   // named after the first look
                    ulong rate = 0;
                    ThrPrevE pv;
                    if (_thrPrev.TryGetValue(key, out pv))
                    {
                        double dt = (now - pv.T).TotalSeconds;
                        if (dt > 0.05 && c >= pv.C) rate = (ulong)((c - pv.C) / dt);
                    }
                    _thrPrev[key] = new ThrPrevE { C = c, T = now };
                    rows.Add(new ThreadRow { Tid = tid, Rate = rate, Prio = Native.GetThreadPriority(ht), Sym = meta.Sym, Desc = meta.Desc });
                }
                finally { Native.CloseHandle(ht); }
            }
            return new ThreadSnap { Ok = true, Level = lvl, Rows = rows.OrderByDescending(r => r.Rate).ToList() };
        }

        // ---------------- apply ----------------
        public List<string> Apply(string target, ApplyOptions o, ApplyReport report)
        {
            var log = new List<string>();
            string gate = Gate(target, Op.Process, ProtectedPriorityAllowed);
            if (gate != null) { AdviceOnce(log, "gate:" + target, gate); return log; }
            Native.EnableDebugPrivilege();
            int topN = o.TopN <= 0 ? 4 : o.TopN;
            bool adv = AdvancedThreadMode;

            Topo topo = Topo.Live();
            if (topo.Groups > 1)
                AdviceOnce(log, "groups", string.Format("[!!] {0} processor groups detected. A 64-bit mask covers group 0 only - CPU Sets are the source of truth.", topo.Groups));

            MaskResult mask = Masks.Build(topo, o.MaskName);

            // per-process affinity rule overrides the global mask. A portable rule
            // (VCache+NoSMT, P+NoCore0, ...) is resolved on this CPU; a hex rule is clamped.
            string tkey = target;
            bool overridden = false;
            if (AffRules.ContainsKey(tkey))
            {
                string err;
                var rr = Masks.Resolve(topo, AffRules[tkey], out err);
                if (rr == null)
                    AdviceOnce(log, "afbad:" + target + AffRules[tkey], string.Format("[AF] {0}: rule '{1}' {2}, global mask kept", target, AffRules[tkey], err));
                else
                {
                    mask = rr; overridden = true;
                    AdviceOnce(log, "afrule:" + target + rr.Spec + rr.Hex, string.Format("[AF] {0}: affinity rule {1} -> {2} LP {3} overrides the global mask", target, rr.Spec, rr.Hex, rr.LPs));
                    // adaptive: interrupt cores leave the game's own rule too (same as ApplyAffRules for targets)
                    ulong idr; ulong adj = GameMask.ForRule(topo, rr.Mask, out idr);
                    if (idr != 0)
                    {
                        var am = Masks.FromValue(topo, adj, rr.Name);
                        am.Spec = rr.Spec; am.Portable = rr.Portable; am.Satisfied = rr.Satisfied; am.Notes = rr.Notes;
                        mask = am;
                        AdviceOnce(log, "afirq:" + target + adj, string.Format("[AF] {0}: rule {1} minus interrupt LP {2} -> {3} LP {4}", target, rr.Hex, Masks.Ranges(idr), Masks.Hex(adj), Masks.Ranges(adj)));
                    }
                }
            }
            if (!overridden)
            {
                // game set: say once what it resolved to on this CPU (family, both sets)
                var gs = Masks.TryParseSpec(o.MaskName);
                if (gs != null && gs.Base == "GameSet" && topo.Split.CanSplit)
                {
                    var sp = topo.Split;
                    AdviceOnce(log, "gameset:" + target + mask.Hex, string.Format("[AF] {0} game set {1} LP {2} / non-game {3} (family {4}, {5})",
                        target, mask.Hex, Masks.Ranges(mask.Mask), Masks.Hex(sp.NonGame), sp.Family, sp.Label));
                }
            }

            // ReservedCpuSets guard: an affinity never mixes reserved and unreserved LPs. The game keeps its unreserved part.
            ulong reserved = Reservation.Reserved();
            if (Reservation.Mixes(mask.Mask, reserved))
            {
                ulong kept = Reservation.Unmix(mask.Mask, reserved, false);
                AdviceOnce(log, "resv:" + target + mask.Hex + reserved, string.Format("[AF] {0}: mask {1} mixes reserved LP {2} with unreserved LP - reserved LPs dropped, {3} LP {4} (ReservedCpuSets guard)",
                    target, mask.Hex, Masks.Ranges(mask.Mask & reserved), Masks.Hex(kept), Masks.Ranges(kept)));
                var um = Masks.FromValue(topo, kept, mask.Name);
                um.Spec = mask.Spec; um.Portable = mask.Portable; um.Satisfied = mask.Satisfied; um.Notes = mask.Notes;
                mask = um;
            }

            // keyed by message: one key per mask swallowed every line after the first
            foreach (var m in Masks.Advice(topo, mask)) AdviceOnce(log, "advice:" + m, m);

            Preset preset; Presets.TryGetValue(target, out preset);
            string cls = preset != null ? preset.Class : "High";
            string io = preset != null ? preset.Io : "Normal";
            string mem = preset != null ? preset.Mem : "Normal";
            if (!PRIO.ContainsKey(cls) || string.Equals(cls, "Realtime", StringComparison.OrdinalIgnoreCase)) cls = "High";   // never Realtime
            if (!IOP.ContainsKey(io) || string.Equals(io, "Critical", StringComparison.OrdinalIgnoreCase)) io = "Normal";
            if (!MEM.ContainsKey(mem)) mem = "Normal";
            bool ecoOff = preset == null || !string.Equals(preset.Eco, "Keep", StringComparison.OrdinalIgnoreCase);
            string pboost = preset != null ? preset.Boost ?? "Keep" : "Keep";

            report.Target = target;
            report.MaskHex = mask.Hex;
            report.MaskLps = mask.LPs;

            int nThread = 0;
            foreach (var p in Procs(target))
            {
                int lvl, err;
                IntPtr hp = Native.OpenProcessGraceful((uint)p.Id, out lvl, out err);
                if (hp == IntPtr.Zero)
                {
                    report.AccessDenied = true;
                    AdviceOnce(log, "deny:" + p.Id, string.Format("[!!] PID {0}: access denied, err {1}. Expected on titles with kernel anti-cheat.", p.Id, err));
                    continue;
                }
                report.Pid = p.Id; report.HandleLevel = lvl;
                try
                {
                    // F7: snapshot "before" on first touch; nothing is set without a captured baseline
                    Identity(p.Id, hp);
                    bool fresh = !_procBefore.ContainsKey(p.Id);
                    if (fresh) _procBefore[p.Id] = Before(hp);
                    Debug.Assert(_procBefore[p.Id].Have, "process baseline must be captured before any set");
                    _gamePids.Add(p.Id);

                    var pb = _procBefore[p.Id];
                    // v4 manual mode: only the parts the user switched on are written (and only those get restored).
                    //   DoProcess = class / I/O / memory priority / EcoQoS off / process boost (preset)
                    //   DoMask    = process affinity + default CPU sets (the game core mask)
                    // Every value is read back first and written only when it differs: a catch-up pass over a running game
                    // changes nothing that is already right (no re-set affinity / CPU sets = no thread migrations).
                    bool okPrio = true, okMem = true, okEco = true, okAff = true, okSets = true, changed = false;
                    int stIo = 0;
                    if (o.DoProcess)
                    {
                        pb.Class = pb.IoSet = pb.MemSet = true;
                        if (Native.GetPriorityClass(hp) != PRIO[cls]) { okPrio = Native.SetPriorityClass(hp, PRIO[cls]); changed = true; }
                        if (Native.GetProcIo(hp) != IOP[io]) { stIo = Native.SetProcIo(hp, IOP[io]); changed = true; }
                        if (Native.GetMemPrio(hp) != MEM[mem]) { okMem = Native.SetMemPrio(hp, MEM[mem], false); changed = true; }
                        if (ecoOff)
                        {
                            pb.Eco = true;
                            uint ec, es;
                            bool already = Native.GetPowerThrottling(hp, out ec, out es) && (ec & 1) != 0 && (es & 1) == 0;
                            if (!already) { okEco = Native.SetPowerThrottling(hp, false); changed = true; }
                        }
                        bool wantOff = string.Equals(pboost, "Off", StringComparison.OrdinalIgnoreCase), wantOn = string.Equals(pboost, "On", StringComparison.OrdinalIgnoreCase);
                        if (wantOff || wantOn)
                        {
                            pb.Boost = true;
                            bool disabled; Native.GetProcessPriorityBoost(hp, out disabled);
                            if (disabled != wantOff) { Native.SetProcessPriorityBoost(hp, wantOff); changed = true; }
                        }
                    }
                    uint rbClass = Native.GetPriorityClass(hp);
                    if (o.DoMask)
                    {
                        pb.AffSet = pb.SetsSet = true;
                        if (Native.GetProcAffinity(hp) != mask.Mask) { okAff = Native.SetProcAffinity(hp, mask.Mask); changed = true; }
                        string sets = string.Join(",", mask.SetIds);
                        if (!string.Equals(pb.LastSets, sets, StringComparison.Ordinal))
                        {
                            okSets = Native.SetProcessDefaultCpuSets(hp, mask.SetIds, (uint)mask.SetIds.Length);
                            if (okSets) pb.LastSets = sets;
                            changed = true;
                        }
                    }
                    ulong eff = Native.GetProcAffinity(hp);

                    _appliedGame.Add(p.Id);
                    if (fresh || changed)
                    {
                        string rbName; PRIONAME.TryGetValue((int)rbClass, out rbName);
                        log.Add(string.Format("[OK] {0} PID {1} L{2} class={3} (read-back {4}) io={5} mem={6} mask=0x{7:X} LP {8}{9}",
                            target, p.Id, lvl, o.DoProcess ? cls : "keep", rbName ?? "?", o.DoProcess ? io : "keep", o.DoProcess ? mem : "keep",
                            eff, Masks.MaskLps(eff), o.DoMask ? "" : " (mask not set)"));
                    }
                    if (!okPrio) { log.Add("     ! SetPriorityClass failed"); report.Failures.Add("SetPriorityClass"); }
                    if (!okAff)  { log.Add("     ! SetProcessAffinityMask failed"); report.Failures.Add("SetProcessAffinityMask"); }
                    if (!okSets) { log.Add("     ! SetProcessDefaultCpuSets failed"); report.Failures.Add("SetProcessDefaultCpuSets"); }
                    if (!okEco)  { log.Add("     ! ProcessPowerThrottling failed"); report.Failures.Add("PowerThrottling"); }
                    if (!okMem)  { log.Add("     ! MemoryPriority failed"); report.Failures.Add("MemoryPriority"); }
                    if (stIo != 0) { log.Add(string.Format("     ! IoPriority NTSTATUS 0x{0:X8}", stIo)); report.Failures.Add("IoPriority"); }
                }
                finally { Native.CloseHandle(hp); }

                if (o.DoThreads)
                {
                    if (o.Warmup) { Snapshot(p.Id); Thread.Sleep(700); }
                    var snap = Snapshot(p.Id);
                    if (!snap.Ok || snap.Rows.Count == 0) { AdviceOnce(log, "nothr:" + p.Id, "[!!] threads not accessible"); }
                    else
                    {
                        var cand = Masks.IdealCandidates(topo, o.SkipCpu0, mask.Mask);
                        var done = new HashSet<uint>();
                        string pre = p.Id + ":";
                        var alive = new HashSet<string>(snap.Rows.Select(r => pre + r.Tid));
                        // LPs already given to live threads of this game: a new thread gets a free one (no two hot threads
                        // share an Ideal CPU while there are candidates left)
                        var usedLps = new HashSet<uint>(_ideal.Where(kv => alive.Contains(kv.Key)).Select(kv => kv.Value));
                        int ci = 0;
                        Func<uint> nextLp = () =>
                        {
                            for (int k = 0; k < cand.Count; k++)
                            {
                                uint lp = cand[(ci + k) % cand.Count].LP;
                                if (!usedLps.Contains(lp)) { ci += k + 1; usedLps.Add(lp); return lp; }
                            }
                            uint wrap = cand[ci % cand.Count].LP; ci++;
                            return wrap;
                        };

                        // threads that start in an anti-cheat module are never touched (named rules and hot threads)
                        foreach (var ac in snap.Rows.Select(r => ModuleOf(r.Sym)).Where(IsAntiCheat).Distinct(StringComparer.OrdinalIgnoreCase))
                            AdviceOnce(log, "acmod:" + target + ac, string.Format("[THR] anti-cheat target skipped: {0} (threads in {1})", ac, target));
                        if (preset != null && preset.Threads.Count > 0)
                        {
                            foreach (var rule in preset.Threads.Where(x => x.Enabled && !string.IsNullOrEmpty(x.Pattern)))
                            {
                                string rname = (rule.Match == "module" ? "mod:" : "") + rule.Pattern;
                                if (IsAntiCheat(rule.Pattern)) { AdviceOnce(log, "acrule:" + target + rname, "[THR] anti-cheat target skipped: " + rule.Pattern); continue; }
                                var hits = snap.Rows.Where(r => RuleHits(rule, r.Desc, r.Sym) && SafeThread(r)).ToList();
                                if (hits.Count == 0) { AdviceOnce(log, "nomatch:" + target + rname, string.Format("[TH] rule {0} - no match", rname)); continue; }
                                hits = FilterTid(hits, rule.Tid);
                                foreach (var r in hits)
                                {
                                    if (done.Contains(r.Tid)) continue;
                                    IntPtr ht = Native.OpenThreadOf(p.Id, r.Tid);
                                    if (ht == IntPtr.Zero) continue;
                                    try
                                    {
                                        string tkey2 = pre + r.Tid;
                                        var tb = ThreadBaseline(tkey2, ht);
                                        Debug.Assert(_thrBefore.ContainsKey(tkey2), "thread baseline must be captured before any set");
                                        var acts = new List<string>();
                                        int fixedLp; uint had;
                                        // Ideal CPU: given once per thread for the session (a later pass keeps it)
                                        if (rule.Ideal == "auto")
                                        {
                                            if (!_ideal.TryGetValue(tkey2, out had))
                                            {
                                                uint lp = nextLp();
                                                Native.SetThreadIdealProcessor(ht, lp);
                                                _ideal[tkey2] = lp;
                                                acts.Add("idealCPU=" + lp);
                                            }
                                        }
                                        else if (int.TryParse(rule.Ideal, out fixedLp) && fixedLp >= 0 && fixedLp < topo.LogicalCount)
                                        {
                                            if (!_ideal.TryGetValue(tkey2, out had) || had != (uint)fixedLp)
                                            {
                                                Native.SetThreadIdealProcessor(ht, (uint)fixedLp);
                                                _ideal[tkey2] = (uint)fixedLp;
                                                acts.Add("idealCPU=" + fixedLp + " (fixed)");
                                            }
                                        }
                                        int tp;
                                        if (ThreadPrio(rule.Prio, adv, out tp))
                                        {
                                            MarkPrio(tb, ht);
                                            if (Native.GetThreadPriority(ht) != tp)
                                                acts.Add(Native.SetThreadPriority(ht, tp) ? "prio=" + rule.Prio + (tp == TimeCriticalValue ? " (15, Advanced)" : "") : "prio=FAIL");
                                        }
                                        bool wantOff = string.Equals(rule.Boost, "Off", StringComparison.OrdinalIgnoreCase), wantOn = string.Equals(rule.Boost, "On", StringComparison.OrdinalIgnoreCase);
                                        if (wantOff || wantOn)
                                        {
                                            MarkBoost(tb, ht);
                                            bool disabled; Native.GetThreadPriorityBoost(ht, out disabled);
                                            if (disabled != wantOff) acts.Add(Native.SetThreadPriorityBoost(ht, wantOff) ? (wantOff ? "boost=off" : "boost=on") : "boost=FAIL");
                                        }
                                        done.Add(r.Tid); nThread++; report.NamedHits++;
                                        if (acts.Count > 0) log.Add(string.Format("[TH] {0,-24} TID {1,-7} {2,12} c/s  {3}", rname, r.Tid, r.Rate, string.Join(" ", acts)));
                                    }
                                    finally { Native.CloseHandle(ht); }
                                }
                            }
                        }

                        // unnamed hot threads need a KNOWN, non-anti-cheat start module (fail closed: an unreadable module
                        // list - VM_READ stripped - could hide an injected anti-cheat DLL)
                        if (snap.Rows.Count > 0 && snap.Rows.All(r => ModuleOf(r.Sym).Length == 0))
                            AdviceOnce(log, "nomods:" + target, string.Format("[THR] {0}: thread modules unreadable - thread rules and hot threads skipped (an anti-cheat module cannot be ruled out); process priority and cores still apply", target));

                        // Hot threads, sticky for the session: a picked thread keeps its Ideal CPU and priority; only threads
                        // without one can be picked, up to the hot-thread count. A picked thread that stays idle for ~5
                        // passes frees its slot (its hint stays - harmless), so the pick follows menu -> match without
                        // reshuffling the threads that are busy right now.
                        ulong top = snap.Rows.Count > 0 ? snap.Rows[0].Rate : 0;
                        foreach (var k in _hotPick.Where(x => x.StartsWith(pre, StringComparison.Ordinal)).ToList())
                        {
                            if (!alive.Contains(k)) { _hotPick.Remove(k); _hotIdle.Remove(k); continue; }
                            var row = snap.Rows.First(r => pre + r.Tid == k);
                            bool idle = top > 0 && row.Rate < top / 100;
                            int n2; _hotIdle.TryGetValue(k, out n2);
                            n2 = idle ? n2 + 1 : 0;
                            if (n2 >= 5) { _hotPick.Remove(k); _hotIdle.Remove(k); } else _hotIdle[k] = n2;
                        }
                        int picked = _hotPick.Count(x => x.StartsWith(pre, StringComparison.Ordinal));
                        int slots = Math.Max(1, cand.Count - done.Count);
                        int effTopN = Math.Min(topN, slots);
                        if (effTopN < topN)
                            AdviceOnce(log, "topn:" + target, string.Format("[i ] Hot threads {0} -> {1}: only {2} candidate cores, {3} taken by named rules", topN, effTopN, cand.Count, done.Count));
                        int room = Math.Max(0, effTopN - picked);
                        var hot = room == 0 ? new List<ThreadRow>() :
                            snap.Rows.Where(r => !done.Contains(r.Tid) && SafeThread(r) && !_hotPick.Contains(pre + r.Tid) && !_ideal.ContainsKey(pre + r.Tid) && r.Rate > 0).Take(room).ToList();
                        bool first = done.Count == 0 && picked == 0;
                        foreach (var r in hot)
                        {
                            IntPtr ht = Native.OpenThreadOf(p.Id, r.Tid);
                            if (ht == IntPtr.Zero) continue;
                            try
                            {
                                string tkey2 = pre + r.Tid;
                                var tb = ThreadBaseline(tkey2, ht);
                                uint lp = nextLp();
                                Native.SetThreadIdealProcessor(ht, lp);
                                _ideal[tkey2] = lp;
                                _hotPick.Add(tkey2); _hotIdle[tkey2] = 0;
                                string ptxt = "-";
                                string want = first ? (preset != null ? preset.HotFirst : "AboveNormal") : (preset != null ? preset.HotOthers : "");
                                int wp;
                                if (ThreadPrio(want, adv, out wp)) { MarkPrio(tb, ht); if (Native.GetThreadPriority(ht) == wp || Native.SetThreadPriority(ht, wp)) ptxt = want; }
                                first = false;
                                report.CycleFallbackTids.Add((int)r.Tid);
                                log.Add(string.Format("[TH] {0,-24} TID {1,-7} {2,12} c/s  idealCPU={3} prio={4} {5}", "(by cycles)", r.Tid, r.Rate, lp, ptxt, r.Sym));
                            }
                            finally { Native.CloseHandle(ht); }
                        }
                        nThread += _hotPick.Count(x => x.StartsWith(pre, StringComparison.Ordinal));
                    }
                }
            }
            LastThreads = nThread;
            return log;
        }

        // ---------------- affinity-only rules ----------------
        public List<string> ApplyAffRules()
        {
            var log = new List<string>();
            if (AffRules.Count == 0) return log;
            Topo topo = Topo.Live();
            foreach (var exe in AffRules.Keys.ToList())
            {
                string gate = Gate(exe, Op.Affinity, ProtectedPriorityAllowed);
                if (gate != null) { AdviceOnce(log, "gate:af:" + exe, gate); continue; }
                string why;
                var rr = Masks.Resolve(topo, AffRules[exe], out why);
                if (rr == null) { AdviceOnce(log, "afbad:" + exe + AffRules[exe], string.Format("[AF] {0}: rule '{1}' {2}, skipped", exe, AffRules[exe], why)); continue; }
                foreach (var n in rr.Notes) AdviceOnce(log, "afnote:" + exe + n, string.Format("[AF] {0}: {1}, using {2}", exe, n, rr.Hex));
                ulong m = rr.Mask;
                bool target = Targets != null && Targets.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
                if (target) { ulong idr; m = GameMask.ForRule(topo, m, out idr); }     // the game's rule is adaptive too (Apply does the same)
                // ReservedCpuSets guard: targets keep the unreserved part, helpers (voice, VPN) the reserved part
                ulong reserved = Reservation.Reserved();
                if (Reservation.Mixes(m, reserved))
                {
                    ulong kept = Reservation.Unmix(m, reserved, !target);
                    AdviceOnce(log, "resvrule:" + exe + m + reserved, string.Format("[AF] {0}: rule {1} mixes reserved and unreserved LP - using {2} LP {3} (ReservedCpuSets guard)", exe, rr.Hex, Masks.Hex(kept), Masks.Ranges(kept)));
                    m = kept;
                }
                foreach (var p in Procs(exe))
                {
                    int lvl, err;
                    IntPtr hp = Native.OpenProcessGraceful((uint)p.Id, out lvl, out err);
                    if (hp == IntPtr.Zero) { AdviceOnce(log, "afdeny:" + p.Id, string.Format("[AF] {0} PID {1}: access denied", exe, p.Id)); continue; }
                    try
                    {
                        Identity(p.Id, hp);
                        if (!_procBefore.ContainsKey(p.Id)) _procBefore[p.Id] = Before(hp);
                        Debug.Assert(_procBefore[p.Id].Have, "process baseline must be captured before any set");
                        _procBefore[p.Id].AffSet = true;
                        bool ok = Native.SetProcAffinity(hp, m);
                        ulong eff = Native.GetProcAffinity(hp);
                        _appliedGame.Add(p.Id);
                        log.Add(string.Format("[AF] {0,-24} PID {1,-6} mask 0x{2:X} -> 0x{3:X}  LP {4}  {5}", exe, p.Id, m, eff, Masks.MaskLps(eff), ok ? "ok" : "FAILED"));
                    }
                    finally { Native.CloseHandle(hp); }
                }
            }
            return log;
        }

        // A removed per-process rule: the running process gets its captured cores back now (not only on DISABLE).
        public List<string> ReleaseAffinity(string exe)
        {
            var log = new List<string>();
            foreach (var p in ByName(exe))
            {
                ProcBefore b;
                if (!VerifyPid(p.Id)) continue;
                if (!_procBefore.TryGetValue(p.Id, out b) || !b.Have || !b.AffSet) continue;
                int lvl, err;
                IntPtr hp = Native.OpenProcessGraceful((uint)p.Id, out lvl, out err);
                if (hp == IntPtr.Zero) continue;
                try
                {
                    Native.SetProcAffinity(hp, b.Aff != 0 ? b.Aff : SafeFull());
                    if (b.SetsSet) { Native.SetProcessDefaultCpuSets(hp, null, 0); b.SetsSet = false; }
                    b.AffSet = false;
                    log.Add(string.Format("[AF] {0} PID {1}: cores restored to 0x{2:X}", exe, p.Id, Native.GetProcAffinity(hp)));
                }
                finally { Native.CloseHandle(hp); }
            }
            return log;
        }

        // ---------------- background layer ----------------
        // user = the user's background list (game optimizer), sys = the built-in profile's list (system optimizer): one pass,
        // one baseline per process even when a process is on both lists.
        public List<string> BgApply(bool bgAffinity, string maskName, bool user, bool sys) { lock (_gate) return BgApplyCore(bgAffinity, maskName, user, sys); }
        private List<string> BgApplyCore(bool bgAffinity, string maskName, bool user, bool sys)
        {
            var log = new List<string>();
            if (Targets == null) return log;
            var list = (user ? Settings.BgProcs() : new string[0]).Concat(sys ? SystemBg.Keys.ToArray() : new string[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (list.Length == 0) return log;

            Topo topo = Topo.Live();
            var targets = new HashSet<string>(Targets, StringComparer.OrdinalIgnoreCase);
            var mask = Masks.Build(topo, maskName);
            ulong bgMask = Masks.BgMask(topo, mask.Mask);
            // ReservedCpuSets guard: background keeps its reserved part when the free cores straddle the reservation
            ulong reserved = Reservation.Reserved();
            if (Reservation.Mixes(bgMask, reserved)) bgMask = Reservation.Unmix(bgMask, reserved, true);
            int n = 0;
            foreach (var b in list)
            {
                string bn = Base(b);
                string gate = Gate(b, Op.Process, ProtectedPriorityAllowed);
                if (gate != null) { AdviceOnce(log, "gate:bg:" + bn, gate); continue; }
                if (targets.Contains(b) || targets.Contains(bn + ".exe")) continue;   // a game in the background list is still the game
                foreach (var bp in Procs(b))
                {
                    int lvl, err;
                    IntPtr hp = Native.OpenProcessGraceful((uint)bp.Id, out lvl, out err);
                    if (hp == IntPtr.Zero) continue;
                    try
                    {
                        Identity(bp.Id, hp);
                        if (!_procBefore.ContainsKey(bp.Id)) _procBefore[bp.Id] = Before(hp);
                        Debug.Assert(_procBefore[bp.Id].Have, "process baseline must be captured before any set");
                        var bb = _procBefore[bp.Id];
                        bb.BgClass = bb.BgIo = bb.BgMem = true;
                        string clsB = "BelowNormal";
                        if (BgClasses.ContainsKey(b)) clsB = BgClasses[b];
                        else if (BgClasses.ContainsKey(bn + ".exe")) clsB = BgClasses[bn + ".exe"];
                        else if (SystemBg.ContainsKey(b)) clsB = SystemBg[b];
                        uint clsV;
                        if (!PRIO.TryGetValue(clsB, out clsV) || string.Equals(clsB, "Realtime", StringComparison.OrdinalIgnoreCase) || string.Equals(clsB, "High", StringComparison.OrdinalIgnoreCase))
                        { clsB = "BelowNormal"; clsV = PRIO[clsB]; }
                        // starvation guard: a process the game waits on (Steam) steps aside only to Below normal, with normal
                        // I/O and memory priority - never under the background apps, never paged out first
                        bool guarded = Guarded(b);
                        if (guarded && string.Equals(clsB, "Idle", StringComparison.OrdinalIgnoreCase))
                        {
                            clsB = "BelowNormal"; clsV = PRIO[clsB];
                            AdviceOnce(log, "guardbg:" + bn, string.Format("[BG] {0}: Below normal instead of Idle (starvation guard: the game waits on it)", bn));
                        }
                        uint ioV = guarded ? IOP["Normal"] : IOP["VeryLow"], memV = guarded ? MEM["Normal"] : MEM["Low"];
                        bool bgChanged = false;
                        if (Native.GetPriorityClass(hp) != clsV) { Native.SetPriorityClass(hp, clsV); bgChanged = true; }
                        if (Native.GetProcIo(hp) != ioV) { Native.SetProcIo(hp, ioV); bgChanged = true; }
                        if (Native.GetMemPrio(hp) != memV) { Native.SetMemPrio(hp, memV, false); bgChanged = true; }
                        string mtxt = "mask untouched";
                        bool ruled = AffRules.ContainsKey(b) || AffRules.ContainsKey(bn + ".exe");      // a per-process rule wins
                        if (bgAffinity && !ruled)
                        {
                            bb.BgAff = true; mtxt = "mask=0x" + bgMask.ToString("X");
                            if (Native.GetProcAffinity(hp) != bgMask) { Native.SetProcAffinity(hp, bgMask); bgChanged = true; }
                        }
                        else if (bb.BgAff && !bb.AffSet)
                        {
                            // pinning switched off (or a Baseline template): the pinned process gets its own cores back
                            Native.SetProcAffinity(hp, bb.Aff != 0 ? bb.Aff : SafeFull());
                            bb.BgAff = false; mtxt = "cores restored"; bgChanged = true;
                        }
                        else if (ruled) mtxt = "mask = its affinity rule";
                        _appliedBg.Add(bp.Id); n++;
                        if (bgChanged) log.Add(string.Format("[BG] {0,-22} PID {1,-6} {2} / io {3} / {4}", bp.Name, bp.Id, clsB, guarded ? "Normal" : "VeryLow", mtxt));
                    }
                    finally { Native.CloseHandle(hp); }
                }
            }
            LastBg = n; BgOn = true;
            return log;
        }

        public List<string> BgRelease() { lock (_gate) return BgReleaseCore(); }
        private List<string> BgReleaseCore()
        {
            var log = new List<string>();
            if (_appliedBg.Count == 0) { BgOn = false; return log; }
            int n = 0;
            foreach (int id in _appliedBg.ToList())
            {
                if (!VerifyPid(id)) continue;                 // the PID belongs to another process now
                int lvl, err;
                IntPtr hp = Native.OpenProcessGraceful((uint)id, out lvl, out err);
                if (hp == IntPtr.Zero) continue;
                try
                {
                    if (RestoreBg(hp, id)) n++;
                }
                catch { } finally { Native.CloseHandle(hp); }
            }
            _appliedBg.Clear(); LastBg = 0; BgOn = false;
            log.Add(string.Format("[BG] released {0} background processes (game lost focus)", n));
            return log;
        }

        public bool GameForeground()
        {
            uint fg = Native.ForegroundPid();
            if (fg == 0) return false;
            try
            {
                var p = Process.GetProcessById((int)fg);
                string nm = (p.ProcessName + ".exe").ToLowerInvariant();
                return Targets.Any(t => t.ToLowerInvariant() == nm);
            }
            catch { return false; }
        }

        // Disposes the Process[] it enumerates (GetProcessesByName leaks a handle per object otherwise).
        private int RunningCount(string exe) { return Procs(exe).Count; }

        public List<string> ApplyAll(ApplyOptions o)
        {
            lock (_gate)
            {
                _tidCache = Native.AllThreadIdsByPid();   // Lever 1: one system-wide thread snapshot shared by the whole cycle
                FillPidCache();                           // ...and one process snapshot
                try { return ApplyAllCore(o); }
                finally { _tidCache = null; _pidCache = null; }
            }
        }

        private List<string> ApplyAllCore(ApplyOptions o)
        {
            var log = new List<string>();
            LastReports = new List<ApplyReport>();
            var found = new List<string>();
            foreach (var t in Targets)
            {
                if (RunningCount(t) == 0) continue;
                found.Add(t);
                if (!o.Game) continue;                        // system optimizer only: games are detected, never touched
                var report = new ApplyReport();
                foreach (var l in Apply(t, o, report)) log.Add(l);
                LastReports.Add(report);
            }
            LiveTarget = string.Join(", ", found);

            if (o.Game && o.DoAffRules) foreach (var l in ApplyAffRules()) log.Add(l);
            if ((o.Game && Settings.GetBool("ApplyAllPresets")) || (o.Sys && SystemPresets.Count > 0)) foreach (var l in ApplyImportedPresets(o)) log.Add(l);
            UpdateSysStats();
            UpdateGameStats();

            if (found.Count == 0)
            {
                if (BgOn) foreach (var l in BgRelease()) log.Add(l);
                if (o.Game) log.Add("[--] no game running - armed and waiting");
                return log;
            }

            // background: the user's list (game optimizer + its switch) and the built-in profile's list (system optimizer).
            // ApplyAll runs only inside a game session (Optimizer), which already encodes "the game is in front".
            bool userBg = o.Game && o.DoBackground, sysBg = o.Sys && SystemBg.Count > 0;
            if (userBg || sysBg) foreach (var l in BgApplyCore(userBg && o.BgAffinity, o.BgMaskName ?? o.MaskName, userBg, sysBg)) log.Add(l);
            else if (BgOn) foreach (var l in BgRelease()) log.Add(l);
            return log;
        }

        // The system threads optimizer on its own (every 10 s, and when no game runs): the built-in profile only.
        public List<string> ApplySystemLayer()
        {
            lock (_gate)
            {
                var log = new List<string>();
                if (SystemPresets == null || SystemPresets.Count == 0) { UpdateSysStats(); return log; }
                _tidCache = Native.AllThreadIdsByPid();
                FillPidCache();
                try
                {
                    Native.EnableDebugPrivilege();
                    ApplySet(SystemPresets, new HashSet<string>(Targets ?? new string[0], StringComparer.OrdinalIgnoreCase), log, "sys:");
                }
                finally { _tidCache = null; _pidCache = null; }
                UpdateSysStats();
                return log;
            }
        }

        // What the system optimizer holds right now (processes / threads with a recorded baseline): the Threads page and
        // the tray read these without taking the engine lock.
        private readonly HashSet<int> _sysPids = new HashSet<int>();
        public int SysProcCount { get; private set; }
        public int SysThreadCount { get; private set; }
        private void UpdateGameStats()
        {
            int thr = 0;
            foreach (var kv in _thrBefore)
            {
                int c = kv.Key.IndexOf(':'), pid;
                if (c > 0 && int.TryParse(kv.Key.Substring(0, c), out pid) && _gamePids.Contains(pid) && (kv.Value.PrioSet || kv.Value.BoostSet || _ideal.ContainsKey(kv.Key))) thr++;
            }
            GameThreadCount = thr;
        }

        private void UpdateSysStats()
        {
            int procs = 0, thr = 0;
            foreach (int pid in _sysPids) if (_procBefore.ContainsKey(pid) || _appliedGame.Contains(pid)) procs++;
            foreach (var k in _thrBefore.Keys)
            {
                int c = k.IndexOf(':'), pid;
                if (c > 0 && int.TryParse(k.Substring(0, c), out pid) && _sysPids.Contains(pid)) thr++;
            }
            SysProcCount = procs; SysThreadCount = thr;
        }

        // ---------------- revert (spec F7) ----------------
        // Restores only what grobyq changed on this PID, to the captured baseline. force = no baseline exists (a fresh
        // engine: /off, recovery after a crash): the documented Windows defaults of a normal process are written.
        // Returns true if anything was written.
        private bool RestoreProc(IntPtr hp, int id, bool force)
        {
            ProcBefore b;
            if (!_procBefore.TryGetValue(id, out b) || !b.Have)
            {
                if (!force) return false;
                Native.SetPriorityClass(hp, PRIO["Normal"]);
                Native.SetProcIo(hp, IOP["Normal"]);
                Native.SetMemPrio(hp, MEM["Normal"], false);
                Native.SetProcAffinity(hp, SafeFull());
                Native.SetProcessDefaultCpuSets(hp, null, 0);
                Native.SetPowerThrottlingDefault(hp);
                return true;
            }
            bool any = false;
            if (b.Class || b.BgClass) { Native.SetPriorityClass(hp, b.Prio); any = true; }
            if (b.IoSet || b.BgIo) { Native.SetProcIo(hp, b.Io); any = true; }
            if (b.MemSet || b.BgMem) { Native.SetMemPrio(hp, b.Mem, false); any = true; }
            if (b.AffSet || b.BgAff) { Native.SetProcAffinity(hp, b.Aff != 0 ? b.Aff : SafeFull()); any = true; }
            if (b.SetsSet) { Native.SetProcessDefaultCpuSets(hp, null, 0); any = true; }
            if (b.Eco) { if (b.EcoKnown) Native.SetPowerThrottlingState(hp, b.EcoCtl, b.EcoState); else Native.SetPowerThrottlingDefault(hp); any = true; }   // captured state, else "Windows decides" - never forced on
            if (b.Boost) { Native.SetProcessPriorityBoost(hp, b.BoostDisabled); any = true; }
            return any;
        }

        // Alt-tab release: what the background layer changed goes back to the captured baseline (class, I/O, memory,
        // cores); a per-process rule keeps its own mask, a process boost stays.
        private bool RestoreBg(IntPtr hp, int id)
        {
            ProcBefore b;
            if (!_procBefore.TryGetValue(id, out b) || !b.Have) return false;
            bool any = false;
            if (b.BgClass) { Native.SetPriorityClass(hp, b.Prio); b.BgClass = false; b.Class = false; any = true; }
            if (b.BgIo) { Native.SetProcIo(hp, b.Io); b.BgIo = false; any = true; }
            if (b.BgMem) { Native.SetMemPrio(hp, b.Mem, false); b.BgMem = false; any = true; }
            if (b.BgAff) { if (!b.AffSet) Native.SetProcAffinity(hp, b.Aff != 0 ? b.Aff : SafeFull()); b.BgAff = false; any = true; }   // a rule's own mask stays
            return any;
        }

        private static ProcBefore Before(IntPtr hp)
        {
            bool bd; Native.GetProcessPriorityBoost(hp, out bd);
            var b = new ProcBefore { Prio = Native.GetPriorityClass(hp), Aff = Native.GetProcAffinity(hp), Have = true, BoostDisabled = bd, Io = Native.GetProcIo(hp), Mem = Native.GetMemPrio(hp) };
            uint ec, es;
            if (Native.GetPowerThrottling(hp, out ec, out es)) { b.EcoKnown = true; b.EcoCtl = ec; b.EcoState = es; }
            return b;
        }

        // ---------------- manual actions from the Threads page ----------------
        // Snapshot first, so DISABLE / Exit / /off put the thread back.
        public string ThreadManual(int pid, uint tid, string prio, string ideal, string boost) { lock (_gate) return ThreadManualCore(pid, tid, prio, ideal, boost); }
        private string ThreadManualCore(int pid, uint tid, string prio, string ideal, string boost)
        {
            string exe = ProcName(pid);
            if (exe.Length == 0) return string.Format("[TH] PID {0} is gone: nothing changed", pid);
            VerifyPid(pid);                                   // a reused PID drops the old process's thread metadata first
            string gate = Gate(exe, Op.Thread, ProtectedPriorityAllowed);
            if (gate != null) return gate;
            ThrMetaE meta;
            bool known = _thrMeta.TryGetValue(pid + ":" + tid, out meta) && ModuleOf(meta.Sym).Length > 0;
            if (known && IsAntiCheat(ModuleOf(meta.Sym))) return "[THR] anti-cheat target skipped: " + ModuleOf(meta.Sym);
            if (!known && !IsProtected(exe))
                return string.Format("[THR] TID {0}: start module unknown - not changed (an anti-cheat module cannot be ruled out)", tid);
            if (!string.IsNullOrEmpty(ideal) && IsProtected(exe)) return string.Format("[--] {0} is a protected system process: only thread priority and boost may change, not the Ideal CPU.", exe);
            Native.EnableDebugPrivilege();
            IntPtr ht = Native.OpenThreadOf(pid, tid);
            if (ht == IntPtr.Zero) return string.Format("[TH] TID {0}: not accessible (thread exited, belongs to another process now, or a kernel anti-cheat strips thread rights)", tid);
            // the metadata must describe THIS thread: a TID reused since the last look is refused, not guessed
            if (meta != null && meta.Born != 0)
            {
                long now = Native.ThreadCreated(ht);
                if (now != 0 && now != meta.Born) { Native.CloseHandle(ht); return string.Format("[THR] TID {0}: a new thread took this ID since the list was read - not changed, refresh and try again", tid); }
            }
            try
            {
                string key = pid + ":" + tid;
                var tb = ThreadBaseline(key, ht);
                Debug.Assert(_thrBefore.ContainsKey(key), "thread baseline must be captured before any set");
                _appliedGame.Add(pid);
                var acts = new List<string>();
                int tp;
                if (ThreadPrio(prio, AdvancedThreadMode, out tp))
                {
                    MarkPrio(tb, ht);
                    bool ok = Native.SetThreadPriority(ht, tp);
                    acts.Add(ok ? string.Format("prio={0} (read-back {1})", prio, Native.GetThreadPriority(ht)) : "prio=FAIL");
                }
                int lp;
                if (!string.IsNullOrEmpty(ideal) && int.TryParse(ideal, out lp) && lp >= 0)
                    acts.Add(Native.SetThreadIdealProcessor(ht, (uint)lp) != 0xFFFFFFFF ? "idealCPU=" + lp : "idealCPU=FAIL");
                if (!string.IsNullOrEmpty(boost))
                {
                    bool off = string.Equals(boost, "Off", StringComparison.OrdinalIgnoreCase);
                    MarkBoost(tb, ht);
                    acts.Add(Native.SetThreadPriorityBoost(ht, off) ? "boost=" + (off ? "off" : "on") : "boost=FAIL");
                }
                return string.Format("[TH] manual PID {0} TID {1}: {2}", pid, tid, string.Join(" ", acts));
            }
            finally { Native.CloseHandle(ht); }
        }

        public string ProcessClassManual(int pid, string cls) { lock (_gate) return ProcessClassManualCore(pid, cls); }
        private string ProcessClassManualCore(int pid, string cls)
        {
            uint v;
            if (!PRIO.TryGetValue(cls, out v) || string.Equals(cls, "Realtime", StringComparison.OrdinalIgnoreCase)) return "[!!] class " + cls + " is not allowed";
            string pexe = ProcName(pid);
            if (pexe.Length == 0) return string.Format("[PR] PID {0} is gone: nothing changed", pid);
            string gate = Gate(pexe, Op.Process, ProtectedPriorityAllowed);
            if (gate != null) return gate;
            Native.EnableDebugPrivilege();
            int lvl, err;
            IntPtr hp = Native.OpenProcessGraceful((uint)pid, out lvl, out err);
            if (hp == IntPtr.Zero) return string.Format("[!!] PID {0}: access denied, err {1}", pid, err);
            try
            {
                Identity(pid, hp);
                if (!_procBefore.ContainsKey(pid)) _procBefore[pid] = Before(hp);
                Debug.Assert(_procBefore[pid].Have, "process baseline must be captured before any set");
                _procBefore[pid].Class = true;
                _appliedGame.Add(pid);
                bool ok = Native.SetPriorityClass(hp, v);
                string rb; PRIONAME.TryGetValue((int)Native.GetPriorityClass(hp), out rb);
                return string.Format("[PR] manual PID {0}: class {1} {2} (read-back {3})", pid, cls, ok ? "ok" : "FAILED", rb ?? "?");
            }
            finally { Native.CloseHandle(hp); }
        }

        // Which matching threads a rule touches: "" = all, first/0, last, or an ordinal N.
        // Creation order is approximated by ascending TID (Windows hands out TIDs roughly in order).
        private static List<ThreadRow> FilterTid(List<ThreadRow> hits, string tid)
        {
            if (string.IsNullOrEmpty(tid) || hits.Count <= 1) return hits;
            var ord = hits.OrderBy(r => r.Tid).ToList();
            string t = tid.Trim().ToLowerInvariant();
            if (t == "last") return new List<ThreadRow> { ord[ord.Count - 1] };
            if (t == "first") return new List<ThreadRow> { ord[0] };
            int n;
            if (int.TryParse(t, out n) && n >= 0 && n < ord.Count) return new List<ThreadRow> { ord[n] };
            return hits;
        }

        public static bool ProtectedPriorityAllowed { get { return Settings.GetBool("AllowProtectedPriority"); } }

        // Priority-only preset pass over processes that are NOT game targets (programs, services, nvcontainer,
        // svchost...). Sets thread priority / boost / ideal CPU per the preset's rules, reversibly (_thrBefore).
        // Protected system processes are touched only when the Advanced gate is on. No suspend / terminate exists.
        public List<string> ApplyImportedPresets(ApplyOptions o)
        {
            var log = new List<string>();
            var targets = new HashSet<string>(Targets ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Native.EnableDebugPrivilege();
            if (o.Game && Settings.GetBool("ApplyAllPresets") && Presets != null) ApplySet(Presets, targets, log, "");
            if (o.Sys && SystemPresets != null && SystemPresets.Count > 0) ApplySet(SystemPresets, targets, log, "sys:");
            return log;
        }

        private void ApplySet(Dictionary<string, Preset> set, HashSet<string> targets, List<string> log, string tag)
        {
            bool adv = AdvancedThreadMode, sys = tag == "sys:";
            foreach (var kv in set)
            {
                string exe = kv.Key;
                if (targets.Contains(exe)) continue;                          // game targets go through Apply()
                var preset = kv.Value;
                bool procBoost = preset != null && (string.Equals(preset.Boost, "Off", StringComparison.OrdinalIgnoreCase) || string.Equals(preset.Boost, "On", StringComparison.OrdinalIgnoreCase));
                if (preset == null || !preset.Enabled || (preset.Threads.Count == 0 && !procBoost)) continue;
                string gate = Gate(exe, Op.Thread, ProtectedPriorityAllowed);
                if (gate != null) { AdviceOnce(log, "gate:" + tag + exe, gate); continue; }
                {
                    foreach (var p in Procs(exe))
                    {
                        int pid = p.Id;
                        // ProcessBoost is a cheap, idempotent process attribute (it early-returns when already set) - leave it per cycle.
                        if (procBoost && ProcessBoost(pid, string.Equals(preset.Boost, "Off", StringComparison.OrdinalIgnoreCase), log) && sys) _sysPids.Add(pid);
                        // Lever 3: the per-thread pass (Snapshot + SetThreadPriority) is the expensive part. Run it only when
                        // this PID is new or grew a thread since last cycle - background apps (chrome/steam) keep their priorities
                        // once set, so re-walking them every tick was pure cost. A changed count (incl. PID reuse) re-applies.
                        List<uint> tl; int cur = (_tidCache != null && _tidCache.TryGetValue((uint)pid, out tl)) ? tl.Count : -1;
                        int prev;
                        if (cur < 0 || !_presetTidCount.TryGetValue(pid, out prev) || prev != cur)
                        {
                            int hits;
                            foreach (var l in ApplyPriorityPreset(pid, preset, adv, sys, out hits)) log.Add(l);
                            if (hits > 0 && sys) _sysPids.Add(pid);
                            if (cur >= 0) _presetTidCount[pid] = cur;
                        }
                    }
                }
            }
        }

        // [DISABLEBOOST] / preset boost for a non-target process: SetProcessPriorityBoost only (a priority attribute),
        // baseline captured, restored by DISABLE.
        // true = grobyq holds a boost baseline for this process (set now or earlier)
        private bool ProcessBoost(int pid, bool off, List<string> log)
        {
            int lvl, err;
            IntPtr hp = Native.OpenProcessGraceful((uint)pid, out lvl, out err);
            if (hp == IntPtr.Zero) return false;
            try
            {
                Identity(pid, hp);
                if (!_procBefore.ContainsKey(pid)) _procBefore[pid] = Before(hp);
                Debug.Assert(_procBefore[pid].Have, "process baseline must be captured before any set");
                var b = _procBefore[pid];
                bool was; Native.GetProcessPriorityBoost(hp, out was);
                if (was == off) return b.Boost;
                b.Boost = true;
                _appliedGame.Add(pid);
                if (Native.SetProcessPriorityBoost(hp, off)) AdviceOnce(log, "pboost:" + pid + off, string.Format("[TH] PID {0}: process priority boost {1}", pid, off ? "off" : "on"));
                return true;
            }
            finally { Native.CloseHandle(hp); }
        }

        private List<string> ApplyPriorityPreset(int pid, Preset preset, bool adv, bool sys, out int setCount)
        {
            setCount = 0;
            bool prot = IsProtected(preset.Exe);
            bool guarded = Guarded(preset.Exe);
            var log = new List<string>();
            var snap = Snapshot(pid);
            if (!snap.Ok || snap.Rows.Count == 0) return log;
            int hitN = 0;
            var done = new HashSet<uint>();                  // first matching rule wins per thread (same as the game path)
            foreach (var rule in preset.Threads.Where(x => x.Enabled && !string.IsNullOrEmpty(x.Pattern)))
            {
                if (string.IsNullOrEmpty(rule.Prio) && string.IsNullOrEmpty(rule.Boost)) continue;   // priority-only pass
                if (IsAntiCheat(rule.Pattern)) continue;                                                 // never an anti-cheat module
                var hits = FilterTid(snap.Rows.Where(r => RuleHits(rule, r.Desc, r.Sym) && ThreadOk(r, prot)).ToList(), rule.Tid);
                foreach (var r in hits)
                {
                    if (!done.Add(r.Tid)) continue;
                    IntPtr ht = Native.OpenThreadOf(pid, r.Tid);
                    if (ht == IntPtr.Zero) continue;
                    try
                    {
                        string key = pid + ":" + r.Tid;
                        var tb = ThreadBaseline(key, ht);
                        Debug.Assert(_thrBefore.ContainsKey(key), "thread baseline must be captured before any set");
                        int tp; bool did = false;
                        if (ThreadPrio(rule.Prio, adv, out tp))
                        {
                            if (guarded && tp < TPRIO["BelowNormal"])
                            {
                                tp = TPRIO["BelowNormal"];
                                AdviceOnce(log, "guardthr:" + preset.Exe, string.Format("[TH] {0}: Idle / Lowest rules run as Below normal (starvation guard: the game waits on this process)", preset.Exe));
                            }
                            MarkPrio(tb, ht);
                            if (Native.GetThreadPriority(ht) != tp) { Native.SetThreadPriority(ht, tp); did = true; }
                        }
                        bool wantOff = string.Equals(rule.Boost, "Off", StringComparison.OrdinalIgnoreCase), wantOn = string.Equals(rule.Boost, "On", StringComparison.OrdinalIgnoreCase);
                        if (wantOff || wantOn)
                        {
                            MarkBoost(tb, ht);
                            bool disabled; Native.GetThreadPriorityBoost(ht, out disabled);
                            if (disabled != wantOff) { Native.SetThreadPriorityBoost(ht, wantOff); did = true; }
                        }
                        _appliedGame.Add(pid);
                        if (did) hitN++;
                    }
                    finally { Native.CloseHandle(ht); }
                }
            }
            if (hitN > 0) log.Add(string.Format("[TH] {0} PID {1}: {2} thread(s) set by {3} (priority only)", preset.Exe, pid, hitN, sys ? "the system profile" : "its preset"));
            setCount = hitN;
            return log;
        }

        // fallbackDefaults: only the console "/off" (a fresh engine after a crash) may put running targets back to
        // Windows defaults without a recorded baseline. From the UI, Revert restores recorded baselines only.
        public List<string> Revert(bool fallbackDefaults = false) { lock (_gate) return RevertCore(fallbackDefaults); }
        private List<string> RevertCore(bool fallbackDefaults = false)
        {
            var log = new List<string>();
            Native.EnableDebugPrivilege();

            // every PID we hold a baseline for must still be the same process (a reused PID is forgotten, not restored)
            var held = new HashSet<int>(_appliedGame);
            foreach (int i in _appliedBg) held.Add(i);
            foreach (int i in _procBefore.Keys) held.Add(i);
            foreach (var k in _thrBefore.Keys) { int pp; if (int.TryParse(k.Split(':')[0], out pp)) held.Add(pp); }
            foreach (int pp in held) VerifyPid(pp);

            // every PID with a baseline (RestoreProc writes only flagged fields): a background PID that BgRelease could not
            // open earlier still gets its class / cores back here
            var ids = new HashSet<int>(_appliedGame);
            foreach (int i in _appliedBg) ids.Add(i);
            foreach (int i in _procBefore.Keys) ids.Add(i);
            // nothing recorded (fresh engine: /off, recovery): put the running targets back to Windows defaults
            bool fallback = fallbackDefaults && ids.Count == 0 && _thrBefore.Count == 0;
            var bgFallback = new HashSet<int>();
            if (fallback)
            {
                foreach (var t in Targets)
                {
                    if (Gate(t, Op.Process, false) != null) continue;          // never a protected / anti-cheat exe
                    foreach (var p in ByName(t)) ids.Add(p.Id);
                }
                // background lists (user + built-in profile): only processes still at a class the background layer sets
                // (Idle / Below normal) - evidence grobyq lowered them; anything else is left as it is
                foreach (var b in Settings.BgProcs().Concat(SystemBg.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (Gate(b, Op.Process, false) != null || Targets.Any(t => string.Equals(t, b, StringComparison.OrdinalIgnoreCase))) continue;
                    foreach (var p in ByName(b))
                    {
                        int lv, er; IntPtr h = Native.OpenProcessGraceful((uint)p.Id, out lv, out er);
                        if (h == IntPtr.Zero) continue;
                        // evidence the background layer lowered it: Idle / Below normal class AND VeryLow I/O (what BgApply
                        // writes); a Task Scheduler task at Below normal keeps Normal I/O and is left alone
                        try
                        {
                            uint c = Native.GetPriorityClass(h);
                            if ((c == PRIO["Idle"] || c == PRIO["BelowNormal"]) && Native.GetProcIo(h) == IOP["VeryLow"]) bgFallback.Add(p.Id);
                        }
                        finally { Native.CloseHandle(h); }
                    }
                }
            }

            // background fallback (console /off only): class, I/O and memory back to Normal; cores only if they are exactly
            // the background mask grobyq would set - nothing else is guessed
            if (bgFallback.Count > 0)
            {
                Topo ft = null; ulong bgMask = 0;
                try
                {
                    ft = Topo.Live(); bgMask = GameMask.Background(ft);
                    ulong res = Reservation.Reserved();
                    if (Reservation.Mixes(bgMask, res)) bgMask = Reservation.Unmix(bgMask, res, true);   // exactly what BgApply wrote
                }
                catch { }
                foreach (int id in bgFallback.OrderBy(x => x))
                {
                    int lv, er; IntPtr h = Native.OpenProcessGraceful((uint)id, out lv, out er);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        Native.SetPriorityClass(h, PRIO["Normal"]);
                        Native.SetProcIo(h, IOP["Normal"]);
                        if (Native.GetMemPrio(h) == MEM["Low"]) Native.SetMemPrio(h, MEM["Normal"], false);
                        if (bgMask != 0 && ft != null && Native.GetProcAffinity(h) == bgMask && bgMask != ft.FullMask()) Native.SetProcAffinity(h, SafeFull());
                        log.Add(string.Format("[OFF] PID {0} -> background process back to Normal class / I/O / memory", id));
                    }
                    catch { }
                    finally { Native.CloseHandle(h); }
                }
            }

            // restore cached thread priority/boost on live PIDs
            foreach (var kv in _thrBefore.ToList())
            {
                var parts = kv.Key.Split(':');
                int pid; uint tid;
                if (!int.TryParse(parts[0], out pid) || !uint.TryParse(parts[1], out tid)) continue;
                if (!ids.Contains(pid)) continue;
                IntPtr ht = Native.OpenThreadOf(pid, tid);
                if (ht == IntPtr.Zero) continue;
                try
                {
                    long born = Native.ThreadCreated(ht);
                    if (born != 0 && kv.Value.Born != 0 && born != kv.Value.Born) continue;   // another thread has this TID now
                    if (kv.Value.PrioSet) Native.SetThreadPriority(ht, kv.Value.Prio);
                    if (kv.Value.BoostSet) Native.SetThreadPriorityBoost(ht, kv.Value.BoostDisabled);
                }
                catch { } finally { Native.CloseHandle(ht); }
            }

            foreach (int id in ids.OrderBy(x => x))
            {
                int lvl, err;
                IntPtr hp = Native.OpenProcessGraceful((uint)id, out lvl, out err);
                if (hp == IntPtr.Zero) continue;
                try { if (RestoreProc(hp, id, fallback)) log.Add(string.Format("[OFF] PID {0} -> restored what grobyq changed (class / I/O / memory / cores / EcoQoS / boost) to its baseline", id)); }
                catch { } finally { Native.CloseHandle(hp); }
            }
            _appliedGame.Clear(); _appliedBg.Clear(); _sysPids.Clear();
            _procBefore.Clear(); _thrBefore.Clear(); _born.Clear();
            _ideal.Clear(); _hotPick.Clear(); _hotIdle.Clear(); _gamePids.Clear();
            SysProcCount = 0; SysThreadCount = 0; GameThreadCount = 0;
            _presetTidCount.Clear();   // Lever-3 guard: a disable must force the next enable to re-apply imported presets
            BgOn = false; LastThreads = 0; LastBg = 0; LiveTarget = "";
            log.Add("[i] Thread Ideal Processor is only a hint; a full thread-layer reset = restart the game. Harmless to leave.");
            if (log.Count == 1) log.Insert(0, "[--] nothing to revert");
            return log;
        }

        public void Prune() { lock (_gate) PruneCore(); }
        private void PruneCore()
        {
            var alive = new HashSet<int>();
            try { foreach (var p in Process.GetProcesses()) { alive.Add(p.Id); p.Dispose(); } }
            catch { return; }                                  // no process list = keep every baseline
            if (alive.Count == 0) return;
            foreach (var k in _modCache.Keys.ToList()) if (!alive.Contains(k)) _modCache.Remove(k);
            foreach (var k in _appliedGame.ToList()) if (!alive.Contains(k)) _appliedGame.Remove(k);
            foreach (var k in _appliedBg.ToList()) if (!alive.Contains(k)) _appliedBg.Remove(k);
            foreach (var k in _sysPids.ToList()) if (!alive.Contains(k)) _sysPids.Remove(k);
            foreach (var k in _gamePids.ToList()) if (!alive.Contains(k)) _gamePids.Remove(k);
            Func<string, bool> deadKey = k => { int c = k.IndexOf(':'), pp; return c > 0 && int.TryParse(k.Substring(0, c), out pp) && !alive.Contains(pp); };
            foreach (var k in _ideal.Keys.Where(deadKey).ToList()) _ideal.Remove(k);
            _hotPick.RemoveWhere(k => deadKey(k));
            foreach (var k in _hotIdle.Keys.Where(deadKey).ToList()) _hotIdle.Remove(k);
            foreach (var k in _procBefore.Keys.ToList()) if (!alive.Contains(k)) _procBefore.Remove(k);
            foreach (var k in _born.Keys.ToList()) if (!alive.Contains(k)) _born.Remove(k);
            foreach (var k in _presetTidCount.Keys.ToList()) if (!alive.Contains(k)) _presetTidCount.Remove(k);
            // caches (cycle counters, symbols) age out; thread BASELINES go only with their process - a changed thread
            // that has not been looked at for a while still needs its original priority on DISABLE / exit
            DateTime cut = DateTime.UtcNow.AddMinutes(-5);
            foreach (var k in _thrPrev.Keys.ToList())
            {
                int pid; int.TryParse(k.Split(':')[0], out pid);
                if (!alive.Contains(pid) || _thrPrev[k].T < cut) { _thrPrev.Remove(k); _thrMeta.Remove(k); }
            }
            foreach (var kv in _thrBefore.ToList())
            {
                var parts = kv.Key.Split(':');
                int pid; uint tid;
                if (!int.TryParse(parts[0], out pid) || !alive.Contains(pid) || parts.Length < 2 || !uint.TryParse(parts[1], out tid)) { _thrBefore.Remove(kv.Key); continue; }
                // the thread itself exited (or its TID now belongs to a new thread): nothing left to restore
                IntPtr ht = Native.OpenThreadOf(pid, tid);
                if (ht == IntPtr.Zero) { _thrBefore.Remove(kv.Key); continue; }
                try { long born = Native.ThreadCreated(ht); if (born != 0 && kv.Value.Born != 0 && born != kv.Value.Born) _thrBefore.Remove(kv.Key); }
                finally { Native.CloseHandle(ht); }
            }
        }
    }
}
