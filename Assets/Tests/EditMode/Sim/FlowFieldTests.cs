using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class FlowFieldTests
    {
        private static AsciiFixture Built(string text)
        {
            var f = TestMaps.Parse(text);
            new FlowField().Generate(f.Grid, f.Map.Core);
            return f;
        }

        [Test]
        public void GoalCostsZeroAndCardinalStepsCostTen()
        {
            var f = Built("C . . .");
            Assert.AreEqual(0, f.Grid[0, 0].BestCost);
            Assert.AreEqual(10, f.Grid[1, 0].BestCost);
            Assert.AreEqual(30, f.Grid[3, 0].BestCost);
        }

        [Test]
        public void DiagonalStepCostsFourteen()
        {
            var f = Built(@"
                . .
                C .");
            Assert.AreEqual(14, f.Grid[1, 1].BestCost);
        }

        [Test]
        public void WallTerrainCostMultipliesTheStep()
        {
            // One-row map: no way around, so the route must dig.
            var f = Built("C # .");
            Assert.AreEqual(10 * AsciiMap.WallCost, f.Grid[1, 0].BestCost);
            Assert.AreEqual(10 * AsciiMap.WallCost + 10, f.Grid[2, 0].BestCost);
        }

        [Test]
        public void CheapDetourBeatsDiggingAndTieBreakIsDeterministic()
        {
            var f = Built(@"
                . . .
                C # S
                . . .");
            SimNode spawn = f.Grid[2, 1];

            // Around the wall (4 cardinal steps) = 40, versus 160 through it. The
            // diagonals past the wall are forbidden by the corner-cut rule.
            Assert.AreEqual(40, spawn.BestCost);

            // Top and bottom detours tie at 40. With x-outer/y-inner neighbour order
            // and a FIFO queue, the bottom route (2,0) is relaxed first and wins.
            // Hand-traced; if this flips, neighbour or queue order changed.
            SimNode next = f.Grid.NextOf(spawn);
            Assert.AreEqual(new TileCoord(2, 0), new TileCoord(next.X, next.Y));
        }

        [Test]
        public void NextChainFromEveryReachableTileEndsAtTheGoal()
        {
            var f = Built(@"
                . . . . . . .
                . # # # # . .
                . . . . # . .
                # # # . # . S
                C . . . . . .");
            var route = new List<SimNode>();
            for (int i = 0; i < f.Grid.NodeCount; i++)
            {
                SimNode n = f.Grid.ByIndex(i);
                if (n.BestCost == SimNode.Infinity) continue;
                Route.Collect(f.Grid, n, route);
                Assert.AreEqual(0, route[route.Count - 1].BestCost, "chain from (" + n.X + "," + n.Y + ") did not reach the goal");
                for (int k = 1; k < route.Count; k++)
                    Assert.Less(route[k].BestCost, route[k - 1].BestCost, "cost must strictly decrease along the chain");
            }
        }

        [Test]
        public void UnreachablePocketHasNoCostAndNoStalePointer()
        {
            var f = TestMaps.Parse(@"
                . X .
                X . X
                . X C");
            SimNode pocket = f.Grid[1, 1];
            pocket.BestCost = 7;
            pocket.NextIndex = 3; // stale values from a previous field

            new FlowField().Generate(f.Grid, f.Map.Core);

            Assert.AreEqual(SimNode.Infinity, pocket.BestCost);
            Assert.AreEqual(-1, pocket.NextIndex);
            Assert.IsNull(f.Grid.NextOf(pocket));
        }

        [Test]
        public void StaticBlockersAreNeverEntered()
        {
            var f = Built(@"
                . . .
                C X S");
            Assert.AreEqual(SimNode.Infinity, f.Grid[1, 0].BestCost);

            // Both diagonals that would cut past the blocker's corner are forbidden,
            // so the only route is up, across, across, down: 4 cardinal steps.
            Assert.AreEqual(40, f.Grid[2, 0].BestCost);
        }

        [Test]
        public void UnwalkableGoalLeavesEverythingUnreachableButBumpsVersion()
        {
            var f = TestMaps.Parse("C . X");
            int before = f.Grid.FieldVersion;
            new FlowField().Generate(f.Grid, new TileCoord(2, 0));

            Assert.AreEqual(before + 1, f.Grid.FieldVersion);
            for (int i = 0; i < f.Grid.NodeCount; i++)
                Assert.AreEqual(SimNode.Infinity, f.Grid.ByIndex(i).BestCost);
        }

        [Test]
        public void RebuildAfterClearingAWallRestoresTheShortRoute()
        {
            var f = Built(@"
                . . .
                C # S
                . . .");
            Assert.AreEqual(40, f.Grid[2, 1].BestCost);

            f.Grid.ClearWall(f.Grid[1, 1]);
            new FlowField().Generate(f.Grid, f.Map.Core);

            Assert.AreEqual(20, f.Grid[2, 1].BestCost);
            SimNode next = f.Grid.NextOf(f.Grid[2, 1]);
            Assert.AreEqual(new TileCoord(1, 1), new TileCoord(next.X, next.Y));
        }

        [Test]
        public void RouteCountsHopsLikeTheBenchmark()
        {
            var f = Built("C . . . S");
            Assert.AreEqual(4, Route.CountHops(f.Grid, f.Grid[4, 0]));
            Assert.AreEqual(4.0, Route.CostInTiles(f.Grid[4, 0]));
            Assert.AreEqual(0, Route.CountHops(f.Grid, f.Grid[0, 0]));
        }
    }
}
