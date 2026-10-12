using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // The scripted player that sells (WP-C3). It exists so that recorded episodes
    // contain tower and wall sales; these tests say exactly what it sells.
    public class SellerPolicyTests
    {
        // S is (0,2), C is (11,2): the enemies walk along row 2.
        private const string Field = @"
            . . . . . . . . . . . .
            . . . . . . . . . . . .
            S . . . . . . . . . . C
            . . . . . . . . . . . .
            . . . . . . . . . . . .";

        // One shape, two tiles upright, so a test knows where every wall tile is;
        // one free tower type, so the shop always offers four of it.
        private static Simulation Sim()
        {
            var config = new SimConfig();
            AsciiFixture f = AsciiMap.Parse(Field);
            var post = new ShapeDef { Name = "post", BuildCost = 4, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0), new TileCoord(0, 1) } };
            TowerDef[] towers = { TestTowers.Gun(damage: 1f, range: 3f, cost: 10, id: "gun") };
            return new Simulation(config, f.Map, new[] { post }, towers, 1, new FixedPlanner(FixedPlanner.Group(1, hp: 1f, speed: 8f)));
        }

        private static void Wall(Simulation sim, int x, int y)
        {
            int before = sim.State.PlacementLog.Count;
            TestSims.Send(sim, SimCommand.PlaceShape(x, y));
            if (sim.State.PlacementLog.Count != before + 1) throw new InvalidOperationException("rig: the wall piece at (" + x + "," + y + ") was refused.");
        }

        private static void PlayOneWave(Simulation sim)
        {
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            if (sim.State.Phase != SimPhase.Intermission) throw new InvalidOperationException("rig: expected a build phase, got " + sim.State.Phase + ".");
        }

        private static List<SimCommand> Act(Simulation sim, IPlayerPolicy policy)
        {
            var sent = new List<SimCommand>();
            Action<SimCommand> send = c =>
            {
                sent.Add(c);
                TestSims.Send(sim, c);
            };
            policy.OnIntermission(sim, send, new Pcg32(1, RngStreams.Policy));
            return sent;
        }

        // Four towers along the top row, three wall pieces along the bottom rows.
        private static Simulation Built()
        {
            Simulation sim = Sim();
            for (int slot = 0; slot < 4; slot++) TestTowers.BuySlot(sim, slot, 3 + 2 * slot, 4);
            Wall(sim, 2, 0);
            Wall(sim, 5, 0);
            Wall(sim, 8, 0);
            return sim;
        }

        [Test]
        public void BeforeTheFirstWaveItSellsNothing()
        {
            Simulation sim = Built();
            Assert.AreEqual(0, Act(sim, new SellerPolicy()).Count, "nothing has been tried yet");
        }

        [Test]
        public void ItSellsTheTowerThatDidTheLeastAndTheOldestWallPiece()
        {
            Simulation sim = Built();
            PlayOneWave(sim);
            sim.State.Towers[0].DamageDealt = 5f;
            sim.State.Towers[1].DamageDealt = 2f;
            sim.State.Towers[2].DamageDealt = 9f;
            sim.State.Towers[3].DamageDealt = 2f;      // a tie with tower 1: the lower id goes
            float budget = sim.State.BuildBudget;

            List<SimCommand> sent = Act(sim, new SellerPolicy());

            Assert.AreEqual(2, sent.Count, "one tower and one piece a phase");
            Assert.AreEqual(SimCommandKind.SellTower, sent[0].Kind);
            Assert.AreEqual(5, sent[0].X);
            Assert.AreEqual(4, sent[0].Y);
            Assert.IsTrue(sim.State.Towers[1].Sold);
            Assert.IsFalse(sim.State.Towers[3].Sold);

            Assert.AreEqual(SimCommandKind.SellWall, sent[1].Kind);
            Assert.IsFalse(sim.State.Grid[2, 0].HasWall, "the first piece placed is gone");
            Assert.IsFalse(sim.State.Grid[2, 1].HasWall);
            Assert.IsTrue(sim.State.Grid[5, 0].HasWall);
            Assert.IsTrue(sim.State.Grid[8, 0].HasWall);

            Assert.AreEqual(budget + 5f + 2f, sim.State.BuildBudget, "half of the tower's 10 and half of the piece's 4");
        }

        [Test]
        public void ItKeepsThreeTowersAndOnePiece()
        {
            Simulation sim = Sim();
            for (int slot = 0; slot < 3; slot++) TestTowers.BuySlot(sim, slot, 3 + 2 * slot, 4);
            Wall(sim, 2, 0);
            PlayOneWave(sim);

            Assert.AreEqual(0, Act(sim, new SellerPolicy()).Count, "three towers and one piece are the floor");

            List<SimCommand> sent = Act(sim, new SellerPolicy { MinTowersKept = 0, MinPiecesKept = 0 });
            Assert.AreEqual(2, sent.Count, "with the floor taken away it sells");
        }

        [Test]
        public void ATowerThatIsAlreadyGoneIsNotCounted()
        {
            Simulation sim = Built();
            PlayOneWave(sim);
            sim.State.Towers[0].DamageDealt = 0f;       // would be the weakest
            TestSims.Send(sim, SimCommand.SellTower(3, 4));
            Assert.IsTrue(sim.State.Towers[0].Sold, "rig: sold by hand");

            // Three are left: the floor.
            List<SimCommand> sent = Act(sim, new SellerPolicy());
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(SimCommandKind.SellWall, sent[0].Kind, "only the wall piece");
        }

        [Test]
        public void APieceWithATileLeftStillCountsAndAPieceThatIsGoneIsPassedOver()
        {
            Simulation sim = Built();
            PlayOneWave(sim);
            SimGrid grid = sim.State.Grid;

            // The oldest piece loses one of its two tiles to a breach: still the oldest standing.
            grid.ClearWall(grid[2, 0]);
            List<SimCommand> sent = Act(sim, new SellerPolicy { MinTowersKept = 99 });
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(2, sent[0].X);
            Assert.AreEqual(1, sent[0].Y, "aimed at the tile that is left");
            Assert.IsFalse(grid[2, 1].HasWall);

            // Now that piece is gone altogether; the next oldest is the one at (5,0).
            sent = Act(sim, new SellerPolicy { MinTowersKept = 99 });
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(5, sent[0].X);
            Assert.IsFalse(grid[5, 0].HasWall);
            Assert.IsTrue(grid[8, 0].HasWall);

            // One piece left: the floor.
            Assert.AreEqual(0, Act(sim, new SellerPolicy { MinTowersKept = 99 }).Count);
        }

        [Test]
        public void TheRestlessPlayerSellsThenBuildsAgain()
        {
            Assert.AreEqual("restless", SequencePolicy.Restless().Name);
            Assert.AreEqual("restless", Registry.CreatePolicy("restless").Name);
            CollectionAssert.Contains(Registry.PolicyNames, "restless");

            EpisodeResult run = EpisodeRunner.Run(TestEpisodes.Small(seed: 1, waves: 5, policy: SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3)));

            int towerSales = 0, wallSales = 0, towers = 0, walls = 0;
            foreach (ReplayCommand c in run.Replay.Commands)
            {
                SimCommandKind kind = c.ToCommand().Kind;
                if (kind == SimCommandKind.SellTower) towerSales++;
                if (kind == SimCommandKind.SellWall) wallSales++;
                if (kind == SimCommandKind.PlaceTower) towers++;
                if (kind == SimCommandKind.PlaceShape) walls++;
            }
            Assert.Greater(towerSales, 1, "tower sales");
            Assert.Greater(wallSales, 1, "wall sales");
            Assert.Greater(towers, towerSales, "it buys more than it sells");
            Assert.Greater(walls, wallSales);

            // WORKPLAN WP-C3 review probe: a replay with purchases, tower sales and
            // wall sales verifies, on every tick.
            ReplayReport report = ReplayRunner.Verify(TestEpisodes.Reload(run.Replay), perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());
            Assert.AreEqual(5, report.WavesVerified);
        }
    }
}
