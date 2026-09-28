using System;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class PlannerContractTests
    {
        private const string TwoTiles = "S . . C";

        private static void StartWave(Simulation sim)
        {
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Tick();
        }

        [Test]
        public void APlannerThatWritesTheStateInPlanWaveIsRejected()
        {
            var sim = TestSims.Ascii(TwoTiles, new MutatingPlanner(MutatingPlanner.When.PlanWave));
            var e = Assert.Throws<InvalidOperationException>(() => StartWave(sim));
            StringAssert.Contains("PlanWave", e.Message);
            StringAssert.Contains("mutating", e.Message);
        }

        [Test]
        public void APlannerThatWritesTheStateInOnWaveResolvedIsRejected()
        {
            var sim = TestSims.Ascii(TwoTiles, new MutatingPlanner(MutatingPlanner.When.OnWaveResolved));
            StartWave(sim); // PlanWave is well-behaved this time
            var e = Assert.Throws<InvalidOperationException>(() => TestSims.Run(sim, 200));
            StringAssert.Contains("OnWaveResolved", e.Message);
        }

        [Test]
        public void NegativeCountIsRejected()
        {
            var sim = TestSims.Ascii(TwoTiles, new FixedPlanner(FixedPlanner.Group(-1, 10f)));
            var e = Assert.Throws<InvalidOperationException>(() => StartWave(sim));
            StringAssert.Contains("Count -1", e.Message);
        }

        [Test]
        public void SpawnIndexOutOfRangeIsRejected()
        {
            var sim = TestSims.Ascii(TwoTiles, new FixedPlanner(FixedPlanner.Group(1, 10f, spawnIndex: 1)));
            var e = Assert.Throws<InvalidOperationException>(() => StartWave(sim));
            StringAssert.Contains("spawn 1", e.Message);
        }

        [Test]
        public void ZeroLifetimeIsRejected()
        {
            var sim = TestSims.Ascii(TwoTiles, new FixedPlanner(FixedPlanner.Group(1, 0f)));
            Assert.Throws<InvalidOperationException>(() => StartWave(sim));
        }

        [Test]
        public void ThreatSpentThatMissesTheBudgetIsRejected()
        {
            // FakePricer: budget(wave 1) = 10, price = agent count.
            var planner = new FixedPlanner(FixedPlanner.Group(3, 10f)) { ThreatSpent = 3f };
            var sim = new Simulation(new SimConfig(), AsciiMap.Parse(TwoTiles).Map, TestShapes.SampleSceneLibrary(), 1, planner, new FakePricer());
            var e = Assert.Throws<InvalidOperationException>(() => StartWave(sim));
            StringAssert.Contains("threat budget", e.Message);
        }

        [Test]
        public void ThreatSpentThatDisagreesWithTheCostTableIsRejected()
        {
            // Claims to spend exactly the budget (10), but 3 agents price at 3.
            var planner = new FixedPlanner(FixedPlanner.Group(3, 10f)) { ThreatSpent = 10f };
            var sim = new Simulation(new SimConfig(), AsciiMap.Parse(TwoTiles).Map, TestShapes.SampleSceneLibrary(), 1, planner, new FakePricer());
            var e = Assert.Throws<InvalidOperationException>(() => StartWave(sim));
            StringAssert.Contains("prices it at 3", e.Message);
        }

        [Test]
        public void APlanThatSpendsExactlyTheBudgetIsAccepted()
        {
            var planner = new FixedPlanner(FixedPlanner.Group(10, 10f)) { ThreatSpent = 10f };
            var sim = new Simulation(new SimConfig(), AsciiMap.Parse(TwoTiles).Map, TestShapes.SampleSceneLibrary(), 1, planner, new FakePricer());
            Assert.DoesNotThrow(() => StartWave(sim));
            Assert.AreEqual(1, sim.State.WaveIndex);
        }

        [Test]
        public void ThePlanCannotBeEditedAfterItIsHandedOver()
        {
            var group = FixedPlanner.Group(1, 100f);
            var planner = new FixedPlanner(_ => new[] { group });
            var sim = TestSims.Ascii(TwoTiles, planner);
            StartWave(sim);

            group.Count = 999; // the planner's own object, edited after the fact
            Assert.AreEqual(1, sim.State.CurrentPlan.Groups[0].Count, "Simulation runs its own copy of the plan");
        }
    }
}
