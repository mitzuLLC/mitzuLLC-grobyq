using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Grobyq
{
    // Drives the engine: enable/disable state, the 1s watch tick (target detect, periodic re-apply off the UI thread,
    // background foreground apply) and revert on disable. Ported from the WinForms MainForm loop with the UI stripped:
    // results go to AppLog, and StateChanged fires so the Threads page can read StateName / LiveTarget.
    public sealed class Optimizer
    {
        public static Optimizer Instance { get; private set; }

        public readonly Engine Eng;
        private readonly DispatcherTimer _timer;
        private bool _active;
        private int _tick;
        private int _applyRunning;
        private string _liveTarget = "";
        private string _gameRuleLogged;
        private Topo _topo;
        private DateTime _topoAt;

        public event Action StateChanged;
        public bool Active => _active;
        public string LiveTarget => _liveTarget;
        public string StateName => _active ? (string.IsNullOrEmpty(_liveTarget) ? "ARMED" : "ACTIVE") : "OFF";

        public Optimizer()
        {
            Instance = this;
            Eng = new Engine(Settings.WatchList());
            Eng.AffRules = Settings.LoadAffRules();
            Eng.LoadSystemProfile(Settings.GetBool("SystemThreadOpt"));
            try { var m = Store.Migrate362(Eng); if (m != null && m.Count > 0) AppLog.Add(m); } catch { }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            if (Settings.GetBool("AutoEnable")) SetActive(true);
        }

        private Topo SafeTopo()
        {
            if (_topo != null && (DateTime.UtcNow - _topoAt).TotalSeconds < 30) return _topo;
            try { _topo = Topo.Live(); _topoAt = DateTime.UtcNow; } catch { }
            return _topo;
        }

        public void SetActive(bool on)
        {
            if (on == _active) return;
            _active = on;
            if (on) { AppLog.Add("=== ENABLED ==="); ApplyNow(); }
            else
            {
                AppLog.Add("=== DISABLED ===");
                try { AppLog.Add(Eng.Revert()); } catch (Exception ex) { AppLog.Add("[!!] " + ex.Message); }
                _liveTarget = "";
            }
            StateChanged?.Invoke();
        }

        public ApplyOptions Opts() => new ApplyOptions
        {
            MaskName = GameRule(),
            BgMaskName = GameMask.ManualRule(SafeTopo()),
            TopN = Math.Max(1, Math.Min(32, Settings.GetInt("TopN", 4))),
            SkipCpu0 = Settings.GetBool("SkipCpu0"),
            DoThreads = Settings.GetBool("DoThreads"),
            DoBackground = Settings.GetBool("DoBackground"),
            BgAffinity = Settings.GetBool("BgAffinity"),
            BgForeground = Settings.GetBool("BgForeground"),
            DoAffRules = Settings.GetBool("DoAffRules"),
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
                            _liveTarget = live;
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
                var running = Eng.Targets.Where(t => ByNameCount(t) > 0).ToList();
                string now = string.Join(", ", running);
                if (_active && now != _liveTarget)
                {
                    _liveTarget = now;
                    if (now.Length > 0) { AppLog.Add("[>>] target detected: " + now); ApplyNow(); }
                    else { AppLog.Add("[<<] no target running - back to armed"); if (Eng.BgOn) AppLog.Add(Eng.BgRelease()); }
                    StateChanged?.Invoke();
                }

                if (_active && now.Length > 0 && Settings.GetBool("DoBackground") && Settings.GetBool("BgForeground"))
                {
                    bool fg = Eng.GameForeground();
                    if (fg && !Eng.BgOn) { AppLog.Add(Eng.BgApply(Settings.GetBool("BgAffinity"), GameMask.ManualRule(SafeTopo()))); StateChanged?.Invoke(); }
                    else if (!fg && Eng.BgOn) { AppLog.Add(Eng.BgRelease()); StateChanged?.Invoke(); }
                }

                if (_active && Settings.GetBool("PeriodicReapply") && now.Length > 0)
                {
                    int period = Math.Max(1000, Settings.GetInt("Period", 2000));
                    int every = Math.Max(1, period / 1000);
                    if (_tick % every == 0) ApplyNowAsync();
                }

                if (_tick % 60 == 0) Eng.Prune();
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
            try { return Process.GetProcessesByName(exe.ToLowerInvariant().EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe).Length; }
            catch { return 0; }
        }
    }
}
