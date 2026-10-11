using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Grobyq
{
    // ReservedCpuSets: HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel, REG_BINARY, 8 bytes
    // little-endian, bit N = LP N of processor group 0. Read by the kernel at boot: a write needs a restart.
    // MS Learn "Soft Real-Time on Windows IoT Enterprise" (T1 for the model): reserved LPs run only what is
    // pinned to them; Windows, unpinned programs and hardware interrupts go to the unreserved set.
    // Port of APPLY-RESERVE / REVERT from grobPC_ReservedCpuSets.ps1. Owner rule: only the NON-GAME set of a
    // verified split (family A / C) is ever written - never the game set, never LP 0, never half an SMT pair.
    public static class Reservation
    {
        public static RegistryKey Hive = Registry.LocalMachine;       // tests point these at HKCU
        public static string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
        public const string ValueName = "ReservedCpuSets";
        public static string BackupPath { get { return Path.Combine(Store.Dir, "backup-reservedcpusets.json"); } }
        public static Func<DateTime> BootUtc = () => DateTime.UtcNow - TimeSpan.FromMilliseconds(Native.GetTickCount64());

        public enum Kind { NotSet, Binary, WrongType, Unreadable }

        public sealed class State
        {
            public Kind Kind;
            public byte[] Bytes;
            public ulong Mask;
            public string Error = "";
            public bool HasBackup;
            public string Original = "";     // from the backup: "none" or the bytes before the first grobyq write
            public bool TimeKnown;           // the backup carries our write time
            public DateTime Applied;         // UTC
            public bool Pending;             // written after the last boot -> not active yet
        }

        // ---------------- bytes ----------------
        public static byte[] ToBytes(ulong m)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)(m >> (8 * i));
            return b;
        }

        public static ulong FromBytes(byte[] b)
        {
            ulong m = 0;
            if (b == null) return 0;
            for (int i = 0; i < Math.Min(8, b.Length); i++) m |= (ulong)b[i] << (8 * i);
            return m;
        }

        public static string HexText(byte[] b) { return b == null ? "" : string.Join(" ", b.Select(x => x.ToString("X2", CultureInfo.InvariantCulture))); }

        public static byte[] ParseHex(string t)
        {
            var parts = (t ?? "").Split(new[] { ' ', '\t', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
        }

        // ---------------- read ----------------
        public static State Read()
        {
            var st = new State();
            try
            {
                using (var k = Hive.OpenSubKey(KeyPath, false))
                {
                    object v = k == null ? null : k.GetValue(ValueName, null);
                    if (v == null) st.Kind = Kind.NotSet;
                    else if (v is byte[]) { st.Kind = Kind.Binary; st.Bytes = (byte[])v; st.Mask = FromBytes(st.Bytes); }
                    else { st.Kind = Kind.WrongType; st.Error = k.GetValueKind(ValueName).ToString(); }
                }
            }
            catch (Exception e) { st.Kind = Kind.Unreadable; st.Error = e.Message; }
            var b = ReadBackup();
            if (b != null)
            {
                st.HasBackup = true;
                st.Original = b.Original;
                if (b.Applied.HasValue) { st.TimeKnown = true; st.Applied = b.Applied.Value; }
            }
            if (st.Kind == Kind.Binary && st.TimeKnown)
            {
                try { st.Pending = st.Applied > BootUtc(); } catch { }
            }
            return st;
        }

        // The reserved LP mask the engine guard uses: the stored value, active or pending (0 = none).
        // Registry only, never throws: called before every pin.
        public static ulong Reserved()
        {
            try
            {
                using (var k = Hive.OpenSubKey(KeyPath, false))
                {
                    var v = k == null ? null : k.GetValue(ValueName, null) as byte[];
                    return v == null ? 0 : FromBytes(v);
                }
            }
            catch { return 0; }
        }

        // Never let an affinity mix reserved and unreserved LPs (valleyofdoom ReservedCpuSets notes, T2).
        // Mixed -> keep one side: the game keeps its unreserved part, background / helpers their reserved part.
        public static ulong Unmix(ulong mask, ulong reserved, bool preferReserved)
        {
            if (reserved == 0) return mask;
            ulong r = mask & reserved, u = mask & ~reserved;
            if (r == 0 || u == 0) return mask;
            return preferReserved ? r : u;
        }

        public static bool Mixes(ulong mask, ulong reserved)
        {
            return reserved != 0 && (mask & reserved) != 0 && (mask & ~reserved) != 0;
        }

        // ---------------- refusals ----------------
        // Codes (the UI translates them, Text() is the English form):
        //   groups | family | nosplit | empty | lp0 | smt | game | notnongame
        public static List<string> Refusals(Topo t, ulong mask)
        {
            var r = new List<string>();
            if (t == null) { r.Add("family"); return r; }
            if (t.Groups > 1 || t.LogicalCount > 64) r.Add("groups");
            var p = t.Split;
            if (!p.IsSplitFamily) r.Add("family");
            else if (!p.CanSplit) r.Add("nosplit");
            if (mask == 0) r.Add("empty");
            if ((mask & 1) != 0) r.Add("lp0");
            if (mask != 0 && !CoreSplit.SmtWhole(t, mask)) r.Add("smt");
            if (p.CanSplit && (mask & p.Game) != 0) r.Add("game");
            else if (p.CanSplit && mask != 0 && mask != p.NonGame) r.Add("notnongame");
            return r;
        }

        public static string Text(string code, Topo t)
        {
            switch (code)
            {
                case "groups": return "more than 64 logical processors / several processor groups: an 8-byte group-0 value cannot describe this CPU";
                case "family": return "family " + (t != null ? t.Split.Family : "?") + ": there is no game / non-game split on this CPU";
                case "nosplit": return "the split is not verified: " + (t != null ? t.Split.Why() : "");
                case "empty": return "the set is empty";
                case "lp0": return "the set contains LP 0 (boot processor, default interrupt target)";
                case "smt": return "the set would split an SMT pair";
                case "game": return "the set overlaps the game set";
                case "notnongame": return "only the non-game set can be reserved";
            }
            return code;
        }

        // ---------------- write / revert ----------------
        public sealed class Result { public bool Ok; public string Error = ""; public string Info = ""; public byte[] Written; }

        // ReservedCpuSets = the NON-GAME set of this CPU. Backup first (original bytes or "none" + UTC time), then
        // write, then read back. The backup keeps the value before the FIRST grobyq write; REVERT restores it.
        public static Result ApplyNonGame(Topo t)
        {
            var res = new Result();
            ulong m = t != null ? t.Split.NonGame : 0;
            var bad = Refusals(t, m);
            if (bad.Count > 0) { res.Error = "not written: " + string.Join("; ", bad.Select(c => Text(c, t))); return res; }
            byte[] nb = ToBytes(m);
            var st = Read();
            if (st.Kind == Kind.WrongType) { res.Error = "ReservedCpuSets exists as " + st.Error + ", not REG_BINARY. Revert or fix it by hand."; return res; }
            if (st.Kind == Kind.Unreadable) { res.Error = "cannot read ReservedCpuSets: " + st.Error; return res; }
            if (st.Kind == Kind.Binary && HexText(st.Bytes) == HexText(nb)) { res.Ok = true; res.Written = nb; res.Info = "already set"; if (!st.HasBackup) WriteBackup(HexText(st.Bytes), null); return res; }

            string original = st.Kind == Kind.Binary ? HexText(st.Bytes) : "none";
            var old = ReadBackup();
            if (old != null) original = old.Original;              // keep the state before the first grobyq write
            string berr = WriteBackup(original, DateTime.UtcNow);
            if (berr.Length > 0) { res.Error = "backup failed, nothing written: " + berr; return res; }
            try
            {
                using (var k = Hive.CreateSubKey(KeyPath))
                    k.SetValue(ValueName, nb, RegistryValueKind.Binary);
            }
            catch (Exception e) { res.Error = "write failed: " + e.Message; return res; }
            var back = Read();
            if (back.Kind != Kind.Binary || HexText(back.Bytes) != HexText(nb)) { res.Error = "read-back does not match (" + HexText(back.Bytes) + ")"; return res; }
            res.Ok = true; res.Written = nb; res.Info = "original = " + original;
            return res;
        }

        public static Result Revert()
        {
            var res = new Result();
            var st = Read();
            var b = ReadBackup();
            string target = b != null ? b.Original : "none";
            try
            {
                if (target == "none")
                {
                    if (st.Kind != Kind.NotSet)
                        using (var k = Hive.OpenSubKey(KeyPath, true))
                            if (k != null) k.DeleteValue(ValueName, false);
                    if (Read().Kind != Kind.NotSet) { res.Error = "the value is still there"; return res; }
                    res.Info = st.Kind == Kind.NotSet ? "not set - nothing to revert" : "ReservedCpuSets deleted";
                }
                else
                {
                    var bytes = ParseHex(target);
                    using (var k = Hive.CreateSubKey(KeyPath))
                        k.SetValue(ValueName, bytes, RegistryValueKind.Binary);
                    var back = Read();
                    if (back.Kind != Kind.Binary || HexText(back.Bytes) != HexText(bytes)) { res.Error = "read-back does not match"; return res; }
                    res.Info = "restored " + HexText(bytes);
                }
            }
            catch (Exception e) { res.Error = e.Message; return res; }
            try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
            res.Ok = true;
            return res;
        }

        // ---------------- backup json ----------------
        public sealed class Backup { public string Original = "none"; public DateTime? Applied; }

        public static Backup ReadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath)) return null;
                string j = File.ReadAllText(BackupPath, Encoding.UTF8);
                var b = new Backup();
                var mo = Regex.Match(j, "\"original\"\\s*:\\s*\"([^\"]*)\"");
                if (mo.Success) b.Original = mo.Groups[1].Value.Trim();
                if (b.Original.Length == 0) b.Original = "none";
                if (b.Original != "none") { try { ParseHex(b.Original); } catch { b.Original = "none"; } }
                var ma = Regex.Match(j, "\"applied\"\\s*:\\s*\"([^\"]*)\"");
                DateTime at;
                if (ma.Success && DateTime.TryParse(ma.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at)) b.Applied = at;
                return b;
            }
            catch { return null; }
        }

        private static string WriteBackup(string original, DateTime? applied)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                string at = applied.HasValue ? applied.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : "";
                string j = "{\r\n  \"format\": \"grobyq-reservedcpusets\",\r\n  \"key\": \"HKLM\\\\" + KeyPath.Replace("\\", "\\\\") + "\\\\" + ValueName + "\",\r\n" +
                           "  \"original\": \"" + original + "\",\r\n  \"applied\": \"" + at + "\"\r\n}\r\n";
                File.WriteAllText(BackupPath, j, new UTF8Encoding(false));
                return "";
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
