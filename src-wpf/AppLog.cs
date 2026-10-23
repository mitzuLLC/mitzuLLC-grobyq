using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace Grobyq
{
    public enum LogLevel { Normal, Good, Bad, Dim, Accent, Irq }

    public sealed class LogEntry
    {
        public string Time { get; set; }
        public string Tag { get; set; }
        public string Text { get; set; }
        public LogLevel Level { get; set; }
        public string Raw { get; set; }
    }

    // App-wide log sink. The engine's Apply/Revert/etc. return List<string>; the shell routes those here and the
    // Journal page binds to Entries. Thread-safe: Add marshals onto the UI thread (the re-apply worker is off-thread).
    public static class AppLog
    {
        public static readonly ObservableCollection<LogEntry> Entries = new ObservableCollection<LogEntry>();

        public static void Add(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (var l in lines) Add(l);
        }

        public static void Add(string line)
        {
            if (line == null) return;
            var e = Parse(line);
            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke((Action)(() => Push(e)));
            else
                Push(e);
        }

        private static void Push(LogEntry e)
        {
            Entries.Add(e);
            while (Entries.Count > 2000) Entries.RemoveAt(0);
        }

        public static void Clear()
        {
            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess()) app.Dispatcher.BeginInvoke((Action)Clear);
            else Entries.Clear();
        }

        private static LogEntry Parse(string line)
        {
            string tag = "", text = line;
            if (line.Length > 1 && line[0] == '[')
            {
                int end = line.IndexOf(']');
                if (end > 0) { tag = line.Substring(0, end + 1); text = line.Substring(end + 1).TrimStart(); }
            }
            return new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Tag = tag,
                Text = text,
                Raw = line,
                Level = LevelOf(tag, line)
            };
        }

        private static LogLevel LevelOf(string tag, string line)
        {
            if (line.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase) >= 0) return LogLevel.Bad;
            switch (tag)
            {
                case "[!!]": return LogLevel.Bad;
                case "[APPLIED]":
                case "[==]":
                case "[OK]": return LogLevel.Good;
                case "[IRQ]": return LogLevel.Irq;
                case "[TH]":
                case "[PR]":
                case "[>>]":
                case "[<<]": return LogLevel.Accent;
                case "[SKIP]":
                case "[i]":
                case "[IM]":
                case "[--]":
                case "[OFF]": return LogLevel.Dim;
                default: return LogLevel.Normal;
            }
        }
    }
}
