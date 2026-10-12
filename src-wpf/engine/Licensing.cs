using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Grobyq
{
    public sealed class LicResult
    {
        public bool Ok;
        public string Message = "";
        public DateTime? Expires;
        public DateTime? Purchase;
        public string Note = "";
        public string Source = "";     // "grobyq list" / "offline token"
        public bool Net;               // failure was "could not reach", not a real deny
        public bool AnyOs;             // signed flag: this license also runs outside AerialOS (developer / testers)
    }

    // Single-source licensing: grobyq checks ONLY its own signed list on GitHub,
    // published by grobyq Admin. No third-party server, no shared key.
    //   1) the signed list  (entry = hwid + payload + P-256 signature of the payload)
    //   2) a 48 h signed offline cache of the last good entry - used ONLY when the list
    //      cannot be reached; a reachable list that says no wins immediately (revoke works)
    // HWID formula unchanged from v2, so already-issued HWIDs stay valid.
    public static class Licensing
    {
        // From grobyq.settings.txt (LICENSE_LIST_URL) or the compiled default.
        public const string ListUrlDefault = "https://raw.githubusercontent.com/mitzuLLC/grobpc-licenses/main/licenses.json";
        public const string GrobPubKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEy4Wjp+uo5uW/XOmusuOrpipARDCDqedA12gtdrh+LfqEiCrXfkBOdXlff/LS3IEDs+YobHK2qpDptfUkRGbfcg==";

        public const int OfflineHours = 48;
        private const string UA = "grobyq (mitzu LLC)";

        public static string Hwid() { return GrobLic.Hwid(); }

        private static LicResult _last;
        public static LicResult LastKnown() { return _last; }

        private static string CachePath
        {
            get
            {
                string b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(b, "grobyq", "license.cache");
            }
        }

        public static string ListUrl
        {
            get { return string.IsNullOrEmpty(_listOverride) ? ListUrlDefault : _listOverride; }
        }
        private static string _listOverride;

        // A settings file still carrying the template placeholder must not beat the compiled URL.
        public static void SetListUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (url.IndexOf("CHANGE-ME", StringComparison.OrdinalIgnoreCase) >= 0 || url.StartsWith("@@")) return;
            _listOverride = url;
        }

        // ---------------- public entry ----------------
        public static LicResult Validate()
        {
            var r = ValidateInner();
            _last = r;
            return r;
        }

        private static LicResult ValidateInner()
        {
            string hwid = Hwid();

            try
            {
                var r = FromList(hwid, ListUrl);
                if (r.Ok) { SaveCache(hwid, r); return r; }
                if (!r.Net)
                {
                    // The list answered and said no (revoked, expired, not activated):
                    // that beats an old offline token. The cache only covers "could not reach".
                    DropCache();
                    return r;
                }
            }
            catch { }

            var cached = ReadCache(hwid);
            if (cached != null)
            {
                if (!cached.Ok) cached.Net = true;
                return cached;
            }

            return new LicResult { Ok = false, Net = true,
                Message = L.N("Cannot reach the license list and there is no valid offline token.") };
        }

        // ---------------- grobyq GitHub list ----------------
        private static LicResult FromList(string hwid, string url)
        {
            if (string.IsNullOrEmpty(url) || url.IndexOf("CHANGE-ME", StringComparison.OrdinalIgnoreCase) >= 0 || url.StartsWith("@@"))
                return new LicResult { Ok = false, Net = true, Message = L.N("License list URL is not configured.") };

            string body = FetchList(url);   // throws on network failure -> caught by Validate

            var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            var map = ser.DeserializeObject(body) as Dictionary<string, object>;
            if (map == null || !map.ContainsKey("licenses")) return new LicResult { Ok = false, Message = L.N("License list is malformed.") };
            var arr = map["licenses"] as object[];
            if (arr == null) return new LicResult { Ok = false, Message = L.N("License list is malformed.") };

            foreach (object it in arr)
            {
                var e = it as Dictionary<string, object>;
                if (e == null) continue;
                if (!string.Equals(Str(e, "hwid"), hwid, StringComparison.Ordinal)) continue;
                string payload = Str(e, "payload"), token = Str(e, "token"), note = Str(e, "note");
                if (payload.Length == 0 || token.Length == 0) return new LicResult { Ok = false, Message = L.N("License entry is incomplete.") };
                if (!GrobLic.Verify(payload, token, GrobPubKey)) return new LicResult { Ok = false, Message = L.N("License signature mismatch.") };
                _lastPayload = payload; _lastToken = token;
                return Judge(payload, hwid, note, false);
            }
            return new LicResult { Ok = false, Source = "grobyq list",
                Message = L.N("This PC is not in the license list yet. Send your HWID to the developer; after activation press Retry.") };
        }

        // ---------------- verdict ----------------
        private static LicResult Judge(string payloadJson, string hwid, string note, bool offline)
        {
            string src = offline ? "offline token" : "grobyq list";
            var p = new JavaScriptSerializer().DeserializeObject(payloadJson) as Dictionary<string, object>;
            if (p == null) return new LicResult { Ok = false, Message = L.N("License payload malformed."), Source = src };
            string phwid = Str(p, "hwid");
            DateTime? exp = Date(Str(p, "expires")), pur = Date(Str(p, "purchase"));
            if (!string.Equals(phwid, hwid, StringComparison.Ordinal))
                return new LicResult { Ok = false, Message = L.N("This license belongs to another PC (HWID mismatch)."), Expires = exp, Purchase = pur, Note = note, Source = src };
            if (exp.HasValue && exp.Value < DateTime.UtcNow)
                return new LicResult { Ok = false, Message = L.N("License expired."), Expires = exp, Purchase = pur, Note = note, Source = src };
            object any; bool anyOs = p.TryGetValue("anyos", out any) && any is bool && (bool)any;
            return new LicResult { Ok = true, Message = offline ? L.N("Licensed (offline).") : L.N("Licensed."), Expires = exp, Purchase = pur, Note = note, Source = src, AnyOs = anyOs };
        }

        // ---------------- signed cache ----------------
        // Stores the exact signed payload/token; on read the signature is verified again,
        // so editing the cache file cannot extend or forge a license.
        private static void SaveCache(string hwid, LicResult r)
        {
            try
            {
                string path = CachePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var o = new Dictionary<string, object>
                {
                    { "hwid", hwid }, { "src", "grob" },
                    { "payload", _lastPayload }, { "token", _lastToken },
                    { "note", r.Note }, { "saved", DateTime.UtcNow.Ticks }
                };
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void DropCache() { try { if (File.Exists(CachePath)) File.Delete(CachePath); } catch { } }

        private static LicResult ReadCache(string hwid)
        {
            try
            {
                string path = CachePath;
                if (!File.Exists(path)) return null;
                var c = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (c == null || !string.Equals(Str(c, "hwid"), hwid, StringComparison.Ordinal)) return null;
                if (Str(c, "src") != "grob") return null;   // only our own key is trusted
                string payload = Str(c, "payload"), token = Str(c, "token");
                if (!GrobLic.Verify(payload, token, GrobPubKey)) return null;
                long ticks = Long(c, "saved");
                double hours = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalHours;
                if (hours > OfflineHours || hours < -0.25)
                    return new LicResult { Ok = false, Message = L.N("Offline token expired. Connect to the internet and press Retry.") };
                return Judge(payload, hwid, Str(c, "note"), true);
            }
            catch { return null; }
        }

        [ThreadStatic] private static string _lastPayload;
        [ThreadStatic] private static string _lastToken;

        // ---------------- fetching the list ----------------
        // raw.githubusercontent.com is a CDN that keeps a copy for up to 5 minutes and ignores
        // query strings, so right after an activation it can still serve the old list.
        // The GitHub contents API returns the current file (fresh within ~1 minute), so it is
        // asked first; the raw URL is the fallback (API unreachable or its 60/h per-IP limit hit).
        private static string FetchList(string rawUrl)
        {
            string api = ApiUrlFor(rawUrl);
            if (api != null)
            {
                try
                {
                    string b = HttpGet(api, "application/vnd.github.raw+json", true);
                    if (b.IndexOf("\"licenses\"", StringComparison.Ordinal) >= 0) return b;
                }
                catch { }
            }
            return HttpGet(rawUrl, "application/json, text/plain", false);
        }

        // https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}
        //   -> https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}
        internal static string ApiUrlFor(string rawUrl)
        {
            const string pre = "https://raw.githubusercontent.com/";
            if (string.IsNullOrEmpty(rawUrl) || !rawUrl.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = rawUrl.Substring(pre.Length);
            int q = rest.IndexOf('?'); if (q >= 0) rest = rest.Substring(0, q);
            string[] parts = rest.Split('/');
            if (parts.Length < 4) return null;
            string path = string.Join("/", parts, 3, parts.Length - 3);
            return "https://api.github.com/repos/" + parts[0] + "/" + parts[1] + "/contents/" + path + "?ref=" + Uri.EscapeDataString(parts[2]);
        }

        // ---------------- http ----------------
        // One HttpClient for the process (system proxy, TLS 1.2/1.3 negotiated by the OS), 10 s per request; the calls run on
        // worker threads only (gate, Home > Recheck), so the synchronous Send never blocks the UI.
        private static readonly HttpClient _http = MakeClient();

        private static HttpClient MakeClient()
        {
            var h = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UA);
            return c;
        }

        private static string HttpGet(string url, string accept, bool githubApi)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("Accept", accept);
                if (githubApi) req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                using (var resp = _http.Send(req, HttpCompletionOption.ResponseContentRead))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) throw new Exception("HTTP " + code);
                    using (var st = resp.Content.ReadAsStream())
                    using (var sr = new StreamReader(st, Encoding.UTF8))
                        return sr.ReadToEnd();
                }
            }
        }

        // "   Purchased: 2026-01-02   Expires: 2027-01-02   (grobyq list)" - the license window and Home show it.
        public static string Dates(LicResult r)
        {
            if (r == null) return "";
            string s = "";
            if (r.Purchase.HasValue) s += "   " + L.T("Purchased:") + " " + r.Purchase.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (r.Expires.HasValue) s += "   " + L.T("Expires:") + " " + r.Expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(r.Source)) s += "   (" + r.Source + ")";
            return s;
        }

        // ---------------- small helpers ----------------
        private static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        private static long Long(Dictionary<string, object> d, string k) { object v; if (d.TryGetValue(k, out v) && v != null) { try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { } } return 0; }
        private static DateTime? Date(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return null;
        }
    }
}
