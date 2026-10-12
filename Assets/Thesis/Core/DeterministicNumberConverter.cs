using System;
using Newtonsoft.Json;

namespace Thesis.Core
{
    // Writes every float and double through Json.FormatFloat / FormatDouble, so the
    // text of a number does not depend on the runtime that wrote it (see
    // Json.SerializeLine). NaN and the infinities are written as strings, since
    // JSON has no number for them: a vetoed strategy's score is "-Infinity".
    //
    // Reading takes anything the writer can have produced, and ordinary JSON
    // numbers in any other notation as well.
    public sealed class DeterministicNumberConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(float) || objectType == typeof(double)
                || objectType == typeof(float?) || objectType == typeof(double?);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            bool single = value is float;
            double number = single ? (float)value : (double)value;
            string text = single ? Json.FormatFloat((float)value) : Json.FormatDouble(number);

            if (Json.IsFinite(number)) writer.WriteRawValue(text);
            else writer.WriteValue(text);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            Type target = Nullable.GetUnderlyingType(objectType) ?? objectType;
            bool nullable = target != objectType;

            double number;
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    if (nullable) return null;
                    throw new JsonSerializationException("Cannot read null into a " + objectType.Name + ".");
                case JsonToken.Float:
                case JsonToken.Integer:
                    number = Convert.ToDouble(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case JsonToken.String:
                    switch ((string)reader.Value)
                    {
                        case "NaN": number = double.NaN; break;
                        case "Infinity": number = double.PositiveInfinity; break;
                        case "-Infinity": number = double.NegativeInfinity; break;
                        default: throw new JsonSerializationException("'" + reader.Value + "' is not a number.");
                    }
                    break;
                default:
                    throw new JsonSerializationException("Expected a number, found " + reader.TokenType + ".");
            }

            if (target == typeof(float)) return (float)number;
            return number;
        }
    }
}
