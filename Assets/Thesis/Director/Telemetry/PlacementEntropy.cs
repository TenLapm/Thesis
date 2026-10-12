using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // The primary metric of the study (gate G5), exactly as ARCHITECTURE.md §5.9
    // defines it: how evenly the tiles a player built are spread over the map.
    //
    //   what is counted   every tile of every wall piece and every tower the
    //                     player built; a tile later sold or breached still counts
    //   over what         the region the tile is in: the map cut into
    //                     `partition` x `partition` regions (6 x 6 by default)
    //   which part        the windows of waves 1..W. Row 0, the build phase before
    //                     the first wave, is left out: no wave had been seen yet,
    //                     so nothing in it can be an answer to one
    //   the number        H = -sum(p_r ln p_r) / ln K, with p_r the share of the
    //                     counted tiles in region r and K the number of regions
    //                     that have a buildable tile. 0 = everything in one region,
    //                     1 = spread evenly; 0 when nothing was built
    //
    // It is computed from telemetry rows, after the fact. Nothing in the game reads
    // it, so the definition can change until it is frozen before the pilot.
    //
    // The logarithm is DetMath.Log, like every logarithm in Thesis.* (ARCHITECTURE.md
    // §9 rule 3): the same rows give the same number on every machine.
    public static class PlacementEntropy
    {
        public const int DefaultPartition = 6;

        // H over the first `waves` waves of one session. Use CompletedWaves, or
        // ComparisonWindow for a pair of sessions, to choose `waves`.
        public static double Compute(IReadOnlyList<WaveRecord> rows, MapData map, int waves,
                                     int partition = DefaultPartition, PlacementScope scope = PlacementScope.WallsAndTowers)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (partition < 1) throw new ArgumentOutOfRangeException(nameof(partition), partition, "Must be at least 1.");

            var counts = new int[partition * partition];
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                WaveRecord row = rows[i];
                if (row.Wave < 1 || row.Wave > waves) continue;

                for (int p = 0; p < row.Placements.Length; p++)
                {
                    WaveRecord.PlacementRow placed = row.Placements[p];
                    if (!Counts(placed, scope)) continue;
                    for (int t = 0; t < placed.Tiles.Length; t++)
                    {
                        counts[RegionOf(placed.Tiles[t][0], placed.Tiles[t][1], map, partition)]++;
                        total++;
                    }
                }
            }

            int regions = Regions(map, partition);
            if (total == 0 || regions < 2) return 0.0;

            double h = 0.0;
            for (int r = 0; r < counts.Length; r++)
            {
                if (counts[r] == 0) continue;
                double share = (double)counts[r] / total;
                h -= share * DetMath.Log(share);
            }
            return h / DetMath.Log(regions);
        }

        // How many waves of this session can be compared with another session's: the
        // waves whose whole window was played, the fight AND the build phase after
        // it. A window is whole when the next wave began. The last wave a session
        // reached never is: the session stopped, or the core fell, before the
        // player could answer it.
        public static int CompletedWaves(IReadOnlyList<WaveRecord> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            int last = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i].Wave > last) last = rows[i].Wave;
            return last > 0 ? last - 1 : 0;
        }

        // §5.9: a participant's two sessions are both cut to the shorter one.
        public static int ComparisonWindow(IReadOnlyList<WaveRecord> a, IReadOnlyList<WaveRecord> b)
        {
            return Math.Min(CompletedWaves(a), CompletedWaves(b));
        }

        // K: the regions that have at least one tile a player could build on. A
        // region that is all static blocker, or holds nothing but the spawn or the
        // core, can never receive a tile and is left out, so that an even spread
        // over everything reachable still scores 1.
        public static int Regions(MapData map, int partition = DefaultPartition)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (partition < 1) throw new ArgumentOutOfRangeException(nameof(partition), partition, "Must be at least 1.");

            var buildable = new bool[partition * partition];
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (!map.IsWalkable(x, y)) continue;
                    if (Placement.IsProtected(map, new TileCoord(x, y))) continue;
                    buildable[RegionOf(x, y, map, partition)] = true;
                }
            }

            int k = 0;
            for (int r = 0; r < buildable.Length; r++) if (buildable[r]) k++;
            return k;
        }

        // Whole-number division, so a tile is always in exactly one region:
        // rx = partition * x / width, ry = partition * y / height.
        public static int RegionOf(int x, int y, MapData map, int partition = DefaultPartition)
        {
            int rx = partition * x / map.Width;
            int ry = partition * y / map.Height;
            return rx * partition + ry;
        }

        private static bool Counts(WaveRecord.PlacementRow placed, PlacementScope scope)
        {
            switch (placed.Kind)
            {
                case WaveRecord.PlacementRow.Wall: return scope != PlacementScope.TowersOnly;
                case WaveRecord.PlacementRow.Tower: return scope != PlacementScope.WallsOnly;
                default: return false; // a sale or a reroll builds nothing
            }
        }
    }
}
