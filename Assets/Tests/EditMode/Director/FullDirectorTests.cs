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
    // WP11: the reward, its credit, the thin-cell prior and the full director. The numbers are the
    // student's provisional answers of 2026-11-18 (S2 to S5).
    public class FullDirectorTests
    {
        private static MapData Generated(ulong seed = 7) { return MapGenerator.Generate(seed, 48, 48, deposits: 25); }

        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        // Logs every call in order; can be told to stay cold for ever.
        private sealed class LogEstimator : IStrategyEstimator
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<float> Weights = new List<float>();
            public readonly List<int> Bins = new List<int>();
            public int BinCount => 5;
            public int StrategyCount => 4;
            public Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng)
            {
                Log.Add("sample " + strategy);
                return new Correction { Theta = 0.5f, Alpha = 1f, Beta = 1f, Value = 0f, Cold = true };
            }
            public void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng)
            {
                Log.Add("update " + strategy);
                Weights.Add(creditWeight);
            }
            public EstimatorState Export() { return new BinnedPosterior(5, 5).Export(); } // five strategies since the raid
            public void Import(EstimatorState state) { }
        }

        private static WaveDirector Full(SimConfig config, MapData map, IStrategyEstimator estimator, DirectorConfig cfg = null)
        {
            return new WaveDirector(cfg ?? DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), 1UL, DirectorMode.Full, estimator);
        }

        private static WaveOutcome Outcome(int wave, int coreAfter = 10, float pressure = 0.5f)
        {
            return new WaveOutcome { WaveIndex = wave, CoreHpBefore = 10, CoreHpAfter = coreAfter, PressureShare = pressure };
        }

        // A board with anti-air, so all four strategies are open.
        private static Simulation Board(SimConfig config, MapData map, IWavePlanner planner)
        {
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), 1UL, planner);
            var archer = new TowerState(1, TestTowers.Gun(canHitFlying: true), map.Core, sim.State.Grid.Get(map.Core).Position, map.NodeDiameter, 0);
            sim.State.Towers.Add(archer);
            return sim;
        }

        // ---- sequencing (the must-pass) ----

        [Test]
        public void TheRewardForAWaveIsComputedInsideTheNextPlanBeforeTheDecisionAndOnlyOnceItHasResolved()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var log = new LogEstimator();
            WaveDirector d = Full(config, map, log);
            Simulation sim = Board(config, map, d);
            Func<int, WaveContext> ctx = w => new WaveContext(w, sim.State, map, config);

            d.PlanWave(ctx(1));
            CollectionAssert.DoesNotContain(log.Log, "update 0", "nothing to reward before wave 1");
            Assert.IsFalse(log.Log.Exists(s => s.StartsWith("update")));

            log.Log.Clear();
            d.PlanWave(ctx(2)); // wave 1 has not resolved yet
            Assert.IsFalse(log.Log.Exists(s => s.StartsWith("update")), "an unresolved wave earns nothing");
            Assert.IsNull(d.RewardFor(1));

            d.OnWaveResolved(Outcome(1));
            log.Log.Clear();
            d.PlanWave(ctx(2)); // planned again (as after a core move): now wave 1 has resolved
            Assert.IsNotNull(d.RewardFor(1));
            int lastUpdate = log.Log.FindLastIndex(s => s.StartsWith("update"));
            int firstSample = log.Log.FindIndex(s => s.StartsWith("sample"));
            Assert.GreaterOrEqual(lastUpdate, 0, "wave 1 was credited");
            Assert.Less(lastUpdate, firstSample, "the update comes before the decision for wave 2");

            log.Log.Clear();
            d.PlanWave(ctx(2)); // and again: wave 1 is not credited twice
            Assert.IsFalse(log.Log.Exists(s => s.StartsWith("update")));
        }

        [Test]
        public void CreditGoesBackTwoWavesWithWeightsOneHalfAndAQuarter()
        {
            var config = new SimConfig();
            MapData map = Generated();
            var log = new LogEstimator();
            WaveDirector d = Full(config, map, log);
            Simulation sim = Board(config, map, d);
            for (int w = 1; w <= 4; w++)
            {
                d.PlanWave(new WaveContext(w, sim.State, map, config));
                d.OnWaveResolved(Outcome(w));
            }
            log.Weights.Clear();
            d.PlanWave(new WaveContext(5, sim.State, map, config));
            CollectionAssert.AreEqual(new[] { 1f, 0.5f, 0.25f }, log.Weights, "waves 4, 3 and 2; nothing for wave 1");

            RewardRecord r = d.RewardFor(4);
            Assert.AreEqual(3, r.Credited.Count);
            Assert.AreEqual(4, r.Credited[0].Wave);
            Assert.AreEqual(3, r.Credited[1].Wave);
            Assert.AreEqual(2, r.Credited[2].Wave);
        }

        // ---- the reward (S4, S5) ----

        private static BuildProfile Profile(float maze, float breach, float concentration, float choke = 0.1f)
        {
            return new BuildProfile(BuildProfile.SchemaVersion,
                new[] { "maze_length", "breach_vulnerability", "chokepoint_reliance", "tower_concentration", "damage_type_mix" },
                new[] { maze, breach, choke, concentration, 0.5f },
                new[] { true, true, true, true, true });
        }

        [Test]
        public void RestructureIsTheMeanChangeOfTheBuiltFeaturesCappedAtTau()
        {
            var cfg = DirectorConfig.Provisional();
            float maze = cfg.RangeLow[cfg.RangeIndex(DirectorConfig.MazeLengthName)];
            float low = cfg.RangeLow[cfg.RangeIndex(DirectorConfig.TowerConcentrationName)];
            float high = cfg.RangeHigh[cfg.RangeIndex(DirectorConfig.TowerConcentrationName)];
            BuildProfile a = Profile(maze, 0f, low);
            Assert.AreEqual(0f, RewardEvaluator.Restructure(a, a, cfg), 1e-6f);
            // tower_concentration over its whole range, the others unchanged: a third of 1.
            BuildProfile b = Profile(maze, 0f, high);
            Assert.AreEqual(1f / 3f, RewardEvaluator.Restructure(a, b, cfg), 1e-5f);
            // chokepoint_reliance is about last wave's damage, not the build: it does not count.
            Assert.AreEqual(0f, RewardEvaluator.Restructure(a, Profile(maze, 0f, low, choke: 0.9f), cfg), 1e-6f);
        }

        [Test]
        public void TheGraduatedRewardWeighsItsFourParts()
        {
            var cfg = DirectorConfig.Provisional();
            BuildProfile a = Profile(1.056f, 0f, 0.763f);
            BuildProfile b = Profile(1.056f, 0f, 0.933f); // restructure 1/3, above tau 0.2: full credit
            RewardBreakdown r = RewardEvaluator.Evaluate(a, b, Outcome(3, coreAfter: 4, pressure: 0.5f), null, null, cfg);
            Assert.AreEqual(1f, r.Restructure, 1e-6f);
            Assert.AreEqual(1f, r.Survived);
            Assert.AreEqual(0.5f, r.Pressure, 1e-6f);
            Assert.AreEqual(0f, r.Novelty, "no previous wave to differ from");
            Assert.AreEqual(0.45f + 0.25f + 0.20f * 0.5f, r.Total, 1e-5f);
        }

        [Test]
        public void TheBinaryRewardIsRestructuredAndSurvivedInAllFourCases()
        {
            var cfg = DirectorConfig.Provisional();
            cfg.Reward = RewardMode.Binary;
            BuildProfile a = Profile(1.056f, 0f, 0.763f);
            BuildProfile big = Profile(1.056f, 0f, 0.933f);   // 1/3 >= tau
            BuildProfile small = Profile(1.056f, 0f, 0.773f); // a little, below tau
            Assert.AreEqual(1, RewardEvaluator.Evaluate(a, big, Outcome(2, coreAfter: 5), null, null, cfg).Binary);
            Assert.AreEqual(0, RewardEvaluator.Evaluate(a, big, Outcome(2, coreAfter: 0), null, null, cfg).Binary);
            Assert.AreEqual(0, RewardEvaluator.Evaluate(a, small, Outcome(2, coreAfter: 5), null, null, cfg).Binary);
            Assert.AreEqual(0, RewardEvaluator.Evaluate(a, small, Outcome(2, coreAfter: 0), null, null, cfg).Binary);
            Assert.AreEqual(1f, RewardEvaluator.Evaluate(a, big, Outcome(2, coreAfter: 5), null, null, cfg).Total, "binary mode pays the binary reward");
        }

        // ---- the thin-cell prior (S2) and partial credit (S3) ----

        private static DecisionContext At(int bin, float x = 0.5f, float y = 0.5f) { return new DecisionContext(bin, x, y); }

        [Test]
        public void ACellWithNoResultsAndNoKernelDataNearIsColdAndNudgesNothing()
        {
            var e = new KernelPriorEstimator();
            Correction c = e.SampleCorrection(At(2), 1, new Pcg32(1UL, 4UL));
            Assert.IsTrue(c.Cold);
            Assert.AreEqual(0f, c.Value);
        }

        [Test]
        public void KernelDataNearAThinCellIsAddedOnTopWorthTwoResults()
        {
            var e = new KernelPriorEstimator();
            var rng = new Pcg32(2UL, 5UL);
            // Results for strategy 1 in ANOTHER bin, at the same kernel point: the kernel has them,
            // bin 3's own cell has none.
            for (int i = 0; i < 6; i++) e.Update(At(0), 1, 1f, 1f, rng);
            Correction c = e.SampleCorrection(At(3), 1, rng);
            Assert.IsFalse(c.Cold, "kernel data near: not cold");
            Assert.AreEqual(1f + KernelPriorEstimator.DefaultPriorWeight * 1f, c.Alpha, 1e-4f, "alpha 1 + k * mean (mean 1)");
            Assert.AreEqual(1f, c.Beta, 1e-4f);
        }

        [Test]
        public void TheKernelPriorStopsOnceACellHasFourResults()
        {
            var e = new KernelPriorEstimator();
            var rng = new Pcg32(3UL, 5UL);
            for (int i = 0; i < KernelPriorEstimator.DefaultThinBelow; i++) e.Update(At(2), 1, 1f, 1f, rng);
            Correction c = e.SampleCorrection(At(2), 1, rng);
            var inner = e.Cells;
            Assert.AreEqual((float)inner.Alpha(2, 1), c.Alpha, 1e-5f, "the cell's own counts only");
        }

        [Test]
        public void TheCorrectionStaysWithinPlusMinusOne()
        {
            var e = new KernelPriorEstimator();
            var rng = new Pcg32(4UL, 4UL);
            for (int i = 0; i < 200; i++)
            {
                e.Update(At(i % 5, (i % 7) / 7f, (i % 3) / 3f), i % 4, (i % 3) / 2f, i % 2 == 0 ? 1f : 0.5f, rng);
                Correction c = e.SampleCorrection(At(i % 5), i % 4, rng);
                Assert.LessOrEqual(Math.Abs(c.Value), 1f);
                if (c.Cold) Assert.AreEqual(0f, c.Value);
            }
        }

        [Test]
        public void PartialCreditIsAFractionalUpdate()
        {
            // S3: alpha = 1 + G(alpha - 1) + w * r~, beta = 1 + G(beta - 1) + w * (1 - r~).
            var p = new BinnedPosterior();
            p.Update(At(1), 2, 1f, 0.5f, new Pcg32(1UL, 5UL)); // r = 1: r~ is 1 whatever the draw
            Assert.AreEqual(1.5, p.Alpha(1, 2), 1e-12);
            Assert.AreEqual(1.0, p.Beta(1, 2), 1e-12);
            p.Update(At(1), 2, 0f, 0.25f, new Pcg32(1UL, 5UL)); // r = 0
            Assert.AreEqual(1.0 + 0.95 * 0.5, p.Alpha(1, 2), 1e-12);
            Assert.AreEqual(1.25, p.Beta(1, 2), 1e-12);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Update(At(1), 2, 1f, 0f, new Pcg32(1UL, 5UL)));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Update(At(1), 2, 1f, 1.5f, new Pcg32(1UL, 5UL)));
        }

        [Test]
        public void TheEstimatorStateRoundTripsWithItsKernel()
        {
            var e = new KernelPriorEstimator();
            var rng = new Pcg32(9UL, 5UL);
            for (int i = 0; i < 20; i++) e.Update(At(i % 5, i / 20f, 0.3f), i % 4, (i % 2), 1f, rng);
            EstimatorState s = Json.Deserialize<EstimatorState>(Json.Serialize(e.Export()));
            var f = new KernelPriorEstimator();
            f.Import(s);
            Assert.AreEqual(Json.Serialize(e.Export()), Json.Serialize(f.Export()));
        }

        // ---- the full director in the game ----

        [Test]
        public void HeuristicOnlyAndFullWithAnAlwaysColdEstimatorChooseIdentically()
        {
            // The I4 ablation sanity check.
            SimConfig config = GameConfig();
            var chosen = new List<string>[2];
            for (int k = 0; k < 2; k++)
            {
                MapData map = GameMaps.Generate(5);
                IWavePlanner planner = k == 0
                    ? (IWavePlanner)new WaveDirector(DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), 5UL, DirectorMode.HeuristicOnly)
                    : new WaveDirector(DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), 5UL, DirectorMode.Full, new LogEstimator());
                var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 5UL, planner);
                var policy = new BuilderPolicy();
                var rng = new Pcg32(5UL, RngStreams.Policy);
                Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };
                chosen[k] = new List<string>();
                bool built = false;
                for (int t = 0; t < 400000 && !sim.State.IsGameOver && sim.State.WaveIndex < 8; t++)
                {
                    SimState s = sim.State;
                    bool phase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                    if (phase && !built && s.NextPlan != null)
                    {
                        policy.OnIntermission(sim, send, rng);
                        if (sim.State.NextPlan == null) continue;
                        built = true;
                        chosen[k].Add(sim.State.NextPlan.StrategyId);
                        send(SimCommand.StartWaveNow());
                    }
                    else if (!phase) built = false;
                    sim.Tick();
                }
            }
            Assert.GreaterOrEqual(chosen[0].Count, 4);
            CollectionAssert.AreEqual(chosen[0], chosen[1]);
        }

        [Test]
        public void AFullDirectorSessionWritesItsRewardToTelemetryAndReplaysFromItsSetup()
        {
            var o = new EpisodeOptions
            {
                Config = GameConfig(), Map = GameMaps.Generate(6), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = 6, Planner = WaveDirector.FullId, Policy = new BuilderPolicy(), MaxWaves = 6,
            };
            EpisodeResult r = EpisodeRunner.Run(o);
            Assert.IsNotNull(r.Replay.PlannerSetup, "the director's config and starting estimator are in the replay");

            int rewarded = 0;
            foreach (WaveRecord row in r.Telemetry)
            {
                if (row.Plan == null) continue;
                if (row.Reward != null)
                {
                    rewarded++;
                    Assert.GreaterOrEqual(row.Reward.Total, 0f);
                    Assert.LessOrEqual(row.Reward.Total, 1f);
                    Assert.AreEqual(row.Wave, row.Reward.Credited[0].Wave);
                }
                Assert.IsNotNull(row.EstimatorDigest, "wave " + row.Wave);
                foreach (WaveRecord.StrategyScore s in row.Strategies)
                    if (s.Veto == null) Assert.IsNotNull(s.Correction, "the full director asks the learner");
            }
            Assert.GreaterOrEqual(rewarded, 3, "every wave followed by another decision is rewarded");

            ReplayFile file = ReplayFile.FromJson(r.Replay.ToJson());
            PlanComparison c = ReplayRunner.CompareRebuiltPlanner(file);
            Assert.IsTrue(c.Ok, c.ToString());
            List<WaveRecord> again = ReplayRunner.Telemetry(file);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(r.Telemetry[i].ToJsonLine(), again[i].ToJsonLine(), "row " + i);
        }

        [Test]
        public void ADirectorStartedFromATrainedEstimatorIsRebuiltFromThatSnapshot()
        {
            // The transfer condition: a director seeded from another session's estimator. The replay
            // keeps the state it started from, so a re-run starts there too.
            var trained = new KernelPriorEstimator(strategies: 5); // the pool's five (the raid since 2026-11-24)
            var rng = new Pcg32(12UL, 5UL);
            for (int i = 0; i < 40; i++) trained.Update(At(i % 5, (i % 9) / 9f, (i % 4) / 4f), i % 4, (i % 3) / 2f, 1f, rng);

            SimConfig config = GameConfig();
            MapData map = GameMaps.Generate(8);
            var start = new KernelPriorEstimator(strategies: 5);
            start.Import(trained.Export());
            var d = new WaveDirector(DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), 8UL, DirectorMode.Full, start);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 8UL, d);
            var recorder = new ReplayRecorder(sim, false) { Policy = "builder" };
            var policy = new BuilderPolicy();
            var prng = new Pcg32(8UL, RngStreams.Policy);
            Action<SimCommand> send = c => { recorder.OnCommand(c); sim.Enqueue(c); sim.FlushInput(); };
            bool built = false;
            for (int t = 0; t < 400000 && !sim.State.IsGameOver && sim.State.WaveIndex < 5; t++)
            {
                SimState s = sim.State;
                bool phase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (phase && !built && s.NextPlan != null)
                {
                    policy.OnIntermission(sim, send, prng);
                    if (sim.State.NextPlan == null) continue;
                    built = true;
                    send(SimCommand.StartWaveNow());
                }
                else if (!phase) built = false;
                sim.Tick();
                recorder.AfterTick();
            }
            ReplayFile file = ReplayFile.FromJson(recorder.Snapshot().ToJson());
            PlanComparison c = ReplayRunner.CompareRebuiltPlanner(file);
            Assert.IsTrue(c.Ok, c.ToString());
        }
    }
}
