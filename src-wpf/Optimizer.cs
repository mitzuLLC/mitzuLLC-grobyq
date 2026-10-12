using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Grobyq
{
    // Drives the engine: two independent switches, the 1 s watch tick (target detect, periodic re-apply off the UI thread,
    // background foreground apply) and the restore when a switch goes off. Results go to AppLog; StateChanged fires so the
    // pages and the tray can read the state.
    //
    //   Game threads optimizer   - waits for the chosen games; while one runs, applies only the parts the user switched on
    //                              and saved (process priority, core mask, threads, background, affinity rules).
    //   System threads optimizer - the built-in thread profile (system processes, NVIDIA / Steam / Discord...), priority and
    //                              boost only, re-applied every 10 s; its background list is lowered while a game has focus.
    // Both start off (v4 manual mode) and keep their position across restarts. A switch going off restores every baseline
    // and re-applies the other layer, so exactly what is still switched on stays applied.
    public sealed class Optimizer
    {
        public static Optimizer Instance { get; private set; }

        public readonly Engine Eng;
        private readonly DispatcherTimer _timer;
        private bool _game, _sys;
        private int _tick;
        private int _applyRunning;
        private string _liveTarget = "";
        private string _gameRuleLogged;
        private Topo _topo;
        private DateTime _topoAt;

        public event Action StateChanged;
        public bool GameOn => _game;                 // game threads optimizer switch
        public bool SysOn => _sys;                   // system threads optimizer switch
        public bool Active => _game || _sys;
        public string LiveTarget => _liveTarget;     // running games (comma separated), "" = none
        // OFF = both switches off; ARMED = only the game optimizer, waiting for a game; ACTIVE = something is applied
        public string StateName => !Active ? "OFF" : (_game && !_sys && string.IsNullOrEmpty(_liveTarget)) ? "ARMED" : "ACTIVE";

        // The parts of the game optimizer a user can switch on (Settings keys). None on = the game switch changes nothing.
        public static readonly string[] Parts = { "DoProcess", "DoMask", "DoThreads", "DoBackground", "DoAffRules" };
        public static bool AnyPartOn { get { return Parts.Any(Settings.GetBool); } }

        public Optimizer()
        {
            Instance = this;
            Eng = new Engine(Settings.WatchList());
            if (Eng.Targets == null || Eng.Targets.Length == 0) Eng.Targets = new[] { "cs2.exe" };
            Eng.AffRules = Settings.LoadAffRules();
            try { var m = Store.Migrate362(Eng); if (m != null && m.Count > 0) AppLog.Add(m); } catch { }

            // v4.0 alpha "Enable at start" = the game switch left on
            if (Settings.GetBool("AutoEnable")) { Settings.Set("GameOpt", "1"); Settings.Set("AutoEnable", "0"); }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            // the switches come back in the position they were left in
            if (Settings.GetBool("SystemThreadOpt")) SetSystem(true);
            if (Settings.GetBool("GameOpt")) SetGame(true);
        }

        public void Stop() { try { _timer.Stop(); } catch { } }

        private Topo SafeTopo()
        {
            if (_topo != null && (DateTime.UtcNow - _topoAt).TotalSeconds < 30) return _topo;
            try { _topo = Topo.Live(); _topoAt = DateTime.UtcNow; } catch { }
            return _topo;
        }

        // Game threads optimizer on/off (kept in Settings "GameOpt"). Off restores every baseline, then the system layer
        // (if on) is applied again.
        public void SetGame(bool on)
        {
            if (on == _game) return;
            _game = on;
            Settings.Set("GameOpt", on ? "1" : "0");
            if (on)
            {
                AppLog.Add("=== GAME THREADS OPTIMIZER ON ===");
                if (!AnyPartOn) AppLog.Add("[i] nothing is switched on to optimize: pick the parts on Threads and press Save");
                ApplyNow();
            }
            else
            {
                AppLog.Add("=== GAME THREADS OPTIMIZER OFF ===");
                RevertAll();
                if (_sys) ApplyNow(); else _liveTarget = "";
            }
            StateChanged?.Invoke();
        }

        // System threads optimizer on/off (kept in Settings "SystemThreadOpt"): the built-in profile is loaded / dropped.
        public void SetSystem(bool on)
        {
            if (on == _sys) return;
            _sys = on;
            Settings.Set("SystemThreadOpt", on ? "1" : "0");
            try { Eng.LoadSystemProfile(on); } catch { }
            if (on)
            {
                AppLog.Add(string.Format("=== SYSTEM THREADS OPTIMIZER ON ===  ({0} process profiles, {1} background processes; priority and boost only, anti-cheats skipped{2})",
                    Eng.SystemPresets.Count, Eng.SystemBg.Count, Engine.ProtectedPriorityAllowed ? "" : ", protected system processes skipped"));
                ApplyNow();
            }
            else
            {
                AppLog.Add("=== SYSTEM THREADS OPTIMIZER OFF ===");
                RevertAll();
                if (_game) ApplyNow(); else _liveTarget = "";
            }
            StateChanged?.Invoke();
        }

        // Both switches off at once (Threads > Reset, exit paths): every baseline back.
        public void StopAll()
        {
            if (!Active) return;
            _game = false; _sys = false;
            Settings.Set("GameOpt", "0");
            Settings.Set("SystemThreadOpt", "0");
            try { Eng.LoadSystemProfile(false); } catch { }
            AppLog.Add("=== OPTIMIZERS OFF ===");
            RevertAll();
            _liveTarget = "";
            StateChanged?.Invoke();
        }

        private void RevertAll()
        {
            try { AppLog.Add(Eng.Revert()); } catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
        }

        // After Save: the new choice replaces the old one completely. A switch on = restore every baseline, then apply the
        // saved parts from scratch (a part switched off is undone, not just left alone). Both off = nothing is touched.
        public void Reapply()
        {
            Eng.Targets = Settings.WatchList();
            if (Eng.Targets.Length == 0) Eng.Targets = new[] { "cs2.exe" };
            if (!Active) { StateChanged?.Invoke(); return; }
            RevertAll();
            _liveTarget = "";
            ApplyNow();
        }

        public ApplyOptions Opts() => new ApplyOptions
        {
            MaskName = _game ? GameRule() : Settings.Get("Mask"),   // the game mask matters (and is logged) only for the game layer
            BgMaskName = GameMask.ManualRule(SafeTopo()),
            TopN = Math.Max(1, Math.Min(32, Settings.GetInt("TopN", 4))),
            SkipCpu0 = Settings.GetBool("SkipCpu0"),
            DoProcess = Settings.GetBool("DoProcess"),
            DoMask = Settings.GetBool("DoMask"),
            DoThreads = Settings.GetBool("DoThreads"),
            DoBackground = Settings.GetBool("DoBackground"),
            BgAffinity = Settings.GetBool("BgAffinity"),
            BgForeground = Settings.GetBool("BgForeground"),
            DoAffRules = Settings.GetBool("DoAffRules"),
            Game = _game,
            Sys = _sys,
            Warmup = false
        };

        public void ApplyNow()
        {
            try
            {
                AppLog.Add(Eng.ApplyAll(Opts()));
                _liveTarget = Eng.LiveTarget;
                foreach (var rep in Eng.LastReports) AppLog.Add(RepLine(rep));
            }
            catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
            StateChanged?.Invoke();
        }

        public void ApplyNowAsync()
        {
            if (Interlocked.CompareExchange(ref _applyRunning, 1, 0) != 0) return;   // previous cycle still running
            var opts = Opts();
            var th = new Thread(() =>
            {
                List<string> log = null; List<ApplyReport> reps = null; string live = null; string err = null;
                try { log = Eng.ApplyAll(opts); live = Eng.LiveTarget; reps = new List<ApplyReport>(Eng.LastReports); }
                catch (Exception ex) { err = ex.Message; }
                finally
                {
                    Action post = () =>
                    {
                        if (err != null) AppLog.Add("[!!] " + err);
                        else
                        {
                            if (log != null) AppLog.Add(log);
                            if (Active) _liveTarget = live;
                            if (reps != null) foreach (var rep in reps) AppLog.Add(RepLine(rep));
                            StateChanged?.Invoke();
                        }
                        Interlocked.Exchange(ref _applyRunning, 0);
                    };
                    var app = Application.Current;
                    try { if (app != null) app.Dispatcher.BeginInvoke(post); else post(); }
                    catch { Interlocked.Exchange(ref _applyRunning, 0); }
                }
            })
            { IsBackground = true, Priority = ThreadPriority.Normal };
            th.Start();
        }

        private static string RepLine(ApplyReport rep) =>
            string.Format("[==] {0} L{1} mask {2} [{3}] named {4} cycle {5}{6}",
                rep.Target, rep.HandleLevel, rep.MaskHex, rep.MaskLps, rep.NamedHits, rep.CycleFallbackTids.Count,
                rep.Failures.Count > 0 ? " FAIL:" + string.Join(",", rep.Failures) : "");

        private void Tick()
        {
            try
            {
                _tick++;
                if (!Active) { if (_tick % 60 == 0) Eng.Prune(); return; }

                var running = Eng.Targets.Where(t => ByNameCount(t) > 0).ToList();
                string now = string.Join(", ", running);
                bool applied = false;
                if (now != _liveTarget)
                {
                    _liveTarget = now;
                    if (now.Length > 0) { AppLog.Add("[>>] game detected: " + now); ApplyNow(); applied = true; }
                    else { AppLog.Add("[<<] no game running" + (_game ? " - back to armed" : "")); if (Eng.BgOn) AppLog.Add(Eng.BgRelease()); }
                    StateChanged?.Invoke();
                }

                // background lowering follows the game window: the system profile's list always does, the user's list
                // unless "Release background on alt-tab" is off (then it stays lowered while the game runs)
                bool userBg = _game && Settings.GetBool("DoBackground");
                bool sysBg = _sys && Eng.SystemBg.Count > 0;
                if (now.Length > 0 && (userBg || sysBg) && (!userBg || Settings.GetBool("BgForeground")))
                {
                    bool fg = Eng.GameForeground();
                    if (fg && !Eng.BgOn) { AppLog.Add(Eng.BgApply(userBg && Settings.GetBool("BgAffinity"), GameMask.ManualRule(SafeTopo()), userBg, sysBg)); StateChanged?.Invoke(); }
                    else if (!fg && Eng.BgOn) { AppLog.Add(Eng.BgRelease()); StateChanged?.Invoke(); }
                }

                if (!applied && _game && Settings.GetBool("PeriodicReapply") && now.Length > 0)
                {
                    int period = Math.Max(1000, Settings.GetInt("Period", 2000));
                    int every = Math.Max(1, period / 1000);
                    if (_tick % every == 0) { ApplyNowAsync(); applied = true; }
                }

                // the system profile: every 10 s (new threads in system processes; a no-op for unchanged ones)
                if (!applied && _sys && _tick % 10 == 0) ApplySystemAsync();

                if (_tick % 60 == 0) Eng.Prune();
            }
            catch { }
        }

        private int _sysRunning;
        private void ApplySystemAsync()
        {
            if (Interlocked.CompareExchange(ref _sysRunning, 1, 0) != 0) return;
            var th = new Thread(() =>
            {
                List<string> log = null; string err = null;
                try { log = Eng.ApplySystemLayer(); }
                catch (Exception ex) { err = ex.Message; }
                finally
                {
                    Action post = () =>
                    {
                        if (err != null) AppLog.Add("[!!] " + err);
                        else if (log != null && log.Count > 0) AppLog.Add(log);
                        StateChanged?.Invoke();
                        Interlocked.Exchange(ref _sysRunning, 0);
                    };
                    var app = Application.Current;
                    try { if (app != null) app.Dispatcher.BeginInvoke(post); else post(); }
                    catch { Interlocked.Exchange(ref _sysRunning, 0); }
                }
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "grobyq-sys" };
            th.Start();
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
