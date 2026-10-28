using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
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

        [Test]
        public void EveryGeneratedMapHasOreNearTheMiddle()
        {
            // The student (2026-10-26): it would be bad if every deposit lay out at the edge.
            foreach (int size in new[] { 48, 96 })
                for (ulong seed = 1; seed <= 20; seed++)
                    foreach (int per in new[] { 1, 4 })
                    {
                        MapData map = MapGenerator.Generate(seed, size, size, ridges: size / 4, deposits: 25, depositsPerCluster: per);
                        int nearest = int.MaxValue;
                        foreach (Deposit d in map.Deposits)
                            nearest = System.Math.Min(nearest, System.Math.Max(System.Math.Abs(d.Tile.X - size / 2), System.Math.Abs(d.Tile.Y - size / 2)));
                        Assert.LessOrEqual(nearest, System.Math.Max(12, size / 6 + 3), "size " + size + ", seed " + seed + ", " + per + " a cluster");
                    }
        }

        [Test]
        public void ClusteredDepositsLieInGroupsWithATileBetweenTwoVeins()
        {
            MapData map = MapGenerator.Generate(5, 48, 48, deposits: 24, depositsPerCluster: 4);
            Assert.AreEqual(map.ToJson(), MapGenerator.Generate(5, 48, 48, deposits: 24, depositsPerCluster: 4).ToJson());
            Assert.Greater(map.Deposits.Length, 16);
            foreach (Deposit d in map.Deposits)
            {
                Assert.IsTrue(map.IsWalkable(d.Tile.X, d.Tile.Y));
                Assert.IsFalse(map.InBuildMargin(d.Tile));
                int neighbours = 0;
                foreach (Deposit e in map.Deposits)
                {
                    if (e == d) continue;
                    int apart = System.Math.Max(System.Math.Abs(e.Tile.X - d.Tile.X), System.Math.Abs(e.Tile.Y - d.Tile.Y));
                    Assert.GreaterOrEqual(apart, 2, "a free tile between two veins");
                    if (apart <= 4) neighbours++;
                }
                Assert.Greater(neighbours, 0, "every vein has another of its cluster close by");
            }
        }

        [Test]
        public void RaidsBeginAtWaveSixAndGrowSlowly()
        {
            var planner = new RaidPlanner(new SimConfig(), MapGenerator.Generate(4, deposits: 20));
            for (int wave = 1; wave <= 24; wave++)
            {
                bool raid = false;
                foreach (AgentGroup g in planner.Compose(wave).Groups) if (g.TargetsCollectors) raid = true;
                Assert.AreEqual(wave >= 6 && (wave - 6) % 3 == 0, raid, "wave " + wave);
            }
            Assert.AreEqual(0.4f, RaidPlanner.RaidShare(6));
            Assert.Greater(RaidPlanner.RaidShare(30), RaidPlanner.RaidShare(9));
            Assert.LessOrEqual(RaidPlanner.RaidShare(300), 0.8f);
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
        public void ADepositRunsOutAndAnEmptyOneTakesNoCollector()
        {
            SimConfig config = Config();
            config.DepositOre = 7;
            config.StipendPerWave = 0;
            Simulation sim = New(config, MapWithDeposits());
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0)); // yields 3
            Assert.AreEqual(3, sim.CollectorIncome(s));
            var paid = new List<float>();
            for (int wave = 0; wave < 4; wave++)
            {
                float before = s.BuildBudget;
                PlayOneWave(sim);
                paid.Add(s.BuildBudget - before);
            }
            CollectionAssert.AreEqual(new[] { 3f, 3f, 1f, 0f }, paid, "7 ore in all: 3, 3, what is left, then nothing");
            Assert.AreEqual(0, sim.DepositLeft(s, 0));

            TestSims.Send(sim, SimCommand.SellWall(4, 0));
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            Assert.AreEqual(Occupant.None, s.Grid[4, 0].Occupant, "nothing left to dig there");
        }

        [Test]
        public void APylonMayNotStandNearTheCore()
        {
            SimConfig config = PowerConfig();
            config.PylonCoreKeepOut = 2f;
            var pylon = new TowerDef { Id = "pylon", Cost = 1, PowerCapacity = 2, PowerRadius = 2f, FireIntervalTicks = 50 };
            Simulation sim = New(config, MapWithDeposits(), towers: new[] { TestTowers.Gun(cost: 1), pylon });
            SimState s = sim.State; // the core is (8,2)
            TestSims.Send(sim, SimCommand.BuyTower(1, 7, 2));
            TestSims.Send(sim, SimCommand.BuyTower(1, 7, 1));
            Assert.AreEqual(0, s.Towers.Count, "within two tiles of the core");
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 2));
            TestSims.Send(sim, SimCommand.BuyTower(1, 5, 1));
            Assert.AreEqual(2, s.Towers.Count, "any tower beside the core, a pylon further out");
            Assert.AreEqual("pylon", s.Towers[1].Def.Id);
        }

        [Test]
        public void ATowerCanBeTiedToASourceInReachAndAFullSourceSwapsItsOldestTieOut()
        {
            Simulation sim = New(PowerConfig(), MapWithDeposits(), towers: new[] { TestTowers.Gun(cost: 1) });
            SimState s = sim.State; // the core is (8,2); the collector (4,0) reaches 3 tiles and powers 2
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 5, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 6, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 1)); // the collector only
            var core = new TileCoord(8, 2);
            var collector = new TileCoord(4, 0);
            Assert.AreEqual(3, Powered(s));
            Assert.AreEqual(collector, s.Towers[2].PoweredBy);

            TestSims.Send(sim, SimCommand.SetPowerSource(5, 1, 4, 0));
            TestSims.Send(sim, SimCommand.SetPowerSource(6, 1, 4, 0));
            Assert.AreEqual(collector, s.Towers[0].PoweredBy);
            Assert.AreEqual(collector, s.Towers[1].PoweredBy);
            Assert.IsFalse(s.Towers[2].Powered, "the collector's two places are tied; (2,1) has no other source");

            // A third tie to the full collector lets go of the oldest one, (5,1), which the core then powers.
            TestSims.Send(sim, SimCommand.SetPowerSource(2, 1, 4, 0));
            Assert.AreEqual(TowerState.NoTile, s.Towers[0].PowerPin);
            Assert.AreEqual(core, s.Towers[0].PoweredBy);
            Assert.AreEqual(collector, s.Towers[1].PoweredBy);
            Assert.AreEqual(collector, s.Towers[2].PoweredBy);
            Assert.AreEqual(3, Powered(s));

            // Out of reach: nothing changes. Untied: the matching decides again.
            TestSims.Send(sim, SimCommand.SetPowerSource(2, 1, 8, 2));
            Assert.AreEqual(collector, s.Towers[2].PowerPin);
            TestSims.Send(sim, SimCommand.SetPowerSource(6, 1, -1, -1));
            Assert.AreEqual(TowerState.NoTile, s.Towers[1].PowerPin);
            Assert.AreEqual(3, Powered(s));
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

        [Test]
        public void TheCraftPriceIsTheGearsPriceTimesTheFactor()
        {
            SimConfig config = Config();
            config.GearInventorySlots = 2;
            config.GearCraftPriceFactor = 0.5f;
            Simulation sim = New(config, MapWithDeposits());
            int chill = Index(sim.GearLibrary, "chill");
            Assert.AreEqual(sim.GearLibrary[chill].Price / 2, sim.CraftPrice(chill));
            TestSims.Send(sim, SimCommand.CraftGear(chill));
            Assert.AreEqual(100f - sim.GearLibrary[chill].Price / 2, sim.State.BuildBudget);
        }

        // ------------------------------------------------------------------ undo and redo (2026-10-24)

        [Test]
        public void UndoTakesBackTheLastActionsOfABuildPhaseAndRedoDoesThemAgain()
        {
            SimConfig config = Config();
            config.UndoSteps = 10;
            Simulation sim = New(config, MapWithDeposits());
            SimState start = sim.State;
            ulong empty = sim.ComputeHash();

            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 1));
            ulong both = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.BuildCollector(2, 2)); // not a deposit: changes nothing
            Assert.AreEqual(2, sim.UndoCount, "an action that changed nothing is not kept");

            TestSims.Send(sim, SimCommand.Undo());
            Assert.AreEqual(0, sim.State.Towers.Count);
            Assert.AreEqual(Occupant.Collector, sim.State.Grid[4, 0].Occupant);
            Assert.AreEqual(94f, sim.State.BuildBudget, "the tower's price is back");
            TestSims.Send(sim, SimCommand.Undo());
            Assert.AreEqual(empty, sim.ComputeHash(), "back to where the phase began");
            Assert.AreNotSame(start, sim.State);
            TestSims.Send(sim, SimCommand.Undo());
            Assert.AreEqual(empty, sim.ComputeHash(), "nothing more to take back");

            TestSims.Send(sim, SimCommand.Redo());
            TestSims.Send(sim, SimCommand.Redo());
            Assert.AreEqual(both, sim.ComputeHash(), "both done again");

            // A new action after an undo drops what could have been redone.
            TestSims.Send(sim, SimCommand.Undo());
            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            Assert.AreEqual(0, sim.RedoCount);

            // A wave starts: nothing before it can be taken back.
            PlayOneWave(sim);
            Assert.AreEqual(0, sim.UndoCount);
            int towers = sim.State.Towers.Count;
            TestSims.Send(sim, SimCommand.Undo());
            Assert.AreEqual(towers, sim.State.Towers.Count);

            // The same commands give the same game: undo is part of the record.
            Simulation again = New(config, MapWithDeposits());
            foreach (SimCommand c in new[] { SimCommand.BuildCollector(4, 0), SimCommand.BuyTower(0, 3, 1), SimCommand.Undo(), SimCommand.Undo(), SimCommand.Redo() })
                TestSims.Send(again, c);
            Simulation once = New(config, MapWithDeposits());
            TestSims.Send(once, SimCommand.BuildCollector(4, 0));
            Assert.AreEqual(once.ComputeHash(), again.ComputeHash());

            // And the same when they arrive in one tick, as on a replay: an Undo and a Redo in
            // the middle of a batch must not drop the commands after them.
            Simulation batched = New(config, MapWithDeposits());
            foreach (SimCommand c in new[] { SimCommand.BuildCollector(4, 0), SimCommand.Undo(), SimCommand.Redo(), SimCommand.BuyTower(0, 3, 1) }) batched.Enqueue(c);
            batched.FlushInput();
            Simulation oneByOne = New(config, MapWithDeposits());
            foreach (SimCommand c in new[] { SimCommand.BuildCollector(4, 0), SimCommand.Undo(), SimCommand.Redo(), SimCommand.BuyTower(0, 3, 1) }) TestSims.Send(oneByOne, c);
            Assert.AreEqual(1, batched.State.Towers.Count, "the tower after the Redo was bought");
            Assert.AreEqual(oneByOne.ComputeHash(), batched.ComputeHash());
        }

        // ------------------------------------------------------------------ playing a wave again (2026-10-24)

        [Test]
        public void AWonWaveIsPlayedAgainFromItsBuildPhaseAndTheRetryCanBeTakenBack()
        {
            SimConfig config = Config();
            config.UndoSteps = 5;
            Simulation sim = New(config, MapWithDeposits());
            SimState s = sim.State;
            Assert.IsFalse(sim.CanRetry, "no wave played yet");

            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            PlayOneWave(sim);
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 1));
            Assert.IsTrue(sim.CanRetry);
            Assert.AreEqual(1, sim.RetryWaveNumber);

            TestSims.Send(sim, SimCommand.RetryWave());
            s = sim.State;
            Assert.AreEqual(0, s.WaveIndex, "back before wave 1");
            Assert.AreEqual(0, s.Towers.Count);
            Assert.AreEqual(Occupant.None, s.Grid[4, 4].Occupant, "as the build phase began");
            Assert.AreEqual(100f, s.BuildBudget);
            Assert.AreEqual(1, s.Retries);

            TestSims.Send(sim, SimCommand.Undo());
            Assert.AreEqual(1, sim.State.WaveIndex, "the retry taken back");
            Assert.AreEqual(1, sim.State.Towers.Count);
        }

        [Test]
        public void ALostWaveIsPlayedAgainAndTheRecordingStillReplaysAndMakesTheSameTelemetry()
        {
            SimConfig config = Config();
            config.CoreMaxHp = 1;
            config.AgentsPerWave = 3;
            config.UndoSteps = 5;
            Simulation sim = New(config, MapWithDeposits());
            var recorder = new ReplayRecorder(sim, true) { Session = "t", Build = "t", Policy = "t" };
            var telemetry = new TelemetryRecorder(sim, "t", "t", "t");
            var rows = new List<WaveRecord>();
            System.Action<SimCommand> send = c => { recorder.OnCommand(c); sim.Enqueue(c); sim.FlushInput(); };
            System.Action tick = () => { sim.Tick(); recorder.AfterTick(); rows.AddRange(telemetry.AfterTick()); };

            send(SimCommand.BuildCollector(4, 4));
            send(SimCommand.StartWaveNow());
            for (int i = 0; i < 20000 && !sim.State.IsGameOver; i++) tick();
            Assert.IsTrue(sim.State.IsGameOver, "rig: no towers, the core falls");
            send(SimCommand.BuyTower(0, 3, 1));
            Assert.AreEqual(0, sim.State.Towers.Count, "a lost game takes nothing but a retry");

            send(SimCommand.RetryWave());
            Assert.IsFalse(sim.State.IsGameOver);
            Assert.AreEqual(0, sim.State.WaveIndex);
            Assert.AreEqual(Occupant.None, sim.State.Grid[4, 4].Occupant);
            send(SimCommand.BuyTower(0, 7, 1));
            send(SimCommand.StartWaveNow());
            for (int i = 0; i < 60; i++) tick();
            rows.AddRange(telemetry.Finish());

            ReplayFile file = recorder.Snapshot();
            ReplayReport report = ReplayRunner.Verify(file, true);
            Assert.IsTrue(report.Ok, report.Describe());
            List<WaveRecord> again = ReplayRunner.Telemetry(file);
            Assert.AreEqual(rows.Count, again.Count);
            for (int i = 0; i < rows.Count; i++) Assert.AreEqual(rows[i].ToJsonLine(), again[i].ToJsonLine(), "row " + i);
        }

        // ------------------------------------------------------------------ the pylon, raiders first, each tower its upgrades (2026-10-24)

        [Test]
        public void APylonPowersTowersNearItAndNeverFires()
        {
            SimConfig config = PowerConfig();
            config.CorePowerCapacity = 0; // rig: only the pylon gives power
            TowerDef gun = TestTowers.Gun(damage: 50f, range: 3f, cost: 1);
            var pylon = new TowerDef { Id = "pylon", Cost = 1, Damage = 0f, RangeTiles = 0f, FireIntervalTicks = 50, PowerCapacity = 2, PowerRadius = 3f };
            MapData map = MapWithDeposits();
            var planner = new OneGroup();
            Simulation sim = New(config, map, planner, new[] { gun, pylon });
            planner.Group = Group(config, map, EnemyArchetypes.Basic());
            SimState s = sim.State;

            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 3));
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 1)); // out of the pylon's reach
            Assert.AreEqual(0, Powered(s), "rig: nothing powers them yet");

            TestSims.Send(sim, SimCommand.BuyTower(1, 4, 2));
            Assert.IsTrue(s.Towers[3].Powered, "a pylon has power of its own");
            Assert.IsTrue(s.Towers[0].Powered && s.Towers[1].Powered);
            Assert.IsFalse(s.Towers[2].Powered, "out of reach");

            PlayOneWave(sim);
            Assert.AreEqual(0, s.Towers[3].Shots, "a pylon never fires");
            Assert.AreEqual(1, s.LastOutcome.Killed, "the towers it powered did");
        }

        [Test]
        public void RaidersFirstShootsARaiderBeforeAnyoneCloserToTheCore()
        {
            AsciiFixture f = AsciiMap.Parse(Open);
            var grid = new SimGrid(f.Map);
            new FlowField().Generate(grid, f.Map.Core);
            TowerState tower = TestTowers.StateOn(f, 0, TestTowers.Gun(range: 6f), 4, 2);
            var walker = TestTowers.Agent(0, grid[6, 2].Position);   // nearer the core
            var raider = TestTowers.Agent(1, grid[2, 2].Position);
            raider.TargetsCollectors = true;
            var live = new List<AgentState> { walker, raider };
            SimNode core = grid.Get(f.Map.Core);

            Assert.AreEqual(0, Targeting.Pick(TargetingMode.First, tower, grid, core, live));
            Assert.AreEqual(1, Targeting.Pick(TargetingMode.Raiders, tower, grid, core, live));
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.Raiders, tower, grid, core, new List<AgentState> { walker }), "no raider: as First");
        }

        [Test]
        public void EachTowerHasItsOwnUpgradesAndTheyChangeWhatItDoes()
        {
            SimConfig config = Config();
            config.ChoiceUpgradeCost = 1f;
            config.UpgradesPerTower = 2;
            TowerDef gun = TestTowers.Gun(cost: 1);
            gun.Upgrades = new[]
            {
                new TowerLevelMod { Id = "sky", Label = "Sky: hits flyers", GrantsFlying = true },
                new TowerLevelMod { Id = "pierce", Label = "Pierce: ignores armour, slows", IgnoresResist = true, SlowTicksAdd = 20, SlowFactorTo = 0.5f },
            };
            Simulation sim = New(config, MapWithDeposits(), towers: new[] { gun });
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 1));
            TowerState t = sim.State.Towers[0];

            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 2));
            Assert.AreEqual(0, t.UpgradeCount, "this tower has two choices, not four");
            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 0));
            Assert.IsTrue(t.CanHitFlying);
            Assert.AreEqual("gun+sky", sim.State.PlacementLog[sim.State.PlacementLog.Count - 1].ShapeName);
            TestSims.Send(sim, SimCommand.ChooseUpgrade(3, 1, 1));
            Assert.IsTrue(t.IgnoresResist);
            Assert.AreEqual(20, t.SlowTicks);
            Assert.AreEqual(0.5f, t.SlowFactor);
        }

        private static int Index(GearDef[] gear, string id)
        {
            for (int i = 0; i < gear.Length; i++) if (gear[i].Id == id) return i;
            throw new System.InvalidOperationException("no gear " + id);
        }
    }
}
