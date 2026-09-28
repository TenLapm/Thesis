using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class PathfindingBenchTests
    {
        // A* and the flow field are independent algorithms over the same cost model
        // and the same neighbour rule. On any map they must agree on every cost to the
        // goal - a strong cross-check of both, and of the benchmark's fairness.
        // (First run found the original A* charged the tile ENTERED while the field
        // charges the tile LEFT: 258 vs 398 from a start on a wall. See DEVLOG WP4.)
        [Test]
        public void AStarCostEqualsFlowFieldCostOnScatteredWalls()
        {
            var rng = new Pcg32(3UL, 0UL);
            var f = TestMaps.Parse(@"
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . .
                . . . . . . . . . . . C");
            SimGrid grid = f.Grid;
            for (int i = 0; i < 30; i++)
            {
                SimNode n = grid[rng.NextInt(grid.Width), rng.NextInt(grid.Height)];
                if (!n.HasWall && !(n.X == f.Map.Core.X && n.Y == f.Map.Core.Y)) grid.SetWall(n, 15, 6f);
            }
            new FlowField().Generate(grid, f.Map.Core);
            SimNode goal = grid.Get(f.Map.Core);

            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode start = grid.ByIndex(i);
                Assert.AreEqual(start.BestCost, PathfindingBench.AStarCost(grid, start, goal), "cost from (" + start.X + "," + start.Y + ")");
            }
        }

        [Test]
        public void BenchProducesAllSections()
        {
            var o = new PathfindingBench.Options { RebuildIterations = 2, GridSizes = new[] { 10 }, WallDensities = new[] { 0f, 0.1f }, AStarCounts = new[] { 10 } };
            string json = PathfindingBench.RunToJson(o);

            StringAssert.Contains("\"rebuildSweep\":[", json);
            StringAssert.Contains("\"astarComparison\":{", json);
            StringAssert.Contains("\"done\":true", json);
            Assert.IsNotNull(Json.Deserialize<object>(json), "must be valid JSON");
        }
    }
}
