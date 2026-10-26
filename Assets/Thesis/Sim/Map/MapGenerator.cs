using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Makes a map from a seed (2026-10-13): the same seed gives the same map on every
    // runtime, so a generated map is as replayable as an exported one (I1). Only whole
    // numbers and the seeded generator are used.
    //
    //   Rocks     a handful of ridges: short walks that keep their direction, one or
    //             two tiles thick. Scattered single rocks would make every place on the
    //             board alike; a ridge is something to put a core behind.
    //   The ring  `margin` tiles along the edge stay open, and one more tile inside
    //             it, so no ridge ever closes the ring off. Nothing is built on the
    //             ring (MapData.BuildMargin); enemies enter through it.
    //   Entries   every second tile of the outermost line is an entry point a planner
    //             may use (MapData.Spawns). Which of them a wave uses is the planner's
    //             choice, wave by wave.
    //   The core  the open tile nearest the middle. The player may move it before the
    //             first wave (SimConfig.CorePlacement).
    //
    //   Deposits  `deposits` ore deposits (2026-10-14) on open tiles inside the ring, at
    //             least three tiles apart and not next to the middle. The farther from
    //             the middle, the richer: 2 ore a wave near it, 4 at the ring. Drawn
    //             from a stream of their own, so a map without deposits is the map it was.
    //
    // Any open tile that the ring cannot reach is turned into rock, with the game's own
    // flow field, so wherever the core is put every entry point can walk to it.
    public static class MapGenerator
    {
        private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        public static MapData Generate(ulong seed, int width = 32, int height = 32, float nodeRadius = 1f, int margin = 2, int ridges = 10,
                                       float originX = 0f, float originZ = 0f, int deposits = 0, int depositsPerCluster = 1)
        {
            if (width < 12 || height < 12) throw new ArgumentOutOfRangeException(nameof(width), "A generated map is at least 12 x 12.");
            if (margin < 1) throw new ArgumentOutOfRangeException(nameof(margin), "A generated map needs a ring of at least one tile.");

            var rng = new Pcg32(seed, RngStreams.Map);
            var rock = new bool[width, height];
            int clear = margin + 1;

            for (int r = 0; r < ridges; r++)
            {
                int x = rng.NextInt(clear + 1, width - clear - 1);
                int y = rng.NextInt(clear + 1, height - clear - 1);
                int dir = rng.NextInt(8);
                int length = rng.NextInt(4, 10);
                for (int step = 0; step < length; step++)
                {
                    Stamp(rock, x, y, width, height, clear);
                    if (rng.NextInt(2) == 0) Stamp(rock, x + Dx[(dir + 2) % 8], y + Dy[(dir + 2) % 8], width, height, clear);
                    if (rng.NextInt(3) == 0) dir = (dir + (rng.NextInt(2) == 0 ? 1 : 7)) % 8;
                    x += Dx[dir];
                    y += Dy[dir];
                }
            }

            var map = new MapData
            {
                Name = "gen-" + width + "x" + height + "-" + seed,
                Width = width,
                Height = height,
                NodeRadius = nodeRadius,
                WorldSizeX = (float)(width * (float)(nodeRadius * 2f)),
                WorldSizeY = (float)(height * (float)(nodeRadius * 2f)),
                OriginX = originX,
                OriginZ = originZ,
                BuildMargin = margin,
                Core = new TileCoord(0, 0),
                CoreWorld = new WorldPoint(),
            };

            // Pockets the ring cannot reach become rock.
            map.Rows = Rows(rock, width, height);
            var grid = new SimGrid(map);
            new FlowField().Generate(grid, new TileCoord(0, 0));
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (!rock[x, y] && grid[x, y].BestCost == SimNode.Infinity) rock[x, y] = true;
            map.Rows = Rows(rock, width, height);
            grid = new SimGrid(map);

            // The core: the open tile nearest the middle (ties: lowest y, then lowest x).
            int cx = width / 2, cy = height / 2;
            int best = int.MaxValue;
            for (int y = clear; y < height - clear; y++)
            {
                for (int x = clear; x < width - clear; x++)
                {
                    if (rock[x, y]) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < best) { best = d; map.Core = new TileCoord(x, y); }
                }
            }
            map.CoreWorld = World(grid, map.Core);

            // Entry points: every second tile of the outermost line, side by side in a
            // fixed order (bottom, top, left, right).
            int count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    map.Spawns = new TileCoord[count];
                    map.SpawnWorlds = new WorldPoint[count];
                    count = 0;
                }
                for (int x = 0; x < width; x += 2) { Entry(map, grid, pass, ref count, x, 0); Entry(map, grid, pass, ref count, x, height - 1); }
                for (int y = 2; y < height - 2; y += 2) { Entry(map, grid, pass, ref count, 0, y); Entry(map, grid, pass, ref count, width - 1, y); }
            }

            if (deposits > 0 && depositsPerCluster > 1) PlaceClusters(map, rock, seed, deposits, depositsPerCluster, clear);
            else if (deposits > 0) PlaceDeposits(map, rock, seed, deposits, clear);
            map.Validate();
            return map;
        }

        private static void PlaceDeposits(MapData map, bool[,] rock, ulong seed, int wanted, int clear)
        {
            var rng = new Pcg32(seed, RngStreams.Deposits);
            int width = map.Width, height = map.Height;
            int cx = width / 2, cy = height / 2;
            int half = Math.Max(1, Math.Min(cx, cy) - clear);
            var placed = new System.Collections.Generic.List<Deposit>();
            TileCoord? middle = MiddleSite(rng, rock, width, height, 3, MiddleBand(half, 6));
            if (middle.HasValue) placed.Add(new Deposit { Tile = middle.Value, Yield = 2 });
            for (int attempt = 0; attempt < wanted * 40 && placed.Count < wanted; attempt++)
            {
                // At least one tile inside the build zone's border, never on the ring.
                int x = rng.NextInt(clear + 1, width - clear - 1);
                int y = rng.NextInt(clear + 1, height - clear - 1);
                if (rock[x, y]) continue;
                int ring = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                if (ring < 3) continue; // the middle is where a core goes
                bool crowded = false;
                for (int i = 0; i < placed.Count && !crowded; i++)
                    crowded = Math.Max(Math.Abs(placed[i].Tile.X - x), Math.Abs(placed[i].Tile.Y - y)) < 3;
                if (crowded) continue;
                int yield = 2 + Math.Min(2, 2 * ring / half);
                placed.Add(new Deposit { Tile = new TileCoord(x, y), Yield = yield });
            }
            map.Deposits = placed.ToArray();
        }

        // Deposits in clusters (2026-10-24, after StarCraft's mineral fields): `per` veins
        // round each of a few centres, with a free tile between two veins for a wall or a
        // tower, so one defended outpost holds several collectors. Centres are at least
        // nine tiles apart and away from the middle; a cluster is as rich as its centre is
        // far from the middle (2 to 4 ore a vein).
        private static void PlaceClusters(MapData map, bool[,] rock, ulong seed, int wanted, int per, int clear)
        {
            var rng = new Pcg32(seed, RngStreams.Deposits);
            int width = map.Width, height = map.Height;
            int cx = width / 2, cy = height / 2;
            int half = Math.Max(1, Math.Min(cx, cy) - clear);
            int clusters = (wanted + per - 1) / per;
            var centres = new System.Collections.Generic.List<TileCoord>();
            TileCoord? middle = MiddleSite(rng, rock, width, height, 6, MiddleBand(half, 10));
            if (middle.HasValue) centres.Add(middle.Value);
            for (int attempt = 0; attempt < clusters * 80 && centres.Count < clusters; attempt++)
            {
                int x = rng.NextInt(clear + 3, width - clear - 3);
                int y = rng.NextInt(clear + 3, height - clear - 3);
                if (rock[x, y] || Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) < 6) continue;
                bool crowded = false;
                for (int i = 0; i < centres.Count && !crowded; i++)
                    crowded = Math.Max(Math.Abs(centres[i].X - x), Math.Abs(centres[i].Y - y)) < 9;
                if (!crowded) centres.Add(new TileCoord(x, y));
            }

            var placed = new System.Collections.Generic.List<Deposit>();
            foreach (TileCoord c in centres)
            {
                int ring = Math.Max(Math.Abs(c.X - cx), Math.Abs(c.Y - cy));
                int yield = 2 + Math.Min(2, 2 * ring / half);
                int here = 0;
                for (int attempt = 0; attempt < 60 && here < per && placed.Count < wanted; attempt++)
                {
                    int x = c.X + rng.NextInt(-2, 3);
                    int y = c.Y + rng.NextInt(-2, 3);
                    if (x <= clear || y <= clear || x >= width - clear - 1 || y >= height - clear - 1 || rock[x, y]) continue;
                    bool crowded = false;
                    for (int i = 0; i < placed.Count && !crowded; i++)
                        crowded = Math.Max(Math.Abs(placed[i].Tile.X - x), Math.Abs(placed[i].Tile.Y - y)) < 2;
                    if (crowded) continue;
                    placed.Add(new Deposit { Tile = new TileCoord(x, y), Yield = yield });
                    here++;
                }
            }
            map.Deposits = placed.ToArray();
        }

        // How far from the middle the guaranteed first deposit (or cluster) may lie, in rings:
        // a third of the way out, and at least `least`.
        private static int MiddleBand(int half, int least) { return Math.Max(least, half / 3); }

        // A free tile between rings `inner` and `outer` round the middle (2026-10-26, the student:
        // "it'll be bad if all the spots are on the edge"). Every map gets one there, so a core
        // built in the middle always has ore near it. Tried at random first, then searched ring
        // by ring outward, so a rocky middle still finds one; null only if the band is all rock.
        private static TileCoord? MiddleSite(Pcg32 rng, bool[,] rock, int width, int height, int inner, int outer)
        {
            int cx = width / 2, cy = height / 2;
            outer = Math.Min(outer, Math.Min(cx, cy) - 4);
            if (outer < inner) return null;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                int x = cx + rng.NextInt(-outer, outer + 1);
                int y = cy + rng.NextInt(-outer, outer + 1);
                int ring = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                if (ring >= inner && !rock[x, y]) return new TileCoord(x, y);
            }
            for (int ring = inner; ring <= outer; ring++)
                for (int x = cx - ring; x <= cx + ring; x++)
                    for (int y = cy - ring; y <= cy + ring; y++)
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) == ring && !rock[x, y]) return new TileCoord(x, y);
            return null;
        }

        private static void Entry(MapData map, SimGrid grid, int pass, ref int count, int x, int y)
        {
            if (pass == 1)
            {
                map.Spawns[count] = new TileCoord(x, y);
                map.SpawnWorlds[count] = World(grid, map.Spawns[count]);
            }
            count++;
        }

        private static WorldPoint World(SimGrid grid, TileCoord tile)
        {
            Vec2f p = grid.Get(tile).Position;
            return new WorldPoint(p.X, 0f, p.Y);
        }

        private static void Stamp(bool[,] rock, int x, int y, int width, int height, int clear)
        {
            if (x < clear || y < clear || x >= width - clear || y >= height - clear) return;
            rock[x, y] = true;
        }

        private static string[] Rows(bool[,] rock, int width, int height)
        {
            var rows = new string[height];
            var line = new char[width];
            for (int r = 0; r < height; r++)
            {
                int y = height - 1 - r;
                for (int x = 0; x < width; x++) line[x] = rock[x, y] ? 'X' : '.';
                rows[r] = new string(line);
            }
            return rows;
        }
    }
}
