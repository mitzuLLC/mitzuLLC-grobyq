using System;
using System.Collections.Generic;

namespace Grobyq
{
    // 4.0.3: one-line captions for threads grobyq knows, shown under a rule's pattern on Threads > Thread rules. Keyed by
    // the pattern without wildcards, lower case ("*MainThrd*" -> "mainthrd", "steamclient*.dll" -> "steamclient.dll").
    // English source text (L.N marks it for the language files); L.T translates when shown. Unknown pattern = no caption.
    // A caption says what the thread is, not what priority it should get: that is the rule's job and the A/B's.
    internal static class ThreadNotes
    {
        private static readonly Dictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---- Source 2 (CS2, Dota 2): thread descriptions ----
            { "mainthrd", L.N("Game main thread: simulation and frame preparation - every frame starts here.") },
            { "d3d11renderthread", L.N("Render thread: hands the prepared frame to the D3D11 driver.") },
            { "globpool", L.N("Job pool (GlobPool/0..N): the main thread splits each frame's work over these workers and waits for all of them.") },
            // ---- Source 2: modules ----
            { "embree3.dll", L.N("Intel Embree ray tracing, used by the game's sound (Steam Audio).") },
            { "phonon.dll", L.N("Steam Audio: spatial sound (direction, occlusion, reflections).") },
            { "video64.dll", L.N("Video playback: menu backgrounds and clips.") },
            { "steamclient64.dll", L.N("Steam client inside the game: Steam API calls (friends, achievements, overlay link).") },
            { "steamclient.dll", L.N("Steam client inside the game: Steam API calls (friends, achievements, overlay link).") },
            { "steamnetworkingsockets.dll", L.N("Game networking (Steam Networking Sockets): lowering it can delay network packets.") },
            { "gameoverlayrenderer64.dll", L.N("Steam overlay inside the game.") },
            { "engine2.dll", L.N("Source 2 engine core.") },
            { "client.dll", L.N("Game client code (Source 2).") },
            { "rendersystemdx11.dll", L.N("Source 2 D3D11 render system.") },
            { "inputsystem.dll", L.N("Source 2 input system (keyboard, mouse, controllers).") },
            { "soundsystem.dll", L.N("Source 2 sound system (mixing and output).") },
            { "networksystem.dll", L.N("Source 2 network system.") },
            // ---- graphics driver inside the game ----
            { "nvwgf2umx.dll", L.N("NVIDIA D3D11 driver (user mode): the driver's own worker threads.") },
            // ---- Unreal Engine (VALORANT, Fortnite): thread descriptions ----
            { "gamethread", L.N("Unreal Engine game thread: game logic, the start of every frame.") },
            { "renderthread", L.N("Render thread: prepares draw commands for the GPU.") },
            { "rhithread", L.N("Unreal Engine RHI thread: submits commands to the graphics API.") },
            { "backgroundthreadpool", L.N("Unreal Engine background task pool (streaming, async loading).") },
            { "background worker", L.N("Unreal Engine background workers (low-priority tasks).") },
            { "ftelemetryworker", L.N("Telemetry worker: collects and sends statistics.") },
            { "telemetrysched", L.N("Telemetry scheduler.") },
            { "logworker", L.N("Log writer.") },
            { "screensaverinhibitor", L.N("Keeps the screen saver off while the game runs.") },
            // ---- Windows ----
            { "ntdll.dll", L.N("Windows thread pool workers (they start in ntdll).") },
            { "combase.dll", L.N("COM worker threads.") },
            { "mswsock.dll", L.N("Winsock network worker.") },
        };

        public static string Key(string pattern)
        {
            return (pattern ?? "").Replace("*", "").Replace("?", "").Trim().ToLowerInvariant();
        }

        // The caption for a rule pattern in the current language, or "" when grobyq does not know the thread.
        public static string For(string pattern)
        {
            string en;
            return Notes.TryGetValue(Key(pattern), out en) ? L.T(en) : "";
        }

        public static int Count { get { return Notes.Count; } }
    }
}
