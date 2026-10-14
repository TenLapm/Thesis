using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The fourth round (2026-10-13): few, heavy actions. A generated map with a ring
    // nothing is built on, the core placed by the player, waves that come in at entry
    // points chosen wave by wave, walls from a stock, a limited number of moves, a
    // bench, and duplicate cards merged into towers. All of it is off in a default
    // SimConfig.
    public class RaidRulesTests
    {
        // 9 x 5, open. S is (0,2), C is (8,2).
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        private static Simulation New(SimConfig config, ulong seed = 1, TowerDef[] towers = null)
        {
            MapData map = TestMaps.Parse(Open).Map;
            return new Simulation(config, map, OneTile(), towers ?? new[] { TestTowers.Gun(cost: 5) }, seed, new EscalationPlanner(config, map));
        }

        // The game's wall: one tile.
        private static ShapeDef[] OneTile()
        {
            return new[] { new ShapeDef { Name = "Wall", BuildCost = 1, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } } };
        }

        private static SimConfig Config()
        {
            return new SimConfig { ShopOffers = 9, StartBudget = 100f, CoreMaxHp = 100000, AgentsPerWave = 3, WaitForStart = true };
        }

        private static void PlayOneWave(Simulation sim)
        {
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
        }

        // ------------------------------------------------------------------ the generated map

        [Test]
        public void TheSameSeedGivesTheSameMapAndAnotherSeedAnother()
        {
            MapData a = MapGenerator.Generate(7);
            MapData b = MapGenerator.Generate(7);
            MapData c = MapGenerator.Generate(8);
            Assert.AreEqual(a.ToJson(), b.ToJson());
            Assert.AreNotEqual(string.Join("", a.Rows), string.Join("", c.Rows));
            Assert.AreEqual(32, a.Width);
            Assert.AreEqual(32, a.Height);
            Assert.AreEqual(2, a.BuildMargin);
        }

        [Test]
        public void AGeneratedMapHasAnOpenRingRocksInsideAndEveryOpenTileReachable()
        {
            for (ulong seed = 1; seed <= 25; seed++)
            {
                MapData map = MapGenerator.Generate(seed);
                int rocks = 0;
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        if (map.IsWalkable(x, y)) continue;
                        rocks++;
                        Assert.GreaterOrEqual(map.EdgeDistance(x, y), map.BuildMargin + 1, "seed " + seed + ": a rock at " + x + "," + y + " is in or against the ring");
                    }
                }
                Assert.That(rocks, Is.InRange(20, 160), "seed " + seed + ": " + rocks + " rocks");
                Assert.Greater(map.Spawns.Length, 20, "entry points all round the edge");
                foreach (TileCoord entry in map.Spawns) Assert.AreEqual(0, map.EdgeDistance(entry.X, entry.Y), "an entry is on the outermost line");

                // Wherever the core is put, every open tile can walk to it: the map has no pockets.
                var grid = new SimGrid(map);
                new FlowField().Generate(grid, map.Core);
                for (int i = 0; i < grid.NodeCount; i++)
                {
                    SimNode n = grid.ByIndex(i);
                    if (n.IsWalkable) Assert.AreNotEqual(SimNode.Infinity, n.BestCost, "seed " + seed + ": tile " + n.X + "," + n.Y + " is cut off");
                }
            }
        }

        [Test]
        public void NothingIsBuiltOnTheRing()
        {
            MapData map = MapGenerator.Generate(3);
            var config = new SimConfig { ShopOffers = 4, StartBudget = 100f };
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), new[] { TestTowers.Gun(cost: 5) }, 1, new EscalationPlanner(config, map));
            var grid = sim.State.Grid;

            Assert.IsFalse(Placement.CanPlace(grid, sim.Map, new[] { new TileCoord(0, 10) }));
            Assert.IsFalse(Placement.CanPlace(grid, sim.Map, new[] { new TileCoord(1, 10) }));
            Assert.IsFalse(Placement.CanPlace(grid, sim.Map, new[] { new TileCoord(30, 31) }));
            TestSims.Send(sim, SimCommand.PlaceTower(0, 1, 10));
            Assert.AreEqual(0, sim.State.Towers.Count, "a tower is refused there too");

            // The first open tile inside the ring can be built on.
            TileCoord inside = default(TileCoord);
            for (int y = 2; y < 30 && inside.X == 0; y++) if (map.IsWalkable(2, y) && new TileCoord(2, y) != map.Core) inside = new TileCoord(2, y);
            Assert.IsTrue(Placement.CanPlace(grid, sim.Map, new[] { inside }));
        }

        // ------------------------------------------------------------------ the core

        [Test]
        public void TheCoreCanBePlacedBeforeTheFirstWaveOnlyAndTheFieldFollows()
        {
            MapData map = MapGenerator.Generate(5);
            var config = new SimConfig { CorePlacement = true, WaitForStart = true, PlanAhead = true, CoreMaxHp = 100000, AgentsPerWave = 3 };
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), new[] { TestTowers.Gun() }, 1, new RaidPlanner(config, map));
            sim.Tick();
            Assert.IsNotNull(sim.State.NextPlan);

            TileCoord old = sim.State.Core;
            TileCoord target = default(TileCoord);
            for (int d = 3; d < 9 && target.X == 0; d++) if (map.IsWalkable(old.X - d, old.Y + 2)) target = new TileCoord(old.X - d, old.Y + 2);
            Assert.AreNotEqual(0, target.X, "rig: an open tile near the middle");

            TestSims.Send(sim, SimCommand.PlaceCore(target.X, target.Y));
            Assert.AreEqual(target, sim.State.Core);
            Assert.AreEqual(target, sim.Map.Core);
            Assert.AreEqual(old, sim.InitialMap.Core, "the map the run began with is untouched");
            Assert.AreEqual(0, sim.State.Grid.Get(target).BestCost, "the field leads to the new core");
            Assert.IsNull(sim.State.NextPlan, "the wave is planned again for the new core");
            sim.Tick();
            Assert.IsNotNull(sim.State.NextPlan);

            // Refused: the ring, too close to the edge, a rock, off the map.
            ulong before = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.PlaceCore(1, 10));
            TestSims.Send(sim, SimCommand.PlaceCore(4, 10));
            TestSims.Send(sim, SimCommand.PlaceCore(99, 3));
            Assert.AreEqual(before, sim.ComputeHash());

            // And not once a wave has been played.
            PlayOneWave(sim);
            TestSims.Send(sim, SimCommand.PlaceCore(old.X, old.Y));
            Assert.AreEqual(target, sim.State.Core);

            // Off by default.
            Simulation plain = New(Config());
            TestSims.Send(plain, SimCommand.PlaceCore(4, 2));
            Assert.AreEqual(new TileCoord(8, 2), plain.State.Core);
        }

        // ------------------------------------------------------------------ entry points

        [Test]
        public void TheRaidPlannerSendsAWaveInAtMoreEntriesLaterAndKeepsThemApartAndFarFromTheCore()
        {
            MapData map = MapGenerator.Generate(9);
            var config = new SimConfig { PlanAhead = true, WaitForStart = true, CoreMaxHp = 100000, AgentsPerWave = 9 };
            var planner = new RaidPlanner(config, map);
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), new[] { TestTowers.Gun() }, 1, planner);

            var seen = new HashSet<int>();
            for (int wave = 1; wave <= 9; wave++)
            {
                sim.Tick();
                WavePlan plan = sim.State.NextPlan;
                var entries = new List<int>();
                int total = 0;
                foreach (AgentGroup g in plan.Groups)
                {
                    if (!entries.Contains(g.SpawnIndex)) entries.Add(g.SpawnIndex);
                    total += g.Count;
                }
                Assert.AreEqual(RaidPlanner.EntriesFor(wave), entries.Count, "wave " + wave);
                Assert.AreEqual(planner.Compose(wave).TotalAgents(), total, "wave " + wave + ": as many enemies as the wave is made of, shared out");

                foreach (int e in entries)
                {
                    seen.Add(e);
                    Assert.GreaterOrEqual(sim.State.Grid.Get(map.Spawns[e]).BestCost, (int)(config.MinEntryRouteTiles * 10f), "wave " + wave + ": an entry too near the core");
                    foreach (int other in entries)
                    {
                        if (other == e) continue;
                        int dx = System.Math.Abs(map.Spawns[e].X - map.Spawns[other].X), dy = System.Math.Abs(map.Spawns[e].Y - map.Spawns[other].Y);
                        Assert.GreaterOrEqual(System.Math.Max(dx, dy), config.MinEntrySpacingTiles, "wave " + wave + ": two entries side by side");
                    }
                }
                PlayOneWave(sim);
            }
            Assert.Greater(seen.Count, 5, "the waves did not all come from one place");
        }

        // ------------------------------------------------------------------ the wall stock

        [Test]
        public void WallsComeFromAStockAndOldOnesComeUpOnlySoOften()
        {
            SimConfig config = Config();
            config.WallStock = 5;
            config.WallStockPerWave = 2;
            config.WallRelayPerWave = 1;
            Simulation sim = New(config);
            SimState s = sim.State;

            Assert.AreEqual(5, s.WallsLeft);
            for (int x = 1; x <= 6; x++) TestSims.Send(sim, SimCommand.PlaceShape(x, 0));
            Assert.AreEqual(0, s.WallsLeft, "five tiles, and the sixth was refused");
            Assert.AreEqual(Occupant.None, s.Grid[6, 0].Occupant);
            Assert.AreEqual(100f, s.BuildBudget, "a wall from the stock costs no budget");

            // Laid in this build phase: up again for nothing, as often as wanted.
            TestSims.Send(sim, SimCommand.SellWall(1, 0));
            TestSims.Send(sim, SimCommand.SellWall(2, 0));
            Assert.AreEqual(2, s.WallsLeft);
            Assert.AreEqual(1, s.RelaysLeft);
            Assert.AreEqual(100f, s.BuildBudget, "and pays nothing");

            PlayOneWave(sim);
            Assert.AreEqual(4, s.WallsLeft, "two more after the wave");

            // Now they are old walls: one pick-up a wave.
            TestSims.Send(sim, SimCommand.SellWall(3, 0));
            Assert.AreEqual(5, s.WallsLeft);
            Assert.AreEqual(0, s.RelaysLeft);
            TestSims.Send(sim, SimCommand.SellWall(4, 0));
            Assert.AreEqual(Occupant.Wall, s.Grid[4, 0].Occupant, "the second old wall stays");

            // A wall laid now is fresh again.
            TestSims.Send(sim, SimCommand.PlaceShape(1, 4));
            TestSims.Send(sim, SimCommand.SellWall(1, 4));
            Assert.AreEqual(Occupant.None, s.Grid[1, 4].Occupant);
        }

        [Test]
        public void WallTilesAreAlsoBoughtAndComeOnACard()
        {
            SimConfig config = Config();
            config.WallStock = 1;
            config.WallPackSize = 3;
            config.WallPackPrice = 5f;
            config.WallCardWeight = 50f;
            config.WallCardAmount = 4;
            Simulation sim = New(config);

            TestSims.Send(sim, SimCommand.BuyWalls());
            Assert.AreEqual(4, sim.State.WallsLeft);
            Assert.AreEqual(95f, sim.State.BuildBudget);

            int slot = -1;
            for (int i = 0; i < sim.State.Shop.Offers.Length; i++) if (sim.State.Shop.Offers[i].Kind == CardKind.Wall) slot = i;
            Assert.GreaterOrEqual(slot, 0, "rig: with this weight there is a wall card");
            TestSims.Send(sim, SimCommand.TakeCoin(slot));
            Assert.AreEqual(8, sim.State.WallsLeft);
            Assert.IsTrue(sim.State.Shop.Offers[slot].Bought);

            // Without a stock there is no wall card and nothing to buy.
            Simulation plain = New(Config());
            foreach (TowerOffer o in plain.State.Shop.Offers) Assert.AreNotEqual(CardKind.Wall, o.Kind);
            TestSims.Send(plain, SimCommand.BuyWalls());
            Assert.AreEqual(100f, plain.State.BuildBudget);
        }

        // ------------------------------------------------------------------ moves

        [Test]
        public void ATowerOnTheBoardIsTouchedOnlySoOftenAWaveAndAnExtraMoveIsBought()
        {
            SimConfig config = Config();
            config.MovesPerWave = 2;
            config.MoveTowers = true;
            config.ExtraMovePrice = 10f;
            config.ExtraMovePriceStep = 10f;
            Simulation sim = New(config);
            SimState s = sim.State;

            TowerState a = TestTowers.BuySlot(sim, 0, 3, 1);
            TestTowers.BuySlot(sim, 1, 4, 1);
            TestTowers.BuySlot(sim, 2, 5, 1);
            Assert.AreEqual(2, s.MovesLeft, "placing a card is not a move");

            TestSims.Send(sim, SimCommand.MoveTower(3, 1, 3, 3));
            TestSims.Send(sim, SimCommand.MoveTower(1, 1, 2, 2));
            Assert.AreEqual(1, s.MovesLeft, "a move that did not happen costs nothing");
            TestSims.Send(sim, SimCommand.SellTower(4, 1));
            Assert.AreEqual(0, s.MovesLeft, "selling a tower on the board is a move");

            TestSims.Send(sim, SimCommand.MoveTower(3, 3, 3, 1));
            TestSims.Send(sim, SimCommand.SellTower(5, 1));
            Assert.AreEqual(new TileCoord(3, 3), a.Tile, "no moves left");
            Assert.AreEqual(Occupant.Tower, s.Grid[5, 1].Occupant);

            float budget = s.BuildBudget;
            Assert.AreEqual(10f, sim.ExtraMovePrice);
            TestSims.Send(sim, SimCommand.BuyMove());
            Assert.AreEqual(1, s.MovesLeft);
            Assert.AreEqual(budget - 10f, s.BuildBudget);
            Assert.AreEqual(20f, sim.ExtraMovePrice, "the next one is dearer");
            TestSims.Send(sim, SimCommand.MoveTower(3, 3, 3, 1));
            Assert.AreEqual(new TileCoord(3, 1), a.Tile);

            PlayOneWave(sim);
            Assert.AreEqual(2, s.MovesLeft, "two again after the wave");
            Assert.AreEqual(10f, sim.ExtraMovePrice, "and the price is back");
        }

        [Test]
        public void BeforeTheFirstWaveMovesCostNothingWhenTheConfigSaysSo()
        {
            SimConfig config = Config();
            config.MovesPerWave = 1;
            config.MoveTowers = true;
            config.FreeFirstBuild = true;
            Simulation sim = New(config);
            SimState s = sim.State;

            TowerState a = TestTowers.BuySlot(sim, 0, 3, 1);
            Assert.IsTrue(sim.MovesAreFree);
            for (int i = 0; i < 4; i++) TestSims.Send(sim, SimCommand.MoveTower(a.Tile.X, a.Tile.Y, 3 + (i + 1) % 2, 3));
            Assert.AreEqual(1, s.MovesLeft, "four moves, none counted");

            PlayOneWave(sim);
            Assert.IsFalse(sim.MovesAreFree);
            TestSims.Send(sim, SimCommand.MoveTower(a.Tile.X, a.Tile.Y, 6, 1));
            TestSims.Send(sim, SimCommand.MoveTower(6, 1, 6, 3));
            Assert.AreEqual(new TileCoord(6, 1), a.Tile, "one move a wave from then on");
        }

        [Test]
        public void WithDistinctCardsNoRollRepeatsACardAndTheRollIsStillAFunctionOfTheSeed()
        {
            var config = new SimConfig { DistinctCards = true, CoinCardWeight = 0.3f, GearCardWeight = 0.3f, WallCardWeight = 0.3f, WallStock = 5, RareTowerChance = 0.2f };
            TowerDef[] roster = TestSims.GameTowers();
            GearDef[] gear = GearRoster.Default();
            int repeatsWithout = 0;
            for (int wave = 0; wave < 200; wave++)
            {
                TowerOffer[] a = ShopRoller.Roll(5, wave, 0, roster, 4, config, gear);
                TowerOffer[] b = ShopRoller.Roll(5, wave, 0, roster, 4, config, gear);
                var seen = new HashSet<string>();
                for (int i = 0; i < 4; i++)
                {
                    string what = a[i].Kind == CardKind.Tower ? "t" + a[i].TowerIndex : a[i].Kind == CardKind.Gear ? "g" + a[i].GearIndex : a[i].Kind.ToString();
                    Assert.IsTrue(seen.Add(what), "wave " + wave + ": two cards are " + what);
                    Assert.AreEqual(a[i].Kind, b[i].Kind);
                    Assert.AreEqual(a[i].TowerIndex, b[i].TowerIndex);
                    Assert.AreEqual(a[i].GearIndex, b[i].GearIndex);
                    Assert.AreEqual(a[i].Level, b[i].Level);
                }

                config.DistinctCards = false;
                TowerOffer[] plain = ShopRoller.Roll(5, wave, 0, roster, 4, config, gear);
                config.DistinctCards = true;
                var plainSeen = new HashSet<string>();
                for (int i = 0; i < 4; i++)
                    if (!plainSeen.Add(plain[i].Kind == CardKind.Tower ? "t" + plain[i].TowerIndex : plain[i].Kind == CardKind.Gear ? "g" + plain[i].GearIndex : plain[i].Kind.ToString())) repeatsWithout++;
            }
            Assert.Greater(repeatsWithout, 20, "rig: without the rule, repeats are common");

            // And the cards before the first wave can be made all towers.
            config.FirstRollTowersOnly = true;
            for (int reroll = 0; reroll < 30; reroll++)
                foreach (TowerOffer card in ShopRoller.Roll(5, 0, reroll, roster, 4, config, gear)) Assert.AreEqual(CardKind.Tower, card.Kind);
            bool other = false;
            foreach (TowerOffer card in ShopRoller.Roll(5, 1, 0, roster, 4, config, gear)) if (card.Kind != CardKind.Tower) other = true;
            for (int wave = 2; wave < 20 && !other; wave++) foreach (TowerOffer card in ShopRoller.Roll(5, wave, 0, roster, 4, config, gear)) if (card.Kind != CardKind.Tower) other = true;
            Assert.IsTrue(other, "later rolls still hold coins, gear and walls");
        }

        // ------------------------------------------------------------------ the bench

        [Test]
        public void ACardGoesOntoTheBenchAndABenchedTowerDoesNotStandOrShoot()
        {
            SimConfig config = Config();
            config.BenchSlots = 3;
            config.MovesPerWave = 2;
            config.TowerCap = 1;
            Simulation sim = New(config);
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.BenchCard(0, 1));
            Assert.AreEqual(1, s.Towers.Count);
            TowerState benched = s.Towers[0];
            Assert.IsTrue(benched.OnBench);
            Assert.IsTrue(benched.IsAlive);
            Assert.AreEqual(0, s.Bench[1]);
            Assert.AreEqual(0, TowerCombine.Standing(s.Towers), "it does not count as standing");
            Assert.IsTrue(s.Shop.Offers[0].Bought);
            Assert.AreEqual(95f, s.BuildBudget);

            TestSims.Send(sim, SimCommand.BenchCard(1, 1));
            Assert.AreEqual(1, s.Towers.Count, "the slot is taken");

            // Rearranging the bench is free.
            TestSims.Send(sim, SimCommand.SwapBench(1, 2));
            Assert.AreEqual(TowerBench.Empty, s.Bench[1]);
            Assert.AreEqual(0, s.Bench[2]);
            Assert.AreEqual(2, s.MovesLeft);

            // A wave with the tower on the bench: it fires no shot.
            PlayOneWave(sim);
            Assert.AreEqual(0, benched.Shots);
        }

        [Test]
        public void DeployRecallAndSwapEachCostOneMoveAndRespectTheCap()
        {
            SimConfig config = Config();
            config.BenchSlots = 3;
            config.MovesPerWave = 3;
            config.TowerCap = 1;
            config.GearShopSlots = 1;
            Simulation sim = New(config);
            SimState s = sim.State;

            TowerState first = TestTowers.BuySlot(sim, 0, 3, 1);
            TestSims.Send(sim, SimCommand.BuyShopGear(0, 3, 1));
            TestSims.Send(sim, SimCommand.BenchCard(1, 0));
            TowerState second = s.Towers[1];

            // The board is full (cap 1): the benched tower cannot be put on open ground.
            TestSims.Send(sim, SimCommand.DeployTower(0, 5, 1));
            Assert.IsTrue(second.OnBench);
            Assert.AreEqual(3, s.MovesLeft);

            // But it can change places with the tower that stands: one move.
            TestSims.Send(sim, SimCommand.DeployTower(0, 3, 1));
            Assert.IsFalse(second.OnBench);
            Assert.AreEqual(new TileCoord(3, 1), second.Tile);
            Assert.AreEqual(second.Id, s.Grid[3, 1].TowerId);
            Assert.IsTrue(first.OnBench);
            Assert.AreEqual(first.Id, s.Bench[0]);
            Assert.AreNotEqual(TowerState.NoGear, first.Gear[0], "its gear goes with it");
            Assert.AreEqual(2, s.MovesLeft);

            // Board to an empty bench slot: one move. Then back onto open ground: one more.
            TestSims.Send(sim, SimCommand.RecallTower(3, 1, 1));
            Assert.IsTrue(second.OnBench);
            Assert.AreEqual(Occupant.None, s.Grid[3, 1].Occupant);
            Assert.AreEqual(1, s.MovesLeft);
            TestSims.Send(sim, SimCommand.DeployTower(0, 6, 3));
            Assert.AreEqual(new TileCoord(6, 3), first.Tile);
            Assert.AreEqual(TowerBench.Empty, s.Bench[0]);
            Assert.AreEqual(0, s.MovesLeft);

            TestSims.Send(sim, SimCommand.RecallTower(6, 3, 0));
            Assert.IsFalse(first.OnBench, "no moves left");

            // Selling from the bench is not a move.
            float budget = s.BuildBudget;
            TestSims.Send(sim, SimCommand.SellBenched(1));
            Assert.IsFalse(second.IsAlive);
            Assert.IsTrue(second.Sold);
            Assert.AreEqual(TowerBench.Empty, s.Bench[1]);
            Assert.AreEqual(budget + 2.5f, s.BuildBudget);
        }

        // ------------------------------------------------------------------ merging a duplicate

        [Test]
        public void ADuplicateCardRaisesTheTowerALevelOnTheBoardOrOnTheBench()
        {
            SimConfig config = Config();
            config.FeedDuplicates = true;
            config.BenchSlots = 2;
            TowerDef gun = TestTowers.Gun(cost: 5);
            TowerDef other = TestTowers.Gun(cost: 5, id: "other");
            other.ShopWeight = 0f; // never on a card
            Simulation sim = New(config, towers: new[] { gun, other });
            SimState s = sim.State;

            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            TestSims.Send(sim, SimCommand.FeedCard(1, 3, 1));
            Assert.AreEqual(2, tower.Level);
            Assert.AreEqual(10, tower.PricePaid);
            Assert.AreEqual(1, s.Towers.Count, "the card became a level, not a tower");
            Assert.IsTrue(s.Shop.Offers[1].Bought);
            Assert.AreEqual(1.5f, tower.Damage);

            TestSims.Send(sim, SimCommand.FeedCard(2, 3, 1));
            Assert.AreEqual(3, tower.Level);
            TestSims.Send(sim, SimCommand.FeedCard(3, 3, 1));
            Assert.AreEqual(3, tower.Level, "the top level takes no more");
            Assert.IsFalse(s.Shop.Offers[3].Bought);

            // Onto a benched tower.
            TestSims.Send(sim, SimCommand.BenchCard(3, 0));
            TestSims.Send(sim, SimCommand.FeedCardOnBench(4, 0));
            Assert.AreEqual(2, s.Towers[1].Level);

            // Not on open ground, and not when the rule is off.
            TestSims.Send(sim, SimCommand.FeedCard(5, 6, 3));
            Assert.IsFalse(s.Shop.Offers[5].Bought);
            Simulation plain = New(Config());
            TowerState plainTower = TestTowers.BuySlot(plain, 0, 3, 1);
            TestSims.Send(plain, SimCommand.FeedCard(1, 3, 1));
            Assert.AreEqual(1, plainTower.Level);
        }

        [Test]
        public void ARareCardLiftsACommonTowerToRare()
        {
            SimConfig config = Config();
            config.FeedDuplicates = true;
            config.RareTowerChance = 0.5f;
            Simulation sim = null;
            int common = -1, rare = -1;
            for (ulong seed = 1; seed < 50 && (common < 0 || rare < 0); seed++)
            {
                sim = New(config, seed);
                common = rare = -1;
                for (int i = 0; i < sim.State.Shop.Offers.Length; i++)
                {
                    if (sim.State.Shop.Offers[i].Level == 1 && common < 0) common = i;
                    if (sim.State.Shop.Offers[i].Level == 2 && rare < 0) rare = i;
                }
            }
            Assert.IsTrue(common >= 0 && rare >= 0, "rig: a common and a rare card");

            TowerState tower = TestTowers.BuySlot(sim, common, 3, 1);
            TestSims.Send(sim, SimCommand.FeedCard(rare, 3, 1));
            Assert.AreEqual(2, tower.Level, "the higher of the two, not one above it");
        }

        // ------------------------------------------------------------------ merging three of a kind, by choice

        [Test]
        public void ThreeOfAKindMergeWhenThePlayerDropsOneOnAnotherWhereverTheThirdIs()
        {
            SimConfig config = Config();
            config.FeedDuplicates = true;
            config.MergeCount = 3;
            config.BenchSlots = 2;
            config.MovesPerWave = 2;
            Simulation sim = New(config);
            SimState s = sim.State;

            TowerState a = TestTowers.BuySlot(sim, 0, 3, 1);
            TowerState b = TestTowers.BuySlot(sim, 1, 5, 1);
            Assert.IsFalse(TowerMerge.CanMerge(s, config, b, a), "two are not a set");
            TestSims.Send(sim, SimCommand.MergeTower(5, 1, -1, 3, 1, -1));
            Assert.AreEqual(1, a.Level);
            Assert.IsTrue(b.IsAlive);

            // The third is on the bench. Dropping b on a merges all three into a.
            TestSims.Send(sim, SimCommand.BenchCard(2, 0));
            TowerState c = s.Towers[2];
            Assert.IsTrue(TowerMerge.CanMerge(s, config, b, a));
            TestSims.Send(sim, SimCommand.MergeTower(5, 1, -1, 3, 1, -1));
            Assert.AreEqual(2, a.Level, "the one dropped on gains the level");
            Assert.IsTrue(a.IsAlive);
            Assert.AreEqual(new TileCoord(3, 1), a.Tile);
            Assert.IsFalse(b.IsAlive);
            Assert.IsTrue(b.Combined);
            Assert.IsFalse(c.IsAlive, "the third goes too, from the bench");
            Assert.AreEqual(TowerBench.Empty, s.Bench[0]);
            Assert.AreEqual(Occupant.None, s.Grid[5, 1].Occupant);
            Assert.AreEqual(15, a.PricePaid);
            Assert.AreEqual(1, s.MovesLeft, "one move: towers on the board were touched");
            Assert.AreEqual(PlacementKind.TowerCombined, s.PlacementLog[s.PlacementLog.Count - 1].Kind);

            // A card is one copy of a set: with two commons standing it completes it.
            TowerState d = TestTowers.BuySlot(sim, 3, 6, 3);
            TestSims.Send(sim, SimCommand.FeedCard(5, 6, 3));
            Assert.AreEqual(1, d.Level, "a card and one tower are only two");
            Assert.IsFalse(s.Shop.Offers[5].Bought);
            TowerState e = TestTowers.BuySlot(sim, 4, 7, 3);
            TestSims.Send(sim, SimCommand.FeedCard(5, 6, 3));
            Assert.AreEqual(2, d.Level);
            Assert.IsFalse(e.IsAlive, "the other copy on the board was used up");
            Assert.AreEqual(1, s.MovesLeft, "a card's merge is the pick, not a move");

            // Two rares and no third: nothing.
            TestSims.Send(sim, SimCommand.MergeTower(6, 3, -1, 3, 1, -1));
            Assert.IsTrue(d.IsAlive);
            Assert.AreEqual(2, a.Level);
        }

        [Test]
        public void GearOfASoldOrMergedTowerComesBackAndSpareGearMustBeDealtWithBeforeAWave()
        {
            SimConfig config = Config();
            config.FeedDuplicates = true;
            config.MergeCount = 3;
            config.GearInventorySlots = 1;
            config.GearShopSlots = 3;
            Simulation sim = New(config);
            SimState s = sim.State;
            int price = sim.GearLibrary[s.GearShop.Offers[0].GearIndex].Price;

            // Three towers, a piece on each (bought into the one slot and put on, three times).
            TowerState[] towers = { TestTowers.BuySlot(sim, 0, 3, 1), TestTowers.BuySlot(sim, 1, 4, 1), TestTowers.BuySlot(sim, 2, 5, 1) };
            for (int i = 0; i < 3; i++)
            {
                TestSims.Send(sim, SimCommand.StoreShopGear(i));
                TestSims.Send(sim, SimCommand.EquipGear(0, 3 + i, 1));
                Assert.AreNotEqual(TowerState.NoGear, towers[i].Gear[0], "rig: tower " + i + " carries a piece");
            }

            // Merge: two towers go, their two pieces come back. One fits the inventory, one is spare.
            TestSims.Send(sim, SimCommand.MergeTower(5, 1, -1, 3, 1, -1));
            Assert.AreEqual(2, towers[0].Level);
            Assert.AreNotEqual(TowerState.NoGear, s.GearInventory[0]);
            Assert.AreEqual(1, s.GearOverflow.Count);

            // No wave while a piece waits.
            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(SimPhase.Prep, s.Phase);

            // Put it on the rare, which has a second slot: nothing waits any more.
            TestSims.Send(sim, SimCommand.EquipOverflow(0, 3, 1));
            Assert.AreEqual(0, s.GearOverflow.Count);
            Assert.AreNotEqual(TowerState.NoGear, towers[0].Gear[1]);

            // Selling the tower returns both its pieces: the inventory is full, so both are spare.
            float before = s.BuildBudget;
            TestSims.Send(sim, SimCommand.SellTower(3, 1));
            Assert.IsFalse(towers[0].IsAlive);
            Assert.AreEqual(2, s.GearOverflow.Count);
            TestSims.Send(sim, SimCommand.SellOverflow(0));
            Assert.AreEqual(1, s.GearOverflow.Count);
            Assert.AreEqual(before + 7.5f + price * 0.5f, s.BuildBudget, "the tower's half, and half the piece's price");

            // A slot that becomes free takes a spare piece by itself.
            TowerState fresh = TestTowers.BuySlot(sim, 3, 6, 3);
            TestSims.Send(sim, SimCommand.EquipGear(0, 6, 3));
            Assert.AreEqual(0, s.GearOverflow.Count);
            Assert.AreNotEqual(TowerState.NoGear, s.GearInventory[0]);
            Assert.AreNotEqual(TowerState.NoGear, fresh.Gear[0]);

            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(SimPhase.Spawning, s.Phase);
        }

        // ------------------------------------------------------------------ one upgrade, chosen

        [Test]
        public void ATowerIsUpgradedOnceForBudgetWithTheBuffThePlayerChooses()
        {
            SimConfig config = Config();
            config.ChoiceUpgradeCost = 12f;
            config.BenchSlots = 1;
            Simulation sim = New(config);
            SimState s = sim.State;

            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            float range = tower.RangeTiles;
            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 2)); // Reach
            Assert.AreEqual(2, tower.Upgrade);
            Assert.AreEqual(range + 1.5f, tower.RangeTiles);
            Assert.AreEqual(83f, s.BuildBudget);
            Assert.AreEqual(17, tower.PricePaid, "a sale gives half of it back with the tower's");

            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 0));
            Assert.AreEqual(2, tower.Upgrade, "once only");
            Assert.AreEqual(83f, s.BuildBudget);

            // Each choice does what it says, also on the bench.
            TestSims.Send(sim, SimCommand.BenchCard(1, 0));
            TowerState benched = s.Towers[1];
            TestSims.Send(sim, SimCommand.ChooseUpgradeOnBench(0, 3)); // Volley
            Assert.AreEqual(2, benched.Targets);
            TowerState power = TestTowers.BuySlot(sim, 2, 5, 1);
            TestSims.Send(sim, SimCommand.ChooseUpgrade(5, 1, 0));
            Assert.AreEqual(1.5f, power.Damage);
            TowerState rapid = TestTowers.BuySlot(sim, 3, 6, 1);
            int interval = rapid.FireIntervalTicks;
            TestSims.Send(sim, SimCommand.ChooseUpgrade(6, 1, 1));
            Assert.AreEqual((int)(interval * 0.7f), rapid.FireIntervalTicks);

            // Not a choice that does not exist, not without the budget, not when off.
            TowerState last = TestTowers.BuySlot(sim, 4, 7, 1);
            TestSims.Send(sim, SimCommand.ChooseUpgrade(7, 1, 9));
            Assert.AreEqual(TowerUpgradeChoices.None, last.Upgrade);
            Simulation plain = New(Config());
            TowerState plainTower = TestTowers.BuySlot(plain, 0, 3, 1);
            TestSims.Send(plain, SimCommand.ChooseUpgrade(3, 1, 0));
            Assert.AreEqual(TowerUpgradeChoices.None, plainTower.Upgrade);
        }

        // ------------------------------------------------------------------ which enemy a tower shoots

        [Test]
        public void ATowerShootsTheEnemyItsOrderNames()
        {
            // Three enemies in range of a tower at (4,2): one nearly at the core, one tough, one hurt.
            AsciiFixture f = AsciiMap.Parse(Open);
            var grid = new SimGrid(f.Map);
            new FlowField().Generate(grid, f.Map.Core);
            TowerState tower = TestTowers.StateOn(f, 0, TestTowers.Gun(range: 6f), 4, 2);
            var live = new List<AgentState>
            {
                TestTowers.Agent(0, grid[3, 2].Position, hp: 50f),
                TestTowers.Agent(1, grid[6, 2].Position, hp: 20f),
                TestTowers.Agent(2, grid[2, 2].Position, hp: 5f),
            };
            SimNode core = grid.Get(f.Map.Core);

            Assert.AreEqual(1, Targeting.Pick(TargetingMode.First, tower, grid, core, live), "the one closest to the core");
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.Strongest, tower, grid, core, live));
            Assert.AreEqual(2, Targeting.Pick(TargetingMode.Weakest, tower, grid, core, live));
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.Nearest, tower, grid, core, live), "one tile from the tower");

            // A second bolt goes to the next in the same order.
            Assert.AreEqual(1, Targeting.Pick(TargetingMode.Strongest, tower, grid, core, live, new[] { 0 }, 1));
        }

        [Test]
        public void ThePlayerSetsATowersOrderWhenTheConfigAllowsIt()
        {
            SimConfig config = Config();
            config.ChooseTargeting = true;
            Simulation sim = New(config);
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            Assert.AreEqual(TargetingMode.First, tower.Mode);

            ulong before = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.SetTargeting(3, 1, TargetingMode.Weakest));
            Assert.AreEqual(TargetingMode.Weakest, tower.Mode);
            Assert.AreNotEqual(before, sim.ComputeHash(), "the order is part of the state");

            Simulation plain = New(Config());
            TowerState plainTower = TestTowers.BuySlot(plain, 0, 3, 1);
            TestSims.Send(plain, SimCommand.SetTargeting(3, 1, TargetingMode.Weakest));
            Assert.AreEqual(TargetingMode.First, plainTower.Mode);
        }

        // ------------------------------------------------------------------ more enemy types

        [Test]
        public void TheNewEnemyTypesAreVariantsWithTheirOwnNamesAndSomeShrugOffADamageType()
        {
            var config = new SimConfig();
            MapData map = TestMaps.Parse(Open).Map;
            AgentGroup basic = new EscalationPlanner(config, map).PlanFor(3).Groups[0];

            var names = new HashSet<string>();
            foreach (EnemyArchetype type in EnemyArchetypes.Everything())
            {
                Assert.IsTrue(names.Add(type.Id), "two types are called " + type.Id);
                AgentGroup g = type.Apply(basic, 1f);
                Assert.Greater(g.Count, 0);
                Assert.Greater(g.Hp, 0f);
                Assert.Greater(g.MoveSpeed, 0f);
            }
            Assert.AreEqual(10, names.Count);

            Assert.AreEqual(0.35f, EnemyArchetypes.Armored().Apply(basic, 1f).Resist[(int)DamageType.Physical]);
            Assert.AreEqual(1f, EnemyArchetypes.Armored().Apply(basic, 1f).Resist[(int)DamageType.Fire]);
            Assert.AreEqual(0.25f, EnemyArchetypes.Ember().Apply(basic, 1f).Resist[(int)DamageType.Fire]);
            Assert.AreEqual(0f, EnemyArchetypes.Frostborn().Apply(basic, 1f).Resist[(int)DamageType.Frost], "immune");
            Assert.AreEqual(MovementClass.Flying, EnemyArchetypes.Bats().Apply(basic, 1f).Movement);
            Assert.AreEqual(basic.Hp * 4f, EnemyArchetypes.Brute().Apply(basic, 1f).Hp);
        }

        [Test]
        public void TheRaidPlannersCycleSendsEveryTypeAndEveryPlanIsValid()
        {
            MapData map = MapGenerator.Generate(4);
            var config = new SimConfig();
            var planner = new RaidPlanner(config, map);
            var seen = new HashSet<string>();
            for (int wave = 1; wave <= 2 * RaidPlanner.CycleLength; wave++)
            {
                WavePlan plan = planner.Compose(wave);
                Assert.IsNull(PlanValidator.Check(plan, wave, map, config, null), "wave " + wave);
                foreach (AgentGroup g in plan.Groups) seen.Add(g.Archetype);
            }
            foreach (EnemyArchetype type in EnemyArchetypes.Everything()) CollectionAssert.Contains(seen, type.Id);
        }

        // ------------------------------------------------------------------ any wall comes up

        [Test]
        public void WithFreeWallPickupAnyWallComesUpInABuildPhase()
        {
            SimConfig config = Config();
            config.WallStock = 4;
            config.WallRelayPerWave = 0;
            config.FreeWallPickup = true;
            Simulation sim = New(config);
            SimState s = sim.State;
            for (int x = 1; x <= 4; x++) TestSims.Send(sim, SimCommand.PlaceShape(x, 0));
            PlayOneWave(sim);

            for (int x = 1; x <= 4; x++) TestSims.Send(sim, SimCommand.SellWall(x, 0));
            Assert.AreEqual(4, s.WallsLeft, "all four old walls came up, with no pick-ups to spend");
            Assert.AreEqual(Occupant.None, s.Grid[2, 0].Occupant);
        }

        // ------------------------------------------------------------------ the gear inventory

        [Test]
        public void GearIsOwnedInAnInventoryAndGoesOnAndOffTowersForNothing()
        {
            SimConfig config = Config();
            config.GearInventorySlots = 2;
            config.GearShopSlots = 3;
            config.BenchSlots = 1;
            Simulation sim = New(config);
            SimState s = sim.State;
            int price = sim.GearLibrary[s.GearShop.Offers[0].GearIndex].Price;

            TestSims.Send(sim, SimCommand.StoreShopGear(0));
            TestSims.Send(sim, SimCommand.StoreShopGear(1));
            Assert.AreEqual(s.GearShop.Offers[0].GearIndex, s.GearInventory[0]);
            Assert.AreEqual(s.GearShop.Offers[1].GearIndex, s.GearInventory[1]);
            Assert.AreEqual(100f - 2 * price, s.BuildBudget);
            TestSims.Send(sim, SimCommand.StoreShopGear(2));
            Assert.IsFalse(s.GearShop.Offers[2].Bought, "the inventory is full");
            Assert.AreEqual(100f - 2 * price, s.BuildBudget);

            // Onto a tower on the board, and onto one on the bench.
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            TestSims.Send(sim, SimCommand.BenchCard(1, 0));
            TowerState benched = s.Towers[1];
            float budget = s.BuildBudget;
            int piece = s.GearInventory[0];
            TestSims.Send(sim, SimCommand.EquipGear(0, 3, 1));
            Assert.AreEqual(piece, tower.Gear[0]);
            Assert.AreEqual(TowerState.NoGear, s.GearInventory[0]);
            TestSims.Send(sim, SimCommand.EquipGearOnBench(1, 0));
            Assert.AreNotEqual(TowerState.NoGear, benched.Gear[0]);
            Assert.AreEqual(budget, s.BuildBudget, "putting gear on costs nothing");

            // A common tower has one slot: a second piece stays where it is.
            TestSims.Send(sim, SimCommand.UnequipGearOnBench(0, 0));
            Assert.AreEqual(TowerState.NoGear, benched.Gear[0]);
            int back = System.Array.FindIndex(s.GearInventory, g => g != TowerState.NoGear);
            TestSims.Send(sim, SimCommand.EquipGear(back, 3, 1));
            Assert.AreNotEqual(TowerState.NoGear, s.GearInventory[back], "no free slot on that tower");

            // Off again.
            TestSims.Send(sim, SimCommand.UnequipGear(3, 1, 0));
            Assert.AreEqual(TowerState.NoGear, tower.Gear[0]);
            Assert.AreEqual(budget, s.BuildBudget);
            Assert.AreEqual(5, tower.PricePaid, "the tower's own price is untouched");

            // Off by default: nothing can be stored.
            SimConfig plainConfig = Config();
            plainConfig.GearShopSlots = 1;
            Simulation plain = New(plainConfig);
            TestSims.Send(plain, SimCommand.StoreShopGear(0));
            Assert.IsFalse(plain.State.GearShop.Offers[0].Bought);
        }

        [Test]
        public void TheGearShopCanBeRolledAgainForAPriceThatRises()
        {
            SimConfig config = Config();
            config.GearShopSlots = 3;
            config.GearShopRefreshWaves = 2;
            config.GearShopRerollCost = 3f;
            config.GearShopRerollStep = 2f;
            Simulation sim = New(config);
            SimState s = sim.State;

            ShopState first = s.GearShop;
            Assert.AreEqual(3f, sim.GearRerollPrice);
            TestSims.Send(sim, SimCommand.RerollGearShop());
            Assert.AreNotSame(first, s.GearShop);
            Assert.AreEqual(97f, s.BuildBudget);
            Assert.AreEqual(1, s.GearShop.RerollCount);
            Assert.AreEqual(0, s.GearShop.WaveIndex, "the same stock number, rolled again");
            Assert.AreEqual(5f, sim.GearRerollPrice, "dearer the second time");
            foreach (TowerOffer item in s.GearShop.Offers) Assert.IsFalse(item.Bought);

            // The reroll is a function of the seed, the stock and the reroll count.
            TowerOffer[] again = ShopRoller.RollGearShop(sim.RngSeed, 0, sim.GearLibrary, 3, 1);
            for (int i = 0; i < 3; i++) Assert.AreEqual(again[i].GearIndex, s.GearShop.Offers[i].GearIndex);

            // The next free restock puts the price back.
            PlayOneWave(sim);
            PlayOneWave(sim);
            Assert.AreEqual(1, s.GearShop.WaveIndex);
            Assert.AreEqual(3f, sim.GearRerollPrice);

            // Off by default.
            SimConfig plainConfig = Config();
            plainConfig.GearShopSlots = 3;
            Simulation plain = New(plainConfig);
            TestSims.Send(plain, SimCommand.RerollGearShop());
            Assert.AreEqual(100f, plain.State.BuildBudget);
        }

        // ------------------------------------------------------------------ the commands survive the file

        [Test]
        public void TheNewCommandsSurviveTheReplayFile()
        {
            SimCommand[] commands =
            {
                SimCommand.PlaceCore(12, 19), SimCommand.BuyWalls(), SimCommand.BuyMove(), SimCommand.BenchCard(2, 3), SimCommand.DeployTower(1, 7, 8),
                SimCommand.RecallTower(7, 8, 2), SimCommand.SwapBench(0, 3), SimCommand.SellBenched(2), SimCommand.FeedCard(1, 4, 5), SimCommand.FeedCardOnBench(3, 2),
                SimCommand.StoreGearCard(2), SimCommand.StoreShopGear(1), SimCommand.EquipGear(3, 4, 5), SimCommand.EquipGearOnBench(2, 1),
                SimCommand.UnequipGear(4, 5, 1), SimCommand.UnequipGearOnBench(2, 1),
                SimCommand.MergeTower(3, 4, -1, 6, 7, -1), SimCommand.MergeTower(0, 0, 2, 6, 7, -1), SimCommand.MergeTower(3, 4, -1, 0, 0, 1), SimCommand.MergeTower(0, 0, 0, 0, 0, 3),
                SimCommand.EquipOverflow(1, 4, 5), SimCommand.EquipOverflowOnBench(0, 2), SimCommand.SellOverflow(1), SimCommand.RerollGearShop(),
                SimCommand.ChooseUpgrade(4, 5, 2), SimCommand.ChooseUpgradeOnBench(1, 3), SimCommand.SetTargeting(4, 5, TargetingMode.Weakest), SimCommand.SetTargetingOnBench(2, TargetingMode.Nearest),
            };
            foreach (SimCommand c in commands)
            {
                SimCommand back = ReplayCommand.From(5, c).ToCommand();
                Assert.AreEqual(c.ToString(), back.ToString());
                Assert.AreEqual(c.Kind, back.Kind);
                Assert.AreEqual(c.X, back.X);
                Assert.AreEqual(c.Y, back.Y);
                Assert.AreEqual(c.A, back.A);
                Assert.AreEqual(c.B, back.B);
                Assert.AreEqual(c.C, back.C);
            }
        }

        // ------------------------------------------------------------------ a whole game

        [Test]
        public void AGameOfTheFourthRoundReplaysOnEveryTickAndItsTelemetryIsMadeAgain()
        {
            EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
            {
                Config = PinnedEpisodes.RaidConfig(),
                Map = MapGenerator.Generate(11),
                Shapes = TestSims.GameShapes(),
                Towers = TestSims.GameTowers(),
                Seed = 11,
                Planner = RaidPlanner.Id,
                Policy = Registry.CreatePolicy(RaidPolicy.Id),
                MaxWaves = 10,
                RecordTickHashes = true,
            });
            Assert.AreEqual(10, run.WavesResolved);

            var commands = new HashSet<string>();
            foreach (ReplayCommand c in run.Replay.Commands) commands.Add(c.Cmd);
            foreach (string wanted in new[] { "PlaceCore", "PlaceShape", "SellWall", "PlaceTower", "BenchCard", "DeployTower", "StoreShopGear", "EquipGear" })
                CollectionAssert.Contains(commands, wanted, "the scripted player used " + wanted);

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());

            List<WaveRecord> rebuilt = ReplayRunner.Telemetry(file);
            Assert.AreEqual(run.Telemetry.Count, rebuilt.Count);
            for (int i = 0; i < rebuilt.Count; i++) Assert.AreEqual(run.Telemetry[i].ToJsonLine(), rebuilt[i].ToJsonLine(), "row " + i);
        }

        // ------------------------------------------------------------------ towers on walls and rocks (2026-10-14)

        // 9 x 5 with a rock at (4,2), on the straight line. S is (0,2), C is (8,2).
        private const string Rocky = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . X . . . C
            . . . . . . . . .
            . . . . . . . . .";

        [Test]
        public void WithTowersOnWallsATowerStandsOnAWallOrARockAndTheWallComesBackWhenItLeaves()
        {
            SimConfig config = Config();
            config.TowersOnWalls = true;
            config.WallStock = 4;
            config.FreeWallPickup = true;
            config.MoveTowers = true;
            MapData map = TestMaps.Parse(Rocky).Map;
            var sim = new Simulation(config, map, OneTile(), new[] { TestTowers.Gun(cost: 5) }, 1, new EscalationPlanner(config, map));
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.PlaceTower(0, 3, 1));
            Assert.AreEqual(0, s.Towers.Count, "not on open ground");

            TestSims.Send(sim, SimCommand.PlaceShape(3, 1));
            float wallHealth = s.Grid[3, 1].WallHealth;
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            Assert.AreEqual(Occupant.Tower, s.Grid[3, 1].Occupant);
            Assert.IsFalse(Placement.CanPlaceTower(s.Grid, sim.Map, new TileCoord(3, 1), true), "one tower to a wall");

            TestSims.Send(sim, SimCommand.SellWall(3, 1));
            Assert.AreEqual(Occupant.Tower, s.Grid[3, 1].Occupant, "a wall with a tower on it stays down");

            TestSims.Send(sim, SimCommand.MoveTower(3, 1, 4, 2));
            Assert.AreEqual(new TileCoord(4, 2), tower.Tile, "onto the rock");
            Assert.AreEqual(Occupant.Wall, s.Grid[3, 1].Occupant, "the wall it stood on is back");
            Assert.AreEqual(wallHealth, s.Grid[3, 1].WallHealth);
            Assert.AreEqual(Occupant.Tower, s.Grid[4, 2].Occupant);
            Assert.IsFalse(s.Grid[4, 2].IsWalkable, "a rock stays a rock");

            TestSims.Send(sim, SimCommand.MoveTower(4, 2, 5, 1));
            Assert.AreEqual(new TileCoord(4, 2), tower.Tile, "not onto open ground");
            TestSims.Send(sim, SimCommand.MoveTower(4, 2, 3, 1));
            Assert.AreEqual(new TileCoord(3, 1), tower.Tile, "back onto the wall");

            // Sold, the tower leaves its wall behind; taken up, the wall goes back to the stock.
            TestSims.Send(sim, SimCommand.SellTower(3, 1));
            Assert.IsFalse(tower.IsAlive);
            Assert.AreEqual(Occupant.Wall, s.Grid[3, 1].Occupant);
            int left = s.WallsLeft;
            TestSims.Send(sim, SimCommand.SellWall(3, 1));
            Assert.AreEqual(left + 1, s.WallsLeft);
            Assert.AreEqual(Occupant.None, s.Grid[3, 1].Occupant);
        }

        [Test]
        public void ATowerChewedThroughTakesItsWallWithItAndALiftedOneLeavesItBehind()
        {
            var grid = new SimGrid(TestMaps.Parse(Open).Map);
            SimNode node = grid[3, 1];
            grid.SetWall(node, 15, 6f, 7);
            node.WallHealth = 4f;
            grid.SetTower(node, 20, 8f, 0);
            Assert.AreEqual(20, node.TerrainCost);
            Assert.AreEqual(15, node.FoundationCost);
            Assert.AreEqual(7, node.FoundationPiece);

            SimNode copy = grid.Clone()[3, 1];
            Assert.AreEqual(4f, copy.FoundationHealth, "a copy of the grid keeps the wall under the tower");

            grid.LiftTower(node);
            Assert.AreEqual(Occupant.Wall, node.Occupant);
            Assert.AreEqual(7, node.PieceId);
            Assert.AreEqual(15, node.TerrainCost);
            Assert.AreEqual(4f, node.WallHealth);
            Assert.AreEqual(6f, node.MaxWallHealth);
            Assert.AreEqual(0, node.FoundationCost);

            grid.SetTower(node, 20, 8f, 0);
            grid.ClearWall(node); // what a breach does
            Assert.AreEqual(Occupant.None, node.Occupant);
            Assert.AreEqual(0, node.FoundationCost);
        }

        // ------------------------------------------------------------------ two upgrades: common, rare, epic

        [Test]
        public void WithTwoUpgradesATowerGoesCommonRareEpicAndEachCostsMore()
        {
            SimConfig config = Config();
            config.ChoiceUpgradeCost = 10f;
            config.ChoiceUpgradeCostStep = 5f;
            config.UpgradesPerTower = 2;
            Simulation sim = New(config);
            SimState s = sim.State;
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            float budget = s.BuildBudget;
            Assert.AreEqual(1, tower.Rarity);
            Assert.AreEqual(1, tower.GearSlots);
            Assert.AreEqual(10f, sim.UpgradePrice(tower));

            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 0));
            Assert.AreEqual(2, tower.Rarity);
            Assert.AreEqual(2, tower.GearSlots, "a rare tower has two gear slots");
            Assert.AreEqual(budget - 10f, s.BuildBudget);
            Assert.AreEqual(15f, sim.UpgradePrice(tower));
            float damage = tower.Damage;

            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 0));
            Assert.AreEqual(3, tower.Rarity);
            Assert.AreEqual(0, tower.Upgrade2);
            Assert.AreEqual(budget - 25f, s.BuildBudget);
            Assert.AreEqual((float)(damage * 1.5f), tower.Damage, "the same choice twice stacks");

            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 1));
            Assert.AreEqual(budget - 25f, s.BuildBudget, "epic is the top");

            ulong hash = sim.ComputeHash();
            tower.Upgrade2 = 1;
            Assert.AreNotEqual(hash, sim.ComputeHash(), "the second upgrade is part of the state");
        }

        // ------------------------------------------------------------------ the heal card

        [Test]
        public void AHealCardGivesTheCoreBackHitPointsUpToItsMaximum()
        {
            SimConfig config = Config();
            config.CoreMaxHp = 10;
            config.HealCardWeight = 1000f;
            config.HealCardAmount = 3;
            Simulation sim = New(config);
            SimState s = sim.State;
            Assert.AreEqual(CardKind.Heal, s.Shop.Offers[0].Kind, "rig");
            Assert.AreEqual(CardKind.Heal, s.Shop.Offers[1].Kind, "rig");

            TestSims.Send(sim, SimCommand.TakeCoin(0));
            Assert.IsFalse(s.Shop.Offers[0].Bought, "not taken at full health");

            s.CoreHp = 9; // rig: as if a wave had hurt it
            TestSims.Send(sim, SimCommand.TakeCoin(0));
            Assert.AreEqual(10, s.CoreHp, "up to the maximum");
            Assert.IsTrue(s.Shop.Offers[0].Bought);
            s.CoreHp = 2;
            TestSims.Send(sim, SimCommand.TakeCoin(1));
            Assert.AreEqual(5, s.CoreHp);
            Assert.AreEqual(PlacementKind.CoreHealed, s.PlacementLog[s.PlacementLog.Count - 1].Kind);
        }

        // ------------------------------------------------------------------ gear: no plain boosts, and the extractor

        private static int GearIndex(GearDef[] gear, string id)
        {
            for (int i = 0; i < gear.Length; i++) if (gear[i].Id == id) return i;
            throw new System.InvalidOperationException("no gear " + id);
        }

        [Test]
        public void TheExtractorTakesATowersGearOffAndIsUsedUp()
        {
            SimConfig config = Config();
            config.GearInventorySlots = 3;
            Simulation sim = New(config);
            SimState s = sim.State;
            int chill = GearIndex(sim.GearLibrary, "chill"), extractor = GearIndex(sim.GearLibrary, "extractor");
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);

            s.GearInventory[0] = chill; // rig
            TestSims.Send(sim, SimCommand.EquipGear(0, 3, 1));
            Assert.AreEqual(chill, tower.Gear[0]);
            Assert.Greater(tower.SlowTicks, 0);

            s.GearInventory[1] = extractor;
            TestSims.Send(sim, SimCommand.EquipGear(1, 3, 1));
            Assert.AreEqual(TowerState.NoGear, tower.Gear[0], "the tower's gear is off");
            Assert.AreEqual(0, tower.SlowTicks);
            Assert.AreEqual(chill, s.GearInventory[0], "and in the inventory");
            Assert.AreEqual(TowerState.NoGear, s.GearInventory[1], "the extractor is used up");

            s.GearInventory[2] = extractor;
            TestSims.Send(sim, SimCommand.EquipGear(2, 3, 1));
            Assert.AreEqual(extractor, s.GearInventory[2], "a tower with no gear leaves it unused");
        }

        [Test]
        public void WithoutStatGearTheShopsOfferOnlyGearThatDoesSomethingElse()
        {
            GearDef[] gear = GearRoster.Default();
            var config = new SimConfig { StatGear = false, ExtractorGear = true, GearCardWeight = 5f };
            var shop = new HashSet<string>();
            var cards = new HashSet<string>();
            for (int stock = 0; stock < 60; stock++)
            {
                foreach (TowerOffer item in ShopRoller.RollGearShop(3, stock, gear, 3, 0, config)) shop.Add(gear[item.GearIndex].Id);
                foreach (TowerOffer card in ShopRoller.Roll(3, stock, 0, TestSims.GameTowers(), 4, config, gear))
                    if (card.Kind == CardKind.Gear) cards.Add(gear[card.GearIndex].Id);
            }
            CollectionAssert.AreEquivalent(new[] { "sky-sight", "chill", "extractor" }, shop);
            CollectionAssert.AreEquivalent(new[] { "sky-sight", "chill", "extractor" }, cards);

            // Without a config: the old five and never the extractor, drawn as before.
            var old = new HashSet<string>();
            for (int stock = 0; stock < 60; stock++)
                foreach (TowerOffer item in ShopRoller.RollGearShop(3, stock, gear, 3)) old.Add(gear[item.GearIndex].Id);
            CollectionAssert.AreEquivalent(new[] { "scope", "whetstone", "sky-sight", "powder", "chill" }, old);
        }
    }
}
