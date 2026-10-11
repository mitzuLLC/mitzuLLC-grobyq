using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    // Reservation tab: ReservedCpuSets for the NON-GAME set of a verified split (port of APPLY-RESERVE / REVERT / VERIFY).
    // An explicit experiment: written only by its own button, read at boot, the original kept in backup-reservedcpusets.json.
    public sealed partial class AffinityPage
    {
        private InfoGrid _rInfo;
        private TextBlock _rBanner, _rRefuse, _rStatus;
        private Button _rApply, _rRevert, _rVerify;

        private void BuildResv(StackPanel p)
        {
            SubTitle(p, L.T("Core reservation (ReservedCpuSets)"), true);
            var c = SubCard(p);
            var intro = c.Add(Ui.Text(L.T("Reserves the NON-GAME set for pinned work only (T1 model, MS Learn: Soft Real-Time on Windows IoT Enterprise): reserved LPs run only what is pinned to them; Windows, unpinned programs and hardware interrupts go to the unreserved set - here the game set. The optimizer alone is the default A/B; this is an extra experiment. Judge it by frame-time and WPA (CPU usage, DPC/ISR per CPU)."), 9, "Dim"));
            intro.Margin = new Thickness(0, 0, 0, 8);
            _rInfo = c.Add(new InfoGrid { KeyWidth = 150 });
            _rBanner = c.Add(Ui.Text("", 9.5, "Warn"));
            _rBanner.Margin = new Thickness(0, 6, 0, 0);
            _rRefuse = c.Add(Ui.Text("", 9, "Warn"));
            _rRefuse.Margin = new Thickness(0, 4, 0, 0);
            _rApply = Ui.Btn(L.T("Reserve the non-game set"), "BtnAccent");
            _rApply.Click += (s, e) => ResvApply();
            _rRevert = Ui.Btn(L.T("Revert"), "BtnNormal");
            _rRevert.Click += (s, e) => ResvRevert();
            _rVerify = Ui.Btn(L.T("Verify"), "BtnOutline");
            _rVerify.Click += (s, e) => ResvVerify();
            var strip = Ui.Strip(44, _rApply, _rRevert, _rVerify);
            strip.Margin = new Thickness(0, 10, 0, 0);
            c.Add(strip);
            _rStatus = c.Add(Ui.Text("", 9, "Dim"));
            _rStatus.Margin = new Thickness(0, 4, 0, 4);
            var note = c.Add(Ui.Text(L.T("Read at boot: restart Windows to activate or undo it (a shutdown with Fast Startup does not reload it, T2). Never the game set, never LP 0, never half an SMT pair. If CS2 hitches more: Revert and restart."), 9, "Faint"));
            note.Margin = new Thickness(0, 4, 0, 0);
        }

        private static string Utc(DateTime d) { return d.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"; }

        private void ResvReload()
        {
            if (_rInfo == null) return;
            var t = T;
            var st = Reservation.Read();
            var sp = t != null ? t.Split : null;
            var rows = new List<KeyValuePair<string, string>>();
            string val;
            if (st.Kind == Reservation.Kind.NotSet) val = L.T("not set");
            else if (st.Kind == Reservation.Kind.Binary) val = Reservation.HexText(st.Bytes) + "   =   " + Masks.Hex(st.Mask) + "   ·   LP " + Masks.Ranges(st.Mask);
            else if (st.Kind == Reservation.Kind.WrongType) val = L.F("{0}, not REG_BINARY", st.Error);
            else val = L.T("unreadable") + ": " + st.Error;
            rows.Add(KV(L.T("Registry value"), val));
            string state = st.Kind != Reservation.Kind.Binary ? "—" : st.Pending ? L.T("pending reboot - written after the last boot") : st.TimeKnown ? L.T("active since boot") : L.T("set - write time unknown (restart once if it was just changed)");
            rows.Add(KV(L.T("State"), state));
            bool can = sp != null && sp.CanSplit;
            rows.Add(KV(L.T("Would reserve"), can ? L.T("NON-GAME") + "  " + Masks.Hex(sp.NonGame) + "   ·   LP " + Masks.Ranges(sp.NonGame) + "   ->   " + Reservation.HexText(Reservation.ToBytes(sp.NonGame)) : "—"));
            string bk = st.HasBackup ? L.F("original: {0}", st.Original == "none" ? L.T("none (value did not exist)") : st.Original) + (st.TimeKnown ? "   ·   " + Utc(st.Applied) : "") : L.T("none yet - made before the first write");
            rows.Add(KV(L.T("Backup"), bk));
            _rInfo.Set(rows);
            if (st.Pending) _rInfo.Tint(1, Ui.B("Warn"));
            _rBanner.Text = st.Pending ? L.T("Restart Windows to activate the new ReservedCpuSets value. Until then the previous value stays in effect.") : "";
            _rBanner.Visibility = st.Pending ? Visibility.Visible : Visibility.Collapsed;
            var bad = t == null ? new List<string> { "family" } : Reservation.Refusals(t, can ? sp.NonGame : 0);
            _rRefuse.Text = string.Join("\n", bad.Distinct().Select(Explain.Refusal));
            _rRefuse.Visibility = bad.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool already = can && st.Kind == Reservation.Kind.Binary && st.Mask == sp.NonGame;
            _rApply.IsEnabled = bad.Count == 0 && !already;
            _rRevert.IsEnabled = st.Kind != Reservation.Kind.NotSet || st.HasBackup;
        }

        private void ResvDone(string line, bool warn)
        {
            _rStatus.Foreground = warn ? Ui.B("Warn") : Ui.B("Dim");
            _rStatus.Text = line;
            ResvReload();
            ChipsRefresh();
            CoresShow(false);
        }

        private void ResvApply()
        {
            var t = T; if (t == null) return;
            var sp = t.Split;
            var bad = Reservation.Refusals(t, sp.NonGame);
            if (bad.Count > 0) { ResvDone(string.Join("\n", bad.Distinct().Select(Explain.Refusal)), true); return; }
            string bytes = Reservation.HexText(Reservation.ToBytes(sp.NonGame));
            string text = L.F("Write ReservedCpuSets = {0} (LP {1})?", Masks.Hex(sp.NonGame), Masks.Ranges(sp.NonGame)) + "\n\n" +
                          L.F("Bytes {0} (REG_BINARY, little-endian, bit N = LP N).", bytes) + "\n\n" +
                          L.F("After a restart the reserved LPs run only what is pinned to them (T1). Windows, services, unpinned programs and hardware interrupts move to the unreserved set = the game set {0}. That adds load next to CS2 unless background apps are pinned to the reserved set (Threads > background list).", "LP " + Masks.Ranges(sp.Game)) + "\n\n" +
                          L.T("The original value is saved to backup-reservedcpusets.json; Revert restores it.");
            if (Dlg.Show(null, text, true, L.T("Write"), L.T("Cancel")) != 0) return;
            var r = Reservation.ApplyNonGame(t);
            if (!r.Ok) { AppLog.Add("[RS] ReservedCpuSets not written: " + r.Error); ResvDone("! " + r.Error, true); return; }
            AppLog.Add(string.Format("[RS] ReservedCpuSets = {0} LP {1}  bytes {2}  read back ok  ({3}; backup-reservedcpusets.json). Restart to activate.", Masks.Hex(sp.NonGame), Masks.Ranges(sp.NonGame), Reservation.HexText(r.Written), r.Info));
            ResvDone(r.Info == "already set" ? L.T("This exact value is already set.") : L.T("Written and read back. Restart Windows to activate it."), false);
        }

        private void ResvRevert()
        {
            var st = Reservation.Read();
            string target = st.HasBackup ? st.Original : "none";
            string q = target == "none" ? L.T("Delete ReservedCpuSets? (It did not exist before the first grobyq write, or there is no grobyq backup.)") : L.F("Restore ReservedCpuSets = {0} (the value before the first grobyq write)?", target);
            if (!Dlg.Ask(null, q)) return;
            var r = Reservation.Revert();
            AppLog.Add("[RS] revert: " + (r.Ok ? r.Info : r.Error));
            ResvDone(r.Ok ? L.T("Reverted and read back. Restart Windows to apply.") : "! " + r.Error, !r.Ok);
        }

        private void ResvVerify()
        {
            var t = T; if (t == null) return;
            var sp = t.Split;
            var lines = new List<string>();
            bool pass = true;
            Action<bool, string> chk = (ok, txt) => { lines.Add((ok ? "OK   " : "!!   ") + txt); if (!ok) pass = false; };
            lines.Add(L.F("Family: {0}", Explain.SplitFamily(sp)));
            if (!sp.CanSplit)
            {
                lines.Add("--   " + Explain.SplitWhy(sp));
                ResvDone(string.Join("\n", lines), sp.Family == "ABORT");
                return;
            }
            if (sp.Family == "A") chk(sp.Reported == sp.Expected, L.F("LP count {0}, {1} expects {2}.", sp.Reported, sp.Sku, sp.Expected));
            else chk(sp.Reported == sp.Probe && CountBits(sp.Game) + CountBits(sp.NonGame) == sp.Reported, L.F("LP count {0} = {1} P-core LP + {2} E-core LP.", sp.Reported, CountBits(sp.Game), CountBits(sp.NonGame)));
            chk(sp.Problems.Count == 0, L.T("Game and non-game sets: disjoint, cover every LP, SMT pairs whole."));
            var st = Reservation.Read();
            if (st.Kind == Reservation.Kind.NotSet) lines.Add("--   " + L.T("ReservedCpuSets: not set (optimizer-only setup)."));
            else if (st.Kind != Reservation.Kind.Binary) chk(false, L.T("ReservedCpuSets: unreadable or not REG_BINARY."));
            else if (st.Mask == sp.NonGame) chk(true, L.F("ReservedCpuSets = NON-GAME ({0}).", st.Pending ? L.T("pending reboot") : L.T("active")));
            else if ((st.Mask & sp.Game) != 0) chk(false, L.F("ReservedCpuSets {0} overlaps the game set - Revert.", Masks.Hex(st.Mask)));
            else lines.Add("--   " + L.F("ReservedCpuSets {0} is not the non-game set.", Masks.Hex(st.Mask)));
            if (st.Pending) lines.Add("--   " + L.T("Written after the last boot: restart, then Verify again."));
            int seen = 0;
            foreach (var exe in Settings.WatchList())
            {
                string bn = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe.Substring(0, exe.Length - 4) : exe;
                System.Diagnostics.Process[] ps;
                try { ps = System.Diagnostics.Process.GetProcessesByName(bn); } catch { continue; }
                foreach (var pr in ps)
                {
                    using (pr)
                    {
                        seen++;
                        int lvl, err;
                        IntPtr hp = Native.OpenProcessGraceful((uint)pr.Id, out lvl, out err);
                        if (hp == IntPtr.Zero) { lines.Add("--   " + L.F("{0} PID {1}: affinity not readable.", exe, pr.Id)); continue; }
                        try
                        {
                            ulong a = Native.GetProcAffinity(hp);
                            chk((a & ~sp.Game) == 0, L.F("{0} PID {1}: affinity {2} (LP {3}) inside the game set.", exe, pr.Id, Masks.Hex(a), Masks.Ranges(a)));
                            chk(CoreSplit.SmtWhole(t, a), L.F("{0} PID {1}: no SMT pair split.", exe, pr.Id));
                        }
                        finally { Native.CloseHandle(hp); }
                    }
                }
            }
            if (seen == 0) lines.Add("--   " + L.T("No target is running: start the game with the optimizer on the game set, then Verify again."));
            lines.Add("--   " + L.T("This checks masks, not performance. Task Manager > Details > cs2.exe > Set affinity shows the same LPs."));
            AppLog.Add("[RS] verify: " + (pass ? "pass" : "FAIL"));
            ResvDone(string.Join("\n", lines), !pass);
        }
    }
}
