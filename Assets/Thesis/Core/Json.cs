using System.Globalization;
using Newtonsoft.Json;

namespace Thesis.Core
{
    // The one place JSON settings are defined for telemetry, replays, maps and
    // estimator snapshots. Never JsonUtility in Thesis.* (it is UnityEngine-only and
    // cannot handle nesting or nulls the way the telemetry schema needs).
    public static class Json
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.None,
            // Telemetry records vetoed strategies with score -Infinity; emit it as a
            // string rather than failing or writing invalid JSON.
            FloatFormatHandling = FloatFormatHandling.String,
            FloatParseHandling = FloatParseHandling.Double,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            // Replays and snapshots must never pull in a type named by the file.
            TypeNameHandling = TypeNameHandling.None,
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        // For committed files a human will diff or read while debugging (maps,
        // result summaries). Telemetry stays single-line JSONL.
        public static string SerializeIndented(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented, Settings);
        }
    }
}
