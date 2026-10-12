using System;
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

        // The same, plus numbers written by FormatFloat / FormatDouble below. This is
        // what telemetry lines use: their text must be the same on every runtime.
        public static readonly JsonSerializerSettings LineSettings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.None,
            FloatFormatHandling = FloatFormatHandling.String,
            FloatParseHandling = FloatParseHandling.Double,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
            Converters = { new DeterministicNumberConverter() },
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

        // One line of JSON whose bytes are the same whichever runtime wrote it. The
        // library's own number text is not: it prints the float 0.20000458 as
        // "0.20000458" under .NET and as "0.200004578" under Unity's Mono (both read
        // back to the same bits, so replays do not care, but a telemetry file that
        // is compared byte for byte does).
        public static string SerializeLine(object value)
        {
            return JsonConvert.SerializeObject(value, LineSettings);
        }

        public static T DeserializeLine<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, LineSettings);
        }

        // ---- number text that is the same everywhere ----
        //
        // Plain decimals, no exponent, trailing zeros dropped: 57.8, 0.27, 812, -1.125.
        // A float is written to 7 significant digits and a double to 12, which is as
        // much as either is good for after arithmetic; the float 57.8 (really
        // 57.79999924) therefore reads "57.8", not "57.799999". Below 1 the digits
        // are counted from the decimal point (0.0001234567 reads "0.0001235"), and
        // the whole part of a large number is never cut short (12345678 stays so).
        //
        // The text is for reading and for comparing files. It is NOT the exact bits:
        // a float read back from it can differ in its last place. Anything that has
        // to be bit-exact is in the replay, which telemetry can be made from again.
        //
        // How it is the same everywhere: one multiplication by an exact power of ten,
        // a floor, and a comparison with a half, all on doubles, and then integers.
        // No library routine formats a floating-point number.
        public const int FloatDigits = 7;
        public const int DoubleDigits = 12;

        private static readonly double[] PowersOfTen =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12,
        };

        public static string FormatFloat(float value) { return FormatNumber(value, FloatDigits); }

        public static string FormatDouble(double value) { return FormatNumber(value, DoubleDigits); }

        // NaN and the infinities have no JSON number; callers write these as strings.
        public static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        public static string FormatNumber(double value, int significantDigits)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";
            if (significantDigits < 1 || significantDigits >= PowersOfTen.Length)
                throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Must be 1 to " + (PowersOfTen.Length - 1) + ".");

            bool negative = value < 0.0;
            double magnitude = negative ? -value : value;
            if (magnitude >= 9.0e18)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Too large to write as a plain decimal.");

            // Digits before the point decide how many are left for after it.
            long whole = (long)Math.Floor(magnitude);
            int wholeDigits = 0;
            for (long w = whole; w > 0; w /= 10) wholeDigits++;
            int decimals = significantDigits - wholeDigits;
            if (decimals < 0) decimals = 0;

            double scaled = magnitude * PowersOfTen[decimals];
            long units = (long)Math.Floor(scaled);
            if (scaled - units >= 0.5) units++; // round half up; the sign is handled apart

            if (units == 0) return "0"; // also "-0" and anything that rounds to nothing

            long unitsPerOne = (long)PowersOfTen[decimals];
            long wholePart = units / unitsPerOne;
            long fraction = units % unitsPerOne;

            string text = wholePart.ToString(CultureInfo.InvariantCulture);
            if (fraction != 0)
            {
                string digits = fraction.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0').TrimEnd('0');
                text = text + "." + digits;
            }
            return negative ? "-" + text : text;
        }
    }
}
