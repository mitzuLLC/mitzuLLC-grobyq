using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace Grobyq
{
    // One folder of a cleanup row with its own rule: which files (mask, age, recursion), which processes hold it, and
    // whether it is an app folder that must pass AppCaches.Safe before anything inside is deleted.
    public sealed class CleanPart
    {
        public string Path;
        public string Pattern = "*";
        public int MinAgeDays;                       // only files older than this (logs, replays)
        public bool TopOnly;
        public string[] Owners = new string[0];      // process names (no .exe): while one runs, the folder is skipped
        public string App;                           // shown in "close X" hints and the log
        public bool Guard;                           // app data folder: checked by AppCaches.Safe
        public bool Probe;                           // Chromium cache: skipped while its index files are held open
    }

    // One cleanup category: folders (or a special action), a size scan, a delete.
    public sealed class CleanItem
    {
        public string Key, Title, Desc, Note;
        public bool Recommended, Caution;
        public string Group = "windows";             // windows | apps
        public string AppGroup = "";                 // apps: games | chat | media | stream | devices | work | dev | other
        public Func<List<string>> Roots = () => new List<string>();
        public Func<List<CleanPart>> Parts;          // per-folder rules; when set, Roots / Pattern / MinAgeDays / TopOnly are not used
        public string Pattern = "*";                 // file mask inside the roots
        public int MinAgeDays;                       // only files older than this (logs)
        public bool TopOnly;                         // do not recurse (e.g. thumbcache_*.db)
        public string[] StopServices = new string[0];
        public string[] Owners = new string[0];      // processes that hold the item (Roots items)
        public string Special;                       // "recycle" | "do"
        public bool AskFirst;                        // a second question with the count and size before the delete (CS2 replays)
        public Action<Action<string>> After;         // a step after a clean (Spotify cache limit)

        public long Size; public int Files; public bool Scanned; public string ScanNote = "";
        public List<string> Running = new List<string>();    // apps of this row that run at the last scan (skipped by Clean)
        public List<string> Found = new List<string>();      // apps behind the folders (Other apps row)
        public DateTime Oldest = DateTime.MaxValue, Newest = DateTime.MinValue;   // file dates of the counted files
    }

    public static class Cleaner
    {
        private static string Win { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        private static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        private static string ProgData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }
        private static string LocalLow { get { return Path.Combine(Path.GetDirectoryName(Local) ?? Local, "LocalLow"); } }

        private static List<string> P(params string[] paths) { return paths.Where(p => !string.IsNullOrEmpty(p)).ToList(); }

        // Titles and texts are built in the current UI language when the page is created.
        public static List<CleanItem> Items()
        {
            var list = new List<CleanItem>
            {
                new CleanItem { Key = "usertemp", Recommended = true,
                    Title = L.T("Temporary files (user)"), Desc = L.T("%TEMP%: installers, unpacked archives and app scratch files. Files in use are skipped."),
                    Roots = () => P(Path.GetTempPath()) },
                new CleanItem { Key = "wintemp", Recommended = true,
                    Title = L.T("Temporary files (Windows)"), Desc = L.T("C:\\Windows\\Temp: leftovers of installers, updates and services. Files in use are skipped."),
                    Roots = () => P(Path.Combine(Win, "Temp")) },
                new CleanItem { Key = "wudl", Recommended = true, StopServices = new[] { "wuauserv", "bits" },
                    Title = L.T("Windows Update download cache"), Desc = L.T("SoftwareDistribution\\Download: update packages that are already installed. The Update services are stopped for the delete and started again (Microsoft's documented reset step)."),
                    Roots = () => P(Path.Combine(Win, @"SoftwareDistribution\Download")) },
                new CleanItem { Key = "do", Recommended = true, Special = "do",
                    Title = L.T("Delivery Optimization cache"), Desc = L.T("Update pieces Windows keeps to share with other PCs. Removed with Delete-DeliveryOptimizationCache."),
                    Roots = () => P(Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")) },
                new CleanItem { Key = "wer", Recommended = true,
                    Title = L.T("Error reports (WER)"), Desc = L.T("Queued and archived Windows Error Reporting files of crashed apps."),
                    Roots = () => P(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(ProgData, @"Microsoft\Windows\WER\Temp"), Path.Combine(Local, @"Microsoft\Windows\WER")) },
                new CleanItem { Key = "logs", Recommended = true, MinAgeDays = 7, Pattern = "*",
                    Title = L.T("Old Windows logs"), Desc = L.T("C:\\Windows\\Logs (CBS, DISM, Windows Update, setup) older than 7 days. Fresh logs stay for troubleshooting."),
                    Roots = () => P(Path.Combine(Win, "Logs"), Path.Combine(Win, @"System32\LogFiles\setupcln"), Path.Combine(Win, @"Panther\UnattendGC")) },
                new CleanItem { Key = "dumps", Caution = true,
                    Title = L.T("Crash dumps"), Desc = L.T("Minidumps, MEMORY.DMP, LiveKernelReports and app crash dumps. Can be gigabytes."),
                    Note = L.T("Keep them while you are hunting a BSOD / WHEA problem: they are the evidence."),
                    Roots = () => P(Path.Combine(Win, "Minidump"), Path.Combine(Win, "LiveKernelReports"), Path.Combine(Local, "CrashDumps"), Path.Combine(Win, "MEMORY.DMP")) },
                new CleanItem { Key = "thumbs",
                    Title = L.T("Thumbnail and icon cache"), Desc = L.T("Explorer thumbcache / iconcache databases. Rebuilt automatically; files Explorer holds open are skipped."),
                    Roots = () => P(Path.Combine(Local, @"Microsoft\Windows\Explorer")), Pattern = "*cache_*.db", TopOnly = true },
                new CleanItem { Key = "drivers", Recommended = true,
                    Title = L.T("Driver installer leftovers"), Desc = L.T("Unpacked and downloaded driver installers: C:\\NVIDIA, C:\\AMD and the downloads of the NVIDIA app and GeForce Experience. Not needed once the driver is installed."),
                    Parts = () => AppCaches.DriverParts(Env) },
                new CleanItem { Key = "shaders", Caution = true,
                    Title = L.T("GPU shader caches"), Desc = L.T("DirectX (D3DSCache), NVIDIA (DXCache, GLCache, NV_Cache), AMD (DxCache, VkCache), Intel and Steam shader caches."),
                    Note = L.T("Only after a driver update or when a game shows corrupted shaders: the first match after cleaning recompiles shaders and stutters."),
                    Roots = () => P(new[] { Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"), Path.Combine(LocalLow, @"NVIDIA\PerDriverVersion\DXCache"),
                                   Path.Combine(ProgData, @"NVIDIA Corporation\NV_Cache"), Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\DxcCache"), Path.Combine(Local, @"AMD\VkCache"), Path.Combine(Local, @"AMD\GLCache"),
                                   Path.Combine(LocalLow, @"Intel\ShaderCache") }.Concat(AppCaches.Dirs(Env, @"{SteamLib}\steamapps\shadercache")).ToArray()) },
                new CleanItem { Key = "browsers", Caution = true,
                    Title = L.T("Browser caches"), Desc = L.T("Cache, Code Cache, GPU and site offline caches (Service Worker) of Chrome, Edge, Yandex, Brave, Opera, Vivaldi and the Firefox cache. Logins, history and bookmarks are not touched."),
                    Note = L.T("A running browser is skipped: close it first."),
                    Parts = () => AppCaches.BrowserParts(Env) },
                new CleanItem { Key = "recycle", Special = "recycle", Caution = true,
                    Title = L.T("Recycle Bin"), Desc = L.T("Empties the Recycle Bin on all drives."),
                    Note = L.T("Deleted files cannot be restored afterwards.") },
            };
            return list;
        }

        // ---------------------------------------------------------------- app rows
        // Rows for the apps of the catalog found on this PC, the CS2 replays (CS2 installed) and the Other apps row.
        public static List<CleanItem> AppItems(List<AppDef> catalog)
        {
            var env = Env;
            var list = new List<CleanItem>();
            foreach (var d in catalog)
            {
                if (!AppCaches.Found(d, env)) continue;
                var def = d;
                list.Add(new CleanItem
                {
                    Key = "app:" + d.Key, Group = "apps", AppGroup = d.Group, Title = d.Name, Desc = d.Desc, Note = d.Note,
                    Caution = d.Caution, Recommended = !d.Caution, Owners = d.Owners,
                    Parts = () => AppCaches.Parts(def, Env),
                    After = def.After == null ? null : (Action<Action<string>>)(log => def.After(Env, log))
                });
            }
            if (AppCaches.Cs2Installed(env))
                list.Add(new CleanItem
                {
                    Key = "cs2replays", Group = "apps", AppGroup = "games", Caution = true, AskFirst = true, Owners = new[] { "cs2" },
                    Title = L.T("CS2 match replays (older than 30 days)"),
                    Desc = L.T("Downloaded demos in game\\csgo\\replays, 50-150 MB each. Valve keeps replays for about 30 days, so old ones cannot be downloaded again."),
                    Note = L.T("Asks again before deleting and shows how many and how big."),
                    Parts = () => AppCaches.Cs2Parts(Env)
                });
            list.Add(new CleanItem
            {
                Key = "app:other", Group = "apps", AppGroup = "other", Recommended = true,
                Title = L.T("Other apps (found automatically)"),
                Desc = L.T("Web caches of the other Chromium-based apps on this PC (Electron, CEF, WebView2): Cache, Code Cache and GPU caches only. Sign-ins and settings stay."),
                Parts = () => AppCaches.OtherParts(Env, catalog)
            });
            return list;
        }

        // ---------------------------------------------------------------- pass state
        // Folders and running processes are read once per scan / clean pass (BeginPass), not per row.
        private static CleanEnv _env;
        private static HashSet<string> _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static CleanEnv Env { get { return _env ?? (_env = CleanEnv.Current()); } set { _env = value; } }

        public static void BeginPass()
        {
            _env = CleanEnv.Current();
            _running = RunningNames();
        }

        // Tests set the running list directly.
        public static void SetRunning(IEnumerable<string> names) { _running = new HashSet<string>(names ?? new string[0], StringComparer.OrdinalIgnoreCase); }

        private static HashSet<string> RunningNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try { set.Add(p.ProcessName); } catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return set;
        }

        private static bool OwnerRunning(string[] owners)
        {
            if (owners == null) return false;
            foreach (var o in owners) if (!string.IsNullOrEmpty(o) && _running.Contains(o)) return true;
            return false;
        }

        // A part is busy while one of its processes runs, or (Chromium caches) while its index files are held open.
        private static bool Busy(CleanPart p) { return OwnerRunning(p.Owners) || (p.Probe && AppCaches.InUse(p.Path)); }

        public static List<CleanPart> PartsOf(CleanItem it)
        {
            if (it.Parts != null) { try { return it.Parts() ?? new List<CleanPart>(); } catch { return new List<CleanPart>(); } }
            var res = new List<CleanPart>();
            List<string> roots;
            try { roots = it.Roots(); } catch { roots = new List<string>(); }
            foreach (var r in roots) res.Add(new CleanPart { Path = r, Pattern = it.Pattern, MinAgeDays = it.MinAgeDays, TopOnly = it.TopOnly, Owners = it.Owners ?? new string[0], App = it.Title });
            return res;
        }

        // ---------------------------------------------------------------- scan
        public static void Scan(CleanItem it)
        {
            it.Size = 0; it.Files = 0; it.ScanNote = ""; it.Running = new List<string>(); it.Found = new List<string>();
            it.Oldest = DateTime.MaxValue; it.Newest = DateTime.MinValue;
            if (it.Special == "recycle") { RecycleInfo(out it.Size, out it.Files); it.Scanned = true; return; }
            foreach (var part in PartsOf(it))
            {
                string app = part.App ?? it.Title;
                if (!it.Found.Contains(app)) it.Found.Add(app);
                try { if (Busy(part) && !it.Running.Contains(app)) it.Running.Add(app); } catch { }
                DateTime cutoff = DateTime.Now.AddDays(-part.MinAgeDays);
                string root = part.Path;
                try
                {
                    if (File.Exists(root)) { var fi = new FileInfo(root); if (part.MinAgeDays == 0 || fi.LastWriteTime < cutoff) Count(it, fi); continue; }
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Walk(root, part.Pattern, part.TopOnly))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (part.MinAgeDays > 0 && fi.LastWriteTime >= cutoff) continue;
                            Count(it, fi);
                        }
                        catch { }
                    }
                }
                catch (UnauthorizedAccessException) { it.ScanNote = "access denied"; }
                catch { }
            }
            if (OwnerRunning(it.Owners) && it.Parts == null && !it.Running.Contains(it.Title)) it.Running.Add(it.Title);
            it.Scanned = true;
        }

        private static void Count(CleanItem it, FileInfo fi)
        {
            it.Size += fi.Length; it.Files++;
            var t = fi.LastWriteTime;
            if (t < it.Oldest) it.Oldest = t;
            if (t > it.Newest) it.Newest = t;
        }

        // Directory walk that survives access-denied folders and never follows junctions.
        private static IEnumerable<string> Walk(string root, string pattern, bool topOnly)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files = null, dirs = null;
                try { files = Directory.GetFiles(dir, pattern); } catch { }
                if (files != null) foreach (var f in files) yield return f;
                if (topOnly) continue;
                try { dirs = Directory.GetDirectories(dir); } catch { }
                if (dirs == null) continue;
                foreach (var d in dirs)
                {
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(d);
                }
            }
        }

        // ---------------------------------------------------------------- clean
        // A busy folder (its app runs) is skipped and named in the log; an app folder that fails AppCaches.Safe is never
        // touched. Files in use are skipped, never forced.
        public static long Clean(CleanItem it, Action<string> log)
        {
            if (it.Special == "recycle")
            {
                long sz; int n; RecycleInfo(out sz, out n);
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
                log(hr == 0 || hr == unchecked((int)0x8000FFFF) ? "[CL] Recycle Bin emptied" : "[CL] Recycle Bin: error 0x" + hr.ToString("X"));
                return sz;
            }
            if (it.Special == "do")
            {
                long before = it.Size;
                string o = RunTool("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue\"", null, 120000);
                log("[CL] Delivery Optimization cache: Delete-DeliveryOptimizationCache done" + (o.Trim().Length > 0 ? " (" + o.Trim() + ")" : ""));
                Scan(it);
                return Math.Max(0, before - it.Size);
            }
            if (it.Parts == null && OwnerRunning(it.Owners)) { log("[CL] " + it.Title + ": running - skipped (close it and clean again)"); return 0; }

            var stopped = new List<ServiceController>();
            foreach (var name in it.StopServices)
            {
                try
                {
                    var sc = new ServiceController(name);
                    if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                    {
                        sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        stopped.Add(sc); log("[CL] service stopped: " + name);
                    }
                }
                catch (Exception ex) { log("[CL] could not stop " + name + ": " + ex.Message); }
            }

            long freed = 0; int files = 0, locked = 0;
            var busy = new List<string>();
            bool ownerBusy = OwnerRunning(it.Owners);
            try
            {
                foreach (var part in PartsOf(it))
                {
                    string root = part.Path;
                    if (Busy(part))
                    {
                        string app = part.App ?? it.Title;
                        if (!busy.Contains(app)) busy.Add(app);
                        if (part.Owners != null && part.Owners.Length > 0) ownerBusy = true;
                        continue;
                    }
                    if (part.Guard)
                    {
                        string why;
                        if (!AppCaches.Safe(root, Env, out why)) { log("[CL] not touched: " + root + " (" + why + ")"); continue; }
                    }
                    DateTime cutoff = DateTime.Now.AddDays(-part.MinAgeDays);
                    if (File.Exists(root))
                    {
                        long len = 0; try { len = new FileInfo(root).Length; File.SetAttributes(root, FileAttributes.Normal); File.Delete(root); freed += len; files++; } catch { locked++; }
                        continue;
                    }
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Walk(root, part.Pattern, part.TopOnly).ToList())
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (part.MinAgeDays > 0 && fi.LastWriteTime >= cutoff) continue;
                            long len = fi.Length;
                            if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0) fi.Attributes = FileAttributes.Normal;
                            fi.Delete();
                            freed += len; files++;
                        }
                        catch { locked++; }
                    }
                    if (!part.TopOnly) RemoveEmptyDirs(root);
                }
            }
            finally
            {
                foreach (var sc in stopped)
                {
                    try { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); log("[CL] service started again: " + sc.ServiceName); }
                    catch (Exception ex) { log("[CL] start " + sc.ServiceName + " failed: " + ex.Message + " (it starts on demand anyway)"); }
                }
            }
            if (busy.Count > 0) log("[CL] " + it.Title + ": running, skipped - " + string.Join(", ", busy) + " (close and clean again)");
            log(string.Format("[CL] {0}: {1} files, {2} freed{3}", it.Title, files, Human(freed), locked > 0 ? ", " + locked + " in use / protected (skipped)" : ""));
            if (it.After != null && !ownerBusy)
            {
                try { it.After(log); } catch (Exception ex) { log("[CL] " + it.Title + ": " + ex.Message); }
            }
            return freed;
        }

        private static void RemoveEmptyDirs(string root)
        {
            try
            {
                foreach (var d in Directory.GetDirectories(root))
                {
                    try
                    {
                        if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                        RemoveEmptyDirs(d);
                        if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- recycle bin
        [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        private static void RecycleInfo(out long size, out int items)
        {
            size = 0; items = 0;
            try
            {
                var q = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(null, ref q) == 0) { size = q.i64Size; items = (int)Math.Min(int.MaxValue, q.i64NumItems); }
            }
            catch { }
        }

        // ---------------------------------------------------------------- Windows tools
        // Disk Cleanup handlers run by cleanmgr /sagerun (the official way to remove Windows.old, superseded updates, ...).
        public sealed class Handler { public string Key, Title, Desc; public bool Caution, Default; }

        public static List<Handler> Handlers()
        {
            var all = new List<Handler>
            {
                new Handler { Key = "Update Cleanup", Default = true, Title = L.T("Windows Update cleanup"), Desc = L.T("Superseded update versions in the component store. Installed updates can no longer be uninstalled afterwards.") },
                new Handler { Key = "Previous Installations", Default = true, Title = L.T("Previous Windows installation (Windows.old)"), Desc = L.T("The old Windows after a feature update or reinstall. Going back to the previous version is no longer possible."), Caution = true },
                new Handler { Key = "Windows Upgrade Log Files", Default = true, Title = L.T("Windows upgrade logs"), Desc = L.T("Setup logs from feature updates.") },
                new Handler { Key = "Temporary Setup Files", Default = true, Title = L.T("Temporary setup files"), Desc = L.T("Files left by setup programs.") },
                new Handler { Key = "Setup Log Files", Default = true, Title = L.T("Setup log files"), Desc = L.T("Windows setup logs.") },
                new Handler { Key = "Delivery Optimization Files", Default = true, Title = L.T("Delivery Optimization files"), Desc = L.T("Update pieces cached for peer sharing.") },
                new Handler { Key = "Windows Error Reporting Files", Default = true, Title = L.T("Windows error reports"), Desc = L.T("System-wide error report archives.") },
                new Handler { Key = "Old ChkDsk Files", Default = true, Title = L.T("Old ChkDsk files"), Desc = L.T("Fragments recovered by disk checks.") },
                new Handler { Key = "Feedback Hub Archive log files", Default = true, Title = L.T("Feedback Hub archives"), Desc = L.T("Diagnostic archives from Feedback Hub.") },
                new Handler { Key = "Diagnostic Data Viewer database files", Default = true, Title = L.T("Diagnostic Data Viewer database"), Desc = L.T("Local copy of diagnostic data events.") },
                new Handler { Key = "Device Driver Packages", Title = L.T("Old device driver packages"), Desc = L.T("Older versions of drivers that were updated. Rolling a driver back to those versions is no longer possible."), Caution = true },
                new Handler { Key = "Windows ESD installation files", Title = L.T("Windows ESD installation files"), Desc = L.T("Needed for 'Reset this PC' without a download. Remove only if you reinstall from USB."), Caution = true },
            };
            var res = new List<Handler>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches", false))
                {
                    if (k == null) return res;
                    var have = new HashSet<string>(k.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
                    res.AddRange(all.Where(h => have.Contains(h.Key)));
                }
            }
            catch { }
            return res;
        }

        public static string RunDiskCleanup(IEnumerable<string> keys, Action<string> log)
        {
            const string flag = "StateFlags0777";
            var set = new List<string>();
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches", true))
                {
                    if (root == null) return "VolumeCaches key not found";
                    foreach (var name in root.GetSubKeyNames())
                        using (var h = root.OpenSubKey(name, true)) if (h != null && h.GetValue(flag) != null) h.DeleteValue(flag, false);
                    foreach (var key in keys)
                        using (var h = root.OpenSubKey(key, true)) if (h != null) { h.SetValue(flag, 2, RegistryValueKind.DWord); set.Add(key); }
                }
                if (set.Count == 0) return "nothing selected";
                log("[CL] cleanmgr /sagerun:777 - " + string.Join(", ", set));
                RunTool("cleanmgr.exe", "/sagerun:777", null, 60 * 60 * 1000);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
            finally
            {
                try
                {
                    using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches", true))
                        if (root != null) foreach (var key in set) using (var h = root.OpenSubKey(key, true)) if (h != null) h.DeleteValue(flag, false);
                }
                catch { }
            }
        }

        // Runs a console tool and streams its lines (DISM prints \r progress: only whole lines are forwarded).
        public static string RunTool(string exe, string args, Action<string> onLine, int timeoutMs)
        {
            var all = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Oem(), StandardErrorEncoding = Oem() };
                using (var p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) return; string l = e.Data.Trim(); if (l.Length == 0) return; all.AppendLine(l); if (onLine != null) onLine(l); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; string l = e.Data.Trim(); if (l.Length == 0) return; all.AppendLine(l); if (onLine != null) onLine(l); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } all.AppendLine("timeout"); }
                    else p.WaitForExit();
                }
            }
            catch (Exception ex) { all.AppendLine(ex.Message); }
            return all.ToString();
        }

        private static Encoding Oem()
        {
            try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.Default; }
        }

        public static int ClearEventLogs(Action<string> log)
        {
            int n = 0, fail = 0;
            string list = RunTool("wevtutil.exe", "el", null, 60000);
            foreach (var raw in list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                string o = RunTool("wevtutil.exe", "cl \"" + name + "\"", null, 30000);
                if (o.Trim().Length == 0) n++; else fail++;
            }
            log(string.Format("[CL] event logs cleared: {0} ({1} skipped - in use or protected)", n, fail));
            return n;
        }

        // ---------------------------------------------------------------- hibernation file
        // powercfg /hibernate off deletes hiberfil.sys and turns off Hibernate and Fast Startup; on brings them back (T1, powercfg).
        public static string HiberPath { get { string sd = Env.SystemDrive; return Path.Combine(string.IsNullOrEmpty(sd) ? @"C:\" : sd, "hiberfil.sys"); } }

        public static long HiberSize()
        {
            try { var fi = new FileInfo(HiberPath); return fi.Exists ? fi.Length : -1; } catch { return -1; }
        }

        public static string SetHibernate(bool on, Action<string> log)
        {
            string o = RunTool("powercfg.exe", on ? "/hibernate on" : "/hibernate off", null, 60000).Trim();
            log("[CL] powercfg /hibernate " + (on ? "on" : "off") + (o.Length > 0 ? " - " + o : ""));
            return o;
        }

        public static string Human(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double v = bytes; string[] u = { "KB", "MB", "GB", "TB" }; int i = -1;
            do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + u[i];
        }
    }
}
