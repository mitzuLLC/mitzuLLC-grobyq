using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Grobyq
{
    // Win32 / NT surface (process, thread, CPU-set and interrupt APIs) used by grobyq.
    // No WriteProcessMemory / ReadProcessMemory / VirtualAllocEx / CreateRemoteThread.
    public static class Native
    {
        // PROCESS_QUERY_INFORMATION 0x0400 | PROCESS_QUERY_LIMITED_INFORMATION 0x1000
        // PROCESS_SET_INFORMATION   0x0200 | PROCESS_SET_LIMITED_INFORMATION   0x2000
        // PROCESS_VM_READ           0x0010  (module enumeration only)
        public const uint P_FULL    = 0x0400 | 0x1000 | 0x0200 | 0x2000 | 0x0010;
        public const uint P_NOVM    = 0x0400 | 0x1000 | 0x0200 | 0x2000;
        public const uint P_LIMITED = 0x1000 | 0x2000;

        // THREAD_QUERY_INFORMATION 0x0040 | THREAD_QUERY_LIMITED_INFORMATION 0x0800
        // THREAD_SET_INFORMATION   0x0020 | THREAD_SET_LIMITED_INFORMATION   0x0400
        public const uint T_FULL    = 0x0040 | 0x0800 | 0x0020 | 0x0400;
        public const uint T_LIMITED = 0x0800 | 0x0400;

        public const uint TH32CS_SNAPTHREAD = 0x00000004;
        public const uint LIST_MODULES_ALL  = 0x03;

        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool inh, uint pid);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenThread(uint a, bool inh, uint tid);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool   CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]                    public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetPriorityClass(IntPtr h, uint cls);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetProcessPriorityBoost(IntPtr h, bool disable);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetProcessPriorityBoost(IntPtr h, out bool disabled);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetThreadPriority(IntPtr h, int prio);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern int  GetThreadPriority(IntPtr h);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetThreadPriorityBoost(IntPtr h, bool disable);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetThreadPriorityBoost(IntPtr h, out bool disabled);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern uint SetThreadIdealProcessor(IntPtr h, uint ideal);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool QueryThreadCycleTime(IntPtr h, out ulong cycles);

        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool    SetProcessAffinityMask(IntPtr h, UIntPtr mask);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern UIntPtr SetThreadAffinityMask(IntPtr h, UIntPtr mask);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool    GetProcessAffinityMask(IntPtr h, out UIntPtr p, out UIntPtr s);

        public static bool  SetProcAffinity(IntPtr h, ulong mask) { return SetProcessAffinityMask(h, new UIntPtr(mask)); }
        public static ulong SetThrAffinity (IntPtr h, ulong mask) { return SetThreadAffinityMask(h, new UIntPtr(mask)).ToUInt64(); }
        public static ulong GetProcAffinity(IntPtr h) {
            UIntPtr p, s;
            if (!GetProcessAffinityMask(h, out p, out s)) return 0;
            return p.ToUInt64();
        }

        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetProcessDefaultCpuSets(IntPtr h, uint[] ids, uint count);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetThreadSelectedCpuSets(IntPtr h, uint[] ids, uint count);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetSystemCpuSetInformation(IntPtr info, uint len, out uint ret, IntPtr proc, uint flags);

        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetProcessInformation(IntPtr h, int cls, IntPtr info, uint size);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetThreadInformation(IntPtr h, int cls, IntPtr info, uint size);

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, EntryPoint="GetThreadDescription")]
        private static extern int GetThreadDescriptionNative(IntPtr h, out IntPtr desc);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [StructLayout(LayoutKind.Sequential)]
        public struct THREADENTRY32 {
            public uint dwSize, cntUsage, th32ThreadID, th32OwnerProcessID;
            public int  tpBasePri, tpDeltaPri;
            public uint dwFlags;
        }
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool Thread32First(IntPtr snap, ref THREADENTRY32 te);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool Thread32Next (IntPtr snap, ref THREADENTRY32 te);

        [DllImport("ntdll.dll")] public static extern int NtSetInformationProcess(IntPtr h, int cls, ref uint info, uint len);
        [DllImport("ntdll.dll")] public static extern int NtSetInformationThread (IntPtr h, int cls, ref uint info, uint len);
        [DllImport("ntdll.dll")] public static extern int NtQueryInformationThread(IntPtr h, int cls, out IntPtr info, uint len, IntPtr ret);
        [DllImport("ntdll.dll")] public static extern int NtQueryTimerResolution(out uint min, out uint max, out uint cur);

        [StructLayout(LayoutKind.Sequential)]
        public struct MODULEINFO { public IntPtr Base; public uint SizeOfImage; public IntPtr EntryPoint; }
        [DllImport("psapi.dll", SetLastError=true)] public static extern bool EnumProcessModulesEx(IntPtr h, [Out] IntPtr[] mods, uint cb, out uint needed, uint filter);
        [DllImport("psapi.dll", SetLastError=true)] public static extern bool GetModuleInformation(IntPtr h, IntPtr mod, out MODULEINFO mi, uint cb);
        [DllImport("psapi.dll", CharSet=CharSet.Unicode, EntryPoint="GetModuleBaseNameW", SetLastError=true)]
        public static extern uint GetModuleBaseName(IntPtr h, IntPtr mod, StringBuilder name, uint size);

        [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] public struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }
        [DllImport("advapi32.dll", SetLastError=true)] public static extern bool OpenProcessToken(IntPtr p, uint acc, out IntPtr tok);
        [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool LookupPrivilegeValue(string sys, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError=true)] public static extern bool AdjustTokenPrivileges(IntPtr tok, bool disAll, ref TOKEN_PRIVILEGES nw, uint len, IntPtr prev, IntPtr retLen);

        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]   public static extern bool   ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]   public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        public static void HideConsole() { IntPtr h = GetConsoleWindow(); if (h != IntPtr.Zero) ShowWindow(h, 0); }

        public static uint ForegroundPid()
        {
            uint p = 0;
            IntPtr w = GetForegroundWindow();
            if (w == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(w, out p);
            return p;
        }

        public static bool EnableDebugPrivilege()
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out tok)) return false;
            try {
                LUID luid;
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return false;
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.Count = 1; tp.Luid = luid; tp.Attributes = 2;
                bool ok = AdjustTokenPrivileges(tok, false, ref tp, 16, IntPtr.Zero, IntPtr.Zero);
                return ok && Marshal.GetLastWin32Error() == 0;
            } finally { CloseHandle(tok); }
        }

        // Anti-cheat drivers can strip rights from game handles. Degrade, do not fail.
        public static IntPtr OpenProcessGraceful(uint pid, out int level, out int lastErr)
        {
            lastErr = 0;
            IntPtr h = OpenProcess(P_FULL, false, pid);
            if (h != IntPtr.Zero) { level = 2; return h; }
            lastErr = Marshal.GetLastWin32Error();
            h = OpenProcess(P_NOVM, false, pid);
            if (h != IntPtr.Zero) { level = 1; return h; }
            lastErr = Marshal.GetLastWin32Error();
            h = OpenProcess(P_LIMITED, false, pid);
            if (h != IntPtr.Zero) { level = 0; return h; }
            lastErr = Marshal.GetLastWin32Error();
            level = -1;
            return IntPtr.Zero;
        }

        public static IntPtr OpenThreadGraceful(uint tid)
        {
            IntPtr h = OpenThread(T_FULL, false, tid);
            if (h != IntPtr.Zero) return h;
            return OpenThread(T_LIMITED, false, tid);
        }

        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint GetProcessIdOfThread(IntPtr thread);

        // A thread of THIS process only: a TID Windows reused for another process's thread is closed and refused.
        public static IntPtr OpenThreadOf(int pid, uint tid)
        {
            IntPtr h = OpenThreadGraceful(tid);
            if (h == IntPtr.Zero) return h;
            uint owner = 0;
            try { owner = GetProcessIdOfThread(h); } catch { }
            if (owner != 0 && owner == (uint)pid) return h;
            CloseHandle(h);
            return IntPtr.Zero;
        }

        // SYSTEM_CPU_SET_INFORMATION:
        // Size+0 Type+4 Id+8 Group+12 LP+14 Core+15 LLC+16 Numa+17 Eff+18 Flags+19
        public struct CpuSet {
            public uint Id; public ushort Group; public byte LP; public byte Core; public byte LLC;
            public byte Numa; public byte Eff; public byte Flags;
        }

        [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        // LPs Windows reports across all groups (ALL_PROCESSOR_GROUPS). Checks the CPU Set probe.
        public static int ActiveLpCount()
        {
            try { uint n = GetActiveProcessorCount(0xFFFF); if (n > 0) return (int)n; } catch { }
            return Environment.ProcessorCount;
        }

        // ProcessorNameString of CPU 0 (cached: it does not change while grobyq runs).
        private static string _cpuName;
        public static string CpuName()
        {
            if (_cpuName != null) return _cpuName;
            string n = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                    if (k != null) n = Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
            }
            catch { }
            return _cpuName = n;
        }

        public static List<CpuSet> GetCpuSets()
        {
            var list = new List<CpuSet>();
            uint need = 0;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out need, GetCurrentProcess(), 0);
            if (need == 0) return list;
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            try {
                uint got;
                if (!GetSystemCpuSetInformation(buf, need, out got, GetCurrentProcess(), 0)) return list;
                long off = 0;
                while (off < (long)got) {
                    IntPtr p = new IntPtr(buf.ToInt64() + off);
                    int size = Marshal.ReadInt32(p, 0);
                    int type = Marshal.ReadInt32(p, 4);
                    if (size <= 0) break;
                    if (type == 0) {
                        CpuSet c = new CpuSet();
                        c.Id    = (uint)Marshal.ReadInt32(p, 8);
                        c.Group = (ushort)Marshal.ReadInt16(p, 12);
                        c.LP    = Marshal.ReadByte(p, 14);
                        c.Core  = Marshal.ReadByte(p, 15);
                        c.LLC   = Marshal.ReadByte(p, 16);
                        c.Numa  = Marshal.ReadByte(p, 17);
                        c.Eff   = Marshal.ReadByte(p, 18);
                        c.Flags = Marshal.ReadByte(p, 19);
                        list.Add(c);
                    }
                    off += size;
                }
            } finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        // ---- L3 sizes (for V-Cache CCD detection) ----
        // GetSystemCpuSetInformation gives LastLevelCacheIndex but not the cache SIZE.
        // The size comes from GetLogicalProcessorInformationEx(RelationCache).
        [DllImport("kernel32.dll", SetLastError=true)]
        private static extern bool GetLogicalProcessorInformationEx(int relType, IntPtr buf, ref uint len);

        public sealed class L3Cache
        {
            public ulong Size;
            public List<KeyValuePair<ushort, ulong>> Masks = new List<KeyValuePair<ushort, ulong>>();  // (group, group-relative LP mask)
        }

        public static List<L3Cache> GetL3Caches()
        {
            uint len = 0;
            GetLogicalProcessorInformationEx(2, IntPtr.Zero, ref len);   // RelationCache = 2
            if (len == 0) return new List<L3Cache>();
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try {
                if (!GetLogicalProcessorInformationEx(2, buf, ref len)) return new List<L3Cache>();
                var bytes = new byte[len];
                Marshal.Copy(buf, bytes, 0, (int)len);
                return ParseL3(bytes, (int)len);
            } finally { Marshal.FreeHGlobal(buf); }
        }

        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX + CACHE_RELATIONSHIP (x64):
        //   +0 Relationship  +4 Size  +8 Level  +9 Associativity  +10 LineSize  +12 CacheSize
        //   +16 Type  +20 Reserved[18]  +38 GroupCount (0 before Win11 = one mask)
        //   +40 GROUP_AFFINITY[GroupCount] { +0 Mask(8)  +8 Group(2)  +10 Reserved(6) }
        // Pure: takes raw bytes so the tests can feed a synthetic buffer.
        public static List<L3Cache> ParseL3(byte[] b, int len)
        {
            var res = new List<L3Cache>();
            int off = 0;
            while (off + 8 <= len)
            {
                int rel  = BitConverter.ToInt32(b, off);
                int size = BitConverter.ToInt32(b, off + 4);
                if (size <= 0 || off + size > len) break;
                if (rel == 2 && size >= 56)
                {
                    byte level = b[off + 8];
                    uint csize = BitConverter.ToUInt32(b, off + 12);
                    int  type  = BitConverter.ToInt32(b, off + 16);   // 1 = instruction, 2 = data
                    if (level == 3 && type != 1 && type != 2)
                    {
                        int gc = BitConverter.ToUInt16(b, off + 38);
                        if (gc == 0) gc = 1;
                        var c = new L3Cache { Size = csize };
                        for (int i = 0; i < gc; i++)
                        {
                            int g = off + 40 + i * 16;
                            if (g + 16 > off + size) break;
                            c.Masks.Add(new KeyValuePair<ushort, ulong>(BitConverter.ToUInt16(b, g + 8), BitConverter.ToUInt64(b, g)));
                        }
                        res.Add(c);
                    }
                }
                off += size;
            }
            return res;
        }

        // PROCESSINFOCLASS.ProcessIoPriority = 33 ; THREADINFOCLASS.ThreadIoPriority = 22
        public static int SetProcIo(IntPtr h, uint v) { return NtSetInformationProcess(h, 33, ref v, 4); }
        public static int SetThrIo (IntPtr h, uint v) { return NtSetInformationThread (h, 22, ref v, 4); }

        [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int cls, out uint info, uint len, out uint ret);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetProcessInformation(IntPtr h, int cls, IntPtr info, uint size);

        // Baselines for the revert: current I/O priority (2 = Normal if unreadable) and memory priority (5 = Normal).
        public static uint GetProcIo(IntPtr h) { uint v, r; try { return NtQueryInformationProcess(h, 33, out v, 4, out r) >= 0 && v <= 4 ? v : 2; } catch { return 2; } }
        public static uint GetMemPrio(IntPtr h)
        {
            IntPtr p = Marshal.AllocHGlobal(4);
            try { Marshal.WriteInt32(p, 5); return GetProcessInformation(h, 0, p, 4) ? (uint)Marshal.ReadInt32(p) : 5; }
            catch { return 5; }
            finally { Marshal.FreeHGlobal(p); }
        }

        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
        // Creation time (FILETIME) = the identity of a PID: Windows reuses PIDs, never (pid, creation time). 0 = unknown.
        public static long ProcessCreated(IntPtr h) { long c, e, k, u; try { return GetProcessTimes(h, out c, out e, out k, out u) ? c : 0; } catch { return 0; } }

        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetThreadTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
        // Thread creation time = the identity of a TID inside its process (TIDs are reused after a thread exits). 0 = unknown.
        public static long ThreadCreated(IntPtr h) { long c, e, k, u; try { return GetThreadTimes(h, out c, out e, out k, out u) ? c : 0; } catch { return 0; } }

        // Current EcoQoS state (PROCESS_POWER_THROTTLING_STATE). false = this Windows cannot report it.
        public static bool GetPowerThrottling(IntPtr h, out uint control, out uint state)
        {
            control = 0; state = 0;
            IntPtr p = Marshal.AllocHGlobal(12);
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, 0); Marshal.WriteInt32(p, 8, 0);
                if (!GetProcessInformation(h, 4, p, 12)) return false;
                control = (uint)Marshal.ReadInt32(p, 4); state = (uint)Marshal.ReadInt32(p, 8);
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public static bool SetPowerThrottlingState(IntPtr h, uint control, uint state)
        {
            IntPtr p = Marshal.AllocHGlobal(12);
            try { Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, (int)control); Marshal.WriteInt32(p, 8, (int)state); return SetProcessInformation(h, 4, p, 12); }
            finally { Marshal.FreeHGlobal(p); }
        }

        // EcoQoS back to "Windows decides" (ControlMask 0). SetPowerThrottling(h, true) would FORCE throttling on.
        public static bool SetPowerThrottlingDefault(IntPtr h)
        {
            IntPtr p = Marshal.AllocHGlobal(12);
            try {
                Marshal.WriteInt32(p, 0, 1);
                Marshal.WriteInt32(p, 4, 0);
                Marshal.WriteInt32(p, 8, 0);
                return SetProcessInformation(h, 4, p, 12);
            } finally { Marshal.FreeHGlobal(p); }
        }

        // ProcessMemoryPriority = 0 (same value for thread)
        public static bool SetMemPrio(IntPtr h, uint v, bool thread)
        {
            IntPtr p = Marshal.AllocHGlobal(4);
            try {
                Marshal.WriteInt32(p, (int)v);
                return thread ? SetThreadInformation(h, 0, p, 4) : SetProcessInformation(h, 0, p, 4);
            } finally { Marshal.FreeHGlobal(p); }
        }

        // ProcessPowerThrottling = 4 ; ControlMask EXECUTION_SPEED(1) ; StateMask 0 = EcoQoS off
        public static bool SetPowerThrottling(IntPtr h, bool throttle)
        {
            IntPtr p = Marshal.AllocHGlobal(12);
            try {
                Marshal.WriteInt32(p, 0, 1);
                Marshal.WriteInt32(p, 4, 1);
                Marshal.WriteInt32(p, 8, throttle ? 1 : 0);
                return SetProcessInformation(h, 4, p, 12);
            } finally { Marshal.FreeHGlobal(p); }
        }

        public static string ThreadDesc(IntPtr h)
        {
            IntPtr p;
            if (GetThreadDescriptionNative(h, out p) < 0 || p == IntPtr.Zero) return "";
            try { string s = Marshal.PtrToStringUni(p); return s == null ? "" : s; }
            finally { LocalFree(p); }
        }

        // THREADINFOCLASS.ThreadQuerySetWin32StartAddress = 9
        public static ulong StartAddress(IntPtr h)
        {
            IntPtr a;
            if (NtQueryInformationThread(h, 9, out a, 8, IntPtr.Zero) != 0) return 0;
            return (ulong)a.ToInt64();
        }

        public static List<uint> ThreadIds(uint pid)
        {
            var ids = new List<uint>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snap == new IntPtr(-1)) return ids;
            try {
                THREADENTRY32 te = new THREADENTRY32();
                te.dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32));
                if (Thread32First(snap, ref te)) {
                    do { if (te.th32OwnerProcessID == pid) ids.Add(te.th32ThreadID); }
                    while (Thread32Next(snap, ref te));
                }
            } finally { CloseHandle(snap); }
            return ids;
        }

        // One system-wide thread snapshot, bucketed by owner PID. A re-apply cycle calls this ONCE and every
        // per-process Snapshot() reads its own PID's list from the result - instead of taking a full
        // TH32CS_SNAPTHREAD snapshot of the whole system once per process (which, over a 15-20 process ini
        // profile with chrome/steam/discord, was the 2-3s global stall that recurred every re-apply tick).
        public static Dictionary<uint, List<uint>> AllThreadIdsByPid()
        {
            var map = new Dictionary<uint, List<uint>>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snap == new IntPtr(-1)) return map;
            try {
                THREADENTRY32 te = new THREADENTRY32();
                te.dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32));
                if (Thread32First(snap, ref te)) {
                    do {
                        List<uint> l;
                        if (!map.TryGetValue(te.th32OwnerProcessID, out l)) { l = new List<uint>(); map[te.th32OwnerProcessID] = l; }
                        l.Add(te.th32ThreadID);
                    } while (Thread32Next(snap, ref te));
                }
            } finally { CloseHandle(snap); }
            return map;
        }

        public struct ModRange { public string Name; public ulong Base; public ulong End; }

        public static List<ModRange> Modules(IntPtr hProc)
        {
            var res = new List<ModRange>();
            IntPtr[] mods = new IntPtr[2048];
            uint needed;
            if (!EnumProcessModulesEx(hProc, mods, (uint)(mods.Length * IntPtr.Size), out needed, LIST_MODULES_ALL))
                return res;
            int n = (int)(needed / (uint)IntPtr.Size);
            if (n > mods.Length) n = mods.Length;
            for (int i = 0; i < n; i++) {
                MODULEINFO mi;
                if (!GetModuleInformation(hProc, mods[i], out mi, (uint)Marshal.SizeOf(typeof(MODULEINFO)))) continue;
                StringBuilder sb = new StringBuilder(260);
                if (GetModuleBaseName(hProc, mods[i], sb, 260) == 0 || sb.Length == 0) continue;   // no name = unknown, never ""
                ModRange r = new ModRange();
                r.Name = sb.ToString();
                r.Base = (ulong)mi.Base.ToInt64();
                r.End  = r.Base + mi.SizeOfImage;
                res.Add(r);
            }
            return res;
        }
    }
}
