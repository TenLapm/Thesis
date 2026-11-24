using System;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // The raid strategy (the student, 2026-11-24: "there must be something that is aiming for the
    // player's collectors"): the fifth of the study's pool, the director's own choice.
    public class RaidStrategyTests
    {
        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static Simulation Game(out MapData map, out SimConfig config, IWavePlanner planner = null)
        {
            config = GameConfig();
            map = GameMaps.Generate(109);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 109UL, planner ?? new EscalationPlanner(config, map));
            sim.Enqueue(SimCommand.PlaceCore(sim.State.Core.X, sim.State.Core.Y));
            sim.FlushInput();
            return sim;
        }

        private static TileCoord BuildCollector(Simulation sim)
        {
            foreach (Deposit d in sim.Map.Deposits)
            {
                if (sim.State.Grid.Get(d.Tile).Occupant != Occupant.None) continue;
                sim.Enqueue(SimCommand.BuildCollector(d.Tile.X, d.Tile.Y));
                sim.FlushInput();
                if (sim.State.Grid.Get(d.Tile).Occupant == Occupant.Collector) return d.Tile;
            }
            throw new InvalidOperationException("no collector could be built");
        }

        private static ProfileInput Input(Simulation sim)
        {
            return ProfileInput.From(sim.State, sim.Map, EmptyBoard.For(sim.Map, sim.Config, sim.State.Core));
        }

        [Test]
        public void ExposureIsTheShareOfCollectorsNoTowerReaches()
        {
            Simulation sim = Game(out _, out _);
            var feature = new CollectorExposure();
            Assert.IsFalse(feature.Compute(Input(sim)).HasData, "no collector: nothing to raid");

            TileCoord c = BuildCollector(sim);
            FeatureValue open = feature.Compute(Input(sim));
            Assert.IsTrue(open.HasData);
            Assert.AreEqual(1f, open.Value, "a collector and no tower: fully exposed");

            var guard = new TowerState(99, TestTowers.Gun(), new TileCoord(c.X + 1, c.Y), sim.State.Grid[c.X + 1, c.Y].Position, sim.Map.NodeDiameter, 0);
            sim.State.Towers.Add(guard);
            Assert.AreEqual(0f, feature.Compute(Input(sim)).Value, "a tower that reaches it covers it");
        }

        [Test]
        public void TheProfileHasSixFeaturesNow()
        {
            Assert.AreEqual(3, BuildProfile.SchemaVersion);
            CollectionAssert.Contains(BuildProfiler.Default().Names, CollectorExposure.FeatureName);
        }

        [Test]
        public void TheRaidSendsRaidersAndWalkersOnTheWavesBudget()
        {
            Simulation sim = Game(out MapData map, out SimConfig config);
            BuildCollector(sim);
            var table = new ThreatCostTable(config, map);
            var raid = new RaidOutposts(new StrategyKit(config, map, table));
            Assert.AreEqual(4, raid.Id);
            WavePlan plan = raid.BuildPlan(table.BudgetForWave(6), new WaveContext(6, sim.State, map, config), new Pcg32(1UL, RngStreams.Strategy));

            int raiders = 0, others = 0;
            foreach (AgentGroup g in plan.Groups) { if (g.TargetsCollectors) raiders += g.Count; else others += g.Count; }
            Assert.Greater(raiders, 0, "raiders go for the collectors");
            Assert.Greater(others, 0, "and part of the wave still goes for the core");
            Assert.IsNull(PlanValidator.Check(plan, 6, map, config, table), "priced, and spends the wave's budget");
        }

        [Test]
        public void ARaidIsBannedWithoutCollectorsAndBeforeWaveFour()
        {
            var rule = new NoRaidTarget(DirectorConfig.Provisional().RaidsFromWave);
            Simulation sim = Game(out _, out _);
            Assert.AreEqual(NoRaidTarget.Reason, rule.Check(RaidOutposts.StrategyName, sim.State, 6), "nothing to raid");
            BuildCollector(sim);
            Assert.IsNull(rule.Check(RaidOutposts.StrategyName, sim.State, 6));
            Assert.AreEqual(NoRaidTarget.Reason, rule.Check(RaidOutposts.StrategyName, sim.State, 3), "not before wave 4");
            Assert.IsNull(rule.Check(SplitGroups.StrategyName, sim.State, 1), "only raids need a target");
        }

        [Test]
        public void TheDirectorHasFiveStrategiesAndScoresTheRaid()
        {
            Simulation sim = Game(out MapData map, out SimConfig config);
            var d = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, config, map, 109UL);
            BuildCollector(sim);
            d.PlanWave(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(5, d.LastTrace.Rows.Count);
            StrategyRow raid = d.LastTrace.Rows[4];
            Assert.AreEqual(RaidOutposts.StrategyName, raid.Name);
            Assert.IsNull(raid.Veto);
            Assert.AreEqual(4f, raid.Heuristic.Value, 1e-5f, "an unguarded collector scores the top");
            Assert.IsFalse(string.IsNullOrEmpty(AnnouncementTable.Line(AnnouncementTable.Id(RaidOutposts.StrategyName, 2))));
        }
    }
}
