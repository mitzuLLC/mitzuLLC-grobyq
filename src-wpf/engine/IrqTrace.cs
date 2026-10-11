using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Grobyq
{
    // Live interrupt check: a few seconds of kernel ISR and DPC events - the same PerfInfo events WPR / WPA show in
    // "DPC/ISR" - counted per CPU and attributed to kernel modules and interrupt vectors. Read-only; needs administrator.
    //
    // Session: a private system-logger session (EVENT_TRACE_SYSTEM_LOGGER_MODE, Windows 8+, MS Learn "Configuring and
    // Starting a SystemTraceProvider Session"): its own name and GUID, so it never touches the NT Kernel Logger that WPR or
    // xperf may be using. Kernel groups enabled with TraceSetInformation(TraceSystemTraceEnableFlagsInfo) =
    // EVENT_TRACE_FLAG_DPC | EVENT_TRACE_FLAG_INTERRUPT, as in the Windows-driver-samples SystemTraceControl sample.
    // Consumer: real-time OpenTrace / ProcessTrace with an EVENT_RECORD callback on a background thread.
    // Events (PerfInfo class ce1dbfb4-137e-4da6-87b0-3f59aa102cbc, MS Learn "ISR class" / "DPC class"):
    //   ISR 67 and ISR-MSI 50: InitialTime (8), Routine (pointer), ReturnValue (1), Vector (1) ...
    //   DPC 66 and threaded DPC 68: InitialTime (8), Routine (pointer). Timer DPCs (69) are timers, not devices: skipped.
    // Routine -> module: the kernel image with the highest base at or below it (EnumDeviceDrivers, administrator only).
    public static class IrqTrace
    {
        public enum Kind { None, Isr, Dpc }

        public sealed class Result
        {
            public string Error = "";                // "" = ok; "busy" / "admin" / "x64" / an API error text
            public int Seconds;
            public long EventsLost, BuffersLost;     // events dropped / whole real-time buffers dropped (each holds many events)
            public bool ModulesResolved;
            // module (lower case, "?" when unresolved) -> cpu -> count
            public Dictionary<string, Dictionary<int, int>> Isr = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<int, int>> Dpc = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
            // ISR vector -> cpu -> count
            public Dictionary<int, Dictionary<int, int>> IsrVector = new Dictionary<int, Dictionary<int, int>>();
            public int TotalIsr { get { return Isr.Values.Sum(d => d.Values.Sum()); } }
            public int TotalDpc { get { return Dpc.Values.Sum(d => d.Values.Sum()); } }
        }

        public static Func<int, Result> Hook;           // tests / UI demo

        // ---------------- x64 layouts (evntrace.h / evntcons.h); AppTests check them against mirror structs ----------------
        // EVENT_TRACE_PROPERTIES = WNODE_HEADER (48) + 14 ULONG (56) + HANDLE LoggerThreadId (8) + 2 ULONG = 120
        public const int PropsSize = 120, WnodeBufferSize = 0, WnodeGuid = 24, WnodeClientContext = 40, WnodeFlags = 44;
        public const int PropsBufferSize = 48, PropsMinBuffers = 52, PropsMaxBuffers = 56, PropsLogFileMode = 64, PropsFlushTimer = 68;
        public const int PropsEventsLost = 88, PropsRealTimeBuffersLost = 100, PropsLogFileNameOffset = 112, PropsLoggerNameOffset = 116;
        // EVENT_TRACE_LOGFILEW = 2 pointers + LONGLONG + 2 ULONG (32) + EVENT_TRACE (88) + TRACE_LOGFILE_HEADER (280)
        //   + BufferCallback, BufferSize, Filled, EventsLost, EventRecordCallback, IsKernelTrace, Context = 448
        public const int LogfileSize = 448, LogfileLoggerName = 8, LogfileProcessTraceMode = 28, LogfileEventRecordCallback = 424;
        // EVENT_RECORD = EVENT_HEADER (80) + ETW_BUFFER_CONTEXT (4) + 2 USHORT + 3 pointers = 112
        public const int RecFlags = 4, RecProviderId = 24, RecOpcode = 45, RecProcessor = 80, RecUserDataLength = 86, RecUserData = 96, RecUserContext = 104;
        public const int LogfileContext = 440;      // EVENT_TRACE_LOGFILEW.Context -> EVENT_RECORD.UserContext

        public static readonly Guid PerfInfoGuid = new Guid("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc");
        private static readonly Guid SessionGuid = new Guid("5a3c1f0e-7b1d-4e1f-9a4e-67726f627971");   // grobyq's own session GUID
        private const string SessionName = "grobyq-irq-check";

        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000, EVENT_TRACE_REAL_TIME_MODE = 0x00000100, EVENT_TRACE_SYSTEM_LOGGER_MODE = 0x02000000;
        private const uint EVENT_TRACE_FLAG_DPC = 0x00000020, EVENT_TRACE_FLAG_INTERRUPT = 0x00000040, EVENT_TRACE_CONTROL_STOP = 1;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100, PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const int TraceSystemTraceEnableFlagsInfo = 4;
        private const ushort EVENT_HEADER_FLAG_32_BIT_HEADER = 0x0020, EVENT_HEADER_FLAG_PROCESSOR_INDEX = 0x0200;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void RecordCallback(IntPtr record);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] private static extern uint TraceSetInformation(ulong handle, int cls, IntPtr info, uint len);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
        [DllImport("psapi.dll", SetLastError = true)] private static extern bool EnumDeviceDrivers([Out] IntPtr[] bases, uint cb, out uint needed);
        [DllImport("psapi.dll", CharSet = CharSet.Unicode)] private static extern uint GetDeviceDriverBaseNameW(IntPtr b, StringBuilder name, uint size);

        // ---------------- pure parts (the tests call these) ----------------

        // Which event a record is and its payload fields; data = the first len bytes of UserData.
        public static Kind Classify(Guid provider, byte opcode, ushort flags, byte[] data, int len, out ulong routine, out int vector)
        {
            routine = 0; vector = -1;
            if (provider != PerfInfoGuid || data == null) return Kind.None;
            Kind k;
            if (opcode == 67 || opcode == 50) k = Kind.Isr;
            else if (opcode == 66 || opcode == 68) k = Kind.Dpc;
            else return Kind.None;
            int ptr = (flags & EVENT_HEADER_FLAG_32_BIT_HEADER) != 0 ? 4 : 8;
            if (len < 8 + ptr || data.Length < 8 + ptr) return Kind.None;
            routine = ptr == 8 ? BitConverter.ToUInt64(data, 8) : BitConverter.ToUInt32(data, 8);
            if (k == Kind.Isr && len >= 8 + ptr + 2) vector = data[8 + ptr + 1];     // Vector follows ReturnValue (low byte either way)
            return k;
        }

        // The kernel image a routine lives in: the highest base at or below it, within 128 MB (else "?").
        public static string ModuleOf(List<KeyValuePair<ulong, string>> sorted, ulong addr)
        {
            if (sorted == null || sorted.Count == 0 || addr == 0) return "?";
            int lo = 0, hi = sorted.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid].Key <= addr) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (best < 0 || addr - sorted[best].Key > (128UL << 20)) return "?";
            return sorted[best].Value;
        }

        // ---------------- capture ----------------
        private struct Key : IEquatable<Key>
        {
            public ulong Routine; public int Cpu; public int Vector; public Kind Kind;
            public bool Equals(Key o) { return Routine == o.Routine && Cpu == o.Cpu && Vector == o.Vector && Kind == o.Kind; }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode() { return Routine.GetHashCode() * 31 + Cpu * 7 + Vector * 131 + (int)Kind; }
        }

        private static readonly object _run = new object();
        private static Dictionary<Key, int> _agg;
        private static readonly object _aggLock = new object();      // the callback thread vs the snapshot after the stop
        private static readonly byte[] _buf = new byte[32];
        private static readonly RecordCallback _cb = OnRecord;  // one delegate for the process lifetime: a late native call never hits a collected thunk
        private static volatile Thread _stuck;                   // a ProcessTrace that did not end after the stop: no new capture until it does
        private static long _gen;                                // capture generation, passed as LOGFILE.Context, checked in the callback
        private static long _pg1, _pg2;

        public static Result Capture(int seconds)
        {
            if (Hook != null) return Hook(seconds);
            var r = new Result { Seconds = seconds };
            if (IntPtr.Size != 8) { r.Error = "x64"; return r; }
            var stuck = _stuck;
            if (stuck != null) { if (stuck.IsAlive) { r.Error = "busy"; return r; } _stuck = null; }
            if (!Monitor.TryEnter(_run)) { r.Error = "busy"; return r; }
            try { Run(r, Math.Max(2, Math.Min(15, seconds))); }
            catch (Exception ex) { r.Error = ex.Message; }
            finally { Monitor.Exit(_run); }
            return r;
        }

        private static void FillProps(IntPtr p, int len)
        {
            for (int i = 0; i < len; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, WnodeBufferSize, len);
            Marshal.Copy(SessionGuid.ToByteArray(), 0, new IntPtr(p.ToInt64() + WnodeGuid), 16);
            Marshal.WriteInt32(p, WnodeClientContext, 1);                     // QPC clock
            Marshal.WriteInt32(p, WnodeFlags, unchecked((int)WNODE_FLAG_TRACED_GUID));
            Marshal.WriteInt32(p, PropsBufferSize, 64);                        // KB per buffer: 1-4 MB of nonpaged pool for 5 s
            Marshal.WriteInt32(p, PropsMinBuffers, 16);
            Marshal.WriteInt32(p, PropsMaxBuffers, 64);
            Marshal.WriteInt32(p, PropsLogFileMode, unchecked((int)(EVENT_TRACE_REAL_TIME_MODE | EVENT_TRACE_SYSTEM_LOGGER_MODE)));
            Marshal.WriteInt32(p, PropsFlushTimer, 1);                         // seconds
            Marshal.WriteInt32(p, PropsLoggerNameOffset, PropsSize);
        }

        private static void Run(Result r, int seconds)
        {
            var g = PerfInfoGuid.ToByteArray();
            _pg1 = BitConverter.ToInt64(g, 0); _pg2 = BitConverter.ToInt64(g, 8);
            int propsLen = PropsSize + (SessionName.Length + 1) * 2 + 64;
            IntPtr props = Marshal.AllocHGlobal(propsLen), flags = IntPtr.Zero, lf = IntPtr.Zero, name = IntPtr.Zero;
            ulong session = 0, trace = ulong.MaxValue;
            Thread worker = null;
            var pst = new uint[1];
            try
            {
                FillProps(props, propsLen);
                uint st = StartTraceW(out session, SessionName, props);
                if (st == 183)                                                  // ERROR_ALREADY_EXISTS: our own session left by a crash
                {
                    FillProps(props, propsLen); ControlTraceW(0, SessionName, props, EVENT_TRACE_CONTROL_STOP);
                    FillProps(props, propsLen); st = StartTraceW(out session, SessionName, props);
                }
                if (st != 0) { session = 0; r.Error = st == 5 ? "admin" : "StartTrace " + st; return; }

                flags = Marshal.AllocHGlobal(32);                               // PERFINFO_GROUPMASK: 8 ULONGs
                for (int i = 0; i < 32; i++) Marshal.WriteByte(flags, i, 0);
                Marshal.WriteInt32(flags, 0, unchecked((int)(EVENT_TRACE_FLAG_DPC | EVENT_TRACE_FLAG_INTERRUPT)));
                st = TraceSetInformation(session, TraceSystemTraceEnableFlagsInfo, flags, 32);
                if (st != 0) { r.Error = st == 5 ? "admin" : "TraceSetInformation " + st; return; }

                lf = Marshal.AllocHGlobal(LogfileSize);
                for (int i = 0; i < LogfileSize; i++) Marshal.WriteByte(lf, i, 0);
                name = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(lf, LogfileLoggerName, name);
                Marshal.WriteInt32(lf, LogfileProcessTraceMode, unchecked((int)(PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD)));
                Marshal.WriteIntPtr(lf, LogfileEventRecordCallback, Marshal.GetFunctionPointerForDelegate(_cb));
                long gen = Interlocked.Increment(ref _gen);
                Marshal.WriteIntPtr(lf, LogfileContext, new IntPtr(gen));       // comes back as EVENT_RECORD.UserContext
                lock (_aggLock) _agg = new Dictionary<Key, int>();
                trace = OpenTraceW(lf);
                if (trace == ulong.MaxValue) { r.Error = "OpenTrace " + Marshal.GetLastWin32Error(); return; }
                ulong h = trace;
                worker = new Thread(() => { uint ps; try { ps = ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero); } catch { ps = 1; } pst[0] = ps; }) { IsBackground = true, Name = "grobyq irq check" };
                worker.Start();
                Thread.Sleep(seconds * 1000);
            }
            finally
            {
                if (session != 0)
                {
                    FillProps(props, propsLen);
                    if (ControlTraceW(session, null, props, EVENT_TRACE_CONTROL_STOP) == 0)
                    {
                        r.EventsLost = (uint)Marshal.ReadInt32(props, PropsEventsLost);
                        r.BuffersLost = (uint)Marshal.ReadInt32(props, PropsRealTimeBuffersLost);
                    }
                }
                bool ended = worker == null || worker.Join(5000);              // the stop ends ProcessTrace after the last buffers
                if (trace != ulong.MaxValue) CloseTrace(trace);                // required; also cancels a ProcessTrace still draining
                if (!ended) ended = worker.Join(2000);
                if (!ended)
                {
                    // the native side may still read the logfile and its name: leak them on purpose, refuse new captures until it ends
                    _stuck = worker; lf = IntPtr.Zero; name = IntPtr.Zero;
                    if (r.Error.Length == 0) r.Error = "timeout";
                }
                else if (worker != null && r.Error.Length == 0 && pst[0] != 0 && pst[0] != 1223 /*ERROR_CANCELLED*/) r.Error = "ProcessTrace " + pst[0];
                Marshal.FreeHGlobal(props);
                if (flags != IntPtr.Zero) Marshal.FreeHGlobal(flags);
                if (lf != IntPtr.Zero) Marshal.FreeHGlobal(lf);
                if (name != IntPtr.Zero) Marshal.FreeHGlobal(name);
            }
            Dictionary<Key, int> agg;
            lock (_aggLock) { agg = _agg; _agg = null; }
            if (r.Error.Length > 0 || agg == null) return;

            try { Native.EnableDebugPrivilege(); } catch { }                // newer builds zero kernel image bases without it
            var mods = DriverBases();
            r.ModulesResolved = mods.Count > 0;
            var cache = new Dictionary<ulong, string>();
            foreach (var kv in agg)
            {
                string m;
                if (!cache.TryGetValue(kv.Key.Routine, out m)) cache[kv.Key.Routine] = m = ModuleOf(mods, kv.Key.Routine);
                var byMod = kv.Key.Kind == Kind.Isr ? r.Isr : r.Dpc;
                Add(byMod, m, kv.Key.Cpu, kv.Value);
                if (kv.Key.Kind == Kind.Isr && kv.Key.Vector >= 0)
                {
                    Dictionary<int, int> d;
                    if (!r.IsrVector.TryGetValue(kv.Key.Vector, out d)) r.IsrVector[kv.Key.Vector] = d = new Dictionary<int, int>();
                    int c; d.TryGetValue(kv.Key.Cpu, out c); d[kv.Key.Cpu] = c + kv.Value;
                }
            }
        }

        private static void Add(Dictionary<string, Dictionary<int, int>> to, string module, int cpu, int n)
        {
            Dictionary<int, int> d;
            if (!to.TryGetValue(module, out d)) to[module] = d = new Dictionary<int, int>();
            int c; d.TryGetValue(cpu, out c); d[cpu] = c + n;
        }

        // ProcessTrace thread: cheap, no allocation beyond the dictionary.
        private static void OnRecord(IntPtr rec)
        {
            try
            {
                if (rec == IntPtr.Zero) return;
                if (Marshal.ReadIntPtr(rec, RecUserContext).ToInt64() != Interlocked.Read(ref _gen)) return;      // a late call from an older capture
                if (Marshal.ReadInt64(rec, RecProviderId) != _pg1 || Marshal.ReadInt64(rec, RecProviderId + 8) != _pg2) return;
                byte op = Marshal.ReadByte(rec, RecOpcode);
                if (op != 50 && op != 66 && op != 67 && op != 68) return;
                ushort fl = (ushort)Marshal.ReadInt16(rec, RecFlags);
                int cpu = (fl & EVENT_HEADER_FLAG_PROCESSOR_INDEX) != 0 ? (ushort)Marshal.ReadInt16(rec, RecProcessor) : Marshal.ReadByte(rec, RecProcessor);
                int len = (ushort)Marshal.ReadInt16(rec, RecUserDataLength);
                IntPtr ud = Marshal.ReadIntPtr(rec, RecUserData);
                if (ud == IntPtr.Zero || len <= 0) return;
                int n = Math.Min(len, _buf.Length);
                Marshal.Copy(ud, _buf, 0, n);
                ulong routine; int vec;
                var k = Classify(PerfInfoGuid, op, fl, _buf, n, out routine, out vec);
                if (k == Kind.None) return;
                var key = new Key { Routine = routine, Cpu = cpu, Vector = k == Kind.Isr ? vec : -1, Kind = k };
                lock (_aggLock)
                {
                    if (_agg == null) return;
                    int c; _agg.TryGetValue(key, out c); _agg[key] = c + 1;
                }
            }
            catch { }
        }

        // A session of ours left by an exit or crash mid-capture keeps logging every ISR / DPC: stop it (by our name only).
        public static void StopStale()
        {
            if (IntPtr.Size != 8 || Hook != null) return;
            IntPtr p = IntPtr.Zero;
            try
            {
                int len = PropsSize + (SessionName.Length + 1) * 2 + 64;
                p = Marshal.AllocHGlobal(len);
                FillProps(p, len);
                ControlTraceW(0, SessionName, p, EVENT_TRACE_CONTROL_STOP);
            }
            catch { }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        // Loaded kernel images, sorted by base (lower-case file names). Empty without administrator rights (zero bases).
        public static List<KeyValuePair<ulong, string>> DriverBases()
        {
            var list = new List<KeyValuePair<ulong, string>>();
            try
            {
                uint need;
                EnumDeviceDrivers(null, 0, out need);
                if (need == 0) return list;
                var bases = new IntPtr[need / (uint)IntPtr.Size + 16];
                if (!EnumDeviceDrivers(bases, (uint)(bases.Length * IntPtr.Size), out need)) return list;
                int count = (int)Math.Min(bases.Length, need / (uint)IntPtr.Size);
                var sb = new StringBuilder(260);
                for (int i = 0; i < count; i++)
                {
                    ulong b = (ulong)bases[i].ToInt64();
                    if (b == 0) continue;
                    sb.Length = 0;
                    if (GetDeviceDriverBaseNameW(bases[i], sb, (uint)sb.Capacity) == 0) continue;
                    list.Add(new KeyValuePair<ulong, string>(b, sb.ToString().ToLowerInvariant()));
                }
            }
            catch { }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }
    }
}
