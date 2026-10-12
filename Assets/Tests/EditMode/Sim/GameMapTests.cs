using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The board the game is played on since WP-C4 (GDD 3 and 4.1), as SampleScene
    // exports it: 24 x 24 tiles with rocks, and walls of one tile. The rules these
    // tests lean on are older and tested elsewhere on fixtures; what is pinned here is
    // that the real map and the real shape file put them to work as the GDD says.
    public class GameMapTests
    {
        private static List<TileCoord> Rocks(MapData map)
        {
            var rocks = new List<TileCoord>();
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (!map.IsWalkable(x, y)) rocks.Add(new TileCoord(x, y));
            return rocks;
        }

        [Test]
        public void TheBoardIs24By24WithTheSpawnAndTheCoreInOppositeCorners()
        {
            MapData map = TestSims.GameMap();

            Assert.AreEqual(24, map.Width);
            Assert.AreEqual(24, map.Height);
            Assert.AreEqual(1, map.Spawns.Length);
            Assert.AreEqual(new TileCoord(2, 2), map.Spawns[0]);
            Assert.AreEqual(new TileCoord(21, 21), map.Core);
        }

        // GDD 3.2, rules 1 and 3: about a tenth of the board, and none close to the
        // spawn or the core. (Rule 4, a way through on foot, is MapData.Validate: the
        // file would not have loaded.)
        [Test]
        public void TheRocksAreAboutATenthOfTheBoardAndKeepClearOfTheSpawnAndTheCore()
        {
            MapData map = TestSims.GameMap();
            List<TileCoord> rocks = Rocks(map);

            double share = rocks.Count / (double)(map.Width * map.Height);
            TestContext.WriteLine(rocks.Count + " rock tiles, " + (100 * share).ToString("0.0") + " % of the board");
            Assert.That(share, Is.InRange(0.07, 0.15), "about a tenth");

            foreach (TileCoord rock in rocks)
            {
                foreach (TileCoord keep in new[] { map.Spawns[0], map.Core })
                {
                    int away = Math.Max(Math.Abs(rock.X - keep.X), Math.Abs(rock.Y - keep.Y));
                    Assert.GreaterOrEqual(away, 4, "rock " + rock + " is too close to " + keep);
                }
            }
        }

        // GDD 3.2, rule 2: the rocks must not decide the maze. On the empty board the
        // walkers' way is only a little longer than the straight line, so the rocks
        // have not built a detour of their own.
        [Test]
        public void OnTheEmptyBoardTheRocksCostTheWalkersLittle()
        {
            Simulation sim = TestSims.Game();
            MapData map = sim.Map;
            SimNode spawn = sim.State.Grid.Get(map.Spawns[0]);

            double straight = Route.CostInTiles(new Simulation(new SimConfig(), OpenBoard(map), TestSims.GameShapes(), TestTowers.Roster(), 1,
                                                               new EscalationPlanner(new SimConfig(), map)).State.Grid.Get(map.Spawns[0]));
            double withRocks = Route.CostInTiles(spawn);

            TestContext.WriteLine("walkers: " + straight.ToString("0.0") + " tiles on the bare board, " + withRocks.ToString("0.0") + " with the rocks");
            Assert.Greater(withRocks, straight, "the rocks are in the way at all");
            Assert.Less(withRocks, straight * 1.25, "and no more than a quarter longer");
        }

        [Test]
        public void EnemiesOnFootGoRoundTheRocks()
        {
            Simulation sim = TestSims.Game();
            SimGrid grid = sim.State.Grid;
            var route = new List<SimNode>();

            foreach (MovementClass movement in new[] { MovementClass.Ground, MovementClass.Sapper })
            {
                Route.Collect(grid, grid.Get(sim.Map.Spawns[0]), route, movement);
                Assert.AreEqual(sim.Map.Core, new TileCoord(route[route.Count - 1].X, route[route.Count - 1].Y), movement + " reaches the core");
                foreach (SimNode node in route) Assert.IsTrue(node.IsWalkable, movement + " route crosses the rock at (" + node.X + "," + node.Y + ")");
            }
        }

        [Test]
        public void NothingCanBeBuiltOnARock()
        {
            Simulation sim = TestSims.Game();
            TileCoord rock = Rocks(sim.Map)[0];
            float budget = sim.State.BuildBudget;

            TestSims.Send(sim, SimCommand.PlaceShape(rock.X, rock.Y));
            TestSims.Send(sim, SimCommand.PlaceTower(0, rock.X, rock.Y));

            Assert.AreEqual(0, sim.State.PlacementLog.Count, "nothing was built");
            Assert.AreEqual(0, sim.State.Towers.Count);
            Assert.AreEqual(budget, sim.State.BuildBudget, "and nothing was paid");
        }

        // GDD 4.1: a wall is one tile, every tile costs the same, and a sale is one
        // tile. A drag in the game is this, one command per tile.
        [Test]
        public void ADrawnLineIsOnePieceATileAndASaleTakesOneTile()
        {
            Simulation sim = TestSims.Game();
            float budget = sim.State.BuildBudget;
            int price = TestSims.GameShapes()[0].BuildCost;

            for (int x = 5; x <= 8; x++) TestSims.Send(sim, SimCommand.PlaceShape(x, 2));

            Assert.AreEqual(4, sim.State.PlacementLog.Count, "one row of the build log per tile");
            foreach (PlacementRecord record in sim.State.PlacementLog) Assert.AreEqual(1, record.Tiles.Length);
            Assert.AreEqual(budget - 4 * price, sim.State.BuildBudget, "one price a tile");

            TestSims.Send(sim, SimCommand.SellWall(6, 2));

            Assert.AreEqual(Occupant.None, sim.State.Grid[6, 2].Occupant, "the sold tile is open ground");
            foreach (int x in new[] { 5, 7, 8 }) Assert.AreEqual(Occupant.Wall, sim.State.Grid[x, 2].Occupant, "its neighbours stand");
            Assert.AreEqual(budget - 4 * price + price * sim.Config.WallSellRefund, sim.State.BuildBudget, 1e-5f, "half the price of one tile back");
        }

        // The same tile cannot be drawn twice: a drag that crosses its own line, or a
        // tower, pays once.
        [Test]
        public void ATileThatIsBuiltCannotBeBuiltOnAgain()
        {
            Simulation sim = TestSims.Game();
            TestSims.Send(sim, SimCommand.PlaceShape(5, 2));
            float after = sim.State.BuildBudget;

            TestSims.Send(sim, SimCommand.PlaceShape(5, 2));

            Assert.AreEqual(1, sim.State.PlacementLog.Count);
            Assert.AreEqual(after, sim.State.BuildBudget);
        }

        [Test]
        public void AnIdlePlayerLosesInWaveOne()
        {
            Simulation sim = TestSims.Game();
            int guard = 200000;
            while (!sim.State.IsGameOver && guard-- > 0) sim.Tick();

            Assert.IsTrue(sim.State.IsGameOver, "an idle player must lose");
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.AreEqual(0, sim.State.LastOutcome.Killed);
            TestContext.WriteLine("idle game over after " + (sim.State.Tick * sim.Config.TickSeconds).ToString("0.0") + " s");
        }

        // The scripted players of the harness were written for tetrominoes on an open
        // board. They must still play on this one: they are what the offline ladder
        // and the balance check run.
        [Test]
        public void TheScriptedPlayersPlayOnThisBoard()
        {
            foreach (string name in new[] { "sentry", "mixed", "restless", "greedy" })
            {
                EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
                {
                    Map = TestSims.GameMap(),
                    Shapes = TestSims.GameShapes(),
                    Seed = 3,
                    Policy = Registry.CreatePolicy(name),
                    MaxWaves = 4,
                });

                TestContext.WriteLine(name + ": " + run.WavesResolved + " wave(s), " + run.Placements + " rows in the build log, " + run.TowersPlaced + " towers");
                Assert.Greater(run.Placements, 0, name + " built something");
                Assert.GreaterOrEqual(run.WavesResolved, 1, name);
            }
        }

        private static MapData OpenBoard(MapData map)
        {
            MapData open = MapData.FromJson(map.ToJson());
            for (int r = 0; r < open.Rows.Length; r++) open.Rows[r] = new string('.', open.Width);
            return open;
        }
    }
}
