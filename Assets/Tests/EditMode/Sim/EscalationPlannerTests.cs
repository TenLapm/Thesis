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

        // Hand-computed anchors, independent of the formula below. Count, spacing and
        // speed are the original WaveSpawner numbers; hit points (10, +2 a wave) are
        // the WP-C1 placeholder that replaced the growing lifetime.
        [TestCase(1, 100, 10, 10f, 1.25f)]
        [TestCase(2, 105, 9, 12f, 1.2875f)]
        [TestCase(4, 115, 8, 16f, 1.3625f)]      // 0.2 - 3*0.02 = 0.14 -> clamped to 0.15 -> 7.5 ticks -> 8
        [TestCase(27, 230, 8, 62f, 2.225f)]
        [TestCase(28, 235, 8, 64f, 2.25f)]       // 1.25 + 27*0.0375 = 2.2625 -> capped at 2.25
        [TestCase(30, 245, 8, 68f, 2.25f)]
        public void AnchorWavesMatchHandComputedValues(int wave, int count, int intervalTicks, float hp, float speed)
        {
            AgentGroup g = SampleScenePlanner().PlanFor(wave).Groups[0];

            Assert.AreEqual(count, g.Count, "count");
            Assert.AreEqual(intervalTicks, g.SpawnIntervalTicks, "spawn interval (ticks)");
            Assert.AreEqual(hp, g.Hp, "hit points");
            Assert.AreEqual(speed, g.MoveSpeed, 1e-5f, "move speed");
            Assert.AreEqual(1f, g.DigRate);
            Assert.AreEqual(0, g.SpawnIndex);
            Assert.AreEqual(EscalationPlanner.Archetype, g.Archetype);
        }

        [Test]
        public void Waves1To30EqualTheFormulasWithSampleSceneValues()
        {
            // The escalation formulas, written out with SampleScene's literal values -
            // so this also catches a wrong number in SimConfig. Written with the same
            // explicit casts the simulation uses: uncast, Unity's Mono evaluates
            // `1.25f + i * 0.0375f` in double and this very test failed under Unity at
            // wave 14 (1.73750007 vs 1.73749995) - see DEVLOG WP4.
            EscalationPlanner planner = SampleScenePlanner();

            for (int wave = 1; wave <= 30; wave++)
            {
                int i = wave - 1;
                int count = 100 + i * 5;
                float delay = Math.Max(0.15f, (float)(0.2f - (float)(i * 0.02f)));
                float speed = Math.Min(2.25f, (float)(1.25f + (float)(i * 0.0375f)));
                float hp = (float)(10f + (float)(i * 2f));

                WavePlan plan = planner.PlanFor(wave);
                AgentGroup g = plan.Groups[0];
                Assert.AreEqual(wave, plan.WaveIndex);
                Assert.AreEqual(EscalationPlanner.Id, plan.StrategyId);
                Assert.AreEqual(1, plan.Groups.Length);
                Assert.AreEqual(count, g.Count, "wave " + wave + " count");
                Assert.AreEqual((int)MathF.Round((float)(delay / 0.02f)), g.SpawnIntervalTicks, "wave " + wave + " interval");
                Assert.AreEqual(speed, g.MoveSpeed, "wave " + wave + " speed");
                Assert.AreEqual(hp, g.Hp, "wave " + wave + " hit points");
            }
        }

        [Test]
        public void EscalationEnemiesHaveNoResistancesOrWeaknesses()
        {
            AgentGroup g = SampleScenePlanner().PlanFor(5).Groups[0];
            Assert.AreEqual(DamageTypes.Count, g.Resist.Length);
            foreach (float r in g.Resist) Assert.AreEqual(1f, r);
        }

        [Test]
        public void EveryPlanItMakesIsValid()
        {
            var config = new SimConfig();
            MapData map = TestSims.SampleSceneMap();
            var planner = new EscalationPlanner(config, map);
            for (int wave = 1; wave <= 60; wave++)
            {
                Assert.IsNull(PlanValidator.Check(planner.PlanFor(wave), wave, map, config, null), "wave " + wave);
            }
        }

        [Test]
        public void ThePlanCopyOwnsItsResistances()
        {
            WavePlan plan = SampleScenePlanner().PlanFor(1);
            WavePlan copy = plan.Copy();
            Assert.AreNotSame(plan.Groups[0].Resist, copy.Groups[0].Resist);

            // A group that leaves Resist unset means "1 for every type".
            var bare = new AgentGroup { Count = 1, Hp = 5f, MoveSpeed = 1f };
            Assert.IsNull(bare.Resist);
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f }, bare.Copy().Resist);
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
