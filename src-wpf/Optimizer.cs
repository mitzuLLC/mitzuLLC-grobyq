using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Grobyq
{
    // Drives the engine with two switches and one rule for WHEN things apply: the game session.
    //
    //   Game threads optimizer   - the game preset (its process part always, its thread rules with "Apply thread rules")
    //                              plus the parts switched on and saved (core mask, affinity rules).
    //   System threads optimizer - the built-in thread profile (system processes, NVIDIA / Steam / Discord...), as written.
    //
    // Session = a chosen game is running (alt-tab changes nothing). Session start: everything switched on is applied once.
    // During the session a catch-up pass every 2 s handles only what is new (threads created later, e.g. on map load): the
    // engine reads each value back and writes only what differs, and a thread gets its Ideal CPU once (only where a rule
    // sets one). Session end (the game closes): every baseline is restored - normal mode. Outside a session nothing is
    // applied, whatever the switches say. 4.0.2: hot threads, the background list and game mode are removed.
    //
    // Engine calls run on one worker thread, in order (apply, catch-up, restore): a restore queued after an apply always
    // lands last. Results go to AppLog; StateChanged fires on the UI thread when something visible changed.
    public sealed class Optimizer
    {
        public static Optimizer Instance { get; private set; }

        public readonly Engine Eng;
        private readonly DispatcherTimer _timer;
        private bool _game, _sys, _session;
        private int _tick;
        private string _liveTarget = "";
        private string _gameRuleLogged;
        private string _stateSig = "";
        private Topo _topo;
        private DateTime _topoAt;

        private readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
        private int _queued;

        public event Action StateChanged;
        public bool GameOn => _game;                 // game threads optimizer switch
        public bool SysOn => _sys;                   // system threads optimizer switch
        public bool Active => _game || _sys;
        public bool InSession => _session;           // a game session: what is switched on is applied
        public string LiveTarget => _liveTarget;     // running games (comma separated), "" = none
        // OFF = both switches off; ARMED = on, waiting for a game; ACTIVE = applied
        public string StateName => !Active ? "OFF" : _session ? "ACTIVE" : "ARMED";

        // The optional parts of the game optimizer (Settings keys); the preset's process part applies on top of these anyway.
        public static readonly string[] Parts = { "DoMask", "DoThreads", "DoAffRules" };   // 4.0.2: background list removed
        public static bool AnyPartOn { get { return Parts.Any(Settings.GetBool); } }
        // Game mode: close the apps marked "Close" when the game process appears, reopen them when it is gone.
        public static bool CloseBgOn { get { return false; } }   // 4.0.2: game mode (close / reopen background apps) is removed - never on, whatever an old stored value says

        public Optimizer()
        {
            Instance = this;
            Eng = new Engine(Settings.WatchList());
            if (Eng.Targets == null || Eng.Targets.Length == 0) Eng.Targets = new[] { "cs2.exe" };
            Eng.AffRules = Settings.LoadAffRules();
            try { var m = Store.Migrate362(Eng); if (m != null && m.Count > 0) AppLog.Add(m); } catch { }
            try { var m2 = Store.MigrateNormalClass(Eng); if (m2 != null && m2.Count > 0) AppLog.Add(m2); } catch { }
            try { var m3 = Store.MigrateHighThreads(Eng); if (m3 != null && m3.Count > 0) AppLog.Add(m3); } catch { }
            try { var m4 = Store.MigrateSafe402(Eng); if (m4 != null && m4.Count > 0) AppLog.Add(m4); } catch { }   // after 4.0.1: Highest -> Above normal

            // v4.0 alpha "Enable at start" = the game switch left on
            if (Settings.GetBool("AutoEnable")) { Settings.Set("GameOpt", "1"); Settings.Set("AutoEnable", "0"); }

            new Thread(Work) { IsBackground = true, Name = "grobyq-engine", Priority = ThreadPriority.Normal }.Start();

            // the switches come back in the position they were left in; nothing is applied until a game session starts
            _sys = Settings.GetBool("SystemThreadOpt");
            if (_sys) { try { Eng.LoadSystemProfile(true); } catch { } }
            _game = Settings.GetBool("GameOpt");
            if (Active) AppLog.Add("[i] " + Which() + " threads optimizer on - waiting for a game");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        private string Which() { return (_game ? "game" : "") + (_game && _sys ? " + " : "") + (_sys ? "system" : ""); }

        // ------------------------------------------------------------------ the engine worker
        private void Work()
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                try { job(); } catch { }
                Interlocked.Decrement(ref _queued);
            }
        }

        // An engine job in the queue; its log lines and a state refresh come back on the UI thread. skipIfBusy: a catch-up
        // is dropped while anything else is still queued (the next tick tries again).
        private void Queue(Func<List<string>> job, bool skipIfBusy = false, bool reports = false)
        {
            if (skipIfBusy && Volatile.Read(ref _queued) > 0) return;
            Interlocked.Increment(ref _queued);
            try
            {
                _jobs.Add(() =>
                {
                    List<string> log = null; List<ApplyReport> reps = null; string err = null;
                    try { log = job(); if (reports) reps = new List<ApplyReport>(Eng.LastReports); }
                    catch (Exception ex) { err = ex.Message; }
                    Post(() =>
                    {
                        if (err != null) AppLog.Add("[!!] " + err);
                        if (log != null && log.Count > 0) AppLog.Add(log);
                        if (reps != null) foreach (var rep in reps) AppLog.Add(RepLine(rep));
                        StateChanged?.Invoke();
                    });
                });
            }
            catch { Interlocked.Decrement(ref _queued); }
        }

        private void QueueApply(bool reports)
        {
            var o = Opts();                          // read on the UI thread (settings, topology), applied on the worker
            Queue(() => Eng.ApplyAll(o), false, reports);
        }

        private void QueueRevert() { Queue(() => Eng.Revert()); }

        private static void Post(Action a)
        {
            var app = Application.Current;
            try { if (app != null) app.Dispatcher.BeginInvoke(a); else a(); } catch { }
        }

        // Exit paths: wait (bounded) until every queued job ran, then the final restore can run.
        public void Drain(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref _queued) > 0 && sw.ElapsedMilliseconds < ms) Thread.Sleep(20);
        }

        public void Stop() { try { _timer.Stop(); } catch { } }

        private Topo SafeTopo()
        {
            if (_topo != null && (DateTime.UtcNow - _topoAt).TotalSeconds < 30) return _topo;
            try { _topo = Topo.Live(); _topoAt = DateTime.UtcNow; } catch { }
            return _topo;
        }

        // ------------------------------------------------------------------ the switches
        // Game threads optimizer on/off (kept in Settings "GameOpt").
        public void SetGame(bool on)
        {
            if (on == _game) return;
            _game = on;
            Settings.Set("GameOpt", on ? "1" : "0");
            AppLog.Add(on ? "=== GAME THREADS OPTIMIZER ON ===" : "=== GAME THREADS OPTIMIZER OFF ===");
            if (on && !AnyPartOn) AppLog.Add("[i] only the game preset's process part applies (thread rules and cores are off): switch them on in Threads / Affinity and press Save");
            Evaluate(true);
        }

        // System threads optimizer on/off (kept in Settings "SystemThreadOpt"): the built-in profile is loaded / dropped.
        public void SetSystem(bool on)
        {
            if (on == _sys) return;
            _sys = on;
            Settings.Set("SystemThreadOpt", on ? "1" : "0");
            try { Eng.LoadSystemProfile(on); } catch { }
            if (on)
                AppLog.Add(string.Format("=== SYSTEM THREADS OPTIMIZER ON ===  ({0} process profiles, {1} background processes; priority and boost only, anti-cheats skipped{2}; applied while a game {3})",
                    Eng.SystemPresets.Count, Eng.SystemBg.Count, Engine.ProtectedPriorityAllowed ? "" : ", protected system processes skipped", "runs"));
            else AppLog.Add("=== SYSTEM THREADS OPTIMIZER OFF ===");
            Evaluate(true);
        }

        // Both switches off at once (Threads > Reset, Settings > Reset): every baseline back.
        public void StopAll()
        {
            bool was = Active || Eng.HasChanges;
            _game = false; _sys = false;
            Settings.Set("GameOpt", "0");
            Settings.Set("SystemThreadOpt", "0");
            try { Eng.LoadSystemProfile(false); } catch { }
            _session = false;
            if (was) { AppLog.Add("=== OPTIMIZERS OFF ==="); QueueRevert(); }
            Queue(() => Eng.RelaunchClosed());                        // game mode off too: reopen anything it closed (no-op if nothing)
            Evaluate(false);
        }

        // After Save: the new choice replaces the old one completely - in a session every baseline is restored and the
        // saved parts are applied from scratch (a part switched off is undone, not left alone).
        public void Reapply()
        {
            Eng.Targets = Settings.WatchList();
            if (Eng.Targets.Length == 0) Eng.Targets = new[] { "cs2.exe" };
            Evaluate(true);
        }

        // Another page saved something the session applies (core mask, affinity rules): rebuild it now.
        public void ApplyNow() { if (_session) Evaluate(true); }

        // The "Close background on game launch" switch was flipped: close now if a game is already running and the mode is on;
        // reopen whatever was closed if the mode is now off. 4.0.2: no caller - game mode is removed (CloseBgOn is false).
        public void CloseModeChanged()
        {
            if (CloseBgOn)
            {
                string now = RunningGames();
                _liveTarget = now;                                  // keep presence in sync so Evaluate sees no spurious transition
                if (now.Length > 0) Queue(() => Eng.CloseForGame());
            }
            else
            {
                Queue(() => Eng.RelaunchClosed());                  // in order after any close still queued; a no-op when nothing was closed
                if (!Active) _liveTarget = "";                      // nothing watches the game now: its next start is seen fresh
            }
        }

        // ------------------------------------------------------------------ the session
        // Re-evaluates the session now. rebuild = what is applied changed (a switch, Save): restore, then apply afresh.
        private void Evaluate(bool rebuild)
        {
            string now = (Active || CloseBgOn) ? RunningGames() : "";
            bool appeared = false, gone = false;
            if (now != _liveTarget)
            {
                bool wasEmpty = _liveTarget.Length == 0;
                appeared = now.Length > 0 && wasEmpty;
                gone = now.Length == 0 && !wasEmpty;
                if (appeared) AppLog.Add("[>>] game started: " + now);
                else if (gone) AppLog.Add("[<<] game closed: " + _liveTarget);
                _liveTarget = now;
            }
            // game mode: the marked apps close BEFORE the session applies (the background layer then has nothing to lower)...
            if (appeared && CloseBgOn) Queue(() => Eng.CloseForGame());
            bool want = Active && now.Length > 0;
            if (want && !_session)
            {
                _session = true;
                AppLog.Add("[>>] " + now + " running - " + Which() + " optimization on");
                QueueApply(true);
            }
            else if (!want && _session)
            {
                _session = false;
                AppLog.Add("[<<] normal mode");
                QueueRevert();
            }
            else if (want && rebuild)
            {
                QueueRevert();
                QueueApply(true);
            }
            // ...and reopen AFTER the session restore, so they start into a system that is back to normal. Keyed on what
            // grobyq actually closed: turning the mode off mid-match still brings the apps back when the game exits. Queued
            // even if nothing is recorded yet - a close job still in the queue (a game that died right after launch) runs
            // first on the same worker, and the reopen is a no-op when nothing was closed.
            if (gone && (Eng.HasClosed || CloseBgOn)) Queue(() => Eng.RelaunchClosed());
            string sig = Active + "|" + _session + "|" + _liveTarget + "|" + _game + "|" + _sys;
            if (rebuild || sig != _stateSig) { _stateSig = sig; StateChanged?.Invoke(); }
        }

        // Which chosen games run now: one process snapshot per tick for all of them.
        private string RunningGames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try { names.Add(p.ProcessName); } catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return string.Join(", ", Eng.Targets.Where(t => names.Contains(t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? t.Substring(0, t.Length - 4) : t)));
        }

        public ApplyOptions Opts() => new ApplyOptions
        {
            MaskName = _game ? GameRule() : Settings.Get("Mask"),   // the game mask matters (and is logged) only for the game layer
            BgMaskName = GameMask.ManualRule(SafeTopo()),
            DoMask = Settings.GetBool("DoMask"),
            DoThreads = Settings.GetBool("DoThreads"),
            DoBackground = false,                                   // 4.0.2: the user background list is removed
            BgAffinity = false,
            DoAffRules = Settings.GetBool("DoAffRules"),
            Game = _game,
            Sys = _sys,
            Warmup = false
        };

        private static string RepLine(ApplyReport rep) =>
            string.Format("[==] {0} L{1} mask {2} [{3}] named {4} cycle {5}{6}",
                rep.Target, rep.HandleLevel, rep.MaskHex, rep.MaskLps, rep.NamedHits, rep.CycleFallbackTids.Count,
                rep.Failures.Count > 0 ? " FAIL:" + string.Join(",", rep.Failures) : "");

        private void Tick()
        {
            try
            {
                _tick++;
                if (Active || _session || CloseBgOn) Evaluate(false);   // close-mode polls for the game even with both optimizers off

                // catch-up inside the session: new threads only (reads everything, writes what differs)
                if (_session)
                {
                    int every = Math.Max(1, Math.Max(1000, Settings.GetInt("Period", 2000)) / 1000);
                    if (_tick % every == 0) { var o = Opts(); Queue(() => Eng.ApplyAll(o), true); }
                }

                if (_tick % 60 == 0) Queue(() => { Eng.Prune(); return null; }, true);
            }
            catch { }
        }

        public string GameRule()
        {
            var t = SafeTopo();
            if (t == null) return Settings.Get("Mask");
            var i = GameMask.Info(t);
            string rule = i.Final == i.Base ? i.BaseRule : Masks.Hex(i.Final);
            string sig = rule + "|" + i.IrqDrop + "|" + i.IrqIgnored;
            if (sig != _gameRuleLogged)
            {
                if (_gameRuleLogged != null || i.Adjusted || i.IrqIgnored)
                    AppLog.Add(string.Format("[AF] game mask: {0} LP {1}  (base {2}{3}{4}{5})", Masks.Hex(i.Final), Masks.Ranges(i.Final), i.BaseRule,
                        i.Add != 0 ? ", +LP " + Masks.Ranges(i.Add) : "", i.Drop != 0 ? ", -LP " + Masks.Ranges(i.Drop) : "",
                        i.IrqDrop != 0 ? ", interrupts -LP " + Masks.Ranges(i.IrqDrop) : i.IrqIgnored ? ", interrupt LPs kept: taking them would leave the game less than half" : ""));
                _gameRuleLogged = sig;
            }
            return rule;
        }

        public static int ByNameCount(string exe)
        {
            try
            {
                var a = Process.GetProcessesByName(exe.ToLowerInvariant().EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe);
                int n = a.Length;
                foreach (var p in a) { try { p.Dispose(); } catch { } }
                return n;
            }
            catch { return 0; }
        }
    }
}
