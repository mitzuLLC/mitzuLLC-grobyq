using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Grobyq
{
    // Main section. Default view: state, the one-switch System thread optimization and Live threads. Everything
    // manual (targets, hot threads, background list, presets, thread rules, .ini import) sits in one collapsed
    // Advanced group: hidden, but active.
    internal sealed partial class ThreadsPage : Page
    {
        private readonly MainForm M;
        private Btn _toggle, _apply;
        private Label _state, _what, _stat;
        private DataGridView _bg;                // background-process list
        private bool _bgBuilding;

        private DropBox _pick, _cls, _io, _mem, _eco, _pboost, _hot1, _hotN;
        private Toggle _enabled;                 // per-process on/off for the "programs & services" bulk pass
        private Toggle _prot;                     // "Priority on protected processes" toggle (unlock gate)
        private SettingRow _clsRow, _ioRow, _memRow;
        private DataGridView _rules;
        private bool _loading;

        private DataGridView _procs, _thr;
        private readonly Dictionary<int, DateTime> _procPrev = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, TimeSpan> _procCpu = new Dictionary<int, TimeSpan>();
        private int _selPid = -1;
        private bool _menuOpen;

        // Stored keys (presets.json) and their translated labels. "TimeCritical" (15) is offered only with Advanced
        // thread mode on (or when a saved rule already uses it, so the editor never loses a value).
        private string[] PrioKeys, PrioText;
        private readonly string[] MatchText, BoostText;
        private string PText(string k) { int i = Array.FindIndex(PrioKeys, x => string.Equals(x, k ?? "", StringComparison.OrdinalIgnoreCase)); return i < 0 ? PrioText[0] : PrioText[i]; }
        private string PKey(string t) { int i = Array.IndexOf(PrioText, t); return i < 0 ? "" : PrioKeys[i]; }

        public ThreadsPage(MainForm m)
        {
            M = m;
            BuildPrioChoices();
            MatchText = new[] { L.T("description"), L.T("module") };
            BoostText = new[] { L.T("keep"), L.T("Off"), L.T("On") };
            ColumnMax = 0;          // full-width layout (wide, like the reference), esp. for Live threads
            BuildHead();
            BuildSystemOpt();       // one-switch built-in system thread profile (baked-in .ini, priority-only)
            BuildLive();
            _adv3 = Group("settings", L.T("Advanced"), L.T("Target processes, hot threads, background list, presets, thread rules, .ini import"));
            Add(new TextBlock(L.T("Advanced - the built-in System thread optimization already covers this for most users."), Theme.Faint, 9f), 6)
                .Margin = new Padding(Theme.D(4), 0, Theme.D(4), Theme.D(4));
            BuildProfiles();        // named profiles, export / import, .ini import, full reset
            BuildOptimizer();
            BuildBgList();          // background toggles + process list (priority lowering)
            BuildPreset();
            BuildRules();
            EndGroup();
            FillPresetPick(null);
            M.StateChanged += UpdateState;
            M.SettingsChanged += ResyncToggles;
#if DEMO
            if (Environment.GetEnvironmentVariable("GROBYQ_DEMO_ADV") == "1") _adv3.Open = true;
#endif
        }

        private void BuildPrioChoices()
        {
            var k = new List<string> { "", "Idle", "Lowest", "BelowNormal", "Normal", "AboveNormal", "Highest" };
            var t = new List<string> { L.T("keep"), L.T("Idle"), L.T("Lowest"), L.T("Below normal"), L.T("Normal"), L.T("Above normal"), L.T("High (Highest)") };
            bool used = M.Eng.Presets.Values.Any(p => p.Threads.Any(r => string.Equals(r.Prio, "TimeCritical", StringComparison.OrdinalIgnoreCase))
                || string.Equals(p.HotFirst, "TimeCritical", StringComparison.OrdinalIgnoreCase) || string.Equals(p.HotOthers, "TimeCritical", StringComparison.OrdinalIgnoreCase));
            if (Engine.AdvancedThreadMode || used) { k.Add("TimeCritical"); t.Add(L.T("Time critical (15) - Advanced")); }
            PrioKeys = k.ToArray(); PrioText = t.ToArray();
        }

        // Advanced thread mode switched: rebuild the priority pickers (hot threads + the rules column).
        private void RefreshPrioChoices()
        {
            BuildPrioChoices();
            if (_hot1 != null) { _hot1.Items.Clear(); _hot1.Items.AddRange(PrioText); }
            if (_hotN != null) { _hotN.Items.Clear(); _hotN.Items.AddRange(PrioText); }
            if (_rules != null) { var col = (DropColumn)_rules.Columns[3]; col.Items.Clear(); col.Items.AddRange(PrioText); }
            LoadPreset();
        }

        // ------------------------------------------------------------------ head: title + state card
        // No banner and no logo here: the switch, Apply now and what the optimizer is doing right now.
        private Card _head;
        private void BuildHead()
        {
            Title(L.T("Threads"));
            _head = Box(14);
            _head.AutoHeight = false; _head.Height = Theme.D(102);
            _toggle = new Btn(L.T("ENABLE"), Btn.Style.Ok, Theme.D(150)) { Height = Theme.D(36) };
            _toggle.Font = Theme.Semi(11f);
            _toggle.Click += (s, e) => M.SetActive(!M.Active);
            _apply = new Btn(L.T("Apply now"), Btn.Style.Normal, Theme.D(120)) { Height = Theme.D(36) };
            _apply.Click += (s, e) => M.ApplyNow();
            _state = Theme.Lbl(L.T("OFF"), Theme.Semi(15f), Theme.Bad);
            _what = Theme.Lbl("", Theme.F(9.5f), Theme.Dim);
            _stat = Theme.Lbl("", Theme.F(9f), Theme.Faint);
            foreach (Control c in new Control[] { _toggle, _apply, _state, _what, _stat }) { c.BackColor = c is Btn ? Color.Transparent : Theme.Card; _head.Controls.Add(c); }
            _head.Layout += (s, e) => LayoutHero();
            LayoutHero();
        }

        private void LayoutHero()
        {
            if (_head == null) return;
            int x = _head.Padding.Left, y = Theme.D(12);
            _toggle.Fit();
            _toggle.Location = new Point(x, y);
            _apply.Location = new Point(_toggle.Right + Theme.D(12), y);
            _state.Location = new Point(_apply.Right + Theme.D(16), y + (_toggle.Height - _state.Height) / 2);
            _what.Location = new Point(x + Theme.D(1), _toggle.Bottom + Theme.D(9));
            _stat.Location = new Point(x + Theme.D(1), _what.Bottom + Theme.D(2));
        }

        private void UpdateState()
        {
            string st = M.StateName;
            _state.Text = M.StateText;
            _state.ForeColor = st == "ACTIVE" ? Theme.Ok : st == "ARMED" ? Theme.Warn : Theme.Bad;
            _toggle.Text = M.Active ? L.T("DISABLE") : L.T("ENABLE");
            _toggle.Look = M.Active ? Btn.Style.Danger : Btn.Style.Ok;
            _toggle.Invalidate();
            _what.Text = st == "ACTIVE" ? L.F("optimizing {0}", M.LiveTarget) : st == "ARMED" ? L.F("armed - waiting for {0}", string.Join(", ", M.Eng.Targets)) : L.T("optimizer disabled - nothing is changed");
            _stat.Text = L.F("threads {0}   ·   background {1}   ·   {2}", M.Eng.LastThreads, M.Eng.LastBg, M.Eng.BgOn ? L.T("background pinned") : L.T("background released"));
            LayoutHero();
        }

        // ------------------------------------------------------------------ optimizer
        private void BuildOptimizer()
        {
            Title(L.T("Optimizer"));
            var card = NewCard();

            var watch = _watch = Theme.Box(Theme.D(300), string.Join(" ", Settings.WatchList()));
            var save = new Btn(L.T("Save"), Btn.Style.Normal, Theme.D(80));
            save.Click += (s, e) => { Settings.Set("WatchList", watch.Text.Trim().Length > 0 ? watch.Text.Trim() : "cs2.exe"); M.RetargetWatchList(); FillPresetPick(null); };
            card.Stack(new SettingRow(L.T("Target processes"), L.T("Games grobyq waits for (space separated). When one starts, its preset and the core mask are applied; when it exits, the optimizer goes back to armed."), new Row(watch, save)));

            var top = _top = Theme.Num(Theme.D(80), 1, 32, Settings.GetInt("TopN", 4));
            top.ValueChanged += (s, e) => Settings.Set("TopN", ((int)top.Value).ToString());
            card.Stack(new SettingRow(L.T("Hot threads"), Explain.Hot, top));

            card.Stack(ToggleRow(L.T("Optimize threads"), L.T("A scheduler hint for the game: Ideal CPU plus a priority on its busiest threads (named rules and hot threads). Secondary to the System thread optimization profile. Off = only the process priority and the core mask."), "DoThreads"));
            card.Stack(ToggleRow(L.T("Avoid CPU 0 for hot threads"), L.T("On 6+ core CPUs no hot thread gets CPU 0 as its Ideal CPU (interrupt-heavy by default)."), "SkipCpu0"));
            card.Stack(ToggleRow(L.T("Per-process affinity rules"), L.T("Apply the per-process affinity rules from Affinity > Processes (e.g. VCache+NoSMT for cs2.exe, fixed LPs for voice / VPN tools)."), "DoAffRules"));
            card.Stack(ToggleRow(L.T("Thread presets for programs and services"), L.T("Apply the thread priority rules of every preset, not only the game's (NVIDIA containers, Steam, Discord, services). Priority and boost only; restored by DISABLE."), "ApplyAllPresets",
                on =>
                {
                    // takes effect at once, game running or not: apply the whole set, or put it back
                    if (!M.Active) { M.Log("[i] thread presets for programs & services: " + (on ? "on (turn the optimizer ON to apply)" : "off")); return; }
                    if (on) M.ApplyNow();
                    else { try { M.Log(M.Eng.Revert()); } catch { } M.ApplyNow(); }
                }));
            _prot = new Toggle(Settings.GetBool("AllowProtectedPriority"));
            _prot.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                if (_prot.Checked && !Settings.GetBool("AllowProtectedPriority"))
                {
                    if (!Dlg.Ask(this, L.T("Allow priority rules on protected system processes (dwm, svchost, audiodg, csrss, winlogon...)?\n\nOnly thread priority and boost change. A wrong value can make the desktop, sound or input sluggish until DISABLE or a reboot. Test one change at a time and measure frame-time.")))
                    { _loading = true; _prot.Checked = false; _loading = false; return; }
                }
                Settings.SetBool("AllowProtectedPriority", _prot.Checked);
                M.Log("[i] priority on protected processes: " + (_prot.Checked ? "ON (advanced)" : "off"));
                if (!_prot.Checked && Settings.GetBool("SystemThreadOpt"))
                    M.Log("[i] system thread optimization stays on, but now skips protected processes (dwm, svchost, audiodg...)");
                if (M.Active) M.ApplyNow();
            };
            card.Stack(new SettingRow(L.T("Priority on protected processes (advanced)"), L.T("Lets thread presets set priority inside dwm, svchost, audiodg and other protected processes. Off by default. Priority is reversible; nothing is frozen or killed."), _prot));
            var adv = _adv = new Toggle(Settings.GetBool("AdvancedThreadMode"));
            adv.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                if (adv.Checked && !Dlg.Ask(this, L.T("TimeCritical (15) usually HURTS frame-time: it can starve the scheduler and cause the very hitching you are removing. Prefer Highest (+2). Measure frame-time A/B before keeping.") + "\n\n" + L.T("Offer Time critical (15) in the thread rules?"), true))
                { _loading = true; adv.Checked = false; _loading = false; return; }
                Settings.SetBool("AdvancedThreadMode", adv.Checked);
                M.Log("[i] advanced thread mode: " + (adv.Checked ? "ON - TimeCritical (15) selectable" : "off - TimeCritical rules run as Highest (+2)"));
                RefreshPrioChoices();
                if (M.Active) M.ApplyNow();
            };
            card.Stack(new SettingRow(L.T("Advanced thread mode (TimeCritical)"), L.T("Adds Time critical (15) to the thread priority lists. Off = a TimeCritical rule runs as Highest (+2). Usually hurts frame-time; measure A/B."), adv));
            card.Stack(ToggleRow(L.T("Re-apply periodically"), L.T("Every few seconds, while the game runs: catches new threads and anything that reset the priority."), "PeriodicReapply"));
            var per = _per = Theme.Num(Theme.D(80), 1, 60, Math.Max(1, Settings.GetInt("Period", 2000) / 1000));
            per.ValueChanged += (s, e) => Settings.Set("Period", ((int)per.Value * 1000).ToString());
            var r2 = card.Stack(new SettingRow(L.T("Re-apply period, seconds"), L.T("2 s is the balance: 1 s re-scans more than it helps, 5 s can miss threads created while a map loads."), per)); r2.Divider = false;
        }

        private SettingRow ToggleRow(string title, string desc, string key, Action<bool> after = null)
        {
            var t = new Toggle(Settings.GetBool(key));
            t.CheckedChanged += (s, e) => { if (_resync) return; Settings.SetBool(key, t.Checked); if (after != null) after(t.Checked); };
            _bound.Add(new KeyValuePair<string, Toggle>(key, t));
            return new SettingRow(title, desc, t);
        }

        // After a profile load / reset: every switch on the page shows the stored value again (no side effects).
        private readonly List<KeyValuePair<string, Toggle>> _bound = new List<KeyValuePair<string, Toggle>>();
        private bool _resync;
        private TextBox _watch;
        private Stepper _per, _top;
        private Toggle _adv;
        private void ResyncToggles()
        {
            _resync = true; _loading = true;
            try
            {
                foreach (var kv in _bound) kv.Value.Checked = Settings.GetBool(kv.Key);
                if (_prot != null) _prot.Checked = Settings.GetBool("AllowProtectedPriority");
                if (_sysOpt != null) _sysOpt.Checked = Settings.GetBool("SystemThreadOpt");
                if (_adv != null) _adv.Checked = Settings.GetBool("AdvancedThreadMode");
                if (_watch != null) _watch.Text = string.Join(" ", Settings.WatchList());
                if (_per != null) _per.Value = Math.Max(1, Math.Min(60, Settings.GetInt("Period", 2000) / 1000));
                if (_top != null) _top.Value = Math.Max(1, Math.Min(32, Settings.GetInt("TopN", 4)));
            }
            finally { _resync = false; _loading = false; }
            M.Eng.LoadSystemProfile(Settings.GetBool("SystemThreadOpt"));
            RefreshPrioChoices();
        }

        // ------------------------------------------------------------------ profiles
        // One card, as wide as the Home license card (the page width): header, the active profile + picker, profile actions, a File
        // row, and the full reset in its own inner strip. Picking a profile in the list loads it (with the same
        // confirmation as Load); Load reloads the picked one.
        private DropBox _profPick;
        private TwoLine _profActive;
        private Btn _profLoad, _profDup, _profDel;
        private bool _profFilling;
        private void BuildProfiles()
        {
            var card = NewCard();
            card.Padding = new Padding(Theme.D(20), Theme.D(10), Theme.D(20), Theme.D(16));

            // A. header
            card.Stack(new SubHead(L.T("Profiles")) { Height = Theme.D(28), Font = Theme.Semi(11.5f), ForeColor = Theme.Fg });
            card.Stack(new TextBlock(L.T("Presets, thread rules, affinity, the background list, the core mask and the switches on this page. Loading replaces the current setup."), Theme.Dim, 9f));
            card.Stack(new Spacer(10));

            // B. active profile + picker
            var ar = card.Stack(new LineRow(Theme.D(52)));
            _profActive = ar.L(new TwoLine(L.T("Active profile"), ""));
            ar.Fill = _profActive;
            _profPick = ar.R(Theme.Combo(Theme.D(280)));
            _profPick.SelectedIndexChanged += (s, e) => { if (!_profFilling) ProfPicked(); };
            card.Stack(new Spacer(6));

            // C. profile actions, one row, equal height
            var acts = card.Stack(new WrapRow());
            int bh = Theme.D(36);
            acts.Add(new Btn(L.T("Save as…"), Btn.Style.Accent) { Height = bh }).Click += (s, e) => ProfSaveAs();
            _profLoad = acts.Add(new Btn(L.T("Load"), Btn.Style.Outline) { Height = bh });
            _profLoad.Click += (s, e) => { if (!ProfLoad()) FillProfiles(null); };
            _profDup = acts.Add(new Btn(L.T("Duplicate"), Btn.Style.Outline) { Height = bh });
            _profDup.Click += (s, e) => ProfDuplicate();
            _profDel = acts.Add(new Btn(L.T("Delete"), Btn.Style.DangerQuiet) { Height = bh, DisabledOutline = true });
            _profDel.Click += (s, e) => ProfDelete();

            // D. hairline
            card.Stack(new Hairline());

            // E. file row
            card.Stack(new SubHead(L.T("File")) { Height = Theme.D(22), TextAlign = ContentAlignment.TopLeft });
            var files = card.Stack(new WrapRow());
            files.Add(new Btn(L.T("Export…"), Btn.Style.Outline) { Height = bh }).Click += (s, e) => ExportProfile();
            files.Add(new Btn(L.T("Import…"), Btn.Style.Outline) { Height = bh }).Click += (s, e) => ImportProfile();
            files.Add(new Btn(L.T("Import .ini"), Btn.Style.Outline) { Height = bh }).Click += (s, e) => ImportThreadIni();
            files.Add(new Btn(L.T("Presets folder"), Btn.Style.Outline) { Height = bh }).Click += (s, e) => { try { Directory.CreateDirectory(Store.Dir); Process.Start(Store.Dir); } catch { } };
            card.Stack(new Spacer(14));

            // F. full reset, its own inner strip
            var fill = Color.FromArgb(12, 12, 15);
            var inner = card.Stack(new Card { Radius = 10, Fill = fill, BackColor = fill, Padding = new Padding(Theme.D(16), Theme.D(6), Theme.D(14), Theme.D(6)) });
            var reset = new Btn(L.T("Reset to Windows"), Btn.Style.DangerQuiet) { Height = bh };
            reset.Click += (s, e) => FullReset();
            var rr = inner.Stack(new SettingRow(L.T("Full reset"), L.T("Optimizer off, every process and thread grobyq changed back to Windows, affinity rules and background pins cleared, built-in presets restored, active profile cleared."), reset) { Divider = false });
            rr.BackColor = fill;
            FillProfiles(null);
        }

        private string ProfSel() { return _profPick.SelectedItem == null ? "" : _profPick.SelectedItem.ToString(); }

        private void FillProfiles(string select)
        {
            string active = Settings.Get("ActiveProfile");
            bool hasActive = active.Length > 0 && Store.ProfileExists(active);
            _profFilling = true;
            try
            {
                _profPick.Items.Clear();
                foreach (var n in Store.ListProfiles()) _profPick.Items.Add(n);
                Theme.Select(_profPick, select ?? (hasActive ? active : ""));
                if (_profPick.SelectedIndex < 0 && _profPick.Items.Count > 0 && hasActive) Theme.Select(_profPick, active);
            }
            finally { _profFilling = false; }
            if (_profPick.Items.Count == 0) { _profActive.Value = L.T("No saved profiles yet"); _profActive.ValueColor = Theme.Faint; }
            else if (hasActive) { _profActive.Value = active; _profActive.ValueColor = Theme.Fg; }
            else { _profActive.Value = L.T("Unsaved setup"); _profActive.ValueColor = Theme.Dim; }
            bool sel = ProfSel().Length > 0;
            _profPick.Enabled = _profPick.Items.Count > 0;
            _profLoad.Enabled = sel; _profDup.Enabled = sel; _profDel.Enabled = sel;
        }

        // picked in the list = Load (same confirmation); cancelled = the list goes back to the active profile
        private void ProfPicked()
        {
            string name = ProfSel();
            if (name.Length == 0) return;
            if (string.Equals(name, Settings.Get("ActiveProfile"), StringComparison.OrdinalIgnoreCase)) { FillProfiles(name); return; }
            if (!ProfLoad()) FillProfiles(null);
        }

        private void ProfSaveAs()
        {
            string def = ProfSel().Length > 0 ? ProfSel() : string.Join("+", M.Eng.Targets.Select(t => t.Replace(".exe", "")));
            string name = Dlg.Prompt(this, L.T("Save profile"), L.T("Profile name:"), def);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = Store.SafeName(name);
            if (name.Length == 0) { Dlg.Info(this, L.T("That name cannot be used as a file name.")); return; }
            if (Store.ProfileExists(name) && !Dlg.Ask(this, L.F("Replace the saved profile {0}?", name))) return;
            string err = Store.SaveNamedProfile(name, M.Eng);
            M.Log(err.Length == 0 ? "[PF] profile saved: " + name : "[!!] profile: " + err);
            FillProfiles(name);
        }

        private bool ProfLoad()
        {
            string name = ProfSel(); if (name.Length == 0) return false;
            if (!Dlg.Ask(this, L.F("Load profile {0}? Presets, rules, affinity rules, the background list and the switches are replaced.", name))) return false;
            try { M.Log(Store.LoadNamedProfile(name, M.Eng)); }
            catch (Exception ex) { M.Log("[!!] profile: " + ex.Message); FillProfiles(null); return false; }
            AfterProfileLoad();
            return true;
        }

        private void ProfDuplicate()
        {
            string name = ProfSel(); if (name.Length == 0) return;
            string to = Dlg.Prompt(this, L.T("Duplicate profile"), L.T("Profile name:"), name + " 2");
            if (string.IsNullOrWhiteSpace(to)) return;
            to = Store.SafeName(to);
            if (to.Length == 0) return;
            if (Store.ProfileExists(to) && !Dlg.Ask(this, L.F("Replace the saved profile {0}?", to))) return;
            string err = Store.DuplicateProfile(name, to);
            M.Log(err.Length == 0 ? "[PF] profile duplicated: " + name + " -> " + to : "[!!] profile: " + err);
            FillProfiles(to);
        }

        private void ProfDelete()
        {
            string name = ProfSel(); if (name.Length == 0) return;
            if (!Dlg.Ask(this, L.F("Delete the saved profile {0}? The current setup is not changed.", name), true)) return;
            string err = Store.DeleteProfile(name);
            M.Log(err.Length == 0 ? "[PF] profile deleted: " + name : "[!!] profile: " + err);
            FillProfiles(null);
        }

        // A loaded / imported profile: the gated switches are asked for, the page shows the new setup, and a running
        // optimizer drops the old setup's changes before applying the new one.
        private void AfterProfileLoad()
        {
            foreach (var k in Store.LastImportWants.ToList())
            {
                if (k == "AllowProtectedPriority" && Dlg.Ask(this, L.T("This profile uses priority on protected system processes (dwm, svchost, audiodg...). Allow it on this PC?\n\nThread priority and boost only - reversible; nothing is frozen or killed.")))
                    Settings.SetBool(k, true);
                if (k == "AdvancedThreadMode" && Dlg.Ask(this, L.T("TimeCritical (15) usually HURTS frame-time: it can starve the scheduler and cause the very hitching you are removing. Prefer Highest (+2). Measure frame-time A/B before keeping.") + "\n\n" + L.T("This profile uses Advanced thread mode. Turn it on?"), true))
                    Settings.SetBool(k, true);
            }
            M.RetargetWatchList();
            FillPresetPick(null);
            RefreshBg();
            ResyncToggles();
            M.NotifyMask();
            FillProfiles(null);
            // the old setup's changes go back to their baselines (a removed affinity rule, a dropped preset), optimizer on or off
            if (M.Active || M.Eng.HasChanges) { try { M.Log(M.Eng.Revert()); } catch { } }
            if (M.Active) M.ApplyNow();
        }


        // ------------------------------------------------------------------ system thread optimization (one switch)
        private Toggle _sysOpt;
        private void BuildSystemOpt()
        {
            Title(L.T("System thread optimization"));
            var card = NewCard();
            card.Stack(new TextBlock(L.T("One switch. Applies grobyq's built-in thread-priority profile across the whole system - games, NVIDIA / Steam / Discord and background services get the tuned priorities. Priority and boost only, fully reversible: DISABLE or Reset restores Windows defaults. Nothing is frozen or killed."), Theme.Dim, 9.5f));
            card.Stack(new TextBlock(L.T("The profile also tunes protected system processes (dwm, svchost, audiodg); grobyq asks once before unlocking them."), Theme.Faint, 9f));
            _sysOpt = new Toggle(Settings.GetBool("SystemThreadOpt"));
            _sysOpt.CheckedChanged += (s, e) => SystemOptChanged();
            var row = card.Stack(new SettingRow(L.T("Enable system thread optimization"), null, _sysOpt));
            row.Divider = false;
        }

        private void SystemOptChanged()
        {
            if (_loading) return;
            bool on = _sysOpt.Checked;
            if (on && !Settings.GetBool("AllowProtectedPriority"))
            {
                if (!Dlg.Ask(this, L.T("System thread optimization also tunes protected system processes (dwm, svchost, audiodg, csrss, winlogon...). Enable 'Priority on protected processes' and turn it on?\n\nPriority and boost only - reversible; nothing is frozen or killed.")))
                { _loading = true; _sysOpt.Checked = false; _loading = false; return; }
                Settings.SetBool("AllowProtectedPriority", true);
                if (_prot != null) { _loading = true; _prot.Checked = true; _loading = false; }
            }
            Settings.SetBool("SystemThreadOpt", on);
            if (on)
            {
                M.Eng.LoadSystemProfile(true);
                M.Log(string.Format("[i] system thread optimization ON ({0} profiles, {1} background processes, priority-only, 300/200 -> Idle, anti-cheats skipped)", M.Eng.SystemPresets.Count, M.Eng.SystemBg.Count));
                if (M.Active) M.ApplyNow(); else M.SetActive(true);   // re-apply if already on (SetActive is a no-op then)
            }
            else
            {
                try { M.Log(M.Eng.Revert()); } catch { }
                M.Eng.LoadSystemProfile(false);
                M.Log("[i] system thread optimization OFF - restored to Windows defaults");
                if (M.Active) M.ApplyNow();
            }
        }

        // ------------------------------------------------------------------ preset
        private void BuildPreset()
        {
            Title(L.T("Process preset"));
            var card = NewCard();
            _pick = Theme.Combo(Theme.D(220));
            var bNew = new Btn(L.T("Add process / service"), Btn.Style.Accent, Theme.D(180));
            var bDel = new Btn(L.T("Delete"), Btn.Style.Normal, Theme.D(80));
            var bReset = new Btn(L.T("Built-in"), Btn.Style.Normal, Theme.D(90));
            card.Stack(new SettingRow(L.T("Process / service"), L.T("Pick a process or service to tune, or add one. Each has its own priority, I/O, memory, EcoQoS and thread rules. Services are threads inside svchost - target them by module / description in the rules below. Changes save at once."), new Row(_pick, bNew, bDel, bReset)));
            bNew.Click += (s, e) => NewPreset(null);
            bDel.Click += (s, e) => DeletePreset();
            bReset.Click += (s, e) => ResetPreset();
            _pick.SelectedIndexChanged += (s, e) => LoadPreset();

            _enabled = new Toggle(true);
            _enabled.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                var p = Cur; if (p == null) return;
                p.Enabled = _enabled.Checked;
                Persist("preset " + p.Exe + ": " + (p.Enabled ? "enabled" : "disabled"));
                if (M.Active && Settings.GetBool("ApplyAllPresets")) M.ApplyNow();
            };
            card.Stack(new SettingRow(L.T("Enabled"), L.T("Off = keep this process saved but skip it in the 'programs & services' pass. Game targets always apply."), _enabled));

            _cls = Theme.Combo(Theme.D(200), new Opt("High", L.T("High")), new Opt("AboveNormal", L.T("Above normal")), new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Idle", L.T("Idle")));
            _clsRow = card.Stack(new SettingRow(L.T("Priority class"), Explain.Class("High"), _cls));
            _io = Theme.Combo(Theme.D(200), new Opt("High", L.T("High")), new Opt("Normal", L.T("Normal")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")));
            _ioRow = card.Stack(new SettingRow(L.T("I/O priority"), Explain.Io("Normal"), _io));
            _mem = Theme.Combo(Theme.D(200), new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Medium", L.T("Medium")), new Opt("Low", L.T("Low")), new Opt("VeryLow", L.T("Very low")));
            _memRow = card.Stack(new SettingRow(L.T("Memory priority"), Explain.Mem("Normal"), _mem));
            _eco = Theme.Combo(Theme.D(200), new Opt("Off", L.T("Off (HighQoS)")), new Opt("Keep", L.T("Keep Windows default")));
            card.Stack(new SettingRow("EcoQoS", Explain.Eco, _eco));
            _pboost = Theme.Combo(Theme.D(200), new Opt("Keep", L.T("Keep")), new Opt("Off", L.T("Off")), new Opt("On", L.T("On")));
            card.Stack(new SettingRow(L.T("Process priority boost"), Explain.ProcBoost, _pboost));
            _hot1 = Theme.Combo(Theme.D(200), PrioText);
            card.Stack(new SettingRow(L.T("Hottest thread priority"), L.T("Priority for the busiest unnamed thread when no named rule matched (usually the main thread)."), _hot1));
            _hotN = Theme.Combo(Theme.D(200), PrioText);
            var last = card.Stack(new SettingRow(L.T("Other hot threads priority"), L.T("Priority for the remaining hot threads. keep = only the Ideal CPU is set."), _hotN));
            last.Divider = false;

            EventHandler save = (s, e) => SavePresetFields();
            foreach (var c in new[] { _cls, _io, _mem, _eco, _pboost, _hot1, _hotN }) c.SelectedIndexChanged += save;
        }

        private Preset Cur
        {
            get
            {
                if (_pick == null || _pick.SelectedItem == null) return null;
                Preset p; return M.Eng.Presets.TryGetValue(_pick.SelectedItem.ToString(), out p) ? p : null;
            }
        }

        private void FillPresetPick(string select)
        {
            _loading = true;
            try
            {
                string keep = select ?? (_pick.SelectedItem != null ? _pick.SelectedItem.ToString() : null);
                _pick.Items.Clear();
                foreach (var k in M.Eng.Presets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) _pick.Items.Add(k);
                if (keep == null) keep = M.Eng.Targets.FirstOrDefault(t => M.Eng.Presets.ContainsKey(t));
                Theme.Select(_pick, keep ?? "");
            }
            finally { _loading = false; }
            LoadPreset();
        }

        private void LoadPreset()
        {
            var p = Cur;
            _loading = true;
            try
            {
                bool on = p != null;
                foreach (var c in new Control[] { _cls, _io, _mem, _eco, _pboost, _hot1, _hotN, _rules, _enabled }) if (c != null) c.Enabled = on;
                if (!on) { if (_rules != null) _rules.Rows.Clear(); return; }
                if (_enabled != null) _enabled.Checked = p.Enabled;
                Theme.SelectVal(_cls, p.Class); Theme.SelectVal(_io, p.Io); Theme.SelectVal(_mem, p.Mem);
                Theme.SelectVal(_eco, string.Equals(p.Eco, "Keep", StringComparison.OrdinalIgnoreCase) ? "Keep" : "Off");
                Theme.SelectVal(_pboost, p.Boost);
                Theme.Select(_hot1, PText(p.HotFirst)); Theme.Select(_hotN, PText(p.HotOthers));
                _clsRow.Desc = Explain.Class(p.Class); _ioRow.Desc = Explain.Io(p.Io); _memRow.Desc = Explain.Mem(p.Mem);
                if (_rules != null) FillRules();
            }
            finally { _loading = false; }
        }

        private void SavePresetFields()
        {
            if (_loading) return;
            var p = Cur; if (p == null) return;
            p.Class = Theme.Val(_cls); p.Io = Theme.Val(_io); p.Mem = Theme.Val(_mem);
            p.Eco = Theme.Val(_eco) == "Keep" ? "Keep" : "Off";
            p.Boost = Theme.Val(_pboost);
            p.HotFirst = PKey(_hot1.Text); p.HotOthers = PKey(_hotN.Text);
            _clsRow.Desc = Explain.Class(p.Class); _ioRow.Desc = Explain.Io(p.Io); _memRow.Desc = Explain.Mem(p.Mem);
            Persist("preset " + p.Exe + ": class " + p.Class + ", io " + p.Io + ", mem " + p.Mem + ", eco " + p.Eco + ", boost " + p.Boost + ", hot " + (p.HotFirst == "" ? "keep" : p.HotFirst) + "/" + (p.HotOthers == "" ? "keep" : p.HotOthers));
        }

        private void Persist(string what)
        {
            string err = M.Eng.SavePresets();
            M.Log(err.Length == 0 ? "[PS] saved - " + what + (M.Active ? "  (next Apply uses it)" : "") : "[!!] presets.json: " + err);
        }

        private void NewPreset(string exe)
        {
            if (exe == null)
            {
                exe = Dlg.Prompt(this, L.T("New preset"), L.T("Process name (e.g. cs2.exe):"), M.Eng.Targets.FirstOrDefault(t => !M.Eng.Presets.ContainsKey(t)) ?? "");
                if (string.IsNullOrWhiteSpace(exe)) return;
            }
            exe = exe.Trim(); if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            if (Engine.IsAntiCheat(exe)) { Dlg.Info(this, L.T("Anti-cheat - grobyq does not touch it")); return; }
            if (!M.Eng.Presets.ContainsKey(exe)) M.Eng.Presets[exe] = new Preset { Exe = exe };
            Persist("new preset " + exe);
            FillPresetPick(exe);
        }

        private void DeletePreset()
        {
            var p = Cur; if (p == null) return;
            if (!Dlg.Ask(this, L.F("Delete the preset for {0}?", p.Exe), true)) return;
            M.Eng.Presets.Remove(p.Exe);
            Persist("deleted preset " + p.Exe);
            FillPresetPick(null);
        }

        private void ResetPreset()
        {
            var p = Cur; if (p == null) return;
            Preset b;
            if (!Store.Defaults().TryGetValue(p.Exe, out b)) { Dlg.Info(this, L.F("{0} has no built-in preset. Built-in: {1}", p.Exe, string.Join(", ", Store.Defaults().Keys))); return; }
            if (!Dlg.Ask(this, L.F("Reset {0} to the built-in preset? Your rules for it are replaced.", p.Exe))) return;
            M.Eng.Presets[p.Exe] = b;
            Persist("reset " + p.Exe + " to built-in");
            FillPresetPick(p.Exe);
        }

        // ------------------------------------------------------------------ rules
        private void BuildRules()
        {
            Title(L.T("Thread rules"));
            var card = NewCard();
            card.Stack(new TextBlock(L.T("Rules for the preset above, top to bottom; the first matching rule wins for a thread. Match by thread description (GetThreadDescription) or by the module its start address is in. Right-click a thread under Live threads to create a rule from it."), Theme.Dim, 9f));

            _rules = Theme.Grid();
            _rules.Height = Theme.D(230);
            _rules.EditMode = DataGridViewEditMode.EditOnEnter;
            var on = new DataGridViewCheckBoxColumn { HeaderText = L.T("On"), FillWeight = 7, FlatStyle = FlatStyle.Flat };
            _rules.Columns.Add(on);
            _rules.Columns.Add(Theme.ComboCol(L.T("Match"), 15, MatchText));
            _rules.Columns.Add(Theme.Col(L.T("Pattern (wildcards * ?)"), 34, false));
            _rules.Columns.Add(Theme.ComboCol(L.T("Priority"), 20, PrioText));
            var ideal = Theme.ComboCol(L.T("Ideal CPU"), 13, L.T("none"), "auto");
            int n = 0; try { n = SafeTopo().LogicalCount; } catch { }
            for (int i = 0; i < n; i++) ideal.Items.Add(i.ToString());
            _rules.Columns.Add(ideal);
            _rules.Columns.Add(Theme.ComboCol(L.T("Boost"), 11, BoostText));
            _rules.CurrentCellDirtyStateChanged += (s, e) => { if (_rules.IsCurrentCellDirty) _rules.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _rules.CellValueChanged += (s, e) => { if (!_loading && e.RowIndex >= 0) RuleFromRow(e.RowIndex); };
            card.Stack(_rules);

            var strip = new Strip();
            strip.Add(new Btn(L.T("Add rule"), Btn.Style.Accent)).Click += (s, e) => AddRule(new ThreadRule { Pattern = "*" });
            strip.Add(new Btn(L.T("Remove"))).Click += (s, e) => RemoveRule();
            strip.Add(new Btn(L.T("Up"), Btn.Style.Normal, Theme.D(56))).Click += (s, e) => MoveRule(-1);
            strip.Add(new Btn(L.T("Down"), Btn.Style.Normal, Theme.D(66))).Click += (s, e) => MoveRule(1);
            card.Stack(strip);

            card.Stack(new TextBlock(L.T("Priority.") + "  " + Explain.ThreadPriority, Theme.Dim, 9f));
            card.Stack(new TextBlock(L.T("Ideal CPU.") + "  " + Explain.IdealCpu, Theme.Dim, 9f));
            card.Stack(new TextBlock(L.T("Boost.") + "  " + Explain.Boost, Theme.Dim, 9f));
        }

        private string IdealText(string v) { return string.IsNullOrEmpty(v) ? L.T("none") : v; }

        private void FillRules()
        {
            var p = Cur;
            _rules.Rows.Clear();
            if (p == null) return;
            foreach (var r in p.Threads)
            {
                string ideal = IdealText(r.Ideal);
                var col = (DropColumn)_rules.Columns[4];
                if (!col.Items.Contains(ideal)) col.Items.Add(ideal);
                _rules.Rows.Add(r.Enabled, r.Match == "module" ? MatchText[1] : MatchText[0], r.Pattern, PText(r.Prio), ideal,
                                string.IsNullOrEmpty(r.Boost) ? BoostText[0] : string.Equals(r.Boost, "Off", StringComparison.OrdinalIgnoreCase) ? BoostText[1] : BoostText[2]);
            }
        }

        private void RuleFromRow(int i)
        {
            var p = Cur; if (p == null || i >= p.Threads.Count) return;
            var row = _rules.Rows[i]; var r = p.Threads[i];
            bool en = row.Cells[0].Value is bool && (bool)row.Cells[0].Value;
            string match = Convert.ToString(row.Cells[1].Value) == MatchText[1] ? "module" : "desc";
            string pat = Convert.ToString(row.Cells[2].Value ?? "").Trim();
            string prio = PKey(Convert.ToString(row.Cells[3].Value));
            string id = Convert.ToString(row.Cells[4].Value); string ideal = id == L.T("none") ? "" : id;
            string b = Convert.ToString(row.Cells[5].Value); string boost = b == BoostText[1] ? "Off" : b == BoostText[2] ? "On" : "";
            if (en == r.Enabled && match == r.Match && pat == r.Pattern && prio == (r.Prio ?? "") && ideal == (r.Ideal ?? "") && boost == (r.Boost ?? "")) return;   // editor commit, nothing changed
            r.Enabled = en; r.Match = match; r.Pattern = pat; r.Prio = prio; r.Ideal = ideal; r.Boost = boost;
            Persist(string.Format("rule {0} {1}: prio {2}, ideal {3}, boost {4}{5}", r.Match, r.Pattern, string.IsNullOrEmpty(r.Prio) ? "keep" : r.Prio, string.IsNullOrEmpty(r.Ideal) ? "none" : r.Ideal, string.IsNullOrEmpty(r.Boost) ? "keep" : r.Boost, r.Enabled ? "" : " (off)"));
        }

        private void AddRule(ThreadRule r)
        {
            var p = Cur;
            if (p == null) { Dlg.Info(this, L.T("Pick or create a preset first.")); return; }
            p.Threads.Add(r);
            _loading = true; try { FillRules(); } finally { _loading = false; }
            if (_rules.Rows.Count > 0) { _rules.ClearSelection(); _rules.Rows[_rules.Rows.Count - 1].Selected = true; _rules.CurrentCell = _rules.Rows[_rules.Rows.Count - 1].Cells[2]; }
            Persist("rule added to " + p.Exe + ": " + r.Match + " " + r.Pattern);
        }

        private void RemoveRule()
        {
            var p = Cur; if (p == null || _rules.CurrentRow == null) return;
            int i = _rules.CurrentRow.Index; if (i < 0 || i >= p.Threads.Count) return;
            var r = p.Threads[i]; p.Threads.RemoveAt(i);
            _loading = true; try { FillRules(); } finally { _loading = false; }
            Persist("rule removed: " + r.Pattern);
        }

        private void MoveRule(int d)
        {
            var p = Cur; if (p == null || _rules.CurrentRow == null) return;
            int i = _rules.CurrentRow.Index, j = i + d;
            if (i < 0 || j < 0 || j >= p.Threads.Count) return;
            var t = p.Threads[i]; p.Threads[i] = p.Threads[j]; p.Threads[j] = t;
            _loading = true; try { FillRules(); } finally { _loading = false; }
            _rules.CurrentCell = _rules.Rows[j].Cells[2];
            Persist("rule order changed");
        }

        private void ExportProfile()
        {
            using (var dlg = new SaveFileDialog { Filter = L.T("grobyq profile") + " (*.json)|*.json", FileName = "grobyq-profile.json" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, Store.ExportProfile(M.Eng)); M.Log("[EX] profile exported: " + dlg.FileName); }
                catch (Exception ex) { M.Log("[!!] export: " + ex.Message); }
            }
        }

        private void ImportProfile()
        {
            using (var dlg = new OpenFileDialog { Filter = L.T("grobyq profile") + " (*.json)|*.json|" + L.T("All files") + "|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int ans = Dlg.Show(this, L.T("How to import the profile?") + "\n\n" + L.T("Replace = presets, affinity rules and background classes are replaced.") + "\n" + L.T("Merge = same names are overwritten, the rest is kept."),
                                   false, L.T("Replace"), L.T("Merge"), L.T("Cancel"));
                if (ans < 0 || ans == 2) return;
                try { M.Log(Store.ImportProfile(File.ReadAllText(dlg.FileName), ans == 0, M.Eng)); }
                catch (Exception ex) { M.Log("[!!] import: " + ex.Message); return; }
                Settings.Set("ActiveProfile", "");
                AfterProfileLoad();
            }
        }

        // Import thread-priority presets from an AerialTool_Threads .ini (priority only; no suspend/terminate).
        private void ImportThreadIni()
        {
            using (var dlg = new OpenFileDialog { Filter = L.T("AerialTool presets") + " (*.ini;*.txt)|*.ini;*.txt|" + L.T("All files") + "|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ThreadIni.Result r;
                try { r = ThreadIni.Parse(File.ReadAllText(dlg.FileName)); }
                catch (Exception ex) { M.Log("[!!] .ini import: " + ex.Message); return; }
                int ans = ShowIniPreview(r);
                if (ans < 0 || ans == 2) return;   // closed / Cancel
                M.Log(ThreadIni.Apply(r, M.Eng, ans == 0));
                M.RetargetWatchList();
                FillPresetPick(null);
                RefreshBg();                                  // the grid shows the imported list (a later edit must not drop it)
                if (_watch != null) _watch.Text = string.Join(" ", Settings.WatchList());
            }
        }

        // Preview grid for the .ini import. Returns 0 = Replace, 1 = Merge, 2 = Cancel, -1 = closed.
        private int ShowIniPreview(ThreadIni.Result r)
        {
            int result = -1;
            using (var f = new Form
            {
                Text = Brand.Name + " · " + L.T("Import .ini presets"),
                FormBorderStyle = FormBorderStyle.Sizable, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = true, MinimizeBox = false, ShowInTaskbar = false, KeyPreview = true,
                BackColor = Theme.Bg, ForeColor = Theme.Fg, Font = Theme.F(9.5f), AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(Theme.D(880), Theme.D(540)), MinimumSize = new Size(Theme.D(640), Theme.D(380)),
            })
            {
                Theme.DarkForm(f);
                try { f.Icon = Brand.AppIcon; } catch { }

                int rules = r.Rows.Count(x => !x.Skipped && (x.Match == "desc" || x.Match == "module"));
                int anti = r.Rows.Count(x => x.AntiCheat), prot = r.Rows.Count(x => x.Protected && !x.Skipped);
                var head = Theme.Lbl(L.F("{0} presets · {1} thread rules · {2} anti-cheat skipped · {3} need Advanced · {4} background · games: {5}",
                                          r.Presets.Count, rules, anti, prot, r.BgClasses.Count, r.Games.Count > 0 ? string.Join(" ", r.Games) : "-")
                                     + (r.HadSuspendCodes ? "\n" + L.T(ThreadIni.SuspendNote) : ""),
                                     Theme.F(10f), Theme.Fg, false);
                head.Dock = DockStyle.Top; head.Height = Theme.D(r.HadSuspendCodes ? 62 : 40); head.Padding = new Padding(Theme.D(16), Theme.D(12), Theme.D(16), 0);

                var grid = Theme.Grid();
                grid.ReadOnly = true; grid.Dock = DockStyle.Fill; grid.EditMode = DataGridViewEditMode.EditProgrammatically;
                grid.Columns.Add(Theme.Col(L.T("Process"), 20));
                grid.Columns.Add(Theme.Col(L.T("Match"), 11));
                grid.Columns.Add(Theme.Col(L.T("Pattern"), 24));
                grid.Columns.Add(Theme.Col(L.T("Source"), 9));
                grid.Columns.Add(Theme.Col(L.T("Mapped"), 12));
                grid.Columns.Add(Theme.Col(L.T("Skipped / Note"), 24));
                foreach (var row in r.Rows)
                {
                    int i = grid.Rows.Add(row.Process, row.Match, row.Pattern, row.SourceValue, row.Mapped, (row.Skipped ? L.T("SKIP") + "  " : "") + row.Note);
                    // grey = anti-cheat (never touched), amber = protected (applies only with the Advanced gate)
                    if (row.AntiCheat) grid.Rows[i].DefaultCellStyle.ForeColor = Theme.Faint;
                    else if (row.Protected) grid.Rows[i].DefaultCellStyle.ForeColor = Theme.Warn;
                }
                var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.D(16), Theme.D(6), Theme.D(16), Theme.D(6)), BackColor = Theme.Bg };
                host.Controls.Add(grid);

                var btns = new Panel { Dock = DockStyle.Bottom, Height = Theme.D(52), BackColor = Theme.Bg };
                var replace = new Btn(L.T("Replace"), Btn.Style.Accent, Theme.D(110)) { Height = Theme.D(34) };
                var merge = new Btn(L.T("Merge"), Btn.Style.Normal, Theme.D(110)) { Height = Theme.D(34) };
                var cancel = new Btn(L.T("Cancel"), Btn.Style.Normal, Theme.D(96)) { Height = Theme.D(34) };
                replace.Click += (s, e) => { result = 0; f.Close(); };
                merge.Click += (s, e) => { result = 1; f.Close(); };
                cancel.Click += (s, e) => { result = 2; f.Close(); };
                btns.Layout += (s, e) =>
                {
                    int y = Theme.D(9), x = btns.ClientSize.Width - Theme.D(16);
                    x -= cancel.Width; cancel.Location = new Point(x, y); x -= Theme.D(10);
                    x -= merge.Width; merge.Location = new Point(x, y); x -= Theme.D(10);
                    x -= replace.Width; replace.Location = new Point(x, y);
                };
                btns.Controls.AddRange(new Control[] { replace, merge, cancel });

                f.Controls.Add(host); f.Controls.Add(btns); f.Controls.Add(head);
                f.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.D(4), BackColor = Theme.Acc });
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { result = 2; f.Close(); } };
                f.ShowDialog(this);
            }
            return result;
        }

        // ------------------------------------------------------------------ live
        private void BuildLive()
        {
            Title(L.T("Live threads"));
            var card = NewCard();
            card.Stack(new TextBlock(L.T("Left: processes (targets first). Right: threads of the selected process, hottest first. Right-click a thread: set priority (incl. High), Ideal CPU or boost now, or turn it into a rule. Manual changes are undone by DISABLE; a rule keeps them on every Apply."), Theme.Dim, 9f));

            var tl = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Height = Theme.D(440), BackColor = Theme.Card };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            _procs = Theme.Grid(); _procs.ReadOnly = true; _procs.Dock = DockStyle.Fill; _procs.Margin = new Padding(0, 0, Theme.D(10), 0);
            _procs.Columns.Add(Theme.Col(L.T("Process"), 40)); _procs.Columns.Add(Theme.Col("PID", 16)); _procs.Columns.Add(Theme.Col(L.T("CPU %"), 14)); _procs.Columns.Add(Theme.Col(L.T("Thr"), 12)); _procs.Columns.Add(Theme.Col(L.T("Class"), 22));
            _thr = Theme.Grid(); _thr.ReadOnly = true; _thr.Dock = DockStyle.Fill;
            _thr.Columns.Add(Theme.Col("TID", 11)); _thr.Columns.Add(Theme.Col(L.T("Cycles/s"), 17)); _thr.Columns.Add(Theme.Col(L.T("Prio"), 8)); _thr.Columns.Add(Theme.Col(L.T("Start address"), 30)); _thr.Columns.Add(Theme.Col(L.T("Description"), 22)); _thr.Columns.Add(Theme.Col(L.T("Rule"), 14));
            tl.Controls.Add(_procs, 0, 0); tl.Controls.Add(_thr, 1, 0);
            card.Stack(tl);

            _procs.SelectionChanged += (s, e) =>
            {
                if (_filling) return;
                if (_procs.CurrentRow != null) { int pid; if (int.TryParse(Convert.ToString(_procs.CurrentRow.Cells[1].Value), out pid)) { _selPid = pid; RefreshThreads(); } }
            };
            _thr.ContextMenuStrip = ThreadMenu();
            _procs.ContextMenuStrip = ProcMenu();
            _thr.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _thr.ClearSelection(); _thr.Rows[e.RowIndex].Selected = true; _thr.CurrentCell = _thr.Rows[e.RowIndex].Cells[0]; } };
            _procs.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _procs.CurrentCell = _procs.Rows[e.RowIndex].Cells[0]; } };
        }

        private bool _filling;
        private string _thrExe = "";

        private void RefreshProcs()
        {
            try { RefreshProcsCore(); }
            catch (Exception ex) { _filling = false; if (!_procErr) { _procErr = true; M.Log("[!!] live processes: " + ex.Message); } }
        }
        private bool _procErr;

        private void RefreshProcsCore()
        {
            var now = DateTime.UtcNow;
            var tgts = M.Eng.Targets.Select(t => (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? t.Substring(0, t.Length - 4) : t).ToLowerInvariant()).ToList();
            var rows = new List<object[]>();
            Process[] all; try { all = Process.GetProcesses(); } catch { all = new Process[0]; }
            var live = new HashSet<int>();
            foreach (var p in all)
            {
                try
                {
                    if (p.Id <= 4) continue;
                    string name; try { name = p.ProcessName; } catch { continue; }       // exited meanwhile
                    live.Add(p.Id);
                    double cpu = 0;
                    try
                    {
                        var tt = p.TotalProcessorTime;
                        DateTime was;
                        if (_procPrev.TryGetValue(p.Id, out was))
                        {
                            double dt = (now - was).TotalSeconds;
                            var d = tt - _procCpu[p.Id];
                            if (dt > 0.05 && d >= TimeSpan.Zero) cpu = Math.Round(d.TotalSeconds / dt / Environment.ProcessorCount * 100, 1);   // < 0 = PID reused
                        }
                        _procPrev[p.Id] = now; _procCpu[p.Id] = tt;
                    }
                    catch { }
                    string pn = "-";
                    try { string n; if (Engine.PRIONAME.TryGetValue((int)p.PriorityClass, out n)) pn = n; } catch { }
                    int thr = 0; try { thr = p.Threads.Count; } catch { }
                    bool isT = tgts.Contains(name.ToLowerInvariant());
                    rows.Add(new object[] { name + ".exe", p.Id, cpu, thr, pn, isT });
                }
                finally { p.Dispose(); }
            }
            foreach (var k in _procPrev.Keys.Where(k => !live.Contains(k)).ToList()) { _procPrev.Remove(k); _procCpu.Remove(k); }
            var shown = rows.Where(r => (bool)r[5]).ToList();
            shown.AddRange(rows.Where(r => !(bool)r[5]).OrderByDescending(r => (double)r[2]).Take(60));

            _filling = true;
            try
            {
                int first = _procs.FirstDisplayedScrollingRowIndex;
                _procs.Rows.Clear();
                foreach (var r in shown)
                {
                    int i = _procs.Rows.Add(r[0], r[1], ((double)r[2]).ToString("0.0"), r[3], r[4]);
                    if ((bool)r[5]) _procs.Rows[i].DefaultCellStyle.ForeColor = Theme.Acc;
                    if ((int)r[1] == _selPid) { _procs.CurrentCell = _procs.Rows[i].Cells[0]; }
                }
                if (_selPid < 0 && _procs.Rows.Count > 0 && (bool)shown[0][5]) { _selPid = (int)shown[0][1]; _procs.CurrentCell = _procs.Rows[0].Cells[0]; }
                if (first >= 0 && first < _procs.Rows.Count) try { _procs.FirstDisplayedScrollingRowIndex = first; } catch { }
            }
            finally { _filling = false; }
            if (_selPid > 0 && !shown.Any(r => (int)r[1] == _selPid)) _selPid = -1;
        }

        private void RefreshThreads()
        {
            try { RefreshThreadsCore(); }
            catch (Exception ex) { _filling = false; if (!_thrErr) { _thrErr = true; M.Log("[!!] live threads: " + ex.Message); } }
        }
        private bool _thrErr;

        private void RefreshThreadsCore()
        {
            _filling = true;
            try
            {
                int first = _thr.FirstDisplayedScrollingRowIndex;
                uint selTid = 0;
                if (_thr.CurrentRow != null) uint.TryParse(Convert.ToString(_thr.CurrentRow.Cells[0].Value), out selTid);
                _thr.Rows.Clear();
                if (_selPid <= 0) return;
                try { using (var sp = Process.GetProcessById(_selPid)) _thrExe = sp.ProcessName + ".exe"; }
                catch { _thrExe = ""; _selPid = -1; return; }   // it exited: clear, never show "access denied" for a process that quit
                if (Engine.IsAntiCheat(_thrExe)) { _thr.Rows.Add("-", "", "", L.T("anti-cheat process: grobyq never opens or changes it"), "", ""); return; }
                var snap = M.Eng.Snapshot(_selPid);
                if (!snap.Ok) { _thr.Rows.Add("-", L.T("access denied"), "", L.F("kernel anti-cheat strips handle rights (err {0})", snap.Err), "", ""); return; }
                Preset preset; M.Eng.Presets.TryGetValue(_thrExe, out preset);
                int n = 0, topN = Settings.GetInt("TopN", 4);
                foreach (var r in snap.Rows)
                {
                    if (n >= 80) break;
                    string rule = "";
                    if (preset != null)
                        foreach (var ru in preset.Threads) if (Engine.RuleHits(ru, r.Desc, r.Sym)) { rule = (ru.Match == "module" ? "mod " : "") + ru.Pattern; break; }
                    int i = _thr.Rows.Add(r.Tid, r.Rate.ToString("N0"), r.Prio, r.Sym, r.Desc, rule);
                    if (rule.Length > 0) _thr.Rows[i].DefaultCellStyle.ForeColor = Theme.Ok;
                    else if (n < topN) _thr.Rows[i].DefaultCellStyle.ForeColor = Theme.Acc;
                    if (r.Tid == selTid) _thr.CurrentCell = _thr.Rows[i].Cells[0];
                    n++;
                }
                if (first >= 0 && first < _thr.Rows.Count) try { _thr.FirstDisplayedScrollingRowIndex = first; } catch { }
            }
            finally { _filling = false; }
        }

        private ContextMenuStrip ThreadMenu()
        {
            var m = Theme.Menu();
            m.Opening += (s, e) =>
            {
                m.Items.Clear();
                var row = _thr.CurrentRow;
                uint tid;
                if (row == null || !uint.TryParse(Convert.ToString(row.Cells[0].Value), out tid)) { e.Cancel = true; return; }
                _menuOpen = true;
                string desc = Convert.ToString(row.Cells[4].Value), sym = Convert.ToString(row.Cells[3].Value);
                string why = Engine.Gate(_thrExe, Engine.Op.Thread, Engine.ProtectedPriorityAllowed);
                if (why == null && Engine.IsAntiCheat(Engine.ModuleOf(sym))) why = "anti-cheat module";
                if (why != null)
                {
                    m.Items.Add(new ToolStripMenuItem(Engine.IsProtected(_thrExe) && !Engine.IsAntiCheat(_thrExe) ? L.T("Protected system process - turn on 'Priority on protected processes' (Advanced) first") : L.T("Anti-cheat - grobyq does not touch it")) { Enabled = false });
                    return;
                }
                var pr = new ToolStripMenuItem(L.T("Priority"));
                for (int i = 1; i < PrioKeys.Length; i++)
                {
                    string k = PrioKeys[i];
                    if (k == "TimeCritical" && !Engine.AdvancedThreadMode) continue;      // live edits: 15 only in Advanced mode
                    pr.DropDownItems.Add(PrioText[i], null, (a, b) => M.Log(M.Eng.ThreadManual(_selPid, tid, k, null, null)));
                }
                m.Items.Add(pr);
                if (Engine.IsProtected(_thrExe)) goto boostOnly;          // protected: priority and boost only
                var id = new ToolStripMenuItem(L.T("Ideal CPU"));
                Topo t = SafeTopo();
                ulong gm = 0; try { gm = GameMask.Effective(t).Mask; } catch { }
                if (t != null)
                    foreach (var s2 in t.Sets.OrderBy(x => x.LP))
                    {
                        int lp = s2.LP;
                        id.DropDownItems.Add("CPU " + lp + (((gm >> lp) & 1) != 0 ? "   (" + L.T("game mask") + ")" : ""), null, (a, b) => M.Log(M.Eng.ThreadManual(_selPid, tid, null, lp.ToString(), null)));
                    }
                m.Items.Add(id);
                boostOnly:
                var bo = new ToolStripMenuItem(L.T("Priority boost"));
                bo.DropDownItems.Add(L.T("Off (fixed priority)"), null, (a, b) => M.Log(M.Eng.ThreadManual(_selPid, tid, null, null, "Off")));
                bo.DropDownItems.Add(L.T("On (Windows default)"), null, (a, b) => M.Log(M.Eng.ThreadManual(_selPid, tid, null, null, "On")));
                m.Items.Add(bo);
                m.Items.Add(new ToolStripSeparator());
                if (!string.IsNullOrEmpty(desc))
                    m.Items.Add(L.T("Make rule:  description =") + " " + desc, null, (a, b) => RuleFromLive("desc", desc));
                string mod = Engine.ModuleOf(sym);
                if (mod.Length > 0)
                    m.Items.Add(L.T("Make rule:  module =") + " " + mod, null, (a, b) => RuleFromLive("module", mod));
                if (string.IsNullOrEmpty(desc) && mod.Length == 0) m.Items.Add(new ToolStripMenuItem(L.T("No description / module - cannot make a rule")) { Enabled = false });
            };
            m.Closed += (s, e) => { _menuOpen = false; };
            return m;
        }

        private ContextMenuStrip ProcMenu()
        {
            var m = Theme.Menu();
            m.Opening += (s, e) =>
            {
                m.Items.Clear();
                var row = _procs.CurrentRow; int pid;
                if (row == null || !int.TryParse(Convert.ToString(row.Cells[1].Value), out pid)) { e.Cancel = true; return; }
                _menuOpen = true;
                string exe = Convert.ToString(row.Cells[0].Value);
                if (Engine.IsAntiCheat(exe)) { m.Items.Add(new ToolStripMenuItem(L.T("Anti-cheat - grobyq does not touch it")) { Enabled = false }); return; }
                if (Engine.Gate(exe, Engine.Op.Process, Engine.ProtectedPriorityAllowed) == null)
                {
                    var pc = new ToolStripMenuItem(L.T("Priority class"));
                    foreach (var c in new[] { new Opt("High", L.T("High")), new Opt("AboveNormal", L.T("Above normal")), new Opt("Normal", L.T("Normal")), new Opt("BelowNormal", L.T("Below normal")), new Opt("Idle", L.T("Idle")) })
                    { string cc = c.Value; pc.DropDownItems.Add(c.Text, null, (a, b) => M.Log(M.Eng.ProcessClassManual(pid, cc))); }
                    m.Items.Add(pc);
                }
                else m.Items.Add(new ToolStripMenuItem(L.T("Protected system process - its class and cores are never changed")) { Enabled = false });
                m.Items.Add(new ToolStripSeparator());
                if (!M.Eng.Targets.Any(t => string.Equals(t, exe, StringComparison.OrdinalIgnoreCase)) && !Engine.IsProtected(exe))
                    m.Items.Add(L.F("Add {0} to target processes", exe), null, (a, b) =>
                    {
                        Settings.Set("WatchList", string.Join(" ", M.Eng.Targets.Concat(new[] { exe })));
                        M.RetargetWatchList();
                        if (_watch != null) _watch.Text = string.Join(" ", Settings.WatchList());
                    });
                if (!M.Eng.Presets.ContainsKey(exe))
                    m.Items.Add(L.F("Create a preset for {0}", exe), null, (a, b) => { NewPreset(exe); ShowEditor(_pick); });
                else
                    m.Items.Add(L.F("Edit the preset for {0}", exe), null, (a, b) => { FillPresetPick(exe); ShowEditor(_pick); });
            };
            m.Closed += (s, e) => { _menuOpen = false; };
            return m;
        }

        // Full reset: undo every live thread/process change, then restore presets, background classes and
        // per-process affinity rules to the built-in defaults. The system goes back to Windows behaviour.
        private void FullReset()
        {
            if (!Dlg.Ask(this, L.T("Reset everything to defaults?\n\nThis turns the optimizer off, restores every process and thread grobyq changed back to Windows defaults, and replaces your presets, background list and per-process affinity rules with the built-in ones. Exported profiles are not touched."), true)) return;
            if (M.Active) M.SetActive(false);
            try { M.Log(M.Eng.Revert()); } catch { }
            M.Eng.Presets = Store.Defaults();
            M.Eng.BgClasses = Store.DefaultBg();
            M.Eng.AffRules.Clear();
            Settings.SaveAffRules(M.Eng.AffRules);
            Settings.Set("BgProcs", Settings.DefaultBgProcs);
            M.Eng.SavePresets();
            Settings.Set("ActiveProfile", "");
            FillPresetPick(null);
            RefreshBg();
            M.NotifyMask();
            FillProfiles(null);
            M.Log("[i] full reset: live changes reverted to their captured baselines; presets, background list and affinity rules back to built-in defaults; no active profile");
        }

        private Expander _adv3;

        // The preset / rule editors live in the Advanced group: open it and bring the editor into view.
        private void ShowEditor(Control c)
        {
            if (_adv3 != null && !_adv3.Open) _adv3.Open = true;
            ScrollIntoView(c);
        }

        private void RuleFromLive(string match, string pattern)
        {
            string exe = _thrExe;
            if (string.IsNullOrEmpty(exe)) return;
            if (Engine.IsAntiCheat(exe) || Engine.IsAntiCheat(pattern)) { Dlg.Info(this, L.T("Anti-cheat - grobyq does not touch it")); return; }
            if (!M.Eng.Presets.ContainsKey(exe)) { M.Eng.Presets[exe] = new Preset { Exe = exe }; }
            FillPresetPick(exe);
            AddRule(new ThreadRule { Match = match, Pattern = pattern, Prio = "AboveNormal" });
            M.Log("[PS] rule added - set its priority / Ideal CPU in Thread rules (Advanced)");
            ShowEditor(_rules);
        }

        // ------------------------------------------------------------------ background list (priority lowering)
        private void BuildBgList()
        {
            Title(L.T("Background processes"));
            var card = NewCard();
            card.Stack(ToggleRow(L.T("Lower background processes"), L.T("The processes in the list below get a lower class, VeryLow I/O and Low memory priority while the game runs."), "DoBackground"));
            card.Stack(ToggleRow(L.T("Pin background off the game cores"), L.T("Background processes get the cores the game mask leaves free (E-cores, SMT siblings, the other CCD). Helps most on multi-CCD CPUs or with heavy background; neutral otherwise - measure."), "BgAffinity"));
            card.Stack(ToggleRow(L.T("Release background on alt-tab"), L.T("Recommended on. When the game loses focus, background processes get their priority and cores back."), "BgForeground"));
            card.Stack(new TextBlock(L.T("While a target runs (and has focus, if 'Release on alt-tab' is on), these get the class below, VeryLow I/O, Low memory priority and - if pinning is on - the cores the game mask leaves free. System processes (dwm, audiodg, csrss, explorer, svchost...) are protected and never touched."), Theme.Dim, 9f));
            _bg = Theme.Grid(); _bg.Height = Theme.D(260);
            _bg.Columns.Add(Theme.Col(L.T("Process"), 60, false));
            _bg.Columns.Add(Theme.ComboCol(L.T("Class while the game runs"), 40, L.T("Below normal"), L.T("Idle"), L.T("Normal")));
            _bg.CurrentCellDirtyStateChanged += (s, e) => { if (_bg.IsCurrentCellDirty) _bg.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _bg.CellValueChanged += (s, e) => { if (!_bgBuilding) SaveBg(); };
            card.Stack(_bg);
            var strip = new Strip();
            strip.Add(new Btn(L.T("Add"), Btn.Style.Accent)).Click += (s, e) =>
            {
                string n = Dlg.Prompt(this, L.T("Background process"), L.T("Process name (e.g. discord.exe):"), "");
                if (string.IsNullOrWhiteSpace(n)) return;
                n = n.Trim(); if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
                if (Engine.IsAntiCheat(n)) { Dlg.Info(this, L.T("Anti-cheat - grobyq does not touch it")); return; }
                if (Engine.IsProtected(n)) { Dlg.Info(this, L.F("{0} is a protected system process: lowering it hurts.", n)); return; }
                _bgBuilding = true; _bg.Rows.Add(n, ClassText("BelowNormal")); _bgBuilding = false; SaveBg();
            };
            strip.Add(new Btn(L.T("Remove"))).Click += (s, e) => { if (_bg.CurrentRow != null) { _bg.Rows.Remove(_bg.CurrentRow); SaveBg(); } };
            strip.Add(new Btn(L.T("Default list"))).Click += (s, e) => { Settings.Set("BgProcs", Settings.DefaultBgProcs); M.Eng.BgClasses = Store.DefaultBg(); M.Eng.SavePresets(); RefreshBg(); M.Log("[BG] default background list restored"); };
            card.Stack(strip);
        }

        private static string ClassText(string c) { return c == "Idle" ? L.T("Idle") : c == "Normal" ? L.T("Normal") : L.T("Below normal"); }
        private static string ClassKey(string t) { return t == L.T("Idle") ? "Idle" : t == L.T("Normal") ? "Normal" : "BelowNormal"; }

        private void RefreshBg()
        {
            _bgBuilding = true;
            try
            {
                _bg.Rows.Clear();
                foreach (var n in Settings.BgProcs())
                {
                    string c; if (!M.Eng.BgClasses.TryGetValue(n, out c)) c = "BelowNormal";
                    _bg.Rows.Add(n, ClassText(c));
                }
            }
            finally { _bgBuilding = false; }
        }

        private void SaveBg()
        {
            var names = new List<string>();
            var cls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in _bg.Rows)
            {
                string n = Convert.ToString(r.Cells[0].Value).Trim();
                if (n.Length == 0) continue;
                if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
                names.Add(n);
                string c = ClassKey(Convert.ToString(r.Cells[1].Value));
                if (c != "BelowNormal") cls[n] = c;
            }
            Settings.Set("BgProcs", string.Join(" ", names));
            M.Eng.BgClasses = cls;
            string err = M.Eng.SavePresets();
            M.Log(err.Length == 0 ? "[BG] background list saved (" + names.Count + " processes)" : "[!!] " + err);
        }

        private static Topo _topo;
        private static DateTime _topoAt;
        internal static Topo SafeTopo()
        {
            if (_topo != null && (DateTime.UtcNow - _topoAt).TotalSeconds < 30) return _topo;
            try { _topo = Topo.Live(); _topoAt = DateTime.UtcNow; } catch { }
            return _topo;
        }

        public override void OnEnter() { UpdateState(); RefreshBg(); RefreshProcs(); RefreshThreads(); }

        public override void OnTick(int tick)
        {
            if (_menuOpen) return;
            if (_rules.IsCurrentCellInEditMode) return;
            if (tick % 5 == 0) UpdateState();
            if (!OnScreen(_procs)) return;                   // Live threads scrolled away: no process walk on the UI thread
            if (tick % 2 == 0) RefreshProcs();               // every 2 s: GetProcesses opens every process
            RefreshThreads();
        }

        internal static string Prompt(string title, string text, string def) { return Dlg.Prompt(null, title, text, def); }
    }
}
