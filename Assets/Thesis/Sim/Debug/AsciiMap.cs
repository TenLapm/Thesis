using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thesis.Core;

namespace Thesis.Sim
{
    public enum AsciiLayer
    {
        Terrain,   // static blockers, walls, spawns, core
        Route,     // Terrain plus '*' on the route from the first spawn to the core
        Occupancy, // AsciiState only: where this wave's agents spent their time, 1-9
        Cost,      // AsciiState only: flow-field cost to the core, 0-9
    }

    public sealed class AsciiFixture
    {
        public MapData Map;
        public SimGrid Grid;
    }

    // Text maps, used both as test fixtures and as the debug render (CLI `ascii`).
    //
    // Orientation (same as MapData.Rows): the FIRST line is the TOP row, y = Height-1;
    // the last line is y = 0. x grows left to right. Spaces and tabs are ignored, so
    // fixtures may be written "S . . #" for readability.
    //
    //   .  open ground            X  static blocker (unwalkable)
    //   #  wall (cost 15, 6 s)    H  heavy wall (cost 200, 99999 s: the benchmark walls)
    //   S  spawn (open ground)    C  core / goal (open ground), exactly one
    //   *  route (render only; parsed as open ground)
    public static class AsciiMap
    {
        // BlockShape defaults: digCost 15, wallHealth 6.
        public const int WallCost = 15;
        public const float WallHealth = 6f;

        // ScenarioBenchmark's static walls.
        public const int HeavyWallCost = 200;
        public const float HeavyWallHealth = 99999f;

        public static AsciiFixture Parse(string text, string name = "ascii", float nodeRadius = 1f)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var lines = new List<string>();
            foreach (string raw in text.Split('\n'))
            {
                var sb = new StringBuilder(raw.Length);
                foreach (char c in raw)
                {
                    if (c != ' ' && c != '\t' && c != '\r') sb.Append(c);
                }
                if (sb.Length > 0) lines.Add(sb.ToString());
            }

            if (lines.Count == 0) throw new InvalidDataException("ASCII map '" + name + "' is empty.");

            int width = lines[0].Length;
            int height = lines.Count;
            for (int r = 0; r < height; r++)
            {
                if (lines[r].Length != width)
                    throw new InvalidDataException("ASCII map '" + name + "': line " + r + " has " + lines[r].Length + " tiles, expected " + width + ".");
            }

            var rows = new string[height];
            var walls = new List<KeyValuePair<TileCoord, char>>();
            var spawns = new List<TileCoord>();
            TileCoord? core = null;

            for (int r = 0; r < height; r++)
            {
                int y = height - 1 - r;
                var row = new StringBuilder(width);
                for (int x = 0; x < width; x++)
                {
                    char c = lines[r][x];
                    switch (c)
                    {
                        case '.':
                        case '*':
                            row.Append('.');
                            break;
                        case 'X':
                            row.Append('X');
                            break;
                        case '#':
                        case 'H':
                            row.Append('.');
                            walls.Add(new KeyValuePair<TileCoord, char>(new TileCoord(x, y), c));
                            break;
                        case 'S':
                            row.Append('.');
                            spawns.Add(new TileCoord(x, y));
                            break;
                        case 'C':
                            if (core.HasValue)
                                throw new InvalidDataException("ASCII map '" + name + "': more than one 'C' (at " + core.Value + " and (" + x + "," + y + ")).");
                            row.Append('.');
                            core = new TileCoord(x, y);
                            break;
                        default:
                            throw new InvalidDataException("ASCII map '" + name + "': unknown tile '" + c + "' at (" + x + "," + y + ").");
                    }
                }
                rows[r] = row.ToString();
            }

            if (!core.HasValue) throw new InvalidDataException("ASCII map '" + name + "' has no core 'C'.");

            var map = new MapData
            {
                Name = name,
                Width = width,
                Height = height,
                NodeRadius = nodeRadius,
                WorldSizeX = (float)((float)(width * nodeRadius) * 2f), // explicit casts: ARCHITECTURE.md §9 rule 3
                WorldSizeY = (float)((float)(height * nodeRadius) * 2f),
                OriginX = 0f,
                OriginZ = 0f,
                Rows = rows,
                Core = core.Value,
                Spawns = spawns.ToArray(),
            };

            var grid = new SimGrid(map);
            map.CoreWorld = ToWorld(grid.Get(map.Core));
            map.SpawnWorlds = new WorldPoint[map.Spawns.Length];
            for (int i = 0; i < map.Spawns.Length; i++) map.SpawnWorlds[i] = ToWorld(grid.Get(map.Spawns[i]));
            map.Validate();

            foreach (var wall in walls)
            {
                SimNode node = grid.Get(wall.Key);
                if (wall.Value == 'H') grid.SetWall(node, HeavyWallCost, HeavyWallHealth);
                else grid.SetWall(node, WallCost, WallHealth);
            }

            return new AsciiFixture { Map = map, Grid = grid };
        }

        // labels: false renders exactly the fixture format (round-trips through Parse);
        // true adds x/y rulers for reading large maps in a terminal.
        public static string Render(SimGrid grid, MapData map, AsciiLayer layer = AsciiLayer.Terrain, bool labels = true)
        {
            var onRoute = new bool[grid.NodeCount];
            if (layer == AsciiLayer.Route && map.Spawns.Length > 0)
            {
                var route = new List<SimNode>();
                Route.Collect(grid, grid.Get(map.Spawns[0]), route);
                foreach (SimNode n in route) onRoute[n.Index] = true;
            }

            var sb = new StringBuilder();
            if (labels)
            {
                sb.Append("     ");
                for (int x = 0; x < grid.Width; x++) sb.Append(x % 10 == 0 ? (char)('0' + (x / 10) % 10) : ' ');
                sb.Append('\n');
                sb.Append("     ");
                for (int x = 0; x < grid.Width; x++) sb.Append((char)('0' + x % 10));
                sb.Append('\n');
            }

            for (int y = grid.Height - 1; y >= 0; y--)
            {
                if (labels) sb.Append(y.ToString().PadLeft(3)).Append("  ");
                for (int x = 0; x < grid.Width; x++)
                {
                    sb.Append(TileChar(grid[x, y], map, onRoute));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static char TileChar(SimNode n, MapData map, bool[] onRoute)
        {
            var t = new TileCoord(n.X, n.Y);
            if (t == map.Core) return 'C';
            for (int i = 0; i < map.Spawns.Length; i++)
                if (t == map.Spawns[i]) return 'S';
            if (!n.IsWalkable) return 'X';
            if (n.HasWall) return n.TerrainCost >= HeavyWallCost ? 'H' : '#';
            if (onRoute[n.Index]) return '*';
            return '.';
        }

        private static WorldPoint ToWorld(SimNode n)
        {
            return new WorldPoint(n.Position.X, 0f, n.Position.Y);
        }
    }
}
