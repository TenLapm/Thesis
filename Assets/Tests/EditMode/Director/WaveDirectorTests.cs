using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Learning;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP10: the constraint and heuristic layers, the bucketizer, the heuristic-only director and
    // the two ladder baselines. The numbers are the student's provisional choices of 2026-11-16
    // (DirectorConfig.Provisional).
    public class WaveDirectorTests
    {
        private static MapData Generated(ulong seed = 7) { return MapGenerator.Generate(seed, 48, 48, deposits: 25); }

        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        // A spy for I5: it throws if it is ever asked about a strategy listed as forbidden.
        private sealed class SpyEstimator : IStrategyEstimator
        {
            public readonly HashSet<int> Forbidden = new HashSet<int>();
            public readonly List<int> Asked = new List<int>();
            public int BinCount => 5;
            public int StrategyCount => 4;
            public Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng)
            {
                if (Forbidden.Contains(strategy)) throw new InvalidOperationException("the estimator was asked about vetoed strategy " + strategy);
                Asked.Add(strategy);
                return new Correction { Theta = 0.5f, Alpha = 1f, Beta = 1f, Value = 0f, Cold = true };
            }
            public void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng) { }
            public EstimatorState Export() { return null; }
            public void Import(EstimatorState state) { }
        }

        private static WaveDirector Director(SimConfig config, MapData map, DirectorMode mode = DirectorMode.HeuristicOnly, IStrategyEstimator estimator = null, DirectorConfig cfg = null)
        {
            return new WaveDirector(cfg ?? DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), 1UL, mode, estimator);
        }

        private static Simulation Sim(SimConfig config, MapData map, IWavePlanner planner)
        {
            return new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), 1UL, planner);
        }

        private const int FlyingBypassId = 3;

        // ---- the constraint layer (I5) ----

        [Test]
        public void AVetoedStrategyScoresMinusInfinityAndTheEstimatorIsNeverAskedAboutIt()
        {
            // No anti-air on the board: flying_bypass is vetoed. The spy throws if asked about it.
            var config = new SimConfig();
            MapData map = Generated();
            var spy = new SpyEstimator();
            spy.Forbidden.Add(FlyingBypassId);
            WaveDirector d = Director(config, map, DirectorMode.Full, spy);
            Simulation sim = Sim(config, map, d);
            d.PlanWave(new WaveContext(1, sim.State, map, config));

            StrategyRow flying = d.LastTrace.Rows[FlyingBypassId];
            Assert.AreEqual(Uncounterable.Reason, flying.Veto);
            Assert.AreEqual(float.NegativeInfinity, flying.Score);
            CollectionAssert.DoesNotContain(spy.Asked, FlyingBypassId);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, spy.Asked, "every surviving strategy is asked, in id order");
        }

        [Test]
        public void WhenEveryStrategyIsVetoedTheDirectorFallsBackToTheBaseline()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var cfg = DirectorConfig.Provisional();
            cfg.MaxStreak = 0; // every strategy has "already run too often"
            WaveDirector d = Director(config, map, cfg: cfg);
            Simulation sim = Sim(config, map, d);
            WavePlan plan = d.PlanWave(new WaveContext(1, sim.State, map, config));
            Assert.AreEqual(WaveDirector.FallbackAllVetoed, d.LastTrace.Fallback);
            Assert.AreEqual(EscalationPlanner.Id, plan.StrategyId);
            Assert.AreEqual(new ThreatCostTable(config, map).BudgetForWave(1), plan.ThreatSpent, 1e-3f, "the fallback is priced too");
        }

        [Test]
        public void TheTraceHasOneRowPerStrategyInIdOrderAndTiesGoToTheLowestId()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var cfg = DirectorConfig.Provisional();
            cfg.HeuristicScale = 0f; // every surviving strategy scores 0: a tie
            WaveDirector d = Director(config, map, cfg: cfg);
            Simulation sim = Sim(config, map, d);
            WavePlan plan = d.PlanWave(new WaveContext(1, sim.State, map, config));
            Assert.AreEqual(5, d.LastTrace.Rows.Count);
            for (int i = 0; i < 5; i++) Assert.AreEqual(i, d.LastTrace.Rows[i].Id);
            Assert.AreEqual(BreachThinWall.StrategyName, plan.StrategyId);
            Assert.AreEqual(BreachThinWall.StrategyName, d.LastTrace.Chosen);
        }

        [Test]
        public void TooSimilarToPreviousVetoesAnIdenticalPlanAndAllowsADifferentOne()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            Simulation sim = Sim(config, map, new EscalationPlanner(config, map));
            IReadOnlyList<IWaveStrategy> pool = StrategyPool.Study(new StrategyKit(config, map, table));
            WavePlan swarm = pool[1].BuildPlan(table.BudgetForWave(4), new WaveContext(4, sim.State, map, config), new Pcg32(1UL, RngStreams.Strategy));
            WavePlan split = pool[2].BuildPlan(table.BudgetForWave(4), new WaveContext(4, sim.State, map, config), new Pcg32(1UL, RngStreams.Strategy));

            var rule = new TooSimilarToPrevious(0.1f);
            Assert.AreEqual(0f, TooSimilarToPrevious.Distance(swarm, swarm.Copy()), 1e-6f);
            Assert.Greater(TooSimilarToPrevious.Distance(swarm, split), 0.1f, "swarm and split differ in type and entries");
            Assert.AreEqual(TooSimilarToPrevious.Reason, rule.Check(swarm, swarm.Copy()));
            Assert.IsNull(rule.Check(split, swarm));
            Assert.IsNull(rule.Check(split, null), "nothing before wave 1");
        }

        [Test]
        public void UncounterableVetoesFlyersUntilAPoweredTowerCanHitThem()
        {
            const string board = @"
                S . . . . . . . . . . . S
                . . . . . . . . . . . . .
                . . . . . . C . . . . . .";
            var config = new SimConfig();
            Simulation sim = TestSims.Ascii(board, new EscalationPlanner(config, AsciiMap.Parse(board).Map), config);
            var rule = new Uncounterable();
            Assert.AreEqual(Uncounterable.Reason, rule.Check(FlyingBypass.StrategyName, sim.State));
            Assert.IsNull(rule.Check(SwarmChokepoint.StrategyName, sim.State), "only flyers need anti-air");

            var ground = new TowerState(1, TestTowers.Gun(), new TileCoord(5, 1), sim.State.Grid[5, 1].Position, sim.Map.NodeDiameter, 0);
            sim.State.Towers.Add(ground);
            Assert.AreEqual(Uncounterable.Reason, rule.Check(FlyingBypass.StrategyName, sim.State), "a tower that cannot hit flyers does not count");

            var archer = new TowerState(2, TestTowers.Gun(canHitFlying: true), new TileCoord(7, 1), sim.State.Grid[7, 1].Position, sim.Map.NodeDiameter, 0);
            archer.Powered = false;
            sim.State.Towers.Add(archer);
            Assert.AreEqual(Uncounterable.Reason, rule.Check(FlyingBypass.StrategyName, sim.State), "an unpowered one does not fire");
            archer.Powered = true;
            Assert.IsNull(rule.Check(FlyingBypass.StrategyName, sim.State));
        }

        [Test]
        public void RepeatStreakVetoesAThirdWaveInARow()
        {
            var rule = new RepeatStreak(2);
            Assert.IsNull(rule.Check("swarm_chokepoint", new[] { "split_groups", "swarm_chokepoint" }));
            Assert.AreEqual(RepeatStreak.Reason, rule.Check("swarm_chokepoint", new[] { "swarm_chokepoint", "swarm_chokepoint" }));
            Assert.IsNull(rule.Check("swarm_chokepoint", new[] { "swarm_chokepoint" }));
        }

        [Test]
        public void OverBudgetVetoesAPlanThatMissesTheBudget()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            WavePlan plan = new EscalationPlanner(config, map, table).PlanFor(3);
            var rule = new OverBudget();
            Assert.IsNull(rule.Check(plan, table.BudgetForWave(3), config));
            plan.ThreatSpent = (float)(plan.ThreatSpent * 1.5f);
            Assert.AreEqual(OverBudget.Reason, rule.Check(plan, table.BudgetForWave(3), config));
        }

        // ---- the heuristic layer ----

        private static BuildProfile Profile(float maze, float breach, float choke, float concentration, bool chokeData = true)
        {
            return new BuildProfile(BuildProfile.SchemaVersion,
                new[] { "maze_length", "breach_vulnerability", "chokepoint_reliance", "tower_concentration", "damage_type_mix" },
                new[] { maze, breach, choke, concentration, 0.5f },
                new[] { true, true, chokeData, true, true });
        }

        [Test]
        public void EachStrategyScoresItsOwnWeaknessFromZeroToFour()
        {
            var h = new AuthoredHeuristic(DirectorConfig.Provisional());
            BuildProfile low = Profile(1.0f, 0f, 0f, 0.5f);
            BuildProfile high = Profile(2.0f, 1f, 1f, 1f);
            for (int id = 0; id < 4; id++)
            {
                Assert.AreEqual(0f, h.Score(id, low), 1e-5f, "id " + id);
                Assert.AreEqual(4f, h.Score(id, high), 1e-5f, "id " + id);
            }
            Assert.Greater(h.Score(0, Profile(1.0f, 0.05f, 0f, 0.5f)), 0f, "breach answers breach_vulnerability");
            Assert.AreEqual(0f, h.Score(1, Profile(1.0f, 0.05f, 0f, 0.5f)), 1e-5f, "and nothing else does");
            Assert.Greater(h.Score(1, Profile(1.0f, 0f, 0.2f, 0.5f)), 0f, "swarm answers chokepoint_reliance");
            Assert.Greater(h.Score(2, Profile(1.0f, 0f, 0f, 0.85f)), 0f, "split answers tower_concentration");
            Assert.Greater(h.Score(3, Profile(1.2f, 0f, 0f, 0.5f)), 0f, "flying answers maze_length");
        }

        [Test]
        public void AFeatureWithNoDataScoresTheMiddle()
        {
            var h = new AuthoredHeuristic(DirectorConfig.Provisional());
            Assert.AreEqual(2f, h.Score(1, Profile(1f, 0f, 0.5f, 0.5f, chokeData: false)), 1e-5f);
        }

        // ---- the bucketizer (S1, provisional) ----

        [Test]
        public void TheBinIsChokepointRelianceCutAtItsProvisionalEdges()
        {
            var b = new ContextBucketizer(DirectorConfig.Provisional());
            Assert.AreEqual(0, b.Bin(Profile(1f, 0f, 0.01f, 0.8f)));
            // The edges widened on 2026-11-24: 0.125 / 0.224 / 0.352 / 0.484.
            Assert.AreEqual(1, b.Bin(Profile(1f, 0f, 0.15f, 0.8f)));
            Assert.AreEqual(2, b.Bin(Profile(1f, 0f, 0.30f, 0.8f)));
            Assert.AreEqual(3, b.Bin(Profile(1f, 0f, 0.40f, 0.8f)));
            Assert.AreEqual(4, b.Bin(Profile(1f, 0f, 0.6f, 0.8f)));
            Assert.AreEqual(1, b.Bin(Profile(1f, 0f, 0.125f, 0.8f)), "an edge belongs to the bin above it");
            Assert.AreEqual(2, b.Bin(Profile(1f, 0f, 0.5f, 0.8f, chokeData: false)), "no data: the middle bin, not the top");
        }

        [Test]
        public void TheKernelCoordinatesAreConcentrationAndMazeLengthNormalised()
        {
            DirectorConfig cfg = DirectorConfig.Provisional();
            var b = new ContextBucketizer(cfg);
            DecisionContext c = b.Context(Profile(cfg.RangeLow[cfg.RangeIndex(DirectorConfig.MazeLengthName)], 0f, 0.1f, cfg.RangeHigh[cfg.RangeIndex(DirectorConfig.TowerConcentrationName)]));
            Assert.AreEqual(1f, c.KernelX, 1e-4f, "tower_concentration at its 95 % point");
            Assert.AreEqual(0f, c.KernelY, 1e-4f, "maze_length at its 5 % point");
        }

        // ---- the announcement ----

        [Test]
        public void EveryStrategyHasALineAndTheStaticVersionHasOne()
        {
            foreach (string name in new[] { "breach_thin_wall", "swarm_chokepoint", "split_groups", "flying_bypass" })
                for (int bin = 0; bin < 5; bin++)
                    Assert.IsFalse(string.IsNullOrEmpty(AnnouncementTable.Line(AnnouncementTable.Id(name, bin))), name);
            Assert.IsFalse(string.IsNullOrEmpty(AnnouncementTable.StaticLine));
            Assert.IsNull(AnnouncementTable.Line("no-such-line"));
        }

        // ---- the ladder baselines ----

        [Test]
        public void TheRandomAndRoundRobinPlannersSpendTheBudgetAndRepeatForASeed()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            Simulation sim = Sim(config, map, new EscalationPlanner(config, map));
            var robin = new RoundRobinPlanner(config, map, table, 3UL);
            var seen = new List<string>();
            for (int w = 1; w <= 8; w++)
            {
                WavePlan p = robin.PlanWave(new WaveContext(w, sim.State, map, config));
                Assert.LessOrEqual(Math.Abs(p.ThreatSpent - table.BudgetForWave(w)), config.ThreatEpsilon * table.BudgetForWave(w));
                seen.Add(p.StrategyId);
            }
            CollectionAssert.AreEqual(new[] { "breach_thin_wall", "swarm_chokepoint", "split_groups", "flying_bypass", "raid_outposts", "breach_thin_wall", "swarm_chokepoint", "split_groups" }, seen);

            string a = "", b = "";
            var r1 = new RandomPlanner(config, map, table, 5UL);
            var r2 = new RandomPlanner(config, map, table, 5UL);
            for (int w = 1; w <= 8; w++)
            {
                a += r1.PlanWave(new WaveContext(w, sim.State, map, config)).StrategyId + " ";
                b += r2.PlanWave(new WaveContext(w, sim.State, map, config)).StrategyId + " ";
            }
            Assert.AreEqual(a, b);
        }

        // ---- the factory, and a real game ----

        [Test]
        public void ThePlannerFactoryMakesEveryPlannerByName()
        {
            var config = new SimConfig();
            MapData map = Generated();
            foreach (string name in PlannerFactory.Names)
            {
                IWavePlanner p = PlannerFactory.Create(name, config, map, 1UL);
                Assert.AreEqual(name, p.Name);
                Assert.AreEqual(name, Registry.CreatePlanner(name, config, map, 1UL).Name, "the harness uses the same factory");
            }
            Assert.Throws<ArgumentException>(() => PlannerFactory.Create("no-such-planner", config, map, 1UL));
        }

        [Test]
        public void TheHeuristicDirectorPlaysTheGameAndWritesItsDecisionToTelemetry()
        {
            SimConfig config = GameConfig();
            var o = new EpisodeOptions
            {
                Config = config, Map = GameMaps.Generate(3), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = 3, Planner = WaveDirector.HeuristicId, Policy = new BuilderPolicy(), MaxWaves = 8,
            };
            EpisodeResult r = EpisodeRunner.Run(o);
            Assert.GreaterOrEqual(r.WavesResolved, 3);
            var chosen = new HashSet<string>();
            foreach (WaveRecord row in r.Telemetry)
            {
                if (row.Plan == null) continue;
                Assert.IsNotNull(row.Profile, "wave " + row.Wave);
                Assert.IsNotNull(row.Bin, "wave " + row.Wave);
                Assert.AreEqual(5, row.Strategies.Length, "wave " + row.Wave);
                Assert.AreEqual(row.Chosen, row.Plan.Strategy, "wave " + row.Wave);
                Assert.IsFalse(string.IsNullOrEmpty(row.Announcement), "wave " + row.Wave);
                Assert.IsNull(row.DirectorMicros, "no wall-clock time in a row (CLAUDE.md §7)");
                chosen.Add(row.Chosen);
            }
            Assert.Greater(chosen.Count, 1, "the anti-repeat rule makes it vary");
        }

        [Test]
        public void TheDirectorsTelemetryIsMadeAgainFromTheReplayByteForByte()
        {
            SimConfig config = GameConfig();
            var o = new EpisodeOptions
            {
                Config = config, Map = GameMaps.Generate(4), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = 4, Planner = WaveDirector.HeuristicId, Policy = new BuilderPolicy(), MaxWaves = 5,
            };
            EpisodeResult r = EpisodeRunner.Run(o);
            List<WaveRecord> again = ReplayRunner.Telemetry(r.Replay);
            Assert.AreEqual(r.Telemetry.Count, again.Count);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(r.Telemetry[i].ToJsonLine(), again[i].ToJsonLine(), "row " + i);
        }

        // The student, 2026-11-24 (rule B3): flyers stay banned without anti-air for the first five
        // waves only; from wave 6 every wave type may come.
        [TestCase(1, true)] [TestCase(5, true)] [TestCase(6, false)] [TestCase(20, false)]
        public void WithoutAntiAirFlyersAreBannedOnlyUpToWaveFive(int wave, bool banned)
        {
            var config = new SimConfig();
            MapData map = Generated();
            WaveDirector d = Director(config, map);
            Simulation sim = Sim(config, map, d);
            d.PlanWave(new WaveContext(wave, sim.State, map, config));
            StrategyRow flying = d.LastTrace.Rows[FlyingBypassId];
            if (banned) Assert.AreEqual(Uncounterable.Reason, flying.Veto);
            else Assert.IsNull(flying.Veto, "wave " + wave);
        }

        // Rule B4, the student 2026-11-24: a wave like the last one is no longer banned. The two-in-a-row
        // limit (B2) still holds.
        [Test]
        public void AWaveLikeTheLastOneIsNoLongerBanned()
        {
            DirectorConfig cfg = DirectorConfig.Provisional();
            Assert.AreEqual(0f, cfg.SimilarityThreshold);
            var config = new SimConfig();
            MapData map = Generated();
            WaveDirector d = Director(config, map);
            Simulation sim = Sim(config, map, d);
            d.PlanWave(new WaveContext(1, sim.State, map, config));
            string first = d.LastTrace.Chosen;
            d.PlanWave(new WaveContext(2, sim.State, map, config));
            StrategyRow same = d.LastTrace.Rows.Find(r => r.Name == first);
            Assert.IsNull(same.Veto, "the same strategy may come twice in a row");
            d.PlanWave(new WaveContext(3, sim.State, map, config));
            if (d.LastTrace.Rows.Find(r => r.Name == first).Veto == null) Assert.AreNotEqual(first, d.TraceFor(2).Chosen, "unless it already came twice");
        }

        // The ranges widened on 2026-11-24 (Results/2026-11-24_director-ranges): the student's own board
        // in the 11-wave play-test no longer maxes out every score.
        [Test]
        public void TheStudentsPlayTestBoardNoLongerMaxesTheScores()
        {
            DirectorConfig cfg = DirectorConfig.Provisional();
            Assert.Less(cfg.Normalise(DirectorConfig.MazeLengthName, 1.354f), 1f);
            Assert.Less(cfg.Normalise(DirectorConfig.TowerConcentrationName, 0.97f), 1f);
            Assert.Less(cfg.Normalise(DirectorConfig.ChokepointRelianceName, 0.5f), 1f);
            Assert.Greater(cfg.Normalise(DirectorConfig.ChokepointRelianceName, 0.5f), cfg.Normalise(DirectorConfig.ChokepointRelianceName, 0.2f));
        }


        // ---- the wave as sent (the student, 2026-11-24) ----

        [TestCase(1, 2)] [TestCase(4, 2)] [TestCase(5, 3)] [TestCase(9, 4)] [TestCase(13, 5)] [TestCase(40, 5)]
        public void AWaveComesFromMoreEntriesAsTheGameGoesOn(int wave, int entries)
        {
            Assert.AreEqual(entries, StrategyKit.EntriesFor(wave));
        }

        [Test]
        public void TheChosenStrategyLeadsAndTheOthersSupportByScore()
        {
            var config = new SimConfig();
            MapData map = Generated();
            WaveDirector d = Director(config, map);
            Simulation sim = Sim(config, map, d);
            var table = new ThreatCostTable(config, map);
            for (int w = 1; w <= 12; w++)
            {
                WavePlan plan = d.PlanWave(new WaveContext(w, sim.State, map, config));
                DirectorTrace t = d.LastTrace;
                Assert.AreEqual(t.Chosen, plan.StrategyId, "the wave is named after its lead");
                StringAssert.StartsWith(t.Chosen + " ", t.Support[0], "the lead comes first");
                Assert.IsNull(PlanValidator.Check(plan, w, map, config, table), "wave " + w + ": the whole wave spends the budget");
                var types = new HashSet<string>();
                foreach (AgentGroup g in plan.Groups) types.Add(g.Archetype);
                Assert.GreaterOrEqual(types.Count, 2, "wave " + w + ": more than one enemy type");
                var used = new HashSet<int>();
                foreach (AgentGroup g in plan.Groups) used.Add(g.SpawnIndex);
                Assert.AreEqual(Math.Min(StrategyKit.EntriesFor(w), map.Spawns.Length), used.Count, "wave " + w + ": exactly as many entries as the static version's (found in play, 2026-11-24: 2 of 3)");
                int surviving = 0;
                foreach (StrategyRow r in t.Rows) if (r.Veto == null && r.Score > 0f) surviving++;
                if (surviving >= 2) Assert.GreaterOrEqual(t.Support.Count, 2, "wave " + w + ": others that scored support the lead");
            }
        }

        [Test]
        public void WithLeadShareOneTheWaveIsTheChosenStrategyAlone()
        {
            var config = new SimConfig();
            MapData map = Generated();
            DirectorConfig cfg = DirectorConfig.Provisional();
            cfg.LeadShare = 1f;
            WaveDirector d = Director(config, map, cfg: cfg);
            Simulation sim = Sim(config, map, d);
            WavePlan plan = d.PlanWave(new WaveContext(3, sim.State, map, config));
            Assert.AreEqual(1, d.LastTrace.Support.Count);
            foreach (AgentGroup g in plan.Groups) Assert.AreNotEqual(EnemyArchetypes.RaiderId, g.Archetype);
        }

        [Test]
        public void TheStaticVersionSpreadsTheEscalationsWalkersOverTheSameNumberOfEntries()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var table = new ThreatCostTable(config, map);
            var planner = new SpreadStaticPlanner(config, map, table, 4UL);
            Simulation sim = Sim(config, map, planner);
            var escalation = new EscalationPlanner(config, map, table);
            for (int w = 1; w <= 20; w++)
            {
                WavePlan plan = planner.PlanWave(new WaveContext(w, sim.State, map, config));
                WavePlan reference = escalation.PlanFor(w);
                var entries = new HashSet<int>();
                int count = 0;
                foreach (AgentGroup g in plan.Groups) { entries.Add(g.SpawnIndex); count += g.Count; Assert.AreEqual(EscalationPlanner.Archetype, g.Archetype); }
                Assert.AreEqual(Math.Min(StrategyKit.EntriesFor(w), map.Spawns.Length), entries.Count, "wave " + w);
                Assert.AreEqual(reference.Groups[0].Count, count, "the same walkers, shared out");
                Assert.AreEqual(table.Price(reference), plan.ThreatSpent, 1e-3f * plan.ThreatSpent, "the same price (I2)");
            }
        }
    }
}
