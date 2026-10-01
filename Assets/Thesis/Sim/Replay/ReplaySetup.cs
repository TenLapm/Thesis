using System;
using System.Reflection;
using Thesis.Core;

namespace Thesis.Sim
{
    // One fingerprint of everything a run is built from besides the seed and the
    // input: the config, the map and the shape library. A replay stores it, and
    // loading a replay recomputes it from the values that came back out of the
    // JSON. If the two differ, a number did not survive the round trip (or the file
    // was edited), and the replay is refused with that message instead of failing
    // later as a mysterious divergence in wave 1.
    public static class ReplaySetup
    {
        public static ulong Hash(SimConfig config, MapData map, ShapeDef[] shapes)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            var h = new Fnv1a64();
            AddConfig(ref h, config);
            AddMap(ref h, map);
            AddShapes(ref h, shapes);
            return h.Value;
        }

        // By reflection, in name order, so a tunable added to SimConfig later is
        // covered without anyone remembering to list it here. This runs once per
        // run, never per tick. Floats go in by bit pattern.
        private static void AddConfig(ref Fnv1a64 h, SimConfig config)
        {
            FieldInfo[] fields = typeof(SimConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            h.Add(fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                h.Add(fields[i].Name);
                object value = fields[i].GetValue(config);
                if (value is float) h.Add((float)value);
                else if (value is int) h.Add((int)value);
                else if (value is bool) h.Add((bool)value);
                else throw new InvalidOperationException("[Replay] SimConfig." + fields[i].Name + " has type " + fields[i].FieldType.Name
                                                         + ", which ReplaySetup cannot hash. Add a case for it.");
            }
        }

        private static void AddMap(ref Fnv1a64 h, MapData map)
        {
            h.Add(map.Name);
            h.Add(map.Width);
            h.Add(map.Height);
            h.Add(map.WorldSizeX);
            h.Add(map.WorldSizeY);
            h.Add(map.NodeRadius);
            h.Add(map.OriginX);
            h.Add(map.OriginZ);

            h.Add(map.Rows.Length);
            for (int i = 0; i < map.Rows.Length; i++) h.Add(map.Rows[i]);

            h.Add(map.Core.X);
            h.Add(map.Core.Y);
            AddPoint(ref h, map.CoreWorld);

            h.Add(map.Spawns.Length);
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                h.Add(map.Spawns[i].X);
                h.Add(map.Spawns[i].Y);
                AddPoint(ref h, map.SpawnWorlds[i]);
            }
        }

        private static void AddPoint(ref Fnv1a64 h, WorldPoint p)
        {
            h.Add(p.X);
            h.Add(p.Y);
            h.Add(p.Z);
        }

        // Order matters and is hashed as such: the bag shuffles the library in the
        // order given, so the same shapes in a different order are a different game.
        private static void AddShapes(ref Fnv1a64 h, ShapeDef[] shapes)
        {
            h.Add(shapes.Length);
            for (int i = 0; i < shapes.Length; i++)
            {
                ShapeDef s = shapes[i];
                h.Add(s.Name);
                h.Add(s.BuildCost);
                h.Add(s.DigCost);
                h.Add(s.WallHealth);
                h.Add(s.LocalTiles.Length);
                for (int t = 0; t < s.LocalTiles.Length; t++)
                {
                    h.Add(s.LocalTiles[t].X);
                    h.Add(s.LocalTiles[t].Y);
                }
            }
        }
    }
}
