using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP9: the shared threat cost table (S6) and the study's four strategies (S10), as the
    // student decided them on 2026-11-16. I2 is the point: every strategy spends the budget of
    // the static baseline's wave, at every wave number.
    public class StrategyTests
    {
        private static MapData Generated(ulong seed = 7) { return MapGenerator.Generate(seed, 48, 48, deposits: 25); }

        private static Simulation Sim(MapData map, SimConfig config, ThreatCostTable table, IWavePlanner planner = null)
        {
            return new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), 1UL,
                                  planner ?? new EscalationPlanner(config, map, table), table);
        }

        private static AgentGroup Group(int count, float hp, float speed, MovementClass movement = MovementClass.Ground)
        {
            return new AgentGroup { Count = count, Hp = hp, MoveSpeed = speed, Movement = movement, Resist = DamageTypes.AllOnes(), SpawnIntervalTicks = 5 };
        }

        private static WavePlan Plan(params AgentGroup[] groups) { return new WavePlan { WaveIndex = 1, Groups = groups }; }

        // ---- the cost table (S6) ----

        [Test]
        public void AGroundEnemyCostsItsHitPointsTimesItsSpeed()
        {
            var table = new ThreatCostTable(new SimConfig(), Generated());
            Assert.AreEqual(40f * 1.5f, table.EnemyPrice(Group(1, 40f, 1.5f)), 1e-4f);
            Assert.AreEqual(3 * 40f * 1.5f + 2 * 10f * 2f, table.Price(Plan(Group(3, 40f, 1.5f), Group(2, 10f, 2f))), 1e-3f);
        }

        [Test]
        public void FlyersAndSappersAreMultipliedByTheirClassFactor()
        {
            var table = new ThreatCostTable(new SimConfig(), Generated(), flyingFactor: 2f, sapperFactor: 3f);
            Assert.AreEqual(2f * 60f, table.EnemyPrice(Group(1, 40f, 1.5f, MovementClass.Flying)), 1e-4f);
            Assert.AreEqual(3f * 60f, table.EnemyPrice(Group(1, 40f, 1.5f, MovementClass.Sapper)), 1e-4f);
            Assert.AreEqual(60f, table.EnemyPrice(Group(1, 40f, 1.5f)), 1e-4f);
        }

        [Test]
        public void TheTableUsesTheMeasuredClassFactorsUnlessToldOtherwise()
        {
            // Results/2026-11-24_threat-factors-balance: flyers 2.48, sappers 1.10 (were 2.35 and 1.30).
            var table = new ThreatCostTable(new SimConfig(), Generated());
            Assert.AreEqual(2.48f, table.FlyingFactor);
            Assert.AreEqual(1.10f, table.SapperFactor);
            Assert.Greater(table.FlyingFactor, table.SapperFactor, "flyers are the harder to stop");
        }

        [Test]
        // Raiders are priced since 2026-11-24 (the raid strategy): as walkers, RaiderFactor 1.
        public void ResistancesRaidersAndSiegeEnemiesAreNotPriced()
        {
            var table = new ThreatCostTable(new SimConfig(), Generated());
            AgentGroup resists = Group(1, 40f, 1f);
            resists.Resist = new[] { 0.35f, 1f, 1f };
            AgentGroup raider = Group(1, 40f, 1f);
            raider.TargetsCollectors = true;
            AgentGroup siege = Group(1, 40f, 1f);
            siege.Siege = true;
            Assert.Throws<ArgumentException>(() => table.EnemyPrice(resists));
            Assert.AreEqual(table.EnemyPrice(Group(1, 40f, 1f)) * ThreatCostTable.RaiderFactor, table.EnemyPrice(raider), 1e-6f);
            Assert.Throws<ArgumentException>(() => table.EnemyPrice(siege));
        }

        [Test]
        public void TheBudgetIsThePriceOfTheStaticBaselinesWave()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            var baseline = new EscalationPlanner(config, map);
            for (int w = 1; w <= 40; w++)
            {
                Assert.AreEqual(table.Price(baseline.PlanFor(w)), table.BudgetForWave(w), "wave " + w);
                if (w > 1) Assert.Greater(table.BudgetForWave(w), table.BudgetForWave(w - 1), "the baseline escalates, wave " + w);
            }
        }

        [Test]
        public void ThePlanCheckAcceptsAPlanWithinARelativeEpsilonOfTheBudget()
        {
            // At wave 40 the budget is in the thousands, where a float's last bit is bigger than
            // an absolute 1e-3: the check is relative (ThreatEpsilon × budget).
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            WavePlan plan = new EscalationPlanner(config, map, table).PlanFor(40);
            Assert.Greater(table.BudgetForWave(40), 1000f);
            Assert.IsNull(PlanValidator.Check(plan, 40, map, config, table));

            WavePlan over = plan.Copy();
            over.Groups[0].Hp = (float)(over.Groups[0].Hp * 1.01f);
            over.ThreatSpent = table.Price(over);
            StringAssert.Contains("(I2)", PlanValidator.Check(over, 40, map, config, table));
        }

        [Test]
        public void APlanTheTableCannotPriceIsRefusedNotThrown()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            WavePlan plan = new EscalationPlanner(config, map, table).PlanFor(3);
            plan.Groups[0].Siege = true;
            StringAssert.Contains("cannot be priced", PlanValidator.Check(plan, 3, map, config, table));
        }

        // ---- the four strategies (S10) ----

        private static IReadOnlyList<IWaveStrategy> Pool(SimConfig config, MapData map, ThreatCostTable table)
        {
            return StrategyPool.Study(new StrategyKit(config, map, table));
        }

        [Test]
        public void ThePoolIsTheProposalsFourInAFixedOrder()
        {
            var config = new SimConfig();
            MapData map = Generated();
            IReadOnlyList<IWaveStrategy> pool = Pool(config, map, new ThreatCostTable(config, map));
            Assert.AreEqual(5, pool.Count, "the proposal's four, then the raid (2026-11-24)");
            CollectionAssert.AreEqual(new[] { "breach_thin_wall", "swarm_chokepoint", "split_groups", "flying_bypass", "raid_outposts" },
                                      new[] { pool[0].Name, pool[1].Name, pool[2].Name, pool[3].Name, pool[4].Name });
            for (int i = 0; i < pool.Count; i++) Assert.AreEqual(i, pool[i].Id);
        }

        [Test]
        public void EveryStrategySpendsTheBudgetAtEveryWaveFromOneToForty()
        {
            // I2, the must-pass of WP9.
            foreach (SimConfig config in new[] { new SimConfig(), new SimConfig { HpGrowthPerWave = 0.04f } })
            {
                MapData map = Generated();
                var table = new ThreatCostTable(config, map, flyingFactor: 1.7f, sapperFactor: 2.3f);
                Simulation sim = Sim(map, config, table);
                foreach (IWaveStrategy strategy in Pool(config, map, table))
                {
                    for (int w = 1; w <= 40; w++)
                    {
                        float budget = table.BudgetForWave(w);
                        WavePlan plan = strategy.BuildPlan(budget, new WaveContext(w, sim.State, map, config), new Pcg32(9UL, RngStreams.Strategy));
                        string at = strategy.Name + " wave " + w;
                        Assert.AreEqual(strategy.Name, plan.StrategyId, at);
                        Assert.LessOrEqual(Math.Abs(plan.ThreatSpent - budget), config.ThreatEpsilon * budget, at);
                        Assert.AreEqual(table.Price(plan), plan.ThreatSpent, at + ": ThreatSpent is the table's price");
                        Assert.IsNull(PlanValidator.Check(plan, w, map, config, table), at);
                    }
                }
            }
        }

        [Test]
        public void PlansAreDeterministicForAGivenSeed()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            Simulation sim = Sim(map, config, table);
            foreach (IWaveStrategy strategy in Pool(config, map, table))
            {
                for (int w = 1; w <= 12; w++)
                {
                    var ctx = new WaveContext(w, sim.State, map, config);
                    string a = Json.Serialize(strategy.BuildPlan(table.BudgetForWave(w), ctx, new Pcg32(11UL, RngStreams.Strategy)));
                    string b = Json.Serialize(strategy.BuildPlan(table.BudgetForWave(w), ctx, new Pcg32(11UL, RngStreams.Strategy)));
                    Assert.AreEqual(a, b, strategy.Name + " wave " + w);
                }
            }
        }

        private static WavePlan PlanOf(string name, int wave, MapData map, SimConfig config = null)
        {
            config = config ?? new SimConfig();
            var table = new ThreatCostTable(config, map);
            Simulation sim = Sim(map, config, table);
            foreach (IWaveStrategy s in Pool(config, map, table))
                if (s.Name == name) return s.BuildPlan(table.BudgetForWave(wave), new WaveContext(wave, sim.State, map, config), new Pcg32(3UL, RngStreams.Strategy));
            throw new InvalidOperationException("no strategy " + name);
        }

        [Test]
        public void SplitGroupsComesFromThreeSpacedEntriesAtOnce()
        {
            MapData map = Generated();
            var config = new SimConfig();
            for (int w = 1; w <= 10; w++)
            {
                WavePlan plan = PlanOf("split_groups", w, map, config);
                var entries = new HashSet<int>();
                foreach (AgentGroup g in plan.Groups) { entries.Add(g.SpawnIndex); Assert.AreEqual(0, g.StartDelayTicks, "all at once"); }
                Assert.AreEqual(3, entries.Count, "wave " + w);
                var list = new List<int>(entries);
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        TileCoord a = map.Spawns[list[i]], b = map.Spawns[list[j]];
                        Assert.GreaterOrEqual(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), config.MinEntrySpacingTiles);
                    }
            }
        }

        [Test]
        public void SplitGroupsUsesAtLeastTwoEntriesWhenTheMapHasTwo()
        {
            WavePlan plan = PlanOf("split_groups", 4, AsciiMap.Parse(@"
                S . . . . . . . . . . . S
                . . . . . . . . . . . . .
                . . . . . . C . . . . . .").Map);
            var entries = new HashSet<int>();
            foreach (AgentGroup g in plan.Groups) entries.Add(g.SpawnIndex);
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        // Since 2026-11-24 each strategy sends pairs (the student): its main type, then a helper,
        // at every entry it uses, the main type with most of the budget.
        [TestCase("flying_bypass", "flyer", "bats")]
        [TestCase("breach_thin_wall", "sapper", "brute")]
        [TestCase("swarm_chokepoint", "swarm", "runner")]
        [TestCase("split_groups", "basic", "runner")]
        [TestCase("raid_outposts", "raider", "basic")]
        public void EachStrategySendsItsKindOfEnemyWithAHelper(string strategy, string main, string helper)
        {
            MapData map = Generated();
            for (int w = 1; w <= 12; w++)
            {
                AgentGroup[] groups = PlanOf(strategy, w, map).Groups;
                Assert.AreEqual(0, groups.Length % 2, strategy + " wave " + w);
                for (int i = 0; i < groups.Length; i += 2)
                {
                    Assert.AreEqual(main, groups[i].Archetype, strategy + " wave " + w);
                    Assert.AreEqual(helper, groups[i + 1].Archetype, strategy + " wave " + w);
                    Assert.AreEqual(groups[i].SpawnIndex, groups[i + 1].SpawnIndex, "a pair comes in together");
                }
            }
        }

        [Test]
        public void NoStrategySendsRaidersSiegeEnemiesOrResistances()
        {
            // S14 closed for the study: nothing in the pool touches income or firepower beyond
            // what the baseline does.
            MapData map = Generated();
            foreach (string name in new[] { "breach_thin_wall", "swarm_chokepoint", "split_groups", "flying_bypass" })
                for (int w = 1; w <= 40; w += 3)
                    foreach (AgentGroup g in PlanOf(name, w, map).Groups)
                    {
                        Assert.IsFalse(g.TargetsCollectors, name);
                        Assert.IsFalse(g.Siege, name);
                        foreach (float r in g.Resist) Assert.AreEqual(1f, r, name);
                    }
        }

        [Test]
        public void TheStrategiesKeepTheirTypesNumbersCloseAndFitTheBudgetWithSmallHitPointChanges()
        {
            // The count is chosen so the budget is nearly spent; the hit points only absorb the
            // remainder. So a swarm is still many weak enemies and a flyer still a light one.
            MapData map = Generated();
            var config = new SimConfig();
            var basic = new EscalationPlanner(config, map).PlanFor(10).Groups[0];
            foreach (AgentGroup g in PlanOf("swarm_chokepoint", 10, map, config).Groups)
            {
                if (g.Archetype != EnemyArchetypes.SwarmId) continue; // the runners beside them
                float typeHp = (float)(basic.Hp * EnemyArchetypes.Swarm().HpFactor);
                Assert.AreEqual(typeHp, g.Hp, typeHp * 0.25f);
                Assert.Greater(g.Count, basic.Count, "a swarm is more enemies than the baseline");
            }
        }

        // ---- where each strategy enters ----

        [Test]
        public void BreachThinWallEntersWhereTheSappersHaveTheLeastWallToDig()
        {
            // Two entries the same distance from the core. The left one's way is behind one wall,
            // the right one's behind three.
            const string board = @"
                S . . . . . . . . . . . S
                # # # . . . . . . . # # #
                . . . . . . . . . . # # #
                . . . . . . . . . . # # #
                . . . . . . C . . . . . .";
            var config = new SimConfig();
            Simulation sim = TestSims.AsciiWithWalls(board, new EscalationPlanner(config, AsciiMap.Parse(board).Map), config);
            var table = new ThreatCostTable(config, sim.Map);
            var breach = new BreachThinWall(new StrategyKit(config, sim.Map, table));
            WavePlan plan = breach.BuildPlan(table.BudgetForWave(3), new WaveContext(3, sim.State, sim.Map, config), new Pcg32(1UL, RngStreams.Strategy));
            Assert.AreEqual(0, plan.Groups[0].SpawnIndex);
        }

        [Test]
        public void FlyingBypassEntersWhereTheFewestAntiAirTowersReach()
        {
            const string board = @"
                S . . . . . . . . . . . S
                . . . . . . . . . . . . .
                . . . . . . . . . . . . .
                . . . . . . C . . . . . .";
            var config = new SimConfig();
            Simulation sim = TestSims.Ascii(board, new EscalationPlanner(config, AsciiMap.Parse(board).Map), config);
            var archer = new TowerState(1, TestTowers.Gun(range: 3f, canHitFlying: true), new TileCoord(2, 2), sim.State.Grid[2, 2].Position, sim.Map.NodeDiameter, 0);
            sim.State.Towers.Add(archer);
            var table = new ThreatCostTable(config, sim.Map);
            var flying = new FlyingBypass(new StrategyKit(config, sim.Map, table));
            WavePlan plan = flying.BuildPlan(table.BudgetForWave(5), new WaveContext(5, sim.State, sim.Map, config), new Pcg32(1UL, RngStreams.Strategy));
            Assert.AreEqual(1, plan.Groups[0].SpawnIndex, "the left side has an archer that hits flyers");
        }

        [Test]
        public void SwarmChokepointEntersThroughTheTowerThatDidTheMostDamage()
        {
            const string board = @"
                S . . . . . . . . . . . S
                . . . . . . . . . . . . .
                . . . . . . . . . . . . .
                . . . . . . C . . . . . .";
            var config = new SimConfig();
            Simulation sim = TestSims.Ascii(board, new EscalationPlanner(config, AsciiMap.Parse(board).Map), config);
            var left = new TowerState(1, TestTowers.Gun(range: 3f), new TileCoord(2, 2), sim.State.Grid[2, 2].Position, sim.Map.NodeDiameter, 0) { WaveDamage = 10f };
            var right = new TowerState(2, TestTowers.Gun(range: 3f), new TileCoord(10, 2), sim.State.Grid[10, 2].Position, sim.Map.NodeDiameter, 0) { WaveDamage = 90f };
            sim.State.Towers.Add(left);
            sim.State.Towers.Add(right);
            var table = new ThreatCostTable(config, sim.Map);
            var swarm = new SwarmChokepoint(new StrategyKit(config, sim.Map, table));
            WavePlan plan = swarm.BuildPlan(table.BudgetForWave(5), new WaveContext(5, sim.State, sim.Map, config), new Pcg32(1UL, RngStreams.Strategy));
            Assert.AreEqual(1, plan.Groups[0].SpawnIndex, "the right tower did most of last wave's damage");
        }

        // ---- in a running game ----

        [Test]
        public void EveryStrategyPlaysFiveWavesInTheSimulationWithTheCostTableEnforced()
        {
            // The simulation runs PlanValidator with the table (I2 at the wave boundary) and checks
            // that planning changed nothing in the state (the planner only reads). No towers, so the
            // core is made sturdy enough for five waves to be played and checked.
            var config = new SimConfig { CoreMaxHp = 100000 };
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            for (int s = 0; s < 4; s++)
            {
                var planner = new FixedStrategyPlanner(StrategyPool.Study(new StrategyKit(config, map, table))[s], table, 21UL);
                Simulation sim = Sim(map, config, table, planner);
                for (int w = 0; w < 5 && !sim.State.IsGameOver; w++) TestSims.RunUntilWaveResolves(sim, 40000);
                Assert.IsFalse(sim.State.IsGameOver, planner.Name);
                Assert.AreEqual(5, sim.State.LastOutcome.WaveIndex, planner.Name);
                Assert.AreEqual(planner.Strategy.Name, sim.State.LastOutcome.StrategyId, planner.Name);
            }
        }

        [Test]
        public void PlanningNeverChangesTowerDefinitionsPricesOrTheConfig()
        {
            // I11: the director shapes waves only.
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            Simulation sim = Sim(map, config, table);
            TowerDef[] towers = TestTowers.Roster();
            string configBefore = Json.Serialize(config), towersBefore = Json.Serialize(towers);
            float budgetBefore = sim.State.BuildBudget;
            ulong hashBefore = StateHasher.Compute(sim.State);
            foreach (IWaveStrategy s in Pool(config, map, table))
                for (int w = 1; w <= 20; w++)
                    s.BuildPlan(table.BudgetForWave(w), new WaveContext(w, sim.State, map, config), new Pcg32(5UL, RngStreams.Strategy));
            Assert.AreEqual(configBefore, Json.Serialize(config));
            Assert.AreEqual(towersBefore, Json.Serialize(towers));
            Assert.AreEqual(budgetBefore, sim.State.BuildBudget);
            Assert.AreEqual(hashBefore, StateHasher.Compute(sim.State));
        }

        [Test]
        public void NothingCanBeBuiltOnAnEntry()
        {
            // "Placement protects every spawn": a wave can always enter where a strategy sends it.
            MapData map = Generated();
            var grid = new SimGrid(map);
            for (int i = 0; i < map.Spawns.Length; i++)
                Assert.IsFalse(Placement.CanPlace(grid, map, new[] { map.Spawns[i] }), "entry " + i);
        }
    }
}
