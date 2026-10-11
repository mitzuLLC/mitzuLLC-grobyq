using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Grobyq
{
    // net48's System.Web.Script.Serialization.JavaScriptSerializer is gone on net8. This is a drop-in replacement
    // backed by System.Text.Json (part of the net8 BCL — no NuGet). It reproduces the exact weak-typed shape the
    // engine relies on, so Store/Licensing/Devices parse presets & licenses identically:
    //   JSON object -> Dictionary<string, object>
    //   JSON array  -> object[]
    //   string/bool/null -> as-is
    //   number -> int if it fits, else long, else double   (matches JavaScriptSerializer's Int32/Int64 boxing)
    public sealed class JavaScriptSerializer
    {
        public int MaxJsonLength = 2 * 1024 * 1024;   // honored as an input-length guard, like the original

        private static readonly JsonSerializerOptions SerOpts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // don't over-escape '+', '<', non-ASCII (match JS serializer)
        };

        public object DeserializeObject(string input)
        {
            if (input == null) return null;
            if (input.Length > MaxJsonLength) throw new ArgumentException("maximum JSON length exceeded");
            using (var doc = JsonDocument.Parse(input, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                return Convert(doc.RootElement);
        }

        public string Serialize(object obj)
        {
            if (obj == null) return "null";
            return JsonSerializer.Serialize(obj, obj.GetType(), SerOpts);   // runtime type, else object serializes as {}
        }

        private static object Convert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Convert(p.Value);
                    return d;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in e.EnumerateArray()) list.Add(Convert(item));
                    return list.ToArray();                      // object[], as JavaScriptSerializer returns
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.Number:
                    if (e.TryGetInt32(out int i)) return i;
                    if (e.TryGetInt64(out long l)) return l;
                    return e.GetDouble();
                default:
                    return null;
            }
        }
    }
}
