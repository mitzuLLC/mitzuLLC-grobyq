using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Grobyq
{
    // Interface languages. The English text in the code is the key (L.T("Apply")); each language is an
    // embedded JSON file lang.<code>.json that maps English -> translation. A missing entry falls back to English.
    // Logs stay in English on purpose: they are A/B evidence and must read the same on every PC.
    public static class L
    {
        public sealed class Language
        {
            public string Code, Native, English;
            public string[] Fonts;          // first installed family wins
            public bool Cjk;                // CJK scripts: no Segoe UI Semibold, bold of the CJK UI font instead
            public string Chip;             // shorter label for the language chip (null = Native)
        }

        // Three shipping languages: English (base), Russian, Ukrainian.
        public static readonly Language[] All =
        {
            new Language { Code = "en", Native = "English", English = "English", Fonts = new[] { "Segoe UI", "Tahoma" } },
            new Language { Code = "ru", Native = "Русский", English = "Russian", Fonts = new[] { "Segoe UI", "Tahoma" } },
            new Language { Code = "uk", Native = "Українська", English = "Ukrainian", Fonts = new[] { "Segoe UI", "Tahoma" } },
        };

        public static Language Cur = All[0];
        private static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _family;

        public static string Code { get { return Cur.Code; } }

        // Translate a literal English UI string. Keep the argument a plain literal: tools/extract_strings.py collects them.
        public static string T(string en)
        {
            if (en == null) return "";
            string s;
            return _map.TryGetValue(en, out s) && !string.IsNullOrEmpty(s) ? s : en;
        }

        // Marks an English literal for extraction without translating it yet (tables built once, translated
        // when shown: the language can change while the app runs). Display with L.T(value).
        public static string N(string en) { return en; }

        // Translate a format string, then fill it. The translation keeps the {0} {1} placeholders.
        public static string F(string en, params object[] args)
        {
            string fmt = T(en);
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, en, args); }
        }

        public static Language Find(string code)
        {
            foreach (var l in All) if (string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        // First run: the Windows display language if grobyq has it, else English.
        public static string Detect()
        {
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (Find(two) != null) return two;
                if (two == "be" || two == "kk") return "ru";
            }
            catch { }
            return "en";
        }

        public static void Load(string code)
        {
            var lang = Find(code) ?? All[0];
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (lang.Code != "en")
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang." + lang.Code + ".json"))
                        if (s != null)
                            using (var r = new StreamReader(s, Encoding.UTF8))
                            {
                                var d = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
                                if (d != null) foreach (var kv in d) if (kv.Value is string) map[kv.Key] = (string)kv.Value;
                            }
                }
                catch { }
            }
            Cur = lang; _map = map; _family = null;
        }

        // Share of UI strings this language covers (Settings shows it).
        public static int Coverage { get { return _map.Count; } }

        // The preferred UI font family name for the current language. WPF resolves it by name and falls back
        // automatically if it is not installed, so no System.Drawing font probing is needed here.
        public static string Family
        {
            get
            {
                if (_family != null) return _family;
                foreach (var f in Cur.Fonts) { if (!string.IsNullOrEmpty(f)) { _family = f; return f; } }
                _family = "Segoe UI";
                return _family;
            }
        }
    }
}
