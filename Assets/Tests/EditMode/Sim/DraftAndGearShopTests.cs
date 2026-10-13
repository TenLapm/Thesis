using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The second round after the student's play (2026-10-12): a roll of cards is a
    // free pick of one, gear is sold in a shop of its own that is restocked every few
    // waves, and the next wave is decided when the build phase begins so the player can
    // be told what is coming. All of it is off in a default SimConfig.
    public class DraftAndGearShopTests
    {
        // 9 x 5, open. S is (0,2), C is (8,2).
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        private static Simulation New(SimConfig config, ulong seed = 1, IWavePlanner planner = null)
        {
            MapData map = TestMaps.Parse(Open).Map;
            return TestSims.Ascii(Open, planner ?? new EscalationPlanner(config, map), config, seed);
        }

        private static void PlayOneWave(Simulation sim)
        {
            TestSims.Send(sim, SimCommand.StartWaveNow());
            int guard = 60000;
            int wave = sim.State.WaveIndex + 1;
            do { sim.Tick(); } while (guard-- > 0 && !(sim.State.WaveIndex == wave && sim.State.Phase == SimPhase.Intermission) && !sim.State.IsGameOver);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase, "the wave resolved");
        }

        // ------------------------------------------------------------------ the draft

        [Test]
        public void ByDefaultTheShopIsNotADraft()
        {
            Simulation sim = New(new SimConfig());
            Assert.AreEqual(-1, sim.State.Shop.PicksLeft, "as many as the budget buys");
            foreach (TowerOffer o in sim.State.Shop.Offers) Assert.Greater(o.Price, 0);
            Assert.AreEqual(0, sim.State.GearShop.Offers.Length, "and there is no gear shop");
            Assert.IsNull(sim.State.NextPlan, "and waves are planned when they start");
        }

        [Test]
        public void InADraftTheCardsAreFreeAndOnlySoManyAreTaken()
        {
            var config = new SimConfig { PicksPerRoll = 1, PicksAtStart = 2, StartBudget = 0f, CoreMaxHp = 100000, AgentsPerWave = 3 };
            Simulation sim = New(config);
            ShopState shop = sim.State.Shop;

            Assert.AreEqual(2, shop.PicksLeft, "two before the first wave");
            foreach (TowerOffer o in shop.Offers) Assert.AreEqual(0, o.Price, "a draft card costs nothing");

            TestTowers.BuySlot(sim, 0, 3, 1);
            TestTowers.BuySlot(sim, 1, 4, 1);
            Assert.AreEqual(0, shop.PicksLeft);
            Assert.AreEqual(0f, sim.State.BuildBudget, "with no budget at all");

            TestSims.Send(sim, SimCommand.PlaceTower(2, 5, 1));
            Assert.AreEqual(2, sim.State.Towers.Count, "the third card stays in the shop");
            Assert.IsFalse(shop.Offers[2].Bought);

            PlayOneWave(sim);
            Assert.AreEqual(1, sim.State.Shop.PicksLeft, "one pick for every wave after that");
            TestTowers.BuySlot(sim, 0, 5, 1);
            TestSims.Send(sim, SimCommand.PlaceTower(1, 6, 1));
            Assert.AreEqual(3, sim.State.Towers.Count);
            Assert.AreEqual(0, sim.State.Towers[2].PricePaid, "so a sale of it refunds nothing");
        }

        [Test]
        public void ACoinCardUsesThePickToo()
        {
            var config = new SimConfig { PicksPerRoll = 1, PicksAtStart = 1, CoinCardWeight = 50f, StartBudget = 0f };
            Simulation sim = null;
            int coin = -1, tower = -1;
            for (ulong seed = 1; seed < 200 && (coin < 0 || tower < 0); seed++)
            {
                sim = New(config, seed);
                coin = tower = -1;
                for (int i = 0; i < sim.State.Shop.Offers.Length; i++)
                {
                    if (sim.State.Shop.Offers[i].Kind == CardKind.Coin) coin = i;
                    if (sim.State.Shop.Offers[i].Kind == CardKind.Tower) tower = i;
                }
            }
            Assert.IsTrue(coin >= 0 && tower >= 0, "a roll with a coin and a tower");

            TestSims.Send(sim, SimCommand.TakeCoin(coin));
            Assert.AreEqual(config.CoinCardAmount, sim.State.BuildBudget);
            TestSims.Send(sim, SimCommand.PlaceTower(tower, 3, 1));
            Assert.AreEqual(0, sim.State.Towers.Count, "the coin was the pick");
        }

        // ------------------------------------------------------------------ the gear shop

        [Test]
        public void TheGearShopSellsForBudgetAndIsRestockedEveryFewWaves()
        {
            var config = new SimConfig { GearShopSlots = 3, GearShopRefreshWaves = 2, StartBudget = 100f, CoreMaxHp = 100000, AgentsPerWave = 3 };
            Simulation sim = New(config);
            GearDef[] gear = sim.GearLibrary;
            ShopState stock = sim.State.GearShop;

            Assert.AreEqual(3, stock.Offers.Length);
            foreach (TowerOffer item in stock.Offers)
            {
                Assert.AreEqual(CardKind.Gear, item.Kind);
                Assert.AreEqual(gear[item.GearIndex].Price, item.Price, "the shop is paid for");
            }

            TowerState tower = TestTowers.BuyAny(sim, 3, 1);
            float before = sim.State.BuildBudget;
            TestSims.Send(sim, SimCommand.BuyShopGear(0, 3, 1));
            Assert.AreEqual(stock.Offers[0].GearIndex, tower.Gear[0]);
            Assert.AreEqual(before - stock.Offers[0].Price, sim.State.BuildBudget);
            Assert.IsTrue(stock.Offers[0].Bought);

            TestSims.Send(sim, SimCommand.BuyShopGear(0, 3, 1));
            TestSims.Send(sim, SimCommand.BuyShopGear(1, 3, 1));
            Assert.AreEqual(before - stock.Offers[0].Price, sim.State.BuildBudget, "an item sells once, and a common tower has one slot");

            PlayOneWave(sim);
            Assert.AreSame(stock, sim.State.GearShop, "the same stock after wave 1");
            PlayOneWave(sim);
            Assert.AreNotSame(stock, sim.State.GearShop, "a new one after wave 2");
            Assert.AreEqual(1, sim.State.GearShop.WaveIndex, "stock number 1");
            foreach (TowerOffer item in sim.State.GearShop.Offers) Assert.IsFalse(item.Bought);
        }

        // I10 for the gear shop: its stock depends on the seed and the stock number only.
        [Test]
        public void TheGearShopsStockIsAFunctionOfTheSeedAndTheStockNumber()
        {
            GearDef[] gear = GearRoster.Default();
            var seen = new HashSet<string>();
            for (int stock = 0; stock < 30; stock++)
            {
                TowerOffer[] a = ShopRoller.RollGearShop(11, stock, gear, 3);
                TowerOffer[] b = ShopRoller.RollGearShop(11, stock, gear, 3);
                string text = "";
                for (int i = 0; i < 3; i++)
                {
                    Assert.AreEqual(a[i].GearIndex, b[i].GearIndex);
                    text += a[i].GearIndex;
                }
                seen.Add(text);
            }
            Assert.Greater(seen.Count, 10, "different stocks differ");
            Assert.AreEqual(0, ShopRoller.RollGearShop(11, 0, gear, 0).Length);
        }

        // ------------------------------------------------------------------ the wave is known ahead

        [Test]
        public void WithPlanAheadTheNextWaveIsKnownThroughTheBuildPhaseAndIsTheWaveThatComes()
        {
            var config = new SimConfig { PlanAhead = true, WaitForStart = true, CoreMaxHp = 100000, AgentsPerWave = 4 };
            MapData map = TestMaps.Parse(Open).Map;
            Simulation sim = New(config, planner: new ClassCyclePlanner(config, map));

            Assert.IsNull(sim.State.NextPlan, "before the first tick");
            sim.Tick();
            WavePlan next = sim.State.NextPlan;
            Assert.IsNotNull(next, "decided as the build phase begins");
            Assert.AreEqual(1, next.WaveIndex);

            for (int i = 0; i < 200; i++) sim.Tick();
            Assert.AreSame(next, sim.State.NextPlan, "and not decided again");

            PlayOneWave(sim);
            Assert.AreEqual(1, sim.State.LastOutcome.WaveIndex);
            sim.Tick();
            Assert.AreEqual(2, sim.State.NextPlan.WaveIndex, "the build phase after wave 1 knows wave 2");
            Assert.AreEqual(MovementClass.Sapper, sim.State.NextPlan.Groups[0].Movement, "the development cycle's second wave is sappers");

            string announced = sim.State.NextPlan.Groups[0].ToString();
            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            Assert.IsNull(sim.State.NextPlan, "used up when the wave starts");
            Assert.AreEqual(announced, sim.State.CurrentPlan.Groups[0].ToString(), "what was announced is what comes");
        }

        // ------------------------------------------------------------------ a whole game

        [Test]
        public void AGameWithTheDraftTheGearShopAndPlannedAheadWavesReplaysOnEveryTick()
        {
            var config = new SimConfig
            {
                CoreMaxHp = 1000, StartBudget = 50f, StipendPerWave = 16, KillReward = 0f, WaitForStart = true, PlanAhead = true,
                PicksPerRoll = 1, PicksAtStart = 2, CoinCardWeight = 0.3f, RareTowerChance = 0.15f, EpicTowerChance = 0.04f,
                GearShopSlots = 3, GearShopRefreshWaves = 2,
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
                MaxWaves = 6,
                RecordTickHashes = true,
            });
            Assert.AreEqual(6, run.WavesResolved);
            Assert.LessOrEqual(run.TowersPlaced, 2 + 5, "two picks before wave 1, then one a wave at most");

            var commands = new HashSet<string>();
            foreach (ReplayCommand c in run.Replay.Commands) commands.Add(c.Cmd);
            CollectionAssert.Contains(commands, "BuyShopGear", "the scripted player used the gear shop");
            CollectionAssert.Contains(commands, "UpgradeTower");

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());

            List<WaveRecord> rebuilt = ReplayRunner.Telemetry(file);
            Assert.AreEqual(run.Telemetry.Count, rebuilt.Count);
            for (int i = 0; i < rebuilt.Count; i++) Assert.AreEqual(run.Telemetry[i].ToJsonLine(), rebuilt[i].ToJsonLine(), "row " + i);
        }
    }
}
