using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Values match ScenarioBenchmark.ScenarioKind, so the serialized "scenario: N"
    // in the Bench_* scenes still means the same layout.
    public enum BenchScenario
    {
        OpenField = 0,
        Maze = 1,
        ChokePoints = 2,
        RandomScatter = 3,
    }

    // Port of ScenarioBenchmark.BuildScenario's layouts. The committed
    // Benchmark_Open/Maze/Choke.json path metrics were produced from exactly these
    // layouts, which makes them a parity oracle for SimGrid + FlowField.
    //
    // Lives in Thesis.Sim (moved from Thesis.Harness in WP4) because the Unity-side
    // ScenarioBenchmark needs it at runtime and Thesis.Harness is Editor-only.
    public static class BenchScenarios
    {
        // Scenario walls are terrain nobody can afford to dig through: cost 200 reads
        // as "a 200-tile detour", and health 99999 means an agent standing on one
        // could not chew through it during a run. Keeps the layout static.
        public const int WallCost = 200;
        public const float WallHealth = 99999f;

        // Seed for RandomScatter. The original used UnityEngine.Random.InitState(12345),
        // whose sequence can't be reproduced outside Unity, so the scatter LAYOUT differs
        // from the pre-port one (and Benchmark_Stress100.json has no parity check). The
        // density and the clearance rule are the same.
        public const ulong ScatterSeed = 12345UL;

        // Returns the number of wall tiles placed (the benchmark's "walls" count).
        // Does NOT rebuild the flow field; the caller does.
        public static int Apply(SimGrid grid, MapData map, BenchScenario scenario, float scatterDensity = 0.10f)
        {
            if (map.Spawns.Length == 0) throw new InvalidOperationException("Bench scenarios need a spawn; map '" + map.Name + "' has none.");

            int w = grid.Width;
            int h = grid.Height;
            TileCoord spawn = map.Spawns[0];
            TileCoord goal = map.Core;
            int placed = 0;

            switch (scenario)
            {
                case BenchScenario.OpenField:
                    break;

                case BenchScenario.Maze:
                    // Horizontal serpentine: a full wall row every 5 tiles with a
                    // 4-tile gap alternating between the left and right end.
                    bool gapLeft = true;
                    for (int y = 5; y <= h - 6; y += 5)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            bool inGap = gapLeft ? (x < 4) : (x >= w - 4);
                            if (!inGap && TrySetWall(grid, x, y, spawn, goal)) placed++;
                        }
                        gapLeft = !gapLeft;
                    }
                    break;

                case BenchScenario.ChokePoints:
                    // Three full walls, each with a single 3-tile gap; the gaps are
                    // staggered so the crowd has to swing across the whole map.
                    int[] rows = { h / 4, h / 2, (3 * h) / 4 };
                    int[] gaps = { w / 2, w / 6, (5 * w) / 6 };
                    for (int i = 0; i < rows.Length; i++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (Math.Abs(x - gaps[i]) > 1 && TrySetWall(grid, x, rows[i], spawn, goal)) placed++;
                        }
                    }
                    break;

                case BenchScenario.RandomScatter:
                    var rng = new Pcg32(ScatterSeed, 0UL);
                    int want = (int)Math.Round((float)(w * h * scatterDensity));
                    int guard = 0;
                    while (placed < want && guard++ < want * 30)
                    {
                        if (TrySetWall(grid, rng.NextInt(w), rng.NextInt(h), spawn, goal)) placed++;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            return placed;
        }

        // Keeps a 2-tile clearance around spawn and goal so agents always have room
        // to enter and leave the maze (same rule as the original).
        private static bool TrySetWall(SimGrid grid, int x, int y, TileCoord spawn, TileCoord goal)
        {
            SimNode n = grid[x, y];
            if (!n.IsWalkable || n.HasWall) return false;
            if (Math.Abs(x - spawn.X) <= 2 && Math.Abs(y - spawn.Y) <= 2) return false;
            if (Math.Abs(x - goal.X) <= 2 && Math.Abs(y - goal.Y) <= 2) return false;

            grid.SetWall(n, WallCost, WallHealth);
            return true;
        }
    }
}
