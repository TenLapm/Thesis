using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The two flow fields of WP-C2 (ARCHITECTURE.md §4.6): the ground field, which
    // must be exactly what it was before, and the sapper field beside it.
    public class FlowFieldSetTests
    {
        // ---------------------------------------------------------------- the ground field is unchanged

        // The flood fill exactly as WP1 ported it, before a second field existed,
        // writing into its own arrays. An oracle that shares no code with FlowField
        // except SimGrid.GetNeighbors (whose order is itself pinned by SimGridTests).
        private static void Wp1Flood(SimGrid grid, TileCoord goal, int[] best, int[] next)
        {
            for (int i = 0; i < grid.NodeCount; i++)
            {
                best[i] = SimNode.Infinity;
                next[i] = -1;
            }

            SimNode target = grid.InBounds(goal) ? grid.Get(goal) : null;
            if (target == null || !target.IsWalkable) return;

            best[target.Index] = 0;
            var open = new Queue<SimNode>();
            var neighbors = new SimNode[8];
            open.Enqueue(target);

            while (open.Count > 0)
            {
                SimNode current = open.Dequeue();
                int count = grid.GetNeighbors(current, neighbors);
                for (int k = 0; k < count; k++)
                {
                    SimNode neighbor = neighbors[k];
                    if (!neighbor.IsWalkable) continue;

                    bool diagonal = neighbor.X != current.X && neighbor.Y != current.Y;
                    int stepCost = (diagonal ? 14 : 10) * neighbor.TerrainCost;
                    int newCost = best[current.Index] + stepCost;
                    if (newCost < best[neighbor.Index])
                    {
                        best[neighbor.Index] = newCost;
                        next[neighbor.Index] = current.Index;
                        open.Enqueue(neighbor);
                    }
                }
            }
        }

        // A 24x18 board with static blockers, cheap and dear walls and towers
        // scattered at random. No spawn: this is about the field, not a playable map.
        private static SimGrid RandomBoard(ulong seed, out TileCoord core)
        {
            const int w = 24, h = 18;
            var rng = new Pcg32(seed, 99UL);
            core = new TileCoord(rng.NextInt(w), rng.NextInt(h));

            var rows = new string[h];
            for (int r = 0; r < h; r++)
            {
                var sb = new StringBuilder(w);
                for (int x = 0; x < w; x++)
                {
                    bool isCore = x == core.X && (h - 1 - r) == core.Y;
                    sb.Append(!isCore && rng.NextInt(10) == 0 ? 'X' : '.');
                }
                rows[r] = sb.ToString();
            }

            var map = new MapData
            {
                Name = "random-" + seed, Width = w, Height = h, NodeRadius = 1f, WorldSizeX = w * 2f, WorldSizeY = h * 2f,
                Rows = rows, Core = core, CoreWorld = new WorldPoint(0f, 0f, 0f),
            };
            map.Validate();

            var grid = new SimGrid(map);
            int[] costs = { 2, 15, 20, 200 };
            int towerId = 0;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode n = grid.ByIndex(i);
                if (!n.IsWalkable || (n.X == core.X && n.Y == core.Y)) continue;
                int roll = rng.NextInt(100);
                if (roll < 22) grid.SetWall(n, costs[rng.NextInt(costs.Length)], 6f);
                else if (roll < 27) grid.SetTower(n, 20, 8f, towerId++);
            }
            return grid;
        }

        // WORKPLAN WP-C2: "the Ground field is byte-identical to the WP1 parity
        // results, so moving to FlowFieldSet changes nothing for Ground."
        [Test]
        public void TheGroundFieldIsExactlyWhatWp1BuiltOnRandomBoards()
        {
            int walls = 0, unreachable = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                TileCoord core;
                SimGrid grid = RandomBoard(seed, out core);
                var best = new int[grid.NodeCount];
                var next = new int[grid.NodeCount];
                Wp1Flood(grid, core, best, next);

                // Through the set, the way the simulation rebuilds.
                TestFields.Set().Generate(grid, core);
                for (int i = 0; i < grid.NodeCount; i++)
                {
                    SimNode n = grid.ByIndex(i);
                    if (n.BestCost != best[i] || n.NextIndex != next[i])
                        Assert.Fail("board " + seed + ", tile (" + n.X + "," + n.Y + "): cost " + n.BestCost + " next " + n.NextIndex + ", WP1 gave cost " + best[i] + " next " + next[i]);
                    if (n.HasWall) walls++;
                    if (n.IsWalkable && n.BestCost == SimNode.Infinity) unreachable++;
                }

                // And through FlowField.Generate alone (a policy's scratch grid).
                SimGrid again = RandomBoard(seed, out core);
                new FlowField().Generate(again, core);
                for (int i = 0; i < again.NodeCount; i++)
                {
                    Assert.AreEqual(best[i], again.ByIndex(i).BestCost, "board " + seed + " tile " + i);
                    Assert.AreEqual(next[i], again.ByIndex(i).NextIndex, "board " + seed + " tile " + i);
                }
            }

            // The boards must actually have had something on them, or this proves little.
            Assert.Greater(walls, 3000, "walls and towers across the boards");
            Assert.Greater(unreachable, 0, "some boards had sealed pockets");
        }

        // ---------------------------------------------------------------- what a sapper pays

        [Test]
        public void ASapperPaysAShareOfWhatABuiltTileCosts()
        {
            AsciiFixture f = TestMaps.Parse("C . # . S");
            SimNode open = f.Grid[1, 0];
            SimNode wall = f.Grid[2, 0];
            Assert.AreEqual(15, wall.TerrainCost, "rig: the fixture's '#' is a default wall");

            Assert.AreEqual(1, FlowField.SapperTerrainCost(open, 0.2f), "open ground costs a sapper what it costs anyone");
            Assert.AreEqual(1, FlowField.SapperTerrainCost(open, 0f));
            Assert.AreEqual(3, FlowField.SapperTerrainCost(wall, 0.2f), "15 x 0.2");
            Assert.AreEqual(15, FlowField.SapperTerrainCost(wall, 1f), "factor 1: no discount");
            Assert.AreEqual(1, FlowField.SapperTerrainCost(wall, 0f), "factor 0: a wall reads as open ground");
            Assert.AreEqual(1, FlowField.SapperTerrainCost(wall, 0.01f), "never below open ground");
            Assert.AreEqual(8, FlowField.SapperTerrainCost(wall, 0.5f), "7.5 rounds to the even neighbour, the same on every runtime");

            f.Grid.SetTower(f.Grid[3, 0], 20, 8f, 0);
            Assert.AreEqual(4, FlowField.SapperTerrainCost(f.Grid[3, 0], 0.2f), "a tower tile is discounted like a wall");
            f.Grid.SetWall(f.Grid[1, 0], 200, 99999f);
            Assert.AreEqual(40, FlowField.SapperTerrainCost(f.Grid[1, 0], 0.2f));
        }

        [Test]
        public void TheSapperFieldPricesAWallAtItsShare()
        {
            // One row: no way round, so both fields go through the wall.
            AsciiFixture f = TestFields.Built("C # . S", 0.2f);
            Assert.AreEqual(150, f.Grid[1, 0].BestCost);
            Assert.AreEqual(30, f.Grid[1, 0].SapperCost, "10 x 3");
            Assert.AreEqual(170, f.Grid[3, 0].BestCost);
            Assert.AreEqual(50, f.Grid[3, 0].SapperCost);
            Assert.AreEqual(0, f.Grid[0, 0].SapperCost, "the core is the goal of both fields");
            Assert.AreEqual(-1, f.Grid[0, 0].SapperNextIndex);
        }

        [Test]
        public void OnABoardWithNothingBuiltTheTwoFieldsAreTheSame()
        {
            AsciiFixture f = TestFields.Built(@"
                . . . X . . .
                S . . X . . C
                . . . . . . .");
            for (int i = 0; i < f.Grid.NodeCount; i++)
            {
                SimNode n = f.Grid.ByIndex(i);
                Assert.AreEqual(n.BestCost, n.SapperCost, "tile " + i);
                Assert.AreEqual(n.NextIndex, n.SapperNextIndex, "tile " + i);
            }
        }

        [Test]
        public void WithAFactorOfOneTheSapperFieldIsTheGroundField()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                TileCoord core;
                SimGrid grid = RandomBoard(seed, out core);
                new FlowFieldSet(1f).Generate(grid, core);
                for (int i = 0; i < grid.NodeCount; i++)
                {
                    SimNode n = grid.ByIndex(i);
                    if (n.BestCost != n.SapperCost || n.NextIndex != n.SapperNextIndex)
                        Assert.Fail("board " + seed + ", tile (" + n.X + "," + n.Y + "): ground " + n.BestCost + "/" + n.NextIndex + ", sapper " + n.SapperCost + "/" + n.SapperNextIndex);
                }
            }
        }

        [Test]
        public void ASapperNeverPaysMoreThanAWalkerAndItsChainAlwaysEndsAtTheCore()
        {
            var route = new List<SimNode>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                TileCoord core;
                SimGrid grid = RandomBoard(seed, out core);
                TestFields.Set().Generate(grid, core);
                for (int i = 0; i < grid.NodeCount; i++)
                {
                    SimNode n = grid.ByIndex(i);
                    Assert.AreEqual(n.BestCost == SimNode.Infinity, n.SapperCost == SimNode.Infinity, "board " + seed + " tile " + i + ": reachable for one means reachable for both");
                    if (n.SapperCost == SimNode.Infinity) continue;
                    Assert.LessOrEqual(n.SapperCost, n.BestCost, "board " + seed + " tile " + i);

                    Route.Collect(grid, n, route, MovementClass.Sapper);
                    Assert.AreEqual(0, route[route.Count - 1].SapperCost, "board " + seed + ": chain from tile " + i + " did not reach the core");
                    for (int k = 1; k < route.Count; k++)
                        Assert.Less(route[k].SapperCost, route[k - 1].SapperCost, "cost must strictly fall along the chain");
                }
            }
        }

        // ---------------------------------------------------------------- around or through

        // A corridor along the bottom row with one wall tile in it, and a way round
        // over the top of a block of static tiles. The way round is 2*height + 6
        // steps; straight through is 6.
        private static string Corridor(int height)
        {
            var sb = new StringBuilder();
            sb.Append(". . . . . . .\n");
            for (int i = 0; i < height - 1; i++) sb.Append(". X X X X X .\n");
            sb.Append("S . . # . . C");
            return sb.ToString();
        }

        private static bool OnRoute(AsciiFixture f, SimNode tile, MovementClass movement)
        {
            var route = new List<SimNode>();
            Route.Collect(f.Grid, f.Grid.Get(f.Map.Spawns[0]), route, movement);
            return route.Contains(tile);
        }

        // WORKPLAN WP-C2: "in a fixture where one wall tile blocks a corridor with a
        // detour, Ground routes around it and Sapper digs through it."
        //
        // The plan said a 20-tile detour. With the game's real wall cost of 15 that
        // does not separate the two: a wall reads as a 14-tile detour to a walker, so
        // a 20-tile one makes even Ground dig (the next test). This fixture's detour
        // is 10 tiles longer than the straight line.
        [Test]
        public void GroundWalksRoundAWallThatASapperDigsThrough()
        {
            AsciiFixture f = TestFields.Built(Corridor(height: 5), 0.2f);
            SimNode spawn = f.Grid.Get(f.Map.Spawns[0]);
            SimNode wall = f.Grid[3, 0];
            Assert.IsTrue(wall.HasWall, "rig");

            Assert.AreEqual(160, spawn.BestCost, "ground: 16 steps round, against 200 through the wall");
            Assert.IsFalse(OnRoute(f, wall, MovementClass.Ground), "ground goes round");
            Assert.AreEqual(16, Route.CountHops(f.Grid, spawn));

            Assert.AreEqual(80, spawn.SapperCost, "sapper: five open tiles and a wall at 3, against 160 round");
            Assert.IsTrue(OnRoute(f, wall, MovementClass.Sapper), "the sapper goes through");
            Assert.AreEqual(6, Route.CountHops(f.Grid, spawn, MovementClass.Sapper));
            Assert.AreEqual(8.0, Route.CostInTiles(spawn, MovementClass.Sapper));
            Assert.AreEqual(16.0, Route.CostInTiles(spawn));
        }

        [Test]
        public void WhenTheWayRoundCostsMoreThanTheWallEvenGroundDigs()
        {
            AsciiFixture f = TestFields.Built(Corridor(height: 10), 0.2f); // 26 steps round: 20 more than straight
            SimNode spawn = f.Grid.Get(f.Map.Spawns[0]);
            SimNode wall = f.Grid[3, 0];

            Assert.AreEqual(200, spawn.BestCost, "through the wall (5 x 10 + 150) beats 260 round");
            Assert.IsTrue(OnRoute(f, wall, MovementClass.Ground));
            Assert.IsTrue(OnRoute(f, wall, MovementClass.Sapper));
        }

        [Test]
        public void TheDiscountDecidesWhereASapperStopsDigging()
        {
            // The same corridor at four shares. Up to 0.5 the wall is cheaper than the
            // 10 extra tiles; at 1 a sapper is a walker and goes round.
            foreach (float factor in new[] { 0f, 0.2f, 0.5f })
            {
                AsciiFixture f = TestFields.Built(Corridor(5), factor);
                Assert.IsTrue(OnRoute(f, f.Grid[3, 0], MovementClass.Sapper), "factor " + factor);
            }
            AsciiFixture walker = TestFields.Built(Corridor(5), 1f);
            Assert.IsFalse(OnRoute(walker, walker.Grid[3, 0], MovementClass.Sapper), "factor 1");
        }

        [Test]
        public void ASapperObeysTheCornerCutRuleLikeEveryoneElse()
        {
            // The core touches the spawn's side only across a diagonal between two
            // walls. Nobody may slip through that diagonal; a sapper enters a wall
            // head-on instead.
            AsciiFixture f = TestFields.Built(@"
                S #
                # C", 0.2f);
            SimNode spawn = f.Grid[0, 1];
            SimNode next = f.Grid.NextOf(spawn, MovementClass.Sapper);
            Assert.IsTrue(next.HasWall, "the first step is into a wall, not across the corner");
            Assert.AreEqual(10 + 30, spawn.SapperCost, "leave the spawn (10), then leave the wall (10 x 3)");
            Assert.AreEqual(10 + 150, spawn.BestCost);
        }

        // ---------------------------------------------------------------- rebuilding

        [Test]
        public void BothFieldsAreRebuiltTogetherAndCountAsOneRebuild()
        {
            AsciiFixture f = TestMaps.Parse(Corridor(5));
            FlowFieldSet set = TestFields.Set();
            int before = f.Grid.FieldVersion;

            set.Generate(f.Grid, f.Map.Core);
            Assert.AreEqual(before + 1, f.Grid.FieldVersion, "two floods, one rebuild");

            SimNode spawn = f.Grid.Get(f.Map.Spawns[0]);
            Assert.AreEqual(160, spawn.BestCost);
            Assert.AreEqual(80, spawn.SapperCost);

            // The wall goes: both fields must see it.
            f.Grid.ClearWall(f.Grid[3, 0]);
            set.Generate(f.Grid, f.Map.Core);
            Assert.AreEqual(before + 2, f.Grid.FieldVersion);
            Assert.AreEqual(60, spawn.BestCost);
            Assert.AreEqual(60, spawn.SapperCost);
        }

        [Test]
        public void NoStaleSapperPointerSurvivesARebuild()
        {
            AsciiFixture f = TestMaps.Parse(@"
                . X .
                X . X
                . X C");
            SimNode pocket = f.Grid[1, 1];
            pocket.SapperCost = 7;
            pocket.SapperNextIndex = 3; // stale values from a previous field

            TestFields.Set().Generate(f.Grid, f.Map.Core);

            Assert.AreEqual(SimNode.Infinity, pocket.SapperCost);
            Assert.AreEqual(-1, pocket.SapperNextIndex);
            Assert.IsNull(f.Grid.NextOf(pocket, MovementClass.Sapper));
        }

        // FlowField.Generate is the ground field only. Pinned so that nobody takes it
        // for a full rebuild: the simulation always goes through FlowFieldSet.
        [Test]
        public void GeneratingTheGroundFieldAloneLeavesTheSapperFieldAsItWas()
        {
            AsciiFixture f = TestMaps.Parse(Corridor(5));
            new FlowField().Generate(f.Grid, f.Map.Core);
            SimNode spawn = f.Grid.Get(f.Map.Spawns[0]);
            Assert.AreEqual(160, spawn.BestCost);
            Assert.AreEqual(SimNode.Infinity, spawn.SapperCost, "never built on this grid");
            Assert.AreEqual(-1, spawn.SapperNextIndex);
        }

        [Test]
        public void TheShareMustBeBetweenZeroAndOne()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FlowFieldSet(-0.1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FlowFieldSet(1.1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FlowFieldSet(float.NaN));
            Assert.DoesNotThrow(() => new FlowFieldSet(0f));
            Assert.DoesNotThrow(() => new FlowFieldSet(1f));
            Assert.AreEqual(0.2f, new FlowFieldSet(0.2f).SapperDigCostFactor);

            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { SapperDigCostFactor = -1f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { SapperDigCostFactor = 2f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { SapperDigCostFactor = float.NaN }.Validate());
        }

        // ---------------------------------------------------------------- the grid

        [Test]
        public void ACloneCarriesBothFields()
        {
            AsciiFixture f = TestFields.Built(Corridor(5), 0.2f);
            SimGrid clone = f.Grid.Clone();
            for (int i = 0; i < f.Grid.NodeCount; i++)
            {
                Assert.AreEqual(f.Grid.ByIndex(i).BestCost, clone.ByIndex(i).BestCost);
                Assert.AreEqual(f.Grid.ByIndex(i).NextIndex, clone.ByIndex(i).NextIndex);
                Assert.AreEqual(f.Grid.ByIndex(i).SapperCost, clone.ByIndex(i).SapperCost);
                Assert.AreEqual(f.Grid.ByIndex(i).SapperNextIndex, clone.ByIndex(i).SapperNextIndex);
            }
            Assert.AreEqual(f.Grid.TileSize, clone.TileSize);
            Assert.AreEqual(2f, f.Grid.TileSize, "ASCII fixtures have tiles two world units wide");
        }

        [Test]
        public void FlyingHasNoFieldToAsk()
        {
            AsciiFixture f = TestFields.Built("C . S");
            SimNode spawn = f.Grid[2, 0];
            Assert.AreEqual(spawn.BestCost, spawn.CostFor(MovementClass.Ground));
            Assert.AreEqual(spawn.SapperCost, spawn.CostFor(MovementClass.Sapper));
            Assert.AreEqual(spawn.NextIndex, spawn.NextIndexFor(MovementClass.Ground));
            Assert.AreEqual(spawn.SapperNextIndex, spawn.NextIndexFor(MovementClass.Sapper));
            Assert.AreSame(f.Grid[1, 0], f.Grid.NextOf(spawn, MovementClass.Sapper));

            Assert.Throws<System.InvalidOperationException>(() => spawn.CostFor(MovementClass.Flying));
            Assert.Throws<System.InvalidOperationException>(() => spawn.NextIndexFor(MovementClass.Flying));
            Assert.Throws<System.InvalidOperationException>(() => Route.Collect(f.Grid, spawn, new List<SimNode>(), MovementClass.Flying));
        }
    }
}
