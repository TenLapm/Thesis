using System;

namespace Thesis.Director
{
    // The features of one board, in a fixed order, with their data flags (ARCHITECTURE.md
    // §5.3). The schema version goes into telemetry once the profile does (WP10), so a
    // change of order, name or formula bumps it.
    public sealed class BuildProfile
    {
        // 1: WP8a, maze_length, tower_concentration, damage_type_mix (2026-11-10).
        public const int SchemaVersion = 1;

        public readonly int Schema;
        public readonly string[] Names;
        public readonly float[] Values;
        public readonly bool[] HasData;

        public BuildProfile(int schema, string[] names, float[] values, bool[] hasData)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (hasData == null) throw new ArgumentNullException(nameof(hasData));
            if (values.Length != names.Length || hasData.Length != names.Length)
                throw new ArgumentException("names, values and flags must have the same length");
            Schema = schema;
            Names = names;
            Values = values;
            HasData = hasData;
        }

        public float this[string name] => Values[IndexOf(name)];

        public bool Has(string name) { return HasData[IndexOf(name)]; }

        public int IndexOf(string name)
        {
            for (int i = 0; i < Names.Length; i++) if (Names[i] == name) return i;
            throw new ArgumentException("the profile has no feature '" + name + "'", nameof(name));
        }
    }
}
