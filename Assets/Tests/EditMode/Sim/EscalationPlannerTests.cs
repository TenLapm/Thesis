using System;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class EscalationPlannerTests
    {
        private static EscalationPlanner SampleScenePlanner()
        {
            return new EscalationPlanner(new SimConfig(), TestSims.SampleSceneMap());
        }

        [Test]
        public void BaseLifeTimeIsDerivedFromTheSpawnToCoreDistance()
        {
            // SampleScene: spawn (-32, 2, -32), core (32, 2, 32).
            // sqrt(64^2 + 64^2) = 90.50967; / 1.25 = 72.40773; + 5 = 77.40773.
            // The serialized baseLifeTime of 60 is overwritten at Start() by this.
            Assert.AreEqual(77.40773f, SampleScenePlanner().BaseLifeTime, 1e-4f);
        }

        // Hand-computed anchors, independent of the formula below.
        [TestCase(1, 100, 10, 77.40773f, 1.25f)]
        [TestCase(2, 105, 9, 81.40773f, 1.2875f)]
        [TestCase(4, 115, 8, 89.40773f, 1.3625f)]      // 0.2 - 3*0.02 = 0.14 -> clamped to 0.15 -> 7.5 ticks -> 8
        [TestCase(27, 230, 8, 181.40773f, 2.225f)]
        [TestCase(28, 235, 8, 185.40773f, 2.25f)]      // 1.25 + 27*0.0375 = 2.2625 -> capped at 2.25
        [TestCase(30, 245, 8, 193.40773f, 2.25f)]
        [TestCase(32, 255, 8, 200f, 2.25f)]            // 77.4 + 31*4 = 201.4 -> capped at 200
        public void AnchorWavesMatchHandComputedValues(int wave, int count, int intervalTicks, float life, float speed)
        {
            AgentGroup g = SampleScenePlanner().PlanFor(wave).Groups[0];

            Assert.AreEqual(count, g.Count, "count");
            Assert.AreEqual(intervalTicks, g.SpawnIntervalTicks, "spawn interval (ticks)");
            Assert.AreEqual(life, g.LifeTime, 1e-3f, "lifetime");
            Assert.AreEqual(speed, g.MoveSpeed, 1e-5f, "move speed");
            Assert.AreEqual(1f, g.DigRate);
            Assert.AreEqual(0, g.SpawnIndex);
        }

        [Test]
        public void Waves1To30EqualTheWaveSpawnerFormulasWithSampleSceneValues()
        {
            // The SpawnTrickleRoutine formulas, written out with SampleScene's literal
            // serialized values - so this also catches a wrong number in SimConfig.
            // Written with the same explicit casts the simulation uses: uncast, Unity's
            // Mono evaluates `1.25f + i * 0.0375f` in double and this very test failed
            // under Unity at wave 14 (1.73750007 vs 1.73749995) - see DEVLOG WP4.
            EscalationPlanner planner = SampleScenePlanner();
            float baseLife = planner.BaseLifeTime;

            for (int wave = 1; wave <= 30; wave++)
            {
                int i = wave - 1;
                int count = 100 + i * 5;
                float delay = Math.Max(0.15f, (float)(0.2f - (float)(i * 0.02f)));
                float life = Math.Min(200f, (float)(baseLife + (float)(i * 4f)));
                float speed = Math.Min(2.25f, (float)(1.25f + (float)(i * 0.0375f)));

                WavePlan plan = planner.PlanFor(wave);
                AgentGroup g = plan.Groups[0];
                Assert.AreEqual(wave, plan.WaveIndex);
                Assert.AreEqual(EscalationPlanner.Id, plan.StrategyId);
                Assert.AreEqual(count, g.Count, "wave " + wave + " count");
                Assert.AreEqual((int)MathF.Round((float)(delay / 0.02f)), g.SpawnIntervalTicks, "wave " + wave + " interval");
                Assert.AreEqual(life, g.LifeTime, "wave " + wave + " life");
                Assert.AreEqual(speed, g.MoveSpeed, "wave " + wave + " speed");
            }
        }

        [Test]
        public void PricerFillsThreatSpentWhenPresent()
        {
            var planner = new EscalationPlanner(new SimConfig(), TestSims.SampleSceneMap(), new FakePricer());
            Assert.AreEqual(100f, planner.PlanFor(1).ThreatSpent, "FakePricer prices a plan at its agent count");
            Assert.AreEqual(0f, SampleScenePlanner().PlanFor(1).ThreatSpent, "no cost table -> 0");
        }

        [Test]
        public void RejectsWaveZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SampleScenePlanner().PlanFor(0));
        }
    }
}
