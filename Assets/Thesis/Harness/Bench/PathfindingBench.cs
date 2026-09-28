using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Port of BenchmarkRunner's two scenarios that never needed Unity (WP4):
    //   A) flow-field rebuild time vs grid size x wall density
    //   B) one shared flow field vs per-agent A* on the same grid and cost model
    // The Unity-only scenarios (frame time vs live agents, edits under load) stay
    // with ScenarioBenchmark in the Bench_* scenes.
    //
    // Differences from the original, on purpose: random walls come from a seeded
    // Pcg32 (the original used unseeded UnityEngine.Random, so no two runs were
    // alike), and it runs under .NET instead of the Unity editor, so absolute times
    // are not comparable with BenchmarkResults.json - only shapes of curves are.
    public static class PathfindingBench
    {
        public sealed class Options
        {
            public int RebuildIterations = 30;
            public int[] GridSizes = { 25, 38, 50, 75, 100 };
            public float[] WallDensities = { 0f, 0.10f, 0.25f };
            public int[] AStarCounts = { 100, 500, 1000 };
            public ulong Seed = 1;
        }

        public static string RunToJson(Options o)
        {
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("\"environment\":{\"runtime\":\"" + RuntimeInformation.FrameworkDescription + "\",\"os\":\"" + Esc(RuntimeInformation.OSDescription)
                        + "\",\"cores\":" + Environment.ProcessorCount + ",\"mode\":\"headless dotnet (Thesis.Cli bench)\",\"seed\":" + o.Seed + "},\n");

            json.Append("\"rebuildSweep\":[");
            bool first = true;
            var rng = new Pcg32(o.Seed, 0UL);
            foreach (int size in o.GridSizes)
            {
                foreach (float density in o.WallDensities)
                {
                    double mean, min, max;
                    BenchRebuild(size, density, o.RebuildIterations, rng, out mean, out min, out max);
                    if (!first) json.Append(",");
                    first = false;
                    json.Append("\n {\"size\":" + size + ",\"density\":" + F(density) + ",\"meanMs\":" + F(mean) + ",\"minMs\":" + F(min) + ",\"maxMs\":" + F(max) + "}");
                }
            }
            json.Append("],\n");

            json.Append("\"astarComparison\":");
            BenchAStar(o, rng, json);
            json.Append("\"done\":true\n}");
            return json.ToString();
        }

        // A square open map of `size` tiles, radius 1, goal at world (0.8 size, 0.8 size)
        // - the same construction BenchmarkRunner.BenchRebuild used.
        private static SimGrid OpenGrid(int size, float goalWorld, out TileCoord goal)
        {
            var rows = new string[size];
            for (int r = 0; r < size; r++) rows[r] = new string('.', size);
            var map = new MapData
            {
                Name = "bench-" + size,
                Width = size,
                Height = size,
                WorldSizeX = size * 2f,
                WorldSizeY = size * 2f,
                NodeRadius = 1f,
                Rows = rows,
                CoreWorld = new WorldPoint(goalWorld, 0f, goalWorld),
            };
            var grid = new SimGrid(map);
            SimNode g = grid.NodeFromPosition(new Vec2f(goalWorld, goalWorld));
            goal = new TileCoord(g.X, g.Y);
            return grid;
        }

        private static void Scatter(SimGrid grid, TileCoord goal, float density, Pcg32 rng)
        {
            int want = (int)Math.Round((float)(grid.Width * grid.Height * density));
            int walls = 0, guard = 0;
            while (walls < want && guard++ < want * 20)
            {
                SimNode n = grid[rng.NextInt(grid.Width), rng.NextInt(grid.Height)];
                if ((n.X == goal.X && n.Y == goal.Y) || n.HasWall) continue;
                grid.SetWall(n, 15, 6f);
                walls++;
            }
        }

        private static void BenchRebuild(int size, float density, int iterations, Pcg32 rng, out double mean, out double min, out double max)
        {
            SimGrid grid = OpenGrid(size, size * 0.8f, out TileCoord goal);
            Scatter(grid, goal, density, rng);
            var field = new FlowField();
            for (int i = 0; i < 3; i++) field.Generate(grid, goal); // warmup

            var sw = new Stopwatch();
            double sum = 0;
            min = double.MaxValue;
            max = 0;
            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                field.Generate(grid, goal);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                sum += ms;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
            }
            mean = sum / iterations;
        }

        // 38x38 with 10% walls - mirrors the game map scale, as in the original.
        private static void BenchAStar(Options o, Pcg32 rng, StringBuilder json)
        {
            SimGrid grid = OpenGrid(38, 30f, out TileCoord goal);
            Scatter(grid, goal, 0.10f, rng);
            var field = new FlowField();
            for (int i = 0; i < 3; i++) field.Generate(grid, goal);
            var sw = Stopwatch.StartNew();
            field.Generate(grid, goal);
            sw.Stop();

            json.Append("{\"gridSize\":" + grid.Width + ",\"wallDensity\":0.10,\"fieldRebuildMs\":" + F(sw.Elapsed.TotalMilliseconds) + ",\"runs\":[");
            SimNode goalNode = grid.Get(goal);
            var neighbors = new SimNode[8];
            bool first = true;
            foreach (int count in o.AStarCounts)
            {
                var starts = new List<SimNode>(count);
                while (starts.Count < count)
                {
                    SimNode n = grid[rng.NextInt(grid.Width), rng.NextInt(grid.Height)];
                    if (!n.HasWall && n != goalNode) starts.Add(n);
                }
                AStar(grid, starts[0], goalNode, neighbors); // warmup
                sw.Restart();
                foreach (SimNode s in starts) AStar(grid, s, goalNode, neighbors);
                sw.Stop();
                double total = sw.Elapsed.TotalMilliseconds;
                if (!first) json.Append(",");
                first = false;
                json.Append("{\"agents\":" + count + ",\"totalMs\":" + F(total) + ",\"perAgentMs\":" + F(total / count) + "}");
            }
            json.Append("]},\n");
        }

        // A* with a binary heap, the same corner-cut neighbour rule as the field (it
        // calls SimGrid.GetNeighbors) and the field's cost model. Note WHICH tile a
        // step is charged for: the flow field floods outward from the goal and prices
        // "neighbor -> current" by neighbor.TerrainCost, i.e. an agent pays for the
        // tile it LEAVES (a wall it stands on must be chewed; the goal tile is never
        // charged). The original BenchmarkRunner's A* charged the tile ENTERED, so its
        // costs differed from the field's whenever a start or goal had terrain > 1 -
        // harmless for timing, wrong for a cost cross-check. This one matches the field.
        private static int AStar(SimGrid grid, SimNode start, SimNode goal, SimNode[] neighbors)
        {
            int total = grid.NodeCount;
            var gCost = new int[total];
            var closed = new bool[total];
            for (int i = 0; i < total; i++) gCost[i] = int.MaxValue;

            var heap = new MinHeap(total);
            gCost[start.Index] = 0;
            heap.Push(start.Index, Heuristic(start, goal));

            while (heap.Count > 0)
            {
                int ci = heap.Pop();
                if (closed[ci]) continue;
                closed[ci] = true;
                SimNode cur = grid.ByIndex(ci);
                if (cur == goal) return gCost[ci];

                int count = grid.GetNeighbors(cur, neighbors);
                for (int k = 0; k < count; k++)
                {
                    SimNode nb = neighbors[k];
                    if (!nb.IsWalkable || closed[nb.Index]) continue;
                    bool diag = nb.X != cur.X && nb.Y != cur.Y;
                    int ng = gCost[ci] + (diag ? FlowField.DiagonalCost : FlowField.CardinalCost) * cur.TerrainCost;
                    if (ng < gCost[nb.Index])
                    {
                        gCost[nb.Index] = ng;
                        heap.Push(nb.Index, ng + Heuristic(nb, goal));
                    }
                }
            }
            return -1;
        }

        // Octile distance, admissible with min terrainCost = 1.
        private static int Heuristic(SimNode n, SimNode goal)
        {
            int dx = Math.Abs(n.X - goal.X), dy = Math.Abs(n.Y - goal.Y);
            int lo = Math.Min(dx, dy), hi = Math.Max(dx, dy);
            return lo * FlowField.DiagonalCost + (hi - lo) * FlowField.CardinalCost;
        }

        private sealed class MinHeap
        {
            private int[] items;
            private long[] keys;
            public int Count;

            public MinHeap(int capacity)
            {
                items = new int[capacity * 4];
                keys = new long[capacity * 4];
            }

            public void Push(int item, long key)
            {
                if (Count == items.Length)
                {
                    Array.Resize(ref items, Count * 2);
                    Array.Resize(ref keys, Count * 2);
                }
                items[Count] = item;
                keys[Count] = key;
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int root = items[0];
                Count--;
                items[0] = items[Count];
                keys[0] = keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, s = i;
                    if (l < Count && keys[l] < keys[s]) s = l;
                    if (r < Count && keys[r] < keys[s]) s = r;
                    if (s == i) break;
                    Swap(i, s);
                    i = s;
                }
                return root;
            }

            private void Swap(int a, int b)
            {
                int ti = items[a]; items[a] = items[b]; items[b] = ti;
                long tk = keys[a]; keys[a] = keys[b]; keys[b] = tk;
            }
        }

        // Test hook: the A* cost from `start` must equal the flow field's BestCost.
        internal static int AStarCost(SimGrid grid, SimNode start, SimNode goal)
        {
            return AStar(grid, start, goal, new SimNode[8]);
        }

        private static string F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        private static string Esc(string s) { return s == null ? "" : s.Replace("\\", "/").Replace("\"", "'"); }
    }
}
