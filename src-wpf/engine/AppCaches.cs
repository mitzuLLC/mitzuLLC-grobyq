using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Grobyq
{
    // Where app-cache cleanup may delete: this user's and the machine's app data roots, Steam and its libraries, and a
    // few dedicated folders (C:\NVIDIA, C:\AMD, the Ubisoft launcher). Resolved once per scan / clean pass; the selftest
    // builds one over a sandbox folder.
    public sealed class CleanEnv
    {
        public string Local = "", Roaming = "", ProgramData = "", LocalLow = "", SystemDrive = "", ProgramFiles = "", ProgramFilesX86 = "", Steam = "";
        public List<string> SteamLibs = new List<string>();

        public sealed class Base { public readonly string Path; public readonly int MinDepth; public Base(string p, int d) { Path = p; MinDepth = d; } }

        public static CleanEnv Current()
        {
            var e = new CleanEnv();
            try
            {
                e.Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? "";
                e.Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? "";
                e.ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) ?? "";
                e.LocalLow = e.Local.Length > 0 ? Path.Combine(Path.GetDirectoryName(e.Local) ?? e.Local, "LocalLow") : "";
                string sd = Environment.GetEnvironmentVariable("SystemDrive");
                e.SystemDrive = string.IsNullOrEmpty(sd) ? "" : sd.TrimEnd('\\') + "\\";
                e.ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) ?? "";
                e.ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) ?? "";
            }
            catch { }
            e.Steam = SteamRoot(e.ProgramFilesX86);
            e.SteamLibs = SteamLibraries(e.Steam);
            return e;
        }

        private static string SteamRoot(string pf86)
        {
            string p = null;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) p = k == null ? null : k.GetValue("SteamPath") as string; } catch { }
            if (string.IsNullOrEmpty(p))
                try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) p = k == null ? null : k.GetValue("InstallPath") as string; } catch { }
            if (string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(pf86)) p = Path.Combine(pf86, "Steam");
            if (string.IsNullOrEmpty(p)) return "";
            try { p = Path.GetFullPath(p.Replace('/', '\\')).TrimEnd('\\'); } catch { return ""; }
            return Directory.Exists(p) ? p : "";
        }

        // The Steam folder plus every library from steamapps\libraryfolders.vdf ("path" "D:\\SteamLibrary").
        public static List<string> SteamLibraries(string steam)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(steam)) return res;
            res.Add(steam);
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    {
                        string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                        try { lib = Path.GetFullPath(lib).TrimEnd('\\'); } catch { continue; }
                        if (Directory.Exists(lib) && !res.Any(x => string.Equals(x, lib, StringComparison.OrdinalIgnoreCase))) res.Add(lib);
                    }
            }
            catch { }
            return res;
        }

        // Allowed roots, most specific first, each with the least depth a target folder must have below it
        // (never %APPDATA% itself or an app's whole folder: always a named cache / log folder inside it).
        public List<Base> Bases()
        {
            var b = new List<Base>();
            Action<string, int> add = (p, d) => { if (!string.IsNullOrEmpty(p)) b.Add(new Base(p, d)); };
            if (Steam.Length > 0) add(Steam, 1);
            foreach (var l in SteamLibs) if (!string.Equals(l, Steam, StringComparison.OrdinalIgnoreCase)) add(l, 2);
            if (SystemDrive.Length > 0) { add(Path.Combine(SystemDrive, "NVIDIA"), 0); add(Path.Combine(SystemDrive, "AMD"), 0); }
            foreach (var pf in new[] { ProgramFilesX86, ProgramFiles }) if (!string.IsNullOrEmpty(pf)) add(Path.Combine(pf, "Ubisoft", "Ubisoft Game Launcher"), 1);
            add(Local, 2); add(Roaming, 2); add(ProgramData, 2); add(LocalLow, 2);
            return b;
        }

        public List<string> Starts(string token)
        {
            switch (token)
            {
                case "Local": return One(Local);
                case "Roaming": return One(Roaming);
                case "ProgramData": return One(ProgramData);
                case "LocalLow": return One(LocalLow);
                case "SysDrive": return One(SystemDrive);
                case "PF": return One(ProgramFiles);
                case "PF86": return One(ProgramFilesX86);
                case "Steam": return One(Steam);
                case "SteamLib": return new List<string>(SteamLibs);
            }
            return new List<string>();
        }

        private static List<string> One(string p) { return string.IsNullOrEmpty(p) ? new List<string>() : new List<string> { p }; }
    }

    // One folder rule of an app: Chromium caches under a profile root, a whole cache folder, or logs older than N days.
    public sealed class AppSpec
    {
        public string Kind;             // cr | dir | logs
        public string Path;             // {Token}\segment\... - wildcards allowed in any segment
        public int Days;
        public string Pattern = "*";
        public bool Top;                // only the files directly in the folder
        public string[] Owners;         // null = the app's processes; empty = none (old logs and crash dumps are never open)
    }

    public sealed class AppDef
    {
        public string Key, Name, Group, Desc, Note;
        public string[] Owners = new string[0];
        public bool Caution;
        public List<AppSpec> Specs = new List<AppSpec>();
        public Action<CleanEnv, Action<string>> After;
    }

    // App caches: a catalog of popular apps (games and launchers, chat, music, streaming, device and driver apps, work
    // and developer tools) with the folders that are pure caches, old logs or crash reports - plus any other
    // Chromium-based app found on the PC (only its Chromium cache folders). Sign-ins, settings, chats, saves, downloads
    // and anti-cheat folders are never touched: see Deny and Safe.
    public static class AppCaches
    {
        private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        // Chromium / Electron / CEF / WebView2 caches inside a profile folder: all re-downloaded or rebuilt on demand.
        // Never Cookies, Local Storage, IndexedDB, Session Storage, Network (sign-ins) or Preferences.
        public static readonly string[] ChromiumDirs =
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GraphiteDawnCache",
            "GrShaderCache", "ShaderCache", @"Service Worker\CacheStorage", @"Service Worker\ScriptCache", "blob_storage", @"Crashpad\reports"
        };

        // A folder with one of these names is never cleaned, at any depth below the base: sign-ins and site data, settings,
        // the user's own files, Spotify downloads (Storage), Steam config / userdata, Telegram's account folder, anti-cheat.
        public static readonly HashSet<string> Deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cookies", "Local Storage", "Session Storage", "IndexedDB", "Network", "Login Data", "Web Data", "Sync Data", "Local State",
            "Storage", "Extensions", "Users", "userdata", "config", "Saved Games", "saves", "Documents", "Desktop", "Downloads", "Pictures", "Videos", "Music",
            "D877F783D5D3EF8C",
            "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye", "FACEIT", "FACEIT AC", "Vanguard", "Riot Vanguard", "Riot Games", "Riot Client",
            "ESEA", "PunkBuster", "GameGuard", "nProtect", "XIGNCODE", "XIGNCODE3", "AntiCheatExpert", "mhyprot"
        };

        // Files never deleted from a guarded folder, whatever folder they are found in (sign-in, settings, Telegram key).
        public static readonly HashSet<string> ProtectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "key_datas", "Cookies", "Cookies-journal", "Login Data", "Login Data-journal", "Login Data For Account", "Web Data", "Web Data-journal",
            "Local State", "Preferences", "Secure Preferences", "prefs", "loginusers.vdf", "config.vdf", "localconfig.vdf", "settings.json"
        };

        // ---------------------------------------------------------------- catalog
        private static string[] Split(string owners) { return string.IsNullOrEmpty(owners) ? new string[0] : owners.Split('|'); }
        private static AppSpec Cr(string path) { return new AppSpec { Kind = "cr", Path = path }; }
        private static AppSpec CrBy(string path, string owners) { return new AppSpec { Kind = "cr", Path = path, Owners = Split(owners) }; }
        private static AppSpec Dir(string path) { return new AppSpec { Kind = "dir", Path = path }; }
        private static AppSpec Dump(string path) { return new AppSpec { Kind = "dir", Path = path, Owners = new string[0] }; }
        private static AppSpec Logs(string path, int days = 7, string pattern = "*") { return new AppSpec { Kind = "logs", Path = path, Days = days, Pattern = pattern, Owners = new string[0] }; }
        private static AppSpec LogsTop(string path, string pattern) { return new AppSpec { Kind = "logs", Path = path, Days = 7, Pattern = pattern, Top = true, Owners = new string[0] }; }

        private static AppDef App(string key, string name, string group, string owners, string desc, params AppSpec[] specs)
        {
            return new AppDef { Key = key, Name = name, Group = group, Owners = Split(owners), Desc = desc, Specs = specs.ToList() };
        }

        // Titles and texts in the current UI language (built when the Cleanup page is created).
        public static List<AppDef> Catalog()
        {
            string web = L.T("Web, code and GPU caches, old logs and crash reports. Sign-in and settings stay.");
            string chat = L.T("Web, code and GPU caches, old logs and crash reports. Sign-in, chats and settings stay.");
            string launcher = L.T("Store web cache, old logs and crash reports. Sign-in and installed games stay.");
            string logs = L.T("Old logs and crash reports. Settings and profiles stay.");
            string device = L.T("Old logs and the app's web cache. Devices, profiles and macros stay.");
            string pkg = L.T("Package download cache. Packages download again when a project needs them.");
            string code = L.T("Web and GPU caches, compiled code cache, downloaded extension packages, old logs. Settings, extensions and workspaces stay.");
            var c = new List<AppDef>();

            // ---- games and launchers
            c.Add(App("steam", "Steam", "games", "steam|steamwebhelper", L.T("Store and library web cache, logs older than 7 days, crash dumps. Sign-in, games, settings and the shader cache stay."),
                Dir(@"{Steam}\appcache\httpcache"), Cr(@"{Local}\Steam\htmlcache"), Logs(@"{Steam}\logs"), Dump(@"{Steam}\dumps")));
            c.Add(App("epic", "Epic Games Launcher", "games", "EpicGamesLauncher|EpicWebHelper", launcher,
                Dir(@"{Local}\EpicGamesLauncher\Saved\webcache*"), Logs(@"{Local}\EpicGamesLauncher\Saved\Logs"), Dump(@"{Local}\EpicGamesLauncher\Saved\Crashes"),
                Cr(@"{Local}\EpicGames\eosoverlay\BrowserCache")));
            c.Add(App("battlenet", "Battle.net", "games", "Battle.net", launcher,
                Cr(@"{Local}\Battle.net\BrowserCaches\*"), Cr(@"{Local}\Battle.net\BrowserCache"), Dir(@"{Local}\Battle.net\Cache"),
                Dir(@"{Local}\Blizzard Entertainment\Battle.net\Cache"), Dir(@"{ProgramData}\Blizzard Entertainment\Battle.net\Cache"),
                Logs(@"{Local}\Battle.net\Logs"), Dump(@"{Local}\Battle.net\Errors")));
            c.Add(App("ea", "EA app", "games", "EADesktop", launcher,
                Dir(@"{Local}\EADesktop\cache"), Cr(@"{Local}\EADesktop\QtWebEngine\Default"), Cr(@"{Local}\Electronic Arts\EA Desktop\*Cache"),
                Logs(@"{Local}\Electronic Arts\EA Desktop\Logs"), Logs(@"{ProgramData}\EA Desktop\Logs")));
            c.Add(App("ubisoft", "Ubisoft Connect", "games", "UbisoftConnect|upc", launcher,
                Dir(@"{PF86}\Ubisoft\Ubisoft Game Launcher\cache\http*"), Dir(@"{PF86}\Ubisoft\Ubisoft Game Launcher\cache\assets"), Logs(@"{PF86}\Ubisoft\Ubisoft Game Launcher\logs"),
                Dir(@"{PF}\Ubisoft\Ubisoft Game Launcher\cache\http*"), Dir(@"{PF}\Ubisoft\Ubisoft Game Launcher\cache\assets"), Logs(@"{PF}\Ubisoft\Ubisoft Game Launcher\logs")));
            c.Add(App("gog", "GOG GALAXY", "games", "GalaxyClient", launcher,
                Cr(@"{ProgramData}\GOG.com\Galaxy\webcache\common"), Dump(@"{ProgramData}\GOG.com\Galaxy\crashdumps"), Logs(@"{ProgramData}\GOG.com\Galaxy\logs")));
            c.Add(App("overwolf", "Overwolf", "games", "Overwolf|OverwolfBrowser", web, Cr(@"{Local}\Overwolf\BrowserCache"), Logs(@"{Local}\Overwolf\Log"), Logs(@"{Local}\Overwolf\Logs")));

            // ---- chat and calls
            c.Add(App("discord", "Discord", "chat", "Discord|DiscordPTB|DiscordCanary|DiscordDevelopment", chat,
                Cr(@"{Roaming}\discord"), Cr(@"{Roaming}\discordptb"), Cr(@"{Roaming}\discordcanary"), Cr(@"{Roaming}\discorddevelopment"), Logs(@"{Roaming}\discord*\logs")));
            c.Add(App("telegram", "Telegram", "chat", "Telegram", L.T("Media and file cache - what Telegram's own Clear cache removes. Chats, sign-in and keys stay."),
                Dir(@"{Roaming}\Telegram Desktop\tdata\user_data*\cache"), Dir(@"{Roaming}\Telegram Desktop\tdata\user_data*\media_cache"), Dump(@"{Roaming}\Telegram Desktop\tdata\dumps")));
            c.Add(App("whatsapp", "WhatsApp", "chat", "WhatsApp|WhatsApp.Root", chat,
                Cr(@"{Roaming}\WhatsApp"), Cr(@"{Local}\Packages\5319275A.WhatsAppDesktop_*\LocalState\EBWebView"), Logs(@"{Roaming}\WhatsApp\logs")));
            c.Add(App("slack", "Slack", "chat", "slack", chat, Cr(@"{Roaming}\Slack"), Cr(@"{Local}\Packages\91750D7E.Slack_*\LocalCache\Roaming\Slack"), Logs(@"{Roaming}\Slack\logs")));
            c.Add(App("teams", "Microsoft Teams", "chat", "ms-teams|Teams", chat,
                Cr(@"{Local}\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView"), Logs(@"{Local}\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs"),
                Cr(@"{Roaming}\Microsoft\Teams")));
            c.Add(App("zoom", "Zoom", "chat", "Zoom", chat, Logs(@"{Roaming}\Zoom\logs"), Cr(@"{Roaming}\Zoom\data\WebviewCache\*\ZoomApps\*\CefWebView")));
            c.Add(App("signal", "Signal", "chat", "Signal", chat, Cr(@"{Roaming}\Signal"), Logs(@"{Roaming}\Signal\logs")));

            // ---- music
            var spotify = App("spotify", "Spotify", "media", "Spotify", L.T("Streaming cache and the built-in browser cache, then the cache is kept under 1 GB (storage.size). Downloaded songs and sign-in stay."),
                Dir(@"{Local}\Spotify\Data"), Cr(@"{Local}\Spotify\Browser"),
                Dir(@"{Local}\Packages\SpotifyAB.SpotifyMusic_*\LocalCache\Spotify\Data"), Cr(@"{Local}\Packages\SpotifyAB.SpotifyMusic_*\LocalCache\Spotify\Browser"));
            spotify.After = SpotifyLimit;
            c.Add(spotify);
            c.Add(App("deezer", "Deezer", "media", "Deezer", web, Cr(@"{Roaming}\Deezer*")));

            // ---- streaming and recording
            c.Add(App("obs", "OBS Studio", "stream", "obs64|obs32", L.T("Old logs, crash reports and the browser source cache. Scenes, profiles and dock sign-ins stay."),
                Logs(@"{Roaming}\obs-studio\logs"), Dump(@"{Roaming}\obs-studio\crashes"), Dump(@"{Roaming}\obs-studio\profiler_data"), Cr(@"{Roaming}\obs-studio\plugin_config\obs-browser")));
            c.Add(App("streamlabs", "Streamlabs Desktop", "stream", "Streamlabs OBS|Streamlabs Desktop", web,
                Cr(@"{Roaming}\slobs-client"), Dump(@"{Roaming}\slobs-client\CrashMemoryDump"), Logs(@"{Roaming}\slobs-client\node-obs\logs")));
            c.Add(App("medal", "Medal", "stream", "Medal", web, Cr(@"{Roaming}\Medal")));
            c.Add(App("nvbroadcast", "NVIDIA Broadcast", "stream", "NVIDIA Broadcast", logs,
                Cr(@"{Roaming}\nvidia-broadcast"), Logs(@"{Roaming}\nvidia-broadcast\logs"), Logs(@"{ProgramData}\NVIDIA Corporation\NVIDIA Broadcast\logs")));

            // ---- drivers and devices
            c.Add(App("nvidiaapp", "NVIDIA app", "devices", "NVIDIA app", L.T("Built-in browser caches of the NVIDIA app, the overlay and GeForce Experience, old logs. Driver settings stay."),
                Cr(@"{Local}\NVIDIA Corporation\NVIDIA App\CefCache"), CrBy(@"{Local}\NVIDIA Corporation\NVIDIA Overlay\CefCache", "NVIDIA Overlay"),
                CrBy(@"{Local}\NVIDIA Corporation\GeForce Experience\CefCache", "NVIDIA GeForce Experience|NVIDIA Share"), LogsTop(@"{ProgramData}\NVIDIA Corporation\NVIDIA App", "*.log")));
            c.Add(App("amd", "AMD Software", "devices", "RadeonSoftware", L.T("Built-in browser cache and old logs of AMD Software: Adrenalin Edition. Driver settings stay."),
                Dir(@"{Local}\AMD\RadeonSoftware\Cache"), Cr(@"{Local}\AMD\RadeonSoftware\QtWebEngine\Default"), Logs(@"{Local}\AMD\CN", 7, "*.log")));
            c.Add(App("razer", "Razer Synapse", "devices", "Razer Synapse|Razer Synapse 3|RazerAppEngine", device,
                Logs(@"{Local}\Razer\*\Log"), Logs(@"{Local}\Razer\*\Logs"), Logs(@"{ProgramData}\Razer\*\Log"), Logs(@"{ProgramData}\Razer\*\Logs"), Cr(@"{Local}\Razer\RazerAppEngine\User Data")));
            c.Add(App("ghub", "Logitech G HUB", "devices", "lghub", device, Cr(@"{Roaming}\G HUB")));
            c.Add(App("steelseries", "SteelSeries GG", "devices", "SteelSeriesGG|SteelSeriesGGClient|SteelSeriesEngine", device,
                Cr(@"{Roaming}\SteelSeries-Engine-*-Client"), Logs(@"{ProgramData}\SteelSeries\SteelSeries Engine*\Logs"), Logs(@"{ProgramData}\SteelSeries\GG\logs")));
            c.Add(App("corsair", "Corsair iCUE", "devices", "iCUE", logs, Logs(@"{Local}\Corsair\CUE*\logs"), Logs(@"{ProgramData}\Corsair\CUE*\Service logs")));

            // ---- work
            c.Add(App("notion", "Notion", "work", "Notion", web, Cr(@"{Roaming}\Notion"), Logs(@"{Roaming}\Notion\logs")));
            c.Add(App("obsidian", "Obsidian", "work", "Obsidian", web, Cr(@"{Roaming}\obsidian")));
            c.Add(App("figma", "Figma", "work", "Figma|figma_agent", web, Cr(@"{Roaming}\Figma"), Cr(@"{Roaming}\Figma\DesktopProfile\*")));
            c.Add(App("onedrive", "OneDrive", "work", "", L.T("Old logs. Sync and settings stay."), Logs(@"{Local}\Microsoft\OneDrive\logs")));
            var adobe = App("adobe", "Adobe media cache", "work", "Adobe Premiere Pro|AfterFX|Adobe Media Encoder", L.T("Media cache of Premiere Pro, After Effects and Media Encoder (conformed audio, peak files)."),
                Dir(@"{Roaming}\Adobe\Common\Media Cache Files"), Dir(@"{Roaming}\Adobe\Common\Media Cache"), Dir(@"{Roaming}\Adobe\Common\PTX"));
            adobe.Caution = true;
            adobe.Note = L.T("Projects open slower once while the media cache is rebuilt.");
            c.Add(adobe);

            // ---- developer tools
            foreach (var vs in new[] { new[] { "vscode", "Visual Studio Code", "Code" }, new[] { "cursor", "Cursor", "Cursor" }, new[] { "windsurf", "Windsurf", "Windsurf" } })
                c.Add(App(vs[0], vs[1], "dev", vs[2], code,
                    Cr(@"{Roaming}\" + vs[2]), Dir(@"{Roaming}\" + vs[2] + @"\CachedData"), Dir(@"{Roaming}\" + vs[2] + @"\CachedExtensionVSIXs"), Logs(@"{Roaming}\" + vs[2] + @"\logs")));
            var jb = App("jetbrains", "JetBrains IDEs", "dev", "idea64|pycharm64|rider64|webstorm64|clion64|goland64|phpstorm64|datagrip64|rustrover64|studio64",
                L.T("Index and cache folders of IntelliJ IDEA, PyCharm, Rider, Android Studio and the other JetBrains IDEs."),
                Dir(@"{Local}\JetBrains\*\caches"), Logs(@"{Local}\JetBrains\*\log"), Dir(@"{Local}\Google\AndroidStudio*\caches"));
            jb.Caution = true;
            jb.Note = L.T("The next project open re-indexes and takes a while.");
            c.Add(jb);
            c.Add(App("githubdesktop", "GitHub Desktop", "dev", "GitHubDesktop", web, Cr(@"{Roaming}\GitHub Desktop"), Logs(@"{Roaming}\GitHub Desktop\logs")));
            c.Add(App("postman", "Postman", "dev", "Postman", web, Cr(@"{Roaming}\Postman"), Logs(@"{Roaming}\Postman\logs")));
            c.Add(App("pip", "pip (Python)", "dev", "", pkg, Dir(@"{Local}\pip\cache")));
            c.Add(App("npm", "npm (Node.js)", "dev", "", pkg, Dir(@"{Local}\npm-cache\_cacache")));
            c.Add(App("yarn", "Yarn", "dev", "", pkg, Dir(@"{Local}\Yarn\Cache")));
            c.Add(App("nuget", "NuGet", "dev", "", pkg, Dir(@"{Local}\NuGet\v3-cache"), Dir(@"{Local}\NuGet\plugins-cache")));
            return c;
        }

        public static string GroupTitle(string g)
        {
            switch (g)
            {
                case "games": return L.T("Games and launchers");
                case "chat": return L.T("Chat and calls");
                case "media": return L.T("Music");
                case "stream": return L.T("Streaming and recording");
                case "devices": return L.T("Drivers and devices");
                case "work": return L.T("Work");
                case "dev": return L.T("Developer tools");
                case "other": return L.T("Other apps");
            }
            return g;
        }

        public static readonly string[] GroupOrder = { "games", "chat", "media", "stream", "devices", "work", "dev", "other" };

        // ---------------------------------------------------------------- browsers (the Windows list's Browser caches row)
        // name | owner process | Chromium user data folder
        private static readonly string[][] ChromiumBrowsers =
        {
            new[] { "Google Chrome", "chrome", @"{Local}\Google\Chrome\User Data" },
            new[] { "Google Chrome", "chrome", @"{Local}\Google\Chrome Beta\User Data" },
            new[] { "Microsoft Edge", "msedge", @"{Local}\Microsoft\Edge\User Data" },
            new[] { "Yandex Browser", "browser", @"{Local}\Yandex\YandexBrowser\User Data" },
            new[] { "Brave", "brave", @"{Local}\BraveSoftware\Brave-Browser\User Data" },
            new[] { "Vivaldi", "vivaldi", @"{Local}\Vivaldi\User Data" },
            new[] { "Chromium", "chrome", @"{Local}\Chromium\User Data" },
            new[] { "Opera", "opera", @"{Roaming}\Opera Software\Opera Stable" },
            new[] { "Opera", "opera", @"{Local}\Opera Software\Opera Stable" },
            new[] { "Opera GX", "opera", @"{Roaming}\Opera Software\Opera GX Stable" },
            new[] { "Opera GX", "opera", @"{Local}\Opera Software\Opera GX Stable" },
        };

        // name | owner process | Gecko profiles folder (the cache lives in the Local copy of each profile)
        private static readonly string[][] GeckoBrowsers =
        {
            new[] { "Firefox", "firefox", @"{Local}\Mozilla\Firefox\Profiles\*" },
            new[] { "Zen", "zen", @"{Local}\zen\Profiles\*" },
            new[] { "Floorp", "floorp", @"{Local}\Floorp\Profiles\*" },
            new[] { "LibreWolf", "librewolf", @"{Local}\LibreWolf\Profiles\*" },
            new[] { "Waterfox", "waterfox", @"{Local}\Waterfox\Profiles\*" },
        };

        private static readonly string[] GeckoDirs = { "cache2", "startupCache", "thumbnails", "jumpListCache" };

        public static List<CleanPart> BrowserParts(CleanEnv env)
        {
            var res = new List<CleanPart>();
            foreach (var b in ChromiumBrowsers)
                foreach (var root in Dirs(env, b[2]))
                    foreach (var p in ChromiumCaches(root))
                        AddPart(res, new CleanPart { Path = p, Owners = new[] { b[1] }, App = b[0], Guard = true, Probe = true });
            foreach (var b in GeckoBrowsers)
                foreach (var prof in Dirs(env, b[2]))
                    foreach (var sub in GeckoDirs)
                    {
                        string p = Path.Combine(prof, sub);
                        if (Directory.Exists(p)) AddPart(res, new CleanPart { Path = p, Owners = new[] { b[1] }, App = b[0], Guard = true });
                    }
            return res;
        }

        // ---------------------------------------------------------------- expansion
        // {Token}\a\b*\c -> the existing folders it names (several for {SteamLib} or a wildcard).
        public static List<string> Dirs(CleanEnv env, string spec)
        {
            var res = new List<string>();
            if (env == null || string.IsNullOrEmpty(spec) || spec[0] != '{') return res;
            int close = spec.IndexOf('}');
            if (close < 0) return res;
            string token = spec.Substring(1, close - 1);
            string[] segs = spec.Substring(close + 1).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var start in env.Starts(token))
            {
                var cur = new List<string> { start };
                foreach (var s in segs)
                {
                    var next = new List<string>();
                    bool wild = s.IndexOfAny(new[] { '*', '?' }) >= 0;
                    foreach (var c in cur)
                    {
                        if (!wild) { next.Add(Path.Combine(c, s)); continue; }
                        try { if (Directory.Exists(c)) next.AddRange(Directory.GetDirectories(c, s)); } catch { }
                    }
                    cur = next;
                    if (cur.Count == 0) break;
                }
                foreach (var c in cur)
                    if (Directory.Exists(c) && !res.Any(x => string.Equals(x, c, OIC))) res.Add(c);
            }
            return res;
        }

        private static IEnumerable<string> SubDirs(string dir)
        {
            try { if (Directory.Exists(dir)) return Directory.GetDirectories(dir); } catch { }
            return new string[0];
        }

        // The root itself (Electron keeps one profile there), its Chromium profiles (Default, Profile N, WebView2 profiles,
        // any folder with a Preferences file) and Electron partitions.
        public static List<string> Profiles(string root)
        {
            var res = new List<string> { root };
            foreach (var s in SubDirs(root))
            {
                string n = Path.GetFileName(s);
                if (n.Equals("Default", OIC) || n.StartsWith("Profile ", OIC) || n.Equals("Guest Profile", OIC) || n.StartsWith("WV2Profile", OIC)
                    || File.Exists(Path.Combine(s, "Preferences")))
                    res.Add(s);
            }
            res.AddRange(SubDirs(Path.Combine(root, "partitions")));
            return res;
        }

        public static List<string> ChromiumCaches(string root)
        {
            var res = new List<string>();
            foreach (var prof in Profiles(root))
                foreach (var c in ChromiumDirs)
                {
                    string p = Path.Combine(prof, c.Replace('\\', Path.DirectorySeparatorChar));
                    if (Directory.Exists(p) && !res.Any(x => string.Equals(x, p, OIC))) res.Add(p);
                }
            return res;
        }

        private static void AddPart(List<CleanPart> list, CleanPart p)
        {
            if (!list.Any(x => string.Equals(x.Path, p.Path, OIC) && x.MinAgeDays == p.MinAgeDays && x.Pattern == p.Pattern)) list.Add(p);
        }

        public static List<CleanPart> Parts(AppDef d, CleanEnv env)
        {
            var res = new List<CleanPart>();
            foreach (var s in d.Specs)
            {
                string[] owners = s.Owners ?? d.Owners;
                foreach (var dir in Dirs(env, s.Path))
                {
                    if (s.Kind == "cr")
                        foreach (var p in ChromiumCaches(dir)) AddPart(res, new CleanPart { Path = p, Owners = owners, App = d.Name, Guard = true, Probe = true });
                    else
                        AddPart(res, new CleanPart { Path = dir, Owners = owners, App = d.Name, Guard = true, MinAgeDays = s.Kind == "logs" ? s.Days : 0, Pattern = s.Pattern ?? "*", TopOnly = s.Top });
                }
            }
            return res;
        }

        // Installed = one of its folders exists (nothing to show for an app that never ran).
        public static bool Found(AppDef d, CleanEnv env)
        {
            foreach (var s in d.Specs) if (Dirs(env, s.Path).Count > 0) return true;
            return false;
        }

        // ---------------------------------------------------------------- safety
        // A target folder must lie inside an allowed base, deep enough below it, with no protected folder name and no
        // junction / symbolic link on the way (a link planted in an app folder must not steer the delete elsewhere).
        public static bool Safe(string path, CleanEnv env, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(path) || env == null) { why = "empty path"; return false; }
            char sep = Path.DirectorySeparatorChar;
            string full;
            try { full = Path.GetFullPath(path).TrimEnd(sep, Path.AltDirectorySeparatorChar); } catch { why = "invalid path"; return false; }
            foreach (var b in env.Bases())
            {
                string root;
                try { root = Path.GetFullPath(b.Path).TrimEnd(sep, Path.AltDirectorySeparatorChar); } catch { continue; }
                if (root.Length < 3) continue;
                bool same = string.Equals(full, root, OIC);
                if (!same && !full.StartsWith(root + sep, OIC)) continue;
                string[] segs = same ? new string[0] : full.Substring(root.Length + 1).Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length < b.MinDepth) { why = "too close to " + root; return false; }
                foreach (var s in segs) if (Deny.Contains(s)) { why = "protected folder '" + s + "'"; return false; }
                string cur = root;
                foreach (var s in segs)
                {
                    cur = Path.Combine(cur, s);
                    try
                    {
                        if (!Directory.Exists(cur)) break;
                        if ((File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0) { why = "junction at " + cur; return false; }
                    }
                    catch { why = "unreadable " + cur; return false; }
                }
                return true;
            }
            why = "outside the app data folders";
            return false;
        }

        // A Chromium cache an app holds open (index / data files locked): the app is running.
        public static bool InUse(string dir)
        {
            foreach (var rel in new[] { "index", "data_0", Path.Combine("Cache_Data", "index"), Path.Combine("Cache_Data", "data_0"), Path.Combine("js", "index"), Path.Combine("wasm", "index") })
            {
                string f = Path.Combine(dir, rel);
                try
                {
                    if (!File.Exists(f)) continue;
                    using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                }
                catch (IOException) { return true; }
                catch { }
            }
            return false;
        }

        // ---------------------------------------------------------------- other Chromium-based apps
        // Electron, CEF and WebView2 apps the catalog does not know: a folder with Local State + Preferences (Chromium's
        // user data signature) one or two levels below Roaming / Local, or its EBWebView / User Data subfolder.
        public static List<string> OtherRoots(CleanEnv env, List<string> known)
        {
            var res = new List<string>();
            foreach (var top in new[] { env.Roaming, env.Local })
            {
                if (string.IsNullOrEmpty(top)) continue;
                foreach (var d1 in SubDirs(top))
                {
                    string n1 = Path.GetFileName(d1);
                    if (n1.Equals("Packages", OIC) || n1.Equals("Temp", OIC) || n1.Equals("CrashDumps", OIC) || Deny.Contains(n1)) continue;
                    Consider(d1, res, known);
                    foreach (var d2 in SubDirs(d1)) if (!Deny.Contains(Path.GetFileName(d2))) Consider(d2, res, known);
                }
            }
            return res;
        }

        private static void Consider(string d, List<string> res, List<string> known)
        {
            foreach (var cand in new[] { d, Path.Combine(d, "EBWebView"), Path.Combine(d, "User Data") })
            {
                if (!LooksChromium(cand) || Covered(cand, known) || res.Any(x => string.Equals(x, cand, OIC))) continue;
                res.Add(cand);
            }
        }

        private static bool LooksChromium(string d)
        {
            try { return File.Exists(Path.Combine(d, "Local State")) && (File.Exists(Path.Combine(d, "Preferences")) || File.Exists(Path.Combine(d, "Default", "Preferences"))); }
            catch { return false; }
        }

        private static bool Under(string a, string b)
        {
            char sep = Path.DirectorySeparatorChar;
            return string.Equals(a, b, OIC) || a.StartsWith(b.TrimEnd(sep) + sep, OIC);
        }

        private static bool Covered(string d, List<string> known) { return known.Any(k => Under(d, k) || Under(k, d)); }

        // Folders the catalog and the browser row already handle.
        public static List<string> KnownRoots(CleanEnv env, List<AppDef> catalog)
        {
            var res = new List<string>();
            foreach (var a in catalog) foreach (var s in a.Specs) if (s.Kind == "cr") res.AddRange(Dirs(env, s.Path));
            foreach (var b in ChromiumBrowsers) res.AddRange(Dirs(env, b[2]));
            return res;
        }

        public static string NameOf(string root)
        {
            string n = Path.GetFileName(root);
            if (n.Equals("EBWebView", OIC) || n.Equals("User Data", OIC)) n = Path.GetFileName(Path.GetDirectoryName(root) ?? root);
            return n;
        }

        public static List<CleanPart> OtherParts(CleanEnv env, List<AppDef> catalog)
        {
            var res = new List<CleanPart>();
            foreach (var root in OtherRoots(env, KnownRoots(env, catalog)))
            {
                string name = NameOf(root);
                foreach (var p in ChromiumCaches(root)) AddPart(res, new CleanPart { Path = p, Owners = new string[0], App = name, Guard = true, Probe = true });
            }
            return res;
        }

        // ---------------------------------------------------------------- Spotify cache limit
        // storage.size (MB) in Spotify's prefs caps the streaming cache. T3: reported by the Spotify community, not documented
        // by Spotify. Written only while Spotify is closed (it rewrites prefs on exit); a smaller limit already set stays.
        public const int SpotifyCacheMb = 1024;

        public static void SpotifyLimit(CleanEnv env, Action<string> log)
        {
            var files = new List<string>();
            if (!string.IsNullOrEmpty(env.Roaming)) files.Add(Path.Combine(env.Roaming, "Spotify", "prefs"));
            foreach (var d in Dirs(env, @"{Local}\Packages\SpotifyAB.SpotifyMusic_*\LocalState\Spotify")) files.Add(Path.Combine(d, "prefs"));
            foreach (var f in files)
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    string result; int was;
                    if (!SetPref(File.ReadAllText(f), "storage.size", SpotifyCacheMb, out result, out was)) { log("[CL] Spotify: cache limit already " + was + " MB"); continue; }
                    File.WriteAllText(f, result, new UTF8Encoding(false));
                    log("[CL] Spotify: cache limit " + SpotifyCacheMb + " MB (storage.size in " + f + ")");
                }
                catch (Exception ex) { log("[CL] Spotify prefs: " + ex.Message); }
            }
        }

        // key=value line in a prefs text; false when a limit at or below the value is already there. Keeps the line ends.
        public static bool SetPref(string text, string key, int value, out string result, out int was)
        {
            was = 0; result = text ?? "";
            string nl = result.Contains("\r\n") ? "\r\n" : "\n";
            var lines = result.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            int idx = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
            if (idx >= 0)
            {
                int v;
                if (int.TryParse(lines[idx].Substring(key.Length + 1).Trim().Trim('"'), out v) && v > 0 && v <= value) { was = v; return false; }
                lines[idx] = key + "=" + value;
            }
            else lines.Add(key + "=" + value);
            result = string.Join(nl, lines) + nl;
            return true;
        }

        // ---------------------------------------------------------------- CS2 replays
        public const string Cs2Replays = @"{SteamLib}\steamapps\common\Counter-Strike Global Offensive\game\csgo\replays";
        public const int Cs2ReplayDays = 30;

        public static bool Cs2Installed(CleanEnv env) { return Dirs(env, @"{SteamLib}\steamapps\common\Counter-Strike Global Offensive\game\csgo").Count > 0; }

        public static List<CleanPart> Cs2Parts(CleanEnv env)
        {
            var res = new List<CleanPart>();
            foreach (var d in Dirs(env, Cs2Replays))
                AddPart(res, new CleanPart { Path = d, Pattern = "*.dem*", MinAgeDays = Cs2ReplayDays, TopOnly = true, Owners = new[] { "cs2" }, App = "Counter-Strike 2", Guard = true });
            return res;
        }

        // ---------------------------------------------------------------- driver installer leftovers
        public static List<CleanPart> DriverParts(CleanEnv env)
        {
            var res = new List<CleanPart>();
            foreach (var spec in new[] { @"{SysDrive}\NVIDIA", @"{SysDrive}\AMD", @"{ProgramData}\NVIDIA Corporation\Downloader", @"{ProgramData}\NVIDIA Corporation\NVIDIA App\UpdateFramework\ota-artifacts" })
                foreach (var d in Dirs(env, spec)) AddPart(res, new CleanPart { Path = d, Guard = true });
            return res;
        }
    }
}
