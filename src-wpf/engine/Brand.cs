using System;
using System.Reflection;

namespace Grobyq
{
    // Brand IDENTITY (names, links, well-known strings). Single source of truth, shared by engine and UI.
    // The logo/image/tray helpers are a UI concern and live in the UI layer (not here), so the engine stays
    // free of System.Drawing / WPF.
    public static class Brand
    {
        public const string Name = "grobyq";
        public const string Tagline = "Core-aware latency tuning";   // shown through L.T()
        public const string Company = "mitzu LLC";
        public const string Credit = "by mitzu LLC  ·  grobPC";
        public const string XUrl = "https://x.com/grobPC";
        public const string SiteUrl = "https://grobpc.com";
        public const string TelegramUrl = "https://t.me/grobPC";
        public const string Mutex = "Global\\grobyq-mitzu";
        public const string TaskName = "grobyq-mitzu";
        public const string OldTaskName = "grobPC-mitzu";
        public const string SettingsFile = "grobyq.settings.txt";
        public const string OldSettingsFile = "grobPC.settings.txt";

        // Distribution: grobyq runs only on AerialOS unless the signed license carries "anyos" (see AerialGate.cs).
        public const bool AerialOnly = true;
        public const string AerialUrl = "https://aerialos.vercel.app";
        public const string AerialDiscord = "https://dsc.gg/aerialos";

        public static string Version
        {
            get { try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } catch { return "?"; } }
        }
    }
}
