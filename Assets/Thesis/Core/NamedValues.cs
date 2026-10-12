using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Thesis.Core
{
    // A short list of named numbers that is written as a JSON object and keeps its
    // order: {"physical":812,"fire":240.5}. For values keyed by an enum whose
    // members may still change (damage types, movement classes, profile features):
    // the names travel with the numbers, so a reader needs no index table.
    //
    // Why not a Dictionary: its order when written is an accident of the
    // implementation, and telemetry lines must come out the same on every runtime.
    //
    // The numbers are written to 7 significant digits (Json.FloatDigits): they are
    // floats or small counts wherever this is used, and a float written with more
    // digits than it has shows its rounding (57.8 would read 57.7999992371).
    [JsonConverter(typeof(NamedValuesConverter))]
    public sealed class NamedValues
    {
        private readonly List<string> names = new List<string>();
        private readonly List<double> values = new List<double>();

        public int Count => names.Count;

        public string NameAt(int index) { return names[index]; }

        public double ValueAt(int index) { return values[index]; }

        // Adds a pair at the end. A name can be used once.
        public NamedValues Add(string name, double value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (names.Contains(name)) throw new ArgumentException("'" + name + "' is already in the list.", nameof(name));
            names.Add(name);
            values.Add(value);
            return this;
        }

        public bool Has(string name) { return names.IndexOf(name) >= 0; }

        // The value under this name; throws when there is none.
        public double this[string name]
        {
            get
            {
                int i = names.IndexOf(name);
                if (i < 0) throw new KeyNotFoundException("No value named '" + name + "'.");
                return values[i];
            }
        }

        public double Sum()
        {
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum;
        }
    }
}
