using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // entropy <replay.json> [<other replay.json>] [--waves W] [--partition P]
    // The study's primary metric (ARCHITECTURE.md §5.9) for one session, or for a
    // pair cut to the same length: how evenly the tiles the player built are spread
    // over the map. The rows are made from the replay, which also carries the map.
    //   one file    the session's completed waves (or the first W of them)
    //   two files   both cut to the shorter one, as a participant's two sessions are
    internal static class EntropyCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--waves", "--partition" }, new[] { "--towers-on-rocks" });
            // --towers-on-rocks: the session was played with SimConfig.TowersOnWalls, so rock is buildable.
            bool rocks = args.Flag("--towers-on-rocks");
            string first = args.Positional(0, "the replay file");
            args.NoMorePositionals(2);
            int partition = args.GetInt("--partition", PlacementEntropy.DefaultPartition);
            if (partition < 1) throw new ArgumentException("entropy: --partition must be at least 1");

            ReplayFile a = ReplayFile.Load(first);
            List<WaveRecord> rowsA = ReplayRunner.Telemetry(a);

            ReplayFile b = null;
            List<WaveRecord> rowsB = null;
            if (args.PositionalCount > 1)
            {
                b = ReplayFile.Load(args.Positional(1, "the second replay file"));
                rowsB = ReplayRunner.Telemetry(b);
            }

            int waves = rowsB == null ? PlacementEntropy.CompletedWaves(rowsA) : PlacementEntropy.ComparisonWindow(rowsA, rowsB);
            if (args.Has("--waves")) waves = Math.Min(waves, args.GetInt("--waves", waves));

            Console.WriteLine("window: the first " + waves + " wave(s)" + (rowsB == null ? "" : ", the shorter of the two sessions")
                              + "; partition " + partition + "x" + partition + ", " + PlacementEntropy.Regions(a.MapData, partition) + " regions with a buildable tile");
            Report(first, a, rowsA, waves, partition, rocks);
            if (rowsB != null) Report(args.Positional(1, "the second replay file"), b, rowsB, waves, partition, rocks);
            return 0;
        }

        private static void Report(string path, ReplayFile file, List<WaveRecord> rows, int waves, int partition, bool rocks)
        {
            int tiles = 0;
            foreach (WaveRecord row in rows)
            {
                if (row.Wave < 1 || row.Wave > waves) continue;
                foreach (WaveRecord.PlacementRow p in row.Placements) if (p.IsBuild) tiles += p.Tiles.Length;
            }

            MapData map = file.MapData;
            Console.WriteLine(path);
            Console.WriteLine("  " + file.Planner + ", player " + file.Policy + ", seed " + file.RngSeed + ": " + PlacementEntropy.CompletedWaves(rows) + " completed wave(s), "
                              + tiles + " tile(s) built in the window");
            Console.WriteLine("  H (walls and towers)  " + Json.FormatDouble(PlacementEntropy.Compute(rows, map, waves, partition, PlacementScope.WallsAndTowers, rocks)) + "   <- the primary metric");
            Console.WriteLine("  H (walls only)        " + Json.FormatDouble(PlacementEntropy.Compute(rows, map, waves, partition, PlacementScope.WallsOnly, rocks)));
            Console.WriteLine("  H (towers only)       " + Json.FormatDouble(PlacementEntropy.Compute(rows, map, waves, partition, PlacementScope.TowersOnly, rocks)));
            Console.WriteLine("  H on 4x4 / 8x8        " + Json.FormatDouble(PlacementEntropy.Compute(rows, map, waves, 4, PlacementScope.WallsAndTowers, rocks)) + " / " + Json.FormatDouble(PlacementEntropy.Compute(rows, map, waves, 8, PlacementScope.WallsAndTowers, rocks)));
        }
    }
}
