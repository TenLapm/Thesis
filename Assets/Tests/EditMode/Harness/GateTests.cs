using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Learning;
using Thesis.Learning.Synthetic;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // WP13: the gates measure what they say, the same way every time.
    public class GateTests
    {
        [Test]
        public void G1GivesTheNumberOfTheG1SyntheticRun()
        {
            // Results/2026-11-10_g1-synth: the learner 0.851 over 20 seeds, episodes 251 to 300.
            Gates.G1Result r = Gates.G1(20);
            Assert.That(r.Learner, Is.EqualTo(0.851).Within(0.0005));
            Assert.That(r.Learner, Is.GreaterThan(r.Random));
            Assert.That(r.Learner, Is.GreaterThan(r.RoundRobin));
            Assert.That(r.Verdict, Is.EqualTo("PASS"));
        }

        [Test]
        public void EpisodesToAccuracyIsTheFirstFullWindowAtTheG1Share()
        {
            var r = new SyntheticRun.Result { Best = new bool[200] };
            for (int e = 60; e < 200; e++) r.Best[e] = true;
            // The window [e - 49, e] needs 42.5 hits: the first is the window ending at episode 102 (60..102 = 43).
            Assert.That(Gates.EpisodesToAccuracy(r), Is.EqualTo(103));
            Assert.That(Gates.EpisodesToAccuracy(new SyntheticRun.Result { Best = new bool[80] }), Is.EqualTo(80), "never: the run's length");
        }

        [Test]
        public void TheBinaryControlIsRestructureAtTauAndSurvived()
        {
            DirectorConfig cfg = DirectorConfig.Provisional();
            var env = new ThresholdBanditEnv(new ThresholdBanditEnv.Options());
            var rng = new Pcg32(4UL, 2UL);
            int ones = 0;
            for (int i = 0; i < 2000; i++)
            {
                float b = Gates.G3Reward(true, env.BestArm(0.1, 0), 0.1, 0, rng, env, cfg);
                Assert.That(b == 0f || b == 1f);
                if (b == 1f) ones++;
            }
            // P(raw >= 0.2) for raw evenly in [0, 0.28] is 0.286, times survival 0.85: about 0.24.
            Assert.That(ones / 2000.0, Is.EqualTo(0.243).Within(0.03));
            for (int i = 0; i < 200; i++) Assert.That(Gates.G3Reward(true, 3, 0.1, 0, rng, env, cfg), Is.EqualTo(0f), "another arm never reaches tau (at most 0.16)");
        }

        [Test]
        public void G3IsTheSameEveryRun()
        {
            Gates.G3Result a = Gates.G3(seeds: 3, episodes: 600), b = Gates.G3(seeds: 3, episodes: 600);
            CollectionAssert.AreEqual(a.Graduated, b.Graduated);
            CollectionAssert.AreEqual(a.Binary, b.Binary);
        }

        [Test]
        public void G2TimesEveryDecisionOfTheDirector()
        {
            SimConfig config = Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json")));
            Gates.G2Result r = Gates.G2(config, TestSims.GameShapes(), TestSims.GameTowers(), new ulong[] { 109 }, 3);
            Assert.That(r.Decisions, Is.GreaterThanOrEqualTo(3));
            Assert.That(r.MaxMs, Is.GreaterThanOrEqualTo(r.MedianMs));
        }
    }
}
