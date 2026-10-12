using System;
using Newtonsoft.Json;

namespace Thesis.Core
{
    // Reads and writes NamedValues as a JSON object, in the list's own order.
    public sealed class NamedValuesConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) { return objectType == typeof(NamedValues); }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var list = (NamedValues)value;
            if (list == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            for (int i = 0; i < list.Count; i++)
            {
                writer.WritePropertyName(list.NameAt(i));
                double v = list.ValueAt(i);
                // Always the deterministic text, whatever settings the caller uses.
                if (Json.IsFinite(v)) writer.WriteRawValue(Json.FormatNumber(v, Json.FloatDigits));
                else writer.WriteValue(Json.FormatNumber(v, Json.FloatDigits));
            }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType != JsonToken.StartObject) throw new JsonSerializationException("Expected an object of named numbers, found " + reader.TokenType + ".");

            var list = new NamedValues();
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName) throw new JsonSerializationException("Expected a name, found " + reader.TokenType + ".");
                string name = (string)reader.Value;
                if (!reader.Read()) throw new JsonSerializationException("The value of '" + name + "' is missing.");

                double number;
                if (reader.TokenType == JsonToken.Float || reader.TokenType == JsonToken.Integer)
                    number = Convert.ToDouble(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
                else if (reader.TokenType == JsonToken.String && (string)reader.Value == "NaN") number = double.NaN;
                else if (reader.TokenType == JsonToken.String && (string)reader.Value == "Infinity") number = double.PositiveInfinity;
                else if (reader.TokenType == JsonToken.String && (string)reader.Value == "-Infinity") number = double.NegativeInfinity;
                else throw new JsonSerializationException("The value of '" + name + "' is not a number.");

                list.Add(name, number);
            }
            return list;
        }
    }
}
