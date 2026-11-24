using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // WP12's must-pass: the counterfactual leaves the real game alone, the ladder's file is the same
    // on one core and on all, and a result without a hypothesis is refused.
    public class LadderTests
    {
        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static Simulation FullDirectorGameAtWave(int wave, out WaveDirector director)
        {
            SimConfig config = GameConfig();
            MapData map = GameMaps.Generate(11);
            director = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, config, map, 11UL);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 11UL, director);
            var policy = new BuilderPolicy();
            var rng = new Pcg32(11UL, RngStreams.Policy);
            Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };
            bool built = false;
            for (int t = 0; t < 400000 && !sim.State.IsGameOver; t++)
            {
                SimState s = sim.State;
                bool phase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (phase && s.WaveIndex >= wave && s.NextPlan != null) break;
                if (phase && !built && s.NextPlan != null)
                {
                    policy.OnIntermission(sim, send, rng);
                    if (sim.State.NextPlan == null) continue;
                    built = true;
                    send(SimCommand.StartWaveNow());
                }
                else if (!phase) built = false;
                sim.Tick();
            }
            return sim;
        }

        private static string Digest(WaveDirector d) { return Json.Serialize(d.ExportEstimator()); }

        [Test]
        public void ACounterfactualLeavesTheGameAndItsDirectorAlone()
        {
            Simulation live = FullDirectorGameAtWave(3, out WaveDirector director);
            ulong hash = live.ComputeHash();
            string learned = Digest(director);

            // The same kind of director, new, plays three waves on from here.
            var own = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, live.Config, live.Map, 11UL);
            List<WaveOutcome> played = Counterfactual.PlayOn(live, own, new BuilderPolicy(), 3, 11UL);

            Assert.That(played.Count, Is.GreaterThan(0), "the copy played on");
            Assert.That(live.ComputeHash(), Is.EqualTo(hash), "the real game is where it was");
            Assert.That(Digest(director), Is.EqualTo(learned), "and its director learned nothing from the copy");
        }

        [Test]
        public void ACounterfactualRefusesTheLiveDirector()
        {
            Simulation live = FullDirectorGameAtWave(1, out WaveDirector director);
            Assert.Throws<ArgumentException>(() => Counterfactual.PlayOn(live, director, new BuilderPolicy(), 1, 1UL));
        }

        private static Ladder.Spec Small(int threads)
        {
            return new Ladder.Spec
            {
                Config = GameConfig(), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seeds = new ulong[] { 109, 116 }, MaxWaves = 4, Threads = threads,
            };
        }

        [Test]
        public void TheLadderIsTheSameOnOneCoreAndOnAll()
        {
            string one = Ladder.Fingerprint(Ladder.Run(Small(1)));
            string all = Ladder.Fingerprint(Ladder.Run(Small(Math.Max(2, Environment.ProcessorCount))));
            Assert.That(all, Is.EqualTo(one));
            StringAssert.Contains("reactive full 116", one);
        }

        [Test]
        public void EveryRungAndPlayerIsMeasuredInTheSameWindow()
        {
            Ladder.Spec spec = Small(Environment.ProcessorCount);
            List<Ladder.Episode> episodes = Ladder.Run(spec);
            Assert.That(episodes.Count, Is.EqualTo(Ladder.Rungs.Length * Ladder.Policies.Length * 2));
            foreach (Ladder.Episode e in episodes)
            {
                Assert.That(e.Window, Is.GreaterThanOrEqualTo(1));
                Assert.That(e.Window, Is.LessThanOrEqualTo(Math.Max(1, PlacementEntropy.CompletedWaves(e.Rows))), e.Rung + " " + e.Policy);
                Assert.That(e.Entropy, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(e.Profiles.Count, Is.GreaterThan(0));
                if (e.Rung == WaveDirector.FullId || e.Rung == WaveDirector.HeuristicId) Assert.That(e.DirectorMicros.Count, Is.GreaterThan(0), "the director's timings are kept for G2");
            }
            List<Ladder.RungSummary> summary = Ladder.Summarise(episodes, spec);
            Assert.That(summary.Count, Is.EqualTo(Ladder.Rungs.Length * Ladder.Policies.Length));
        }

        [Test]
        public void AResultWithoutAHypothesisIsNotWritten()
        {
            string path = Path.Combine(Path.GetTempPath(), "thesis-result-" + Guid.NewGuid().ToString("N"), "README.md");
            var r = new ResultWriter { Title = "t", Date = "2026-11-24", Command = "thesis ladder", Hypothesis = "  " };
            r.Table("rungs", new[] { "a" }, new[] { new[] { "1" } });
            Assert.Throws<InvalidOperationException>(() => r.Write(path));
            Assert.That(File.Exists(path), Is.False);

            r.Hypothesis = "random < escalation < heuristic < full";
            r.Write(path);
            string text = File.ReadAllText(path);
            Assert.That(text.IndexOf("## Hypothesis", StringComparison.Ordinal), Is.LessThan(text.IndexOf("| a |", StringComparison.Ordinal)), "the hypothesis comes before the table");
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        [Test]
        public void TheReactivePlayerRebuildsMoreThanTheBuilder()
        {
            Ladder.Spec spec = Small(Environment.ProcessorCount);
            spec.Rungs = new[] { SpreadStaticPlanner.Id, WaveDirector.FullId };
            spec.MaxWaves = 8;
            List<Ladder.RungSummary> s = Ladder.Summarise(Ladder.Run(spec), spec);
            double builder = 0, reactive = 0;
            foreach (Ladder.RungSummary r in s) { if (r.Policy == BuilderPolicy.Id) builder += r.Restructure; else reactive += r.Restructure; }
            Assert.That(reactive, Is.GreaterThan(builder), "the reason the player exists");
        }

        [Test]
        public void TheReactivePlayerReplaysExactly()
        {
            EpisodeResult r = EpisodeRunner.Run(new EpisodeOptions
            {
                Config = GameConfig(), Map = GameMaps.Generate(116), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = 116, Planner = WaveDirector.FullId, Policy = new ReactivePolicy(), MaxWaves = 6,
            });
            ReplayReport report = ReplayRunner.Verify(TestEpisodes.Reload(r.Replay), perTick: false);
            Assert.IsTrue(report.Ok, report.Describe());
        }
    }
}
