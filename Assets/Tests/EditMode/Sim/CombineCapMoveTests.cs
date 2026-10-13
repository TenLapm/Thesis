using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The third round after the student's play (2026-10-12): three towers of a kind
    // merge into the next level, only so many towers may stand, a tower can be put on
    // another tile between waves, a reroll's price rises, and a tower type can fire at
    // several enemies from the start. All of it is off in a default SimConfig.
    public class CombineCapMoveTests
    {
        // 9 x 5, open. S is (0,2), C is (8,2).
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        // A roster of one tower, so every card is that tower.
        private static Simulation New(SimConfig config, ulong seed = 1, TowerDef[] towers = null)
        {
            MapData map = TestMaps.Parse(Open).Map;
            return TestSims.Ascii(Open, new EscalationPlanner(config, map), config, seed, towers ?? new[] { TestTowers.Gun(cost: 5) });
        }

        private static SimConfig Config()
        {
            return new SimConfig { ShopOffers = 9, StartBudget = 100f, CoreMaxHp = 100000, AgentsPerWave = 3 };
        }

        private static int Standing(Simulation sim) { return TowerCombine.Standing(sim.State.Towers); }

        // ------------------------------------------------------------------ off by default

        [Test]
        public void ByDefaultNothingCombinesNothingIsCappedAndNothingMoves()
        {
            Simulation sim = New(Config());
            for (int i = 0; i < 9; i++) TestTowers.BuySlot(sim, i, i, 0);
            Assert.AreEqual(9, Standing(sim));
            foreach (TowerState t in sim.State.Towers) Assert.AreEqual(1, t.Level);
            Assert.AreEqual(0, sim.TowerCap);

            ulong before = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.MoveTower(0, 0, 0, 4));
            Assert.AreEqual(before, sim.ComputeHash(), "MoveTower does nothing unless the config allows it");
        }

        // ------------------------------------------------------------------ combining

        [Test]
        public void ThreeOfAKindBecomeOneRareOnTheTileOfTheFirst()
        {
            SimConfig config = Config();
            config.CombineCount = 3;
            Simulation sim = New(config);

            TestTowers.BuySlot(sim, 0, 3, 1);
            TestTowers.BuySlot(sim, 1, 4, 1);
            Assert.AreEqual(2, Standing(sim), "two do nothing");

            TestTowers.BuySlot(sim, 2, 5, 1);
            Assert.AreEqual(1, Standing(sim));
            TowerState keeper = sim.State.Towers[0];
            Assert.IsTrue(keeper.IsAlive);
            Assert.AreEqual(2, keeper.Level);
            Assert.AreEqual(new TileCoord(3, 1), keeper.Tile);
            Assert.AreEqual(15, keeper.PricePaid, "what was paid for all three");
            Assert.AreEqual(1.5f, keeper.Damage, "the plain level: half as much damage again");

            for (int id = 1; id <= 2; id++)
            {
                Assert.IsFalse(sim.State.Towers[id].IsAlive);
                Assert.IsTrue(sim.State.Towers[id].Combined);
                Assert.IsFalse(sim.State.Towers[id].Sold);
            }
            Assert.AreEqual(Occupant.None, sim.State.Grid[4, 1].Occupant);
            Assert.AreEqual(Occupant.None, sim.State.Grid[5, 1].Occupant);
            Assert.AreEqual(Occupant.Tower, sim.State.Grid[3, 1].Occupant);

            PlacementRecord row = sim.State.PlacementLog[sim.State.PlacementLog.Count - 1];
            Assert.AreEqual(PlacementKind.TowerCombined, row.Kind);
            Assert.AreEqual(0, row.TowerId);
            Assert.AreEqual(2, row.Tiles.Length);
        }

        [Test]
        public void ThreeRaresBecomeAnEpicAndAnEpicCombinesNoFurther()
        {
            SimConfig config = Config();
            config.CombineCount = 3;
            config.ShopOffers = 16;
            Simulation sim = New(config);

            // Nine commons: three rares, and the third rare completes the epic.
            for (int i = 0; i < 9; i++) TestTowers.BuySlot(sim, i, i, i < 5 ? 0 : 4);
            Assert.AreEqual(1, Standing(sim));
            Assert.AreEqual(3, sim.State.Towers[0].Level);

            // Six more: two more rares. They stay rares beside the epic.
            for (int i = 9; i < 15; i++) TestTowers.BuySlot(sim, i, i - 8, 1);
            Assert.AreEqual(3, Standing(sim));
            Assert.AreEqual(2, TowerCombine.Copies(sim.State.Towers, sim.TowerLibrary[0], 2));
        }

        [Test]
        public void GearOfTheAbsorbedTowersGoesToTheOneThatStaysOrIsPaidBack()
        {
            SimConfig config = Config();
            config.CombineCount = 3;
            config.GearShopSlots = 3;
            Simulation sim = New(config);
            int price = sim.GearLibrary[sim.State.GearShop.Offers[0].GearIndex].Price;

            TestTowers.BuySlot(sim, 0, 3, 1);
            TestTowers.BuySlot(sim, 1, 4, 1);
            TestSims.Send(sim, SimCommand.BuyShopGear(0, 3, 1));
            TestSims.Send(sim, SimCommand.BuyShopGear(1, 4, 1));
            Assert.AreNotEqual(TowerState.NoGear, sim.State.Towers[1].Gear[0], "rig: both carry a piece");

            // A third piece on the third tower cannot be bought before it is placed, so
            // two pieces meet two slots: both fit on the rare.
            TestTowers.BuySlot(sim, 2, 5, 1);
            TowerState keeper = sim.State.Towers[0];
            Assert.AreEqual(2, keeper.Level);
            Assert.AreNotEqual(TowerState.NoGear, keeper.Gear[0]);
            Assert.AreNotEqual(TowerState.NoGear, keeper.Gear[1]);
            Assert.AreEqual(15 + 2 * price, keeper.PricePaid);

            // Now a set in which the pieces do not fit: the keeper is full, so the piece
            // on an absorbed tower is paid back.
            TestTowers.BuySlot(sim, 3, 1, 3);
            TestTowers.BuySlot(sim, 4, 2, 3);
            TestTowers.BuySlot(sim, 5, 3, 3); // a second rare, at (1,3)
            TestTowers.BuySlot(sim, 6, 5, 3);
            TestTowers.BuySlot(sim, 7, 6, 3);
            TestSims.Send(sim, SimCommand.BuyShopGear(2, 1, 3));
            Assert.AreNotEqual(TowerState.NoGear, sim.State.Towers[3].Gear[0], "rig: the second rare carries a piece");

            float before = sim.State.BuildBudget;
            TestTowers.BuySlot(sim, 8, 7, 3); // the third rare: all three merge into the first
            Assert.AreEqual(3, keeper.Level);
            Assert.AreEqual(1, Standing(sim));
            Assert.AreEqual(before - 5 + price, sim.State.BuildBudget, "the piece that found no slot came back as budget");
        }

        // ------------------------------------------------------------------ the cap

        [Test]
        public void TheCapRefusesATowerUnlessItCompletesASet()
        {
            SimConfig config = Config();
            config.TowerCap = 2;
            config.TowerCapEveryWaves = 1;
            config.CombineCount = 3;
            Simulation sim = New(config);
            Assert.AreEqual(2, sim.TowerCap);

            TestTowers.BuySlot(sim, 0, 3, 1);
            TestTowers.BuySlot(sim, 1, 4, 1);
            Assert.IsTrue(TowerCombine.HasRoomFor(sim.State.Towers, sim.TowerLibrary[0], 1, config, 0), "the third completes the set");
            TestTowers.BuySlot(sim, 2, 5, 1);
            Assert.AreEqual(1, Standing(sim));

            // Without combining the same cap simply refuses.
            SimConfig plain = Config();
            plain.TowerCap = 2;
            plain.TowerCapEveryWaves = 1;
            Simulation capped = New(plain);
            TestTowers.BuySlot(capped, 0, 3, 1);
            TestTowers.BuySlot(capped, 1, 4, 1);
            float budget = capped.State.BuildBudget;
            TestSims.Send(capped, SimCommand.PlaceTower(2, 5, 1));
            Assert.AreEqual(2, capped.State.Towers.Count);
            Assert.AreEqual(budget, capped.State.BuildBudget, "refused, so nothing was paid");
            Assert.IsFalse(capped.State.Shop.Offers[2].Bought);

            // One more for every wave started.
            TestSims.Send(capped, SimCommand.StartWaveNow());
            capped.Tick();
            Assert.AreEqual(3, capped.TowerCap);
            TestTowers.BuySlot(capped, 2, 5, 1);
        }

        // ------------------------------------------------------------------ moving a tower

        [Test]
        public void ATowerMovesForNothingBetweenWavesWithItsLevelAndGear()
        {
            SimConfig config = Config();
            config.MoveTowers = true;
            config.GearShopSlots = 1;
            config.WaitForStart = true;
            Simulation sim = New(config);

            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            TestSims.Send(sim, SimCommand.BuyShopGear(0, 3, 1));
            TestSims.Send(sim, SimCommand.UpgradeTower(3, 1));
            float budget = sim.State.BuildBudget;
            int gear = tower.Gear[0];

            TestSims.Send(sim, SimCommand.MoveTower(3, 1, 6, 3));
            Assert.AreEqual(new TileCoord(6, 3), tower.Tile);
            Assert.AreEqual(sim.State.Grid[6, 3].Position.X, tower.Position.X);
            Assert.AreEqual(Occupant.None, sim.State.Grid[3, 1].Occupant);
            Assert.AreEqual(Occupant.Tower, sim.State.Grid[6, 3].Occupant);
            Assert.AreEqual(tower.Id, sim.State.Grid[6, 3].TowerId);
            Assert.AreEqual(2, tower.Level);
            Assert.AreEqual(gear, tower.Gear[0]);
            Assert.AreEqual(budget, sim.State.BuildBudget);
            Assert.AreEqual(PlacementKind.TowerMoved, sim.State.PlacementLog[sim.State.PlacementLog.Count - 1].Kind);

            // Not onto a built tile, a protected tile, or off the map; not from an empty tile.
            TestTowers.BuySlot(sim, 1, 4, 1);
            ulong before = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.MoveTower(6, 3, 4, 1));
            TestSims.Send(sim, SimCommand.MoveTower(6, 3, 8, 2));
            TestSims.Send(sim, SimCommand.MoveTower(6, 3, 40, 2));
            TestSims.Send(sim, SimCommand.MoveTower(1, 1, 2, 2));
            Assert.AreEqual(before, sim.ComputeHash());

            // And not during a wave.
            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(SimPhase.Spawning, sim.State.Phase);
            TestSims.Send(sim, SimCommand.MoveTower(6, 3, 2, 4));
            Assert.AreEqual(new TileCoord(6, 3), tower.Tile);
        }

        // ------------------------------------------------------------------ rerolls and upgrades

        [Test]
        public void ARerollCostsMoreEachTimeUntilTheNextWavesCards()
        {
            SimConfig config = Config();
            config.RerollCost = 2f;
            config.RerollCostStep = 1f;
            config.PicksPerRoll = 1;
            config.PicksAtStart = 2;
            config.WaitForStart = true;
            Simulation sim = New(config);

            Assert.AreEqual(2f, sim.RerollPrice);
            TestTowers.BuySlot(sim, 0, 3, 1);
            Assert.AreEqual(1, sim.State.Shop.PicksLeft);

            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(98f, sim.State.BuildBudget);
            Assert.AreEqual(1, sim.State.Shop.RerollCount);
            Assert.AreEqual(1, sim.State.Shop.PicksLeft, "the pick not yet used carries over");
            Assert.AreEqual(3f, sim.RerollPrice);

            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(95f, sim.State.BuildBudget);
            Assert.AreEqual(4f, sim.RerollPrice);

            TestTowers.BuySlot(sim, 0, 4, 1);
            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(95f, sim.State.BuildBudget, "with no pick left there is nothing to reroll for");

            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(2f, sim.RerollPrice, "back to the base price with the next cards");
        }

        [Test]
        public void AnUpgradeFactorOfZeroSwitchesPaidUpgradesOff()
        {
            SimConfig config = Config();
            config.UpgradeCostFactor = 0f;
            Simulation sim = New(config);
            TowerState tower = TestTowers.BuySlot(sim, 0, 3, 1);
            TestSims.Send(sim, SimCommand.UpgradeTower(3, 1));
            Assert.AreEqual(1, tower.Level, "not a free level");
        }

        // ------------------------------------------------------------------ a tower with several targets

        [Test]
        public void ATowerTypeCanFireAtSeveralEnemiesFromLevelOne()
        {
            TowerDef chain = TestTowers.Gun(cost: 5, id: "chain");
            chain.Targets = 3;
            chain.Validate();
            Simulation sim = New(Config(), towers: new[] { chain });
            Assert.AreEqual(3, TestTowers.BuySlot(sim, 0, 3, 1).Targets);

            chain.Targets = 0;
            Assert.Throws<System.InvalidOperationException>(() => chain.Validate());
        }

        // ------------------------------------------------------------------ a whole game

        [Test]
        public void AGameWithEverythingOfTheThirdRoundReplaysOnEveryTick()
        {
            var config = new SimConfig
            {
                CoreMaxHp = 1000, StartBudget = 30f, StipendPerWave = 10, KillReward = 0f, WaitForStart = true, PlanAhead = true,
                ShopOffers = 4, PicksPerRoll = 1, PicksAtStart = 2, CoinCardWeight = 0.25f, GearCardWeight = 0.25f,
                RareTowerChance = 0.1f, EpicTowerChance = 0.02f, GearShopSlots = 3, GearShopRefreshWaves = 5,
                RerollCost = 2f, RerollCostStep = 1f, CombineCount = 3, TowerCap = 4, TowerCapEveryWaves = 3,
                MoveTowers = true, UpgradeCostFactor = 0f,
            };
            EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
            {
                Config = config,
                Map = TestSims.GameMap(),
                Shapes = TestSims.GameShapes(),
                Towers = TestSims.GameTowers(),
                Seed = 4,
                Planner = ClassCyclePlanner.Id,
                Policy = Registry.CreatePolicy(SentryPolicy.UpgraderId),
                MaxWaves = 10,
                RecordTickHashes = true,
            });
            Assert.AreEqual(10, run.WavesResolved);

            int combines = 0;
            foreach (WaveRecord row in run.Telemetry)
                foreach (WaveRecord.PlacementRow p in row.Placements)
                    if (p.Kind == WaveRecord.PlacementRow.Combine) combines++;
            Assert.Greater(combines, 0, "the scripted player completed a set");

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());

            List<WaveRecord> rebuilt = ReplayRunner.Telemetry(file);
            Assert.AreEqual(run.Telemetry.Count, rebuilt.Count);
            for (int i = 0; i < rebuilt.Count; i++) Assert.AreEqual(run.Telemetry[i].ToJsonLine(), rebuilt[i].ToJsonLine(), "row " + i);
        }

        // A hand-made session with a move in it survives the file and replays.
        [Test]
        public void AMoveTowerCommandSurvivesTheReplayFile()
        {
            ReplayCommand c = ReplayCommand.From(7, SimCommand.MoveTower(3, 1, 6, 3));
            Assert.AreEqual("MoveTower", c.Cmd);
            SimCommand back = c.ToCommand();
            Assert.AreEqual(SimCommandKind.MoveTower, back.Kind);
            Assert.AreEqual(3, back.X);
            Assert.AreEqual(1, back.Y);
            Assert.AreEqual(6, back.B);
            Assert.AreEqual(3, back.C);
        }
    }
}
