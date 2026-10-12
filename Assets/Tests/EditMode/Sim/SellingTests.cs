using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // Selling what was built (WP-C3; ARCHITECTURE.md §4.6): a tower for a share of
    // its price, a wall piece for a share of its cost scaled by how much of it still
    // stands (decision D6). The first half calls Placement directly, where the
    // shapes and the tiles can be chosen; the second half goes through commands.
    public class SellingTests
    {
        // 8 x 5, open. S is (0,2), C is (7,2).
        private const string Open = @"
            . . . . . . . .
            . . . . . . . .
            S . . . . . . C
            . . . . . . . .
            . . . . . . . .";

        private static ShapeDef Shape(string name, int cost, params int[] xy)
        {
            var tiles = new TileCoord[xy.Length / 2];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = new TileCoord(xy[2 * i], xy[2 * i + 1]);
            return new ShapeDef { Name = name, BuildCost = cost, DigCost = 15, WallHealth = 6f, LocalTiles = tiles };
        }

        private static ShapeDef Bar() { return Shape("bar", 8, 0, 0, 1, 0, 2, 0, 3, 0); }      // four in a row
        private static ShapeDef Post() { return Shape("post", 3, 0, 0, 0, 1); }                // two, one above the other

        // A board, a budget and a log, without a Simulation around them.
        private sealed class Rig
        {
            public AsciiFixture F;
            public FlowFieldSet Fields = TestFields.Set();
            public List<PlacementRecord> Log = new List<PlacementRecord>();
            public List<TowerState> Towers = new List<TowerState>();
            public List<SimEvent> Events = new List<SimEvent>();
            public float Budget = 20f;

            public Rig(string map)
            {
                F = TestMaps.Parse(map);
                Fields.Generate(F.Grid, F.Map.Core);
            }

            public SimNode Node(int x, int y) { return F.Grid[x, y]; }

            public bool Place(ShapeDef shape, int x, int y)
            {
                return Placement.TryPlace(F.Grid, F.Map, Fields, shape, 0, new TileCoord(x, y), ref Budget, 0, Log, Events);
            }

            public bool SellWall(int x, int y, float rate = 0.5f)
            {
                Events.Clear();
                return Placement.TrySellWall(F.Grid, F.Map, Fields, new TileCoord(x, y), rate, ref Budget, 7, Log, Events);
            }

            public bool PlaceTower(TowerDef def, int price, int x, int y)
            {
                return Placement.TryPlaceTower(F.Grid, F.Map, Fields, def, price, new TileCoord(x, y), ref Budget, 0, Towers, Log, Events);
            }

            public bool SellTower(int x, int y, float rate = 0.5f)
            {
                Events.Clear();
                return Placement.TrySellTower(F.Grid, F.Map, Fields, new TileCoord(x, y), rate, ref Budget, 7, Towers, Log, Events);
            }

            // What a breach does to a tile (AgentSystem calls the same thing).
            public void Breach(int x, int y)
            {
                F.Grid.ClearWall(Node(x, y));
                Fields.Generate(F.Grid, F.Map.Core);
            }
        }

        // ================================================================ wall pieces

        // WORKPLAN WP-C3: "selling a whole piece refunds cost x WallSellRefund."
        [Test]
        public void SellingAWholePieceClearsEveryTileAndRefundsItsShare()
        {
            var rig = new Rig(Open);
            Assert.IsTrue(rig.Place(Bar(), 2, 4));
            Assert.AreEqual(12f, rig.Budget);
            for (int x = 2; x <= 5; x++) Assert.AreEqual(0, rig.Node(x, 4).PieceId, "every tile carries the piece's id: the index of its row in the log");
            Assert.AreEqual(0, rig.Log[0].PieceId);
            Assert.AreEqual(8f, rig.Log[0].Cost);

            Assert.IsTrue(rig.SellWall(4, 4), "any tile of the piece will do");

            Assert.AreEqual(16f, rig.Budget, "8 x 0.5 back");
            for (int x = 2; x <= 5; x++)
            {
                SimNode n = rig.Node(x, 4);
                Assert.IsFalse(n.HasWall);
                Assert.AreEqual(Occupant.None, n.Occupant);
                Assert.AreEqual(-1, n.PieceId);
                Assert.AreEqual(1, n.TerrainCost);
            }

            Assert.AreEqual(4, rig.Events.Count, "one WallSold per tile, in the piece's tile order");
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(SimEventKind.WallSold, rig.Events[i].Kind);
                Assert.AreEqual(2 + i, rig.Events[i].IntA);
                Assert.AreEqual(4, rig.Events[i].IntB);
                Assert.AreEqual(0, rig.Events[i].IntC, "the piece id");
            }

            Assert.AreEqual(2, rig.Log.Count, "the sale is a row of its own; the row that built the piece stays");
            PlacementRecord sale = rig.Log[1];
            Assert.AreEqual(PlacementKind.WallSold, sale.Kind);
            Assert.IsTrue(sale.IsSale);
            Assert.IsFalse(sale.IsPlacement);
            Assert.AreEqual(0, sale.PieceId, "it points back at the piece");
            Assert.AreEqual("bar", sale.ShapeName);
            Assert.AreEqual(4, sale.Tiles.Length);
            Assert.AreEqual(4f, sale.Refund);
            Assert.AreEqual(0f, sale.Cost);
            Assert.AreEqual(7, sale.Tick);

            Assert.IsFalse(rig.SellWall(4, 4), "it cannot be sold twice");
            Assert.AreEqual(16f, rig.Budget);
        }

        // "After two of its four tiles were breached, it refunds half of that."
        [Test]
        public void APieceThatLostTwoOfItsFourTilesRefundsHalfOfThat()
        {
            var rig = new Rig(Open);
            rig.Place(Bar(), 2, 4);
            rig.Breach(3, 4);
            rig.Breach(5, 4);

            Assert.IsTrue(rig.SellWall(2, 4));
            Assert.AreEqual(12f + 2f, rig.Budget, "8 x 0.5 x 2/4");
            Assert.AreEqual(2, rig.Events.Count);
            Assert.AreEqual(2, rig.Log[1].Tiles.Length, "the row lists only what was cleared");
            CollectionAssert.AreEqual(new[] { new TileCoord(2, 4), new TileCoord(4, 4) }, rig.Log[1].Tiles);
            Assert.IsFalse(rig.Node(2, 4).HasWall);
            Assert.IsFalse(rig.Node(4, 4).HasWall);
        }

        [Test]
        public void TheRefundIsWorkedOutFromTheCostTheShareAndTheTilesLeft()
        {
            // Cost 3, three of four standing, half back: 3 x 0.5 x 3 / 4 = 1.125.
            var rig = new Rig(Open);
            rig.Place(Shape("ell", 3, 0, 0, 1, 0, 2, 0, 2, 1), 1, 0);
            rig.Breach(2, 0);
            rig.SellWall(1, 0);
            Assert.AreEqual(17f + 1.125f, rig.Budget);

            // The share is whatever the caller passes (SimConfig.WallSellRefund).
            var full = new Rig(Open);
            full.Place(Bar(), 2, 4);
            full.SellWall(2, 4, rate: 1f);
            Assert.AreEqual(20f, full.Budget, "all of it back");

            var none = new Rig(Open);
            none.Place(Bar(), 2, 4);
            Assert.IsTrue(none.SellWall(2, 4, rate: 0f), "a sale for nothing is still a sale: the tiles go");
            Assert.AreEqual(12f, none.Budget);
            Assert.IsFalse(none.Node(2, 4).HasWall);
        }

        // "Selling clears only the tiles that still belong to that piece (a tile
        // re-built by a later piece is not touched)."
        [Test]
        public void ATileBuiltOverByALaterPieceBelongsToThatPiece()
        {
            var rig = new Rig(Open);
            rig.Place(Bar(), 2, 4);          // piece 0: (2..5, 4)
            rig.Breach(3, 4);
            Assert.IsTrue(rig.Place(Post(), 3, 3), "rig: the post covers (3,3) and the breached (3,4)"); // piece 1
            Assert.AreEqual(1, rig.Node(3, 4).PieceId);
            Assert.AreEqual(9f, rig.Budget);

            Assert.IsTrue(rig.SellWall(2, 4));
            Assert.AreEqual(9f + 3f, rig.Budget, "three of the bar's four tiles were still its own: 8 x 0.5 x 3/4");
            Assert.IsTrue(rig.Node(3, 4).HasWall, "the post's tile in the middle of the bar is left standing");
            Assert.AreEqual(1, rig.Node(3, 4).PieceId);
            Assert.IsTrue(rig.Node(3, 3).HasWall);
            Assert.IsFalse(rig.Node(2, 4).HasWall);
            Assert.IsFalse(rig.Node(4, 4).HasWall);
            Assert.IsFalse(rig.Node(5, 4).HasWall);

            // And the post sells as a whole from that very tile.
            Assert.IsTrue(rig.SellWall(3, 4));
            Assert.AreEqual(12f + 1.5f, rig.Budget);
            Assert.IsFalse(rig.Node(3, 3).HasWall);
            Assert.AreEqual(1, rig.Log[rig.Log.Count - 1].PieceId);
        }

        [Test]
        public void SellingFromAnyTileOfThePieceGivesTheSameResult()
        {
            float[] budgets = new float[4];
            for (int i = 0; i < 4; i++)
            {
                var rig = new Rig(Open);
                rig.Place(Bar(), 2, 4);
                Assert.IsTrue(rig.SellWall(2 + i, 4));
                budgets[i] = rig.Budget;
                for (int x = 2; x <= 5; x++) Assert.IsFalse(rig.Node(x, 4).HasWall);
            }
            CollectionAssert.AreEqual(new[] { 16f, 16f, 16f, 16f }, budgets);
        }

        // "Selling an empty tile is a no-op."
        [Test]
        public void WhatIsNotAPieceCannotBeSoldAsOne()
        {
            var rig = new Rig(@"
                . . . . . . . .
                . . . # . . . .
                S . . . . . . C
                . . . . . . . .
                . . . . . . . .");
            rig.PlaceTower(TestTowers.Gun(), 5, 5, 1);
            int version = rig.F.Grid.FieldVersion;
            int rows = rig.Log.Count;

            Assert.IsFalse(rig.SellWall(1, 1), "open ground");
            Assert.IsFalse(rig.SellWall(40, 1), "off the map");
            Assert.IsFalse(rig.SellWall(-1, 0));
            Assert.IsFalse(rig.SellWall(5, 1), "a tower is not a wall piece");
            Assert.IsFalse(rig.SellWall(3, 3), "a wall the scenario put there never went through the log: it has no piece");
            Assert.AreEqual(-1, rig.Node(3, 3).PieceId);

            Assert.AreEqual(15f, rig.Budget, "nothing was refunded");
            Assert.AreEqual(rows, rig.Log.Count);
            Assert.AreEqual(0, rig.Events.Count);
            Assert.AreEqual(version, rig.F.Grid.FieldVersion, "and nothing was rebuilt");
            Assert.IsTrue(rig.Node(3, 3).HasWall);

            float budget = 5f;
            Assert.Throws<System.ArgumentNullException>(() =>
                Placement.TrySellWall(rig.F.Grid, rig.F.Map, rig.Fields, new TileCoord(3, 3), 0.5f, ref budget, 0, null, null));
        }

        // "After a sale the flow field is rebuilt and the route can get shorter."
        [Test]
        public void AfterASaleBothFieldsAreRebuiltAndTheRouteIsShortAgain()
        {
            // A corridor one tile high: a wall in it can only be dug through.
            var rig = new Rig(@"
                X X X X X X X
                S . . . . . C
                X X X X X X X");
            SimNode spawn = rig.Node(0, 1);
            Assert.AreEqual(60, spawn.BestCost);

            rig.Place(Shape("plug", 2, 0, 0), 3, 1);
            Assert.AreEqual(60 + 140, spawn.BestCost, "through the plug: its tile costs 15 to leave instead of 1");
            Assert.AreEqual(60 + 20, spawn.SapperCost);
            int version = rig.F.Grid.FieldVersion;

            Assert.IsTrue(rig.SellWall(3, 1));
            Assert.AreEqual(version + 1, rig.F.Grid.FieldVersion, "one rebuild");
            Assert.AreEqual(60, spawn.BestCost);
            Assert.AreEqual(60, spawn.SapperCost, "the sapper field too");
        }

        [Test]
        public void AWallBuiltWithoutALogHasNoPieceAndStaysPut()
        {
            var rig = new Rig(Open);
            float budget = 20f;
            Assert.IsTrue(Placement.TryPlace(rig.F.Grid, rig.F.Map, rig.Fields, Bar(), 0, new TileCoord(2, 4), ref budget, 0, null, null));
            Assert.AreEqual(-1, rig.Node(2, 4).PieceId);
            Assert.IsFalse(rig.SellWall(2, 4));
        }

        // ================================================================ towers

        // WORKPLAN WP-C3: "a sale refunds price x SellRefund, clears the tile, and
        // rebuilds the fields. Selling from an empty tile is a no-op."
        [Test]
        public void SellingATowerRefundsAShareOfWhatWasPaidAndClearsItsTile()
        {
            var rig = new Rig(Open);
            TowerDef def = TestTowers.Gun(cost: 99, id: "gun"); // the list price is not what counts
            Assert.IsTrue(rig.PlaceTower(def, 14, 3, 2));       // on the straight route
            Assert.AreEqual(6f, rig.Budget, "the offer's price was paid, not the roster's cost");
            TowerState tower = rig.Towers[0];
            Assert.AreEqual(14, tower.PricePaid);
            SimNode spawn = rig.Node(0, 2);
            Assert.Greater(spawn.BestCost, 70, "rig: the tower is in the way");
            int version = rig.F.Grid.FieldVersion;

            Assert.IsTrue(rig.SellTower(3, 2));

            Assert.AreEqual(6f + 7f, rig.Budget, "14 x 0.5");
            Assert.IsFalse(tower.IsAlive);
            Assert.IsTrue(tower.Sold);
            Assert.AreEqual(1, rig.Towers.Count, "it keeps its id and its place in the list");
            SimNode node = rig.Node(3, 2);
            Assert.IsFalse(node.HasWall);
            Assert.AreEqual(Occupant.None, node.Occupant);
            Assert.AreEqual(-1, node.TowerId);
            Assert.AreEqual(version + 1, rig.F.Grid.FieldVersion);
            Assert.AreEqual(70, spawn.BestCost, "the straight route is back");
            Assert.AreEqual(70, spawn.SapperCost);

            Assert.AreEqual(1, rig.Events.Count);
            SimEvent e = rig.Events[0];
            Assert.AreEqual(SimEventKind.TowerSold, e.Kind);
            Assert.AreEqual(0, e.IntA);
            Assert.AreEqual(3, e.IntB);
            Assert.AreEqual(2, e.IntC);
            Assert.AreEqual(7f, e.FloatA);

            PlacementRecord sale = rig.Log[1];
            Assert.AreEqual(PlacementKind.TowerSold, sale.Kind);
            Assert.AreEqual("gun", sale.ShapeName);
            Assert.AreEqual(0, sale.TowerId);
            Assert.AreEqual(7f, sale.Refund);
            CollectionAssert.AreEqual(new[] { new TileCoord(3, 2) }, sale.Tiles);
            StringAssert.Contains("sold", tower.ToString());

            Assert.IsFalse(rig.SellTower(3, 2), "it is gone: there is nothing to sell twice");
            Assert.AreEqual(13f, rig.Budget);
        }

        [Test]
        public void WhatIsNotATowerCannotBeSoldAsOne()
        {
            var rig = new Rig(Open);
            rig.Place(Bar(), 2, 4);
            int rows = rig.Log.Count;

            Assert.IsFalse(rig.SellTower(1, 1), "open ground");
            Assert.IsFalse(rig.SellTower(2, 4), "a wall");
            Assert.IsFalse(rig.SellTower(99, 0), "off the map");
            Assert.AreEqual(12f, rig.Budget);
            Assert.AreEqual(rows, rig.Log.Count);
            Assert.AreEqual(0, rig.Events.Count);
        }

        // ================================================================ through commands

        private const string Field = @"
            . . . . . . . . . .
            S . . . . . . . . C
            . . . . . . . . . .";

        private static SimConfig Quick(int coreHp = 1000) { return new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = coreHp }; }

        private static IWavePlanner Trickle() { return new FixedPlanner(FixedPlanner.Group(count: 2, hp: 10f, speed: 5f)); }

        [Test]
        public void SellTowerRefundsHalfThePriceByDefault()
        {
            Simulation sim = TestSims.Ascii(Field, Trickle(), Quick());
            SimNode spawn = sim.State.Grid.Get(sim.Map.Spawns[0]);
            TowerState tower = TestTowers.BuySlot(sim, 0, 4, 1); // on the route
            int price = tower.PricePaid;
            Assert.AreEqual(98, spawn.BestCost, "rig: the route steps round the tower");

            TestSims.Send(sim, SimCommand.SellTower(4, 1));

            Assert.AreEqual(60f - price + price * 0.5f, sim.State.BuildBudget);
            Assert.IsTrue(tower.Sold);
            Assert.IsFalse(tower.IsAlive);
            Assert.AreEqual(90, spawn.BestCost);
            Assert.AreEqual(90, spawn.SapperCost);
            Assert.AreEqual(1, sim.LastTickEvents.Count);
            Assert.AreEqual(SimEventKind.TowerSold, sim.LastTickEvents[0].Kind);
            Assert.AreEqual(2, sim.State.PlacementLog.Count);
            Assert.IsTrue(sim.State.Shop.Offers[0].Bought, "selling does not put the offer back in the shop");
        }

        [Test]
        public void ASoldTowerNeverFiresAndItsTileCanBeBuiltOnAgain()
        {
            var roster = new[] { TestTowers.Gun(damage: 50f, range: 30f, interval: 1) };
            Simulation sim = TestSims.Ascii(Field, Trickle(), Quick(), towers: roster);
            TowerState first = TestTowers.BuySlot(sim, 0, 5, 2);
            TestSims.Send(sim, SimCommand.SellTower(5, 2));

            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(0, first.Shots, "it would have killed both");
            Assert.AreEqual(2, sim.State.LastOutcome.Leaked);

            TowerState second = TestTowers.BuyAny(sim, 5, 2);
            Assert.AreEqual(1, second.Id, "a new tower, a new id");
            Assert.AreEqual(1, sim.State.Grid[5, 2].TowerId);
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(2, sim.State.LastOutcome.Killed);
            Assert.AreEqual(0, first.Shots);
        }

        // The bag deals the piece, so this test reads what it placed from the log.
        [Test]
        public void SellWallSellsThePieceTheClickedTileBelongsTo()
        {
            Simulation sim = TestSims.Ascii(Field, Trickle(), Quick());
            TestSims.Send(sim, SimCommand.PlaceShape(3, 0));
            Assert.AreEqual(1, sim.State.PlacementLog.Count, "rig: every shape fits at (3,0)");
            PlacementRecord piece = sim.State.PlacementLog[0];
            float cost = piece.Cost;
            Assert.AreEqual(60f - cost, sim.State.BuildBudget);

            TileCoord clicked = piece.Tiles[piece.Tiles.Length - 1];
            TestSims.Send(sim, SimCommand.SellWall(clicked.X, clicked.Y));

            Assert.AreEqual(60f - cost + cost * 0.5f, sim.State.BuildBudget);
            foreach (TileCoord t in piece.Tiles) Assert.IsFalse(sim.State.Grid.Get(t).HasWall);
            Assert.AreEqual(piece.Tiles.Length, sim.LastTickEvents.Count);
            foreach (SimEvent e in sim.LastTickEvents) Assert.AreEqual(SimEventKind.WallSold, e.Kind);
            Assert.AreEqual(PlacementKind.WallSold, sim.State.PlacementLog[1].Kind);
            Assert.AreEqual(90, sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost);
        }

        // "A sale during a wave is a no-op while SellDuringWave is off."
        [Test]
        public void SellingWaitsForABuildPhaseUnlessTheConfigAllowsItMidWave()
        {
            foreach (bool allowed in new[] { false, true })
            {
                var config = Quick();
                config.SellDuringWave = allowed;
                // Slow enemies: the wave is still running when the sales are tried.
                Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 2, hp: 1000f, speed: 0.5f)), config);
                TestSims.Send(sim, SimCommand.PlaceShape(3, 0));
                TileCoord wall = sim.State.PlacementLog[0].Tiles[0];
                TestTowers.BuySlot(sim, 0, 8, 2);

                TestSims.Send(sim, SimCommand.StartWaveNow());
                TestSims.Run(sim, 30);
                Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "rig: mid-wave");

                ulong before = sim.ComputeHash();
                TestSims.Send(sim, SimCommand.SellWall(wall.X, wall.Y));
                TestSims.Send(sim, SimCommand.SellTower(8, 2));

                if (allowed)
                {
                    Assert.AreNotEqual(before, sim.ComputeHash());
                    Assert.IsFalse(sim.State.Grid.Get(wall).HasWall);
                    Assert.IsTrue(sim.State.Towers[0].Sold);
                }
                else
                {
                    Assert.AreEqual(before, sim.ComputeHash(), "refused: nothing changed");
                    Assert.IsTrue(sim.State.Grid.Get(wall).HasWall);
                    Assert.IsTrue(sim.State.Towers[0].IsAlive);
                    Assert.AreEqual(2, sim.State.PlacementLog.Count);
                }
            }
        }

        [Test]
        public void SalesWorkInThePrepPhaseAndInAnIntermission()
        {
            Simulation sim = TestSims.Ascii(Field, Trickle(), Quick());
            TestTowers.BuySlot(sim, 0, 4, 2);
            Assert.AreEqual(SimPhase.Prep, sim.State.Phase);
            TestSims.Send(sim, SimCommand.SellTower(4, 2));
            Assert.IsTrue(sim.State.Towers[0].Sold);

            TestTowers.BuySlot(sim, 1, 4, 2);
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase);
            TestSims.Send(sim, SimCommand.SellTower(4, 2));
            Assert.IsTrue(sim.State.Towers[1].Sold);
        }

        [Test]
        public void TheRefundSharesComeFromTheConfig()
        {
            var config = Quick();
            config.SellRefund = 1f;
            config.WallSellRefund = 0.25f;
            Simulation sim = TestSims.Ascii(Field, Trickle(), config);

            TowerState tower = TestTowers.BuySlot(sim, 0, 4, 2);
            TestSims.Send(sim, SimCommand.SellTower(4, 2));
            Assert.AreEqual(60f, sim.State.BuildBudget, "the whole price back");
            Assert.AreEqual((float)tower.PricePaid, sim.State.PlacementLog[1].Refund);

            TestSims.Send(sim, SimCommand.PlaceShape(3, 0));
            PlacementRecord piece = sim.State.PlacementLog[2];
            TestSims.Send(sim, SimCommand.SellWall(piece.Tiles[0].X, piece.Tiles[0].Y));
            Assert.AreEqual(60f - piece.Cost + piece.Cost * 0.25f, sim.State.BuildBudget);

            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { SellRefund = 1.5f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { SellRefund = -0.1f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { WallSellRefund = 2f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { WallSellRefund = float.NaN }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { RerollCost = float.NaN }.Validate());
        }

        // The real thing: enemies chew two tiles of a square piece away, and what is
        // left sells for half of the piece's refund.
        [Test]
        public void EnemiesChewHalfAPieceAwayAndTheRestSellsForHalf()
        {
            // Two rows; a 2x2 piece across both leaves no way round.
            AsciiFixture f = AsciiMap.Parse(@"
                S . . . . . . . . C
                . . . . . . . . . .");
            var square = new[] { Shape("O", 7, 0, 0, 1, 0, 0, 1, 1, 1) };
            var planner = new FixedPlanner(FixedPlanner.Group(count: 3, hp: 10f, speed: 5f, digRate: 4f));
            var sim = new Simulation(Quick(), f.Map, square, new TowerDef[0], 1, planner);

            TestSims.Send(sim, SimCommand.PlaceShape(4, 0));
            Assert.AreEqual(53f, sim.State.BuildBudget);
            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(2, TestTowers.Count(events, SimEventKind.WallBreached), "rig: they dug straight through, two tiles");

            int standing = 0;
            foreach (TileCoord t in sim.State.PlacementLog[0].Tiles) if (sim.State.Grid.Get(t).PieceId == 0) standing++;
            Assert.AreEqual(2, standing);

            float before = sim.State.BuildBudget;
            TileCoord left = default(TileCoord);
            foreach (TileCoord t in sim.State.PlacementLog[0].Tiles) if (sim.State.Grid.Get(t).PieceId == 0) left = t;
            TestSims.Send(sim, SimCommand.SellWall(left.X, left.Y));

            Assert.AreEqual(1.75f, sim.State.BuildBudget - before, "7 x 0.5 x 2/4");
            foreach (TileCoord t in sim.State.PlacementLog[0].Tiles) Assert.IsFalse(sim.State.Grid.Get(t).HasWall);
            Assert.AreEqual(2, sim.State.PlacementLog[1].Tiles.Length);
        }

        [Test]
        public void ATowerThatWasChewedThroughCannotBeSold()
        {
            var blocker = TestTowers.Gun(damage: 0f, range: 0f);
            blocker.TowerHealth = 0.5f;
            Simulation sim = TestSims.Ascii("S . . . . . . . . C", new FixedPlanner(FixedPlanner.Group(count: 2, hp: 10f, speed: 5f)), Quick(), towers: new[] { blocker });
            TestTowers.BuySlot(sim, 0, 5, 0);
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.IsFalse(sim.State.Towers[0].IsAlive, "rig: destroyed");
            Assert.IsFalse(sim.State.Towers[0].Sold);

            ulong before = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.SellTower(5, 0));
            Assert.AreEqual(before, sim.ComputeHash());
        }

        // ================================================================ state plumbing

        [Test]
        public void TheStateHashSeesWhatSellingTouchesAndACloneSellsOnItsOwn()
        {
            Simulation sim = TestSims.Ascii(Field, Trickle(), Quick());
            TestSims.Send(sim, SimCommand.PlaceShape(3, 0));
            TowerState tower = TestTowers.BuySlot(sim, 0, 8, 2);
            SimNode wallTile = sim.State.Grid.Get(sim.State.PlacementLog[0].Tiles[0]);
            PlacementRecord row = sim.State.PlacementLog[0];

            ulong baseline = sim.ComputeHash();
            void Changes(string what, System.Action change, System.Action undo)
            {
                change();
                Assert.AreNotEqual(baseline, sim.ComputeHash(), what + " is not in the state hash");
                undo();
                Assert.AreEqual(baseline, sim.ComputeHash(), what + ": undo did not restore the state");
            }

            Changes("tile PieceId", () => wallTile.PieceId = 5, () => wallTile.PieceId = 0);
            Changes("tower Sold", () => tower.Sold = true, () => tower.Sold = false);
            Changes("log row PieceId", () => row.PieceId = 3, () => row.PieceId = 0);
            Changes("log row Cost", () => row.Cost += 1f, () => row.Cost -= 1f);
            Changes("log row Refund", () => row.Refund += 1f, () => row.Refund -= 1f);

            Simulation copy = sim.Clone(Trickle());
            Assert.AreEqual(baseline, copy.ComputeHash());
            Assert.AreEqual(0, copy.State.Grid.Get(row.Tiles[0]).PieceId, "the clone's tiles know their piece");

            TestSims.Send(copy, SimCommand.SellWall(row.Tiles[0].X, row.Tiles[0].Y));
            TestSims.Send(copy, SimCommand.SellTower(8, 2));
            Assert.IsFalse(copy.State.Grid.Get(row.Tiles[0]).HasWall);
            Assert.IsTrue(copy.State.Towers[0].Sold);
            Assert.AreEqual(baseline, sim.ComputeHash(), "the original still has both");
            Assert.IsTrue(wallTile.HasWall);
            Assert.IsFalse(tower.Sold);
            Assert.AreEqual(2, sim.State.PlacementLog.Count);
            Assert.AreEqual(4, copy.State.PlacementLog.Count);
        }
    }
}
