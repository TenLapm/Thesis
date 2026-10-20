using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The ore economy (2026-10-14): deposits on the map, collectors that pay after every
    // wave they stand through, raiders that go for them, the build menu with research
    // tiers, crafted gear, repairing the core, and waves of fewer, stronger enemies. All of
    // it is off in a default SimConfig and on maps without deposits.
    public class EconomyTests
    {
        // 9 x 5, open. S is (0,2), C is (8,2). Deposits at (4,0) and (4,4), off the straight line.
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        private static MapData MapWithDeposits()
        {
            MapData map = TestMaps.Parse(Open).Map;
            map.Deposits = new[]
            {
                new Deposit { Tile = new TileCoord(4, 0), Yield = 3 },
                new Deposit { Tile = new TileCoord(4, 4), Yield = 2 },
            };
            return map;
        }

        private static SimConfig Config()
        {
            return new SimConfig { ShopOffers = 0, StartBudget = 100f, CoreMaxHp = 100000, AgentsPerWave = 1, WaitForStart = true, StipendPerWave = 2, BuildMenu = true };
        }

        private static ShapeDef[] OneTile()
        {
            return new[] { new ShapeDef { Name = "Wall", BuildCost = 1, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } } };
        }

        // One group, whatever the wave: the test says who comes.
        private sealed class OneGroup : IWavePlanner
        {
            public AgentGroup Group;
            public string Name => "one";
            public WavePlan PlanWave(WaveContext context) { return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "one", Groups = new[] { Group.Copy() } }; }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        private static Simulation New(SimConfig config, MapData map, IWavePlanner planner = null, TowerDef[] towers = null)
        {
            return new Simulation(config, map, OneTile(), towers ?? new[] { TestTowers.Gun(cost: 5) }, 1, planner ?? new EscalationPlanner(config, map));
        }

        private static AgentGroup Group(SimConfig config, MapData map, EnemyArchetype type)
        {
            AgentGroup g = type.Apply(new EscalationPlanner(config, map).PlanFor(1).Groups[0], 1f);
            g.Count = 1;
            g.SpawnIndex = 0;
            return g;
        }

        private static void PlayOneWave(Simulation sim)
        {
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
        }

        // ------------------------------------------------------------------ deposits

        [Test]
        public void AGeneratedMapHasTheSameDepositsForTheSameSeedAndNoneUnlessAsked()
        {
            MapData a = MapGenerator.Generate(5, 48, 48, deposits: 25);
            MapData b = MapGenerator.Generate(5, 48, 48, deposits: 25);
            MapData plain = MapGenerator.Generate(5, 48, 48);
            Assert.AreEqual(a.ToJson(), b.ToJson());
            Assert.AreEqual(0, plain.Deposits.Length);
            Assert.AreEqual(string.Join("", plain.Rows), string.Join("", a.Rows), "the deposits leave the rocks as they were");

            Assert.Greater(a.Deposits.Length, 15);
            int near = 0, far = 0;
            for (int i = 0; i < a.Deposits.Length; i++)
            {
                Deposit d = a.Deposits[i];
                Assert.IsTrue(a.IsWalkable(d.Tile.X, d.Tile.Y));
                Assert.IsFalse(a.InBuildMargin(d.Tile));
                Assert.That(d.Yield, Is.InRange(2, 4));
                Assert.AreEqual(i, a.DepositAt(d.Tile));
                for (int j = 0; j < i; j++)
                    Assert.GreaterOrEqual(System.Math.Max(System.Math.Abs(a.Deposits[j].Tile.X - d.Tile.X), System.Math.Abs(a.Deposits[j].Tile.Y - d.Tile.Y)), 3);
                int ring = System.Math.Max(System.Math.Abs(d.Tile.X - 24), System.Math.Abs(d.Tile.Y - 24));
                if (ring < 8) near += d.Yield; else if (ring > 16) far += d.Yield;
                if (ring < 8) Assert.Less(d.Yield, 4, "near the middle a deposit is poor");
                if (ring > 18) Assert.Greater(d.Yield, 2, "out at the ring it is rich");
            }
        }

        // ------------------------------------------------------------------ collectors

        [Test]
        public void ACollectorStandsOnlyOnADepositAndPaysItsYieldAfterEveryWaveItStandsThrough()
        {
            SimConfig config = Config();
            config.CollectorPrice = 6f;
            MapData map = MapWithDeposits();
            Simulation sim = New(config, map);
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.BuildCollector(3, 0));
            Assert.AreEqual(100f, s.BuildBudget, "not off a deposit");
            TestSims.Send(sim, SimCommand.PlaceShape(4, 4));
            Assert.AreEqual(Occupant.None, s.Grid[4, 4].Occupant, "a deposit is for a collector, not a wall");

            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            Assert.AreEqual(Occupant.Collector, s.Grid[4, 4].Occupant);
            Assert.AreEqual(94f, s.BuildBudget);
            Assert.AreEqual(2, sim.CollectorIncome(s));

            float before = s.BuildBudget;
            PlayOneWave(sim);
            Assert.AreEqual(before + 2f + 2f, s.BuildBudget, "the core's 2 and the collector's 2");

            // Sold with the sell tool for a share of its price.
            TestSims.Send(sim, SimCommand.SellWall(4, 4));
            Assert.AreEqual(Occupant.None, s.Grid[4, 4].Occupant);
            Assert.AreEqual(before + 4f + 3f, s.BuildBudget);
        }

        [Test]
        public void ARaiderGoesForTheNearestCollectorAndOneChewedThroughPaysNothing()
        {
            SimConfig config = Config();
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            Simulation sim = New(config, map, planner);
            planner.Group = Group(config, map, EnemyArchetypes.Raider());
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            Assert.AreEqual(1, s.Grid.RaidGoals);
            float before = s.BuildBudget;
            PlayOneWave(sim);

            Assert.AreEqual(1, s.LastOutcome.CollectorsLost);
            Assert.AreEqual(Occupant.None, s.Grid[4, 0].Occupant, "chewed through");
            Assert.AreEqual(1, s.LastOutcome.Leaked, "then on to the core");
            Assert.AreEqual(before + 2f, s.BuildBudget, "the core's income only");
            Assert.AreEqual(0, s.Grid.RaidGoals);

            // A walker passes it by: the straight line to the core does not touch (4,0).
            planner.Group = Group(config, map, EnemyArchetypes.Basic());
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            before = s.BuildBudget;
            PlayOneWave(sim);
            Assert.AreEqual(0, s.LastOutcome.CollectorsLost);
            Assert.AreEqual(before + 2f + 3f, s.BuildBudget);
        }

        // ------------------------------------------------------------------ the build menu and research

        [Test]
        public void TheBuildMenuSellsAnyTowerOfAResearchedTierForItsPrice()
        {
            SimConfig config = Config();
            config.ResearchTier2Price = 20f;
            config.ResearchTier3Price = 40f;
            TowerDef small = TestTowers.Gun(cost: 5, id: "small");
            TowerDef big = TestTowers.Gun(cost: 9, id: "big");
            big.Tier = 2;
            Simulation sim = New(config, MapWithDeposits(), towers: new[] { small, big });
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.BuyTower(1, 3, 1));
            Assert.AreEqual(0, s.Towers.Count, "tier 2 is not researched");
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 1));
            Assert.AreEqual(1, s.Towers.Count);
            Assert.AreEqual(95f, s.BuildBudget);

            Assert.AreEqual(20f, sim.ResearchPrice);
            TestSims.Send(sim, SimCommand.Research());
            Assert.AreEqual(2, s.ResearchLevel);
            Assert.AreEqual(75f, s.BuildBudget);
            TestSims.Send(sim, SimCommand.BuyTower(1, 5, 1));
            Assert.AreEqual("big", s.Towers[1].Def.Id);
            Assert.AreEqual(66f, s.BuildBudget);

            TestSims.Send(sim, SimCommand.Research());
            Assert.AreEqual(3, s.ResearchLevel);
            Assert.AreEqual(-1f, sim.ResearchPrice, "nothing more to research");
            TestSims.Send(sim, SimCommand.Research());
            Assert.AreEqual(3, s.ResearchLevel);
            Assert.AreEqual(26f, s.BuildBudget);

            // Off without the build menu.
            Simulation plain = New(new SimConfig { StartBudget = 100f, ShopOffers = 0 }, MapWithDeposits());
            TestSims.Send(plain, SimCommand.BuyTower(0, 3, 1));
            TestSims.Send(plain, SimCommand.Research());
            Assert.AreEqual(0, plain.State.Towers.Count);
            Assert.AreEqual(1, plain.State.ResearchLevel);
        }

        [Test]
        public void GearIsCraftedIntoTheInventoryUpToTheResearchedTier()
        {
            SimConfig config = Config();
            config.GearInventorySlots = 4;
            config.StatGear = false;
            config.ExtractorGear = true;
            Simulation sim = New(config, MapWithDeposits());
            SimState s = sim.State;
            GearDef[] gear = sim.GearLibrary;
            int chill = Index(gear, "chill"), sky = Index(gear, "sky-sight"), scope = Index(gear, "scope");

            TestSims.Send(sim, SimCommand.CraftGear(sky));
            Assert.AreEqual(TowerState.NoGear, s.GearInventory[0], "sky-sight is tier 2");
            TestSims.Send(sim, SimCommand.CraftGear(scope));
            Assert.AreEqual(TowerState.NoGear, s.GearInventory[0], "no plain stat gear in this game");
            TestSims.Send(sim, SimCommand.CraftGear(chill));
            Assert.AreEqual(chill, s.GearInventory[0]);
            Assert.AreEqual(100f - gear[chill].Price, s.BuildBudget);
        }

        [Test]
        public void TheCoreIsRepairedForOreUpToItsMaximum()
        {
            SimConfig config = Config();
            config.CoreMaxHp = 10;
            config.RepairPrice = 6f;
            config.HealCardAmount = 3;
            Simulation sim = New(config, MapWithDeposits());
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.RepairCore());
            Assert.AreEqual(100f, s.BuildBudget, "not at full health");
            s.CoreHp = 8; // rig: as if a wave had hurt it
            TestSims.Send(sim, SimCommand.RepairCore());
            Assert.AreEqual(10, s.CoreHp);
            Assert.AreEqual(94f, s.BuildBudget);
        }

        // ------------------------------------------------------------------ fewer, stronger enemies

        [Test]
        public void WavesGrowByAFewEnemiesEverySoManyWavesAndByAShareOfHitPoints()
        {
            var config = new SimConfig { AgentsPerWave = 12, AgentsPerWaveIncrement = 1, AgentsIncrementEveryWaves = 2, BaseHp = 20f, HpIncrementPerWave = 0f, HpGrowthPerWave = 0.1f };
            MapData map = TestMaps.Parse(Open).Map;
            var planner = new EscalationPlanner(config, map);
            Assert.AreEqual(12, planner.PlanFor(1).Groups[0].Count);
            Assert.AreEqual(12, planner.PlanFor(2).Groups[0].Count);
            Assert.AreEqual(13, planner.PlanFor(3).Groups[0].Count);
            Assert.AreEqual(20f, planner.PlanFor(1).Groups[0].Hp);
            Assert.AreEqual((float)((float)(20f * 1.1f) * 1.1f), planner.PlanFor(3).Groups[0].Hp);

            // The defaults are the old growth: a fixed number more, every wave.
            var old = new EscalationPlanner(new SimConfig(), map);
            var defaults = new SimConfig();
            Assert.AreEqual(defaults.AgentsPerWave + 2 * defaults.AgentsPerWaveIncrement, old.PlanFor(3).Groups[0].Count);
            Assert.AreEqual((float)(defaults.BaseHp + (float)(2 * defaults.HpIncrementPerWave)), old.PlanFor(3).Groups[0].Hp);
        }

        // ------------------------------------------------------------------ gear of 2026-10-20

        private static SimConfig GearConfig()
        {
            SimConfig config = Config();
            config.GearInventorySlots = 4;
            config.AbilityGear = true;
            config.ResearchTier2Price = 0f;
            return config;
        }

        private static void Equip(Simulation sim, string piece, int x, int y)
        {
            sim.State.GearInventory[0] = Index(sim.GearLibrary, piece); // rig: as if crafted
            TestSims.Send(sim, SimCommand.EquipGear(0, x, y));
            Assert.AreEqual(TowerState.NoGear, sim.State.GearInventory[0], "rig: " + piece + " went on");
        }

        [Test]
        public void ABeaconMakesEveryCollectorNearItPayOneMore()
        {
            Simulation sim = New(GearConfig(), MapWithDeposits());
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            TestSims.Send(sim, SimCommand.BuyTower(0, 4, 1)); // next to (4,0), three tiles from (4,4)
            Assert.AreEqual(5, sim.CollectorIncome(s));

            Equip(sim, "beacon", 4, 1);
            Assert.AreEqual(7, sim.CollectorIncome(s), "both collectors are within three tiles");
            TestSims.Send(sim, SimCommand.BuyTower(0, 6, 1));
            s.GearInventory[1] = Index(sim.GearLibrary, "beacon");
            TestSims.Send(sim, SimCommand.EquipGear(1, 6, 1));
            Assert.AreEqual(7, sim.CollectorIncome(s), "once per collector, however many beacons");
        }

        [Test]
        public void AntiArmorGearHitsThroughAResistanceAndAnImmunity()
        {
            SimConfig config = GearConfig();
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            TowerDef frost = TestTowers.Gun(damage: 500f, range: 3f, interval: 5, type: DamageType.Frost, cost: 5);
            Simulation sim = New(config, map, planner, new[] { frost });
            planner.Group = Group(config, map, EnemyArchetypes.Frostborn()); // immune to frost
            TestSims.Send(sim, SimCommand.BuyTower(0, 4, 1));

            PlayOneWave(sim);
            Assert.AreEqual(0, sim.State.LastOutcome.Killed, "rig: frost does nothing to it");
            Equip(sim, "anti-armor", 4, 1);
            Assert.IsTrue(sim.State.Towers[0].IgnoresResist);
            Assert.AreEqual((int)(5 * 1.25f), sim.State.Towers[0].FireIntervalTicks, "and fires slower");
            PlayOneWave(sim);
            Assert.AreEqual(1, sim.State.LastOutcome.Killed);
        }

        [Test]
        public void ALureTowerPullsEnemiesWithinReachToItAndIsTougher()
        {
            SimConfig config = GearConfig();
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            TowerDef weak = TestTowers.Gun(damage: 0.001f, cost: 5);
            Simulation sim = New(config, map, planner, new[] { weak });
            planner.Group = Group(config, map, EnemyArchetypes.Basic());
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 4)); // two tiles off the straight line
            float health = s.Grid[3, 4].MaxWallHealth;

            Equip(sim, "lure", 3, 4);
            Assert.IsTrue(s.Grid[3, 4].Lure);
            Assert.AreEqual(health * 3f, s.Grid[3, 4].MaxWallHealth, 1e-4f);
            Assert.AreEqual(1, s.Grid.LureGoals);

            PlayOneWave(sim);
            Assert.AreEqual(1, s.LastOutcome.TowersDestroyed, "the walker left its line, went to the lure and chewed it down");
            Assert.IsFalse(s.Grid[3, 4].Lure);
            Assert.AreEqual(0, s.Grid.LureGoals);
        }

        [Test]
        public void ADestroyedCollectorPaysBackAShareOfItsPriceAtOnce()
        {
            SimConfig config = Config();
            config.CollectorPrice = 6f;
            config.CollectorLossRefund = 0.5f;
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            Simulation sim = New(config, map, planner);
            planner.Group = Group(config, map, EnemyArchetypes.Raider());
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            float before = sim.State.BuildBudget;
            PlayOneWave(sim);
            Assert.AreEqual(1, sim.State.LastOutcome.CollectorsLost);
            Assert.AreEqual(before + 3f + 2f, sim.State.BuildBudget, "half its price back, and the core's 2");
        }

        // ------------------------------------------------------------------ power (2026-10-20)

        private static SimConfig PowerConfig()
        {
            SimConfig config = Config();
            config.PowerRule = true;
            config.CorePowerRadius = 4f;
            config.CorePowerCapacity = 4;
            config.CollectorPowerRadius = 3f;
            config.CollectorPowerCapacity = 2;
            return config;
        }

        private static int Powered(SimState s)
        {
            int n = 0;
            foreach (TowerState t in s.Towers) if (t.IsAlive && t.Powered) n++;
            return n;
        }

        [Test]
        public void TheCorePowersFourTowersNearItAndACollectorTwoMore()
        {
            Simulation sim = New(PowerConfig(), MapWithDeposits(), towers: new[] { TestTowers.Gun(cost: 1) });
            SimState s = sim.State; // the core is (8,2)
            int[][] near = { new[] { 5, 1 }, new[] { 5, 3 }, new[] { 6, 1 }, new[] { 6, 3 }, new[] { 7, 1 } };
            foreach (int[] t in near) TestSims.Send(sim, SimCommand.BuyTower(0, t[0], t[1]));
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 1)); // far from the core
            Assert.AreEqual(6, s.Towers.Count);
            Assert.AreEqual(4, Powered(s), "the core has four places");
            Assert.IsFalse(s.Towers[5].Powered, "out of the core's reach");

            // A collector at (4,0) reaches (2,1) and (5,1), not (7,1): (5,1) moves over to it and
            // its place at the core goes to (7,1). Every tower that can have power gets it.
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            Assert.AreEqual(6, Powered(s));

            TestSims.Send(sim, SimCommand.SellWall(4, 0)); // the collector comes down
            Assert.AreEqual(4, Powered(s));
            Assert.IsFalse(s.Towers[5].Powered);
        }

        [Test]
        public void ATowerWithoutPowerDoesNotFireAndWithoutTheRuleEveryTowerHasPower()
        {
            SimConfig config = PowerConfig();
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            TowerDef gun = TestTowers.Gun(damage: 500f, range: 3f, interval: 5, cost: 1);
            Simulation sim = New(config, map, planner, new[] { gun });
            planner.Group = Group(config, map, EnemyArchetypes.Basic());
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 1)); // six tiles from the core
            Assert.IsFalse(sim.State.Towers[0].Powered);
            PlayOneWave(sim);
            Assert.AreEqual(0, sim.State.LastOutcome.Killed, "dark: it never fired");
            Assert.AreEqual(0, sim.State.Towers[0].Shots);

            SimConfig off = Config();
            Simulation plain = New(off, map, planner, new[] { gun });
            TestSims.Send(plain, SimCommand.BuyTower(0, 2, 1));
            Assert.IsTrue(plain.State.Towers[0].Powered);
            PlayOneWave(plain);
            Assert.AreEqual(1, plain.State.LastOutcome.Killed);
        }

        private static int Index(GearDef[] gear, string id)
        {
            for (int i = 0; i < gear.Length; i++) if (gear[i].Id == id) return i;
            throw new System.InvalidOperationException("no gear " + id);
        }
    }
}
