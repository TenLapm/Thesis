using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // ARCHITECTURE.md §6: "a participant session never crashes because of the
    // director". SafePlanner is where that promise is kept.
    public class SafePlannerTests
    {
        private const string Map = @"
            S . . . . .
            . . . . . .
            . . . . . C";

        private static SimConfig Config() { return new SimConfig { PrepSeconds = 0.1f, IntermissionSeconds = 0.1f, AgentsPerWave = 2, CoreMaxHp = 1000 }; }

        private static FixedPlanner Fixed() { return new FixedPlanner(FixedPlanner.Group(count: 2, life: 0.3f, speed: 1f)); }

        private sealed class Rig
        {
            public Simulation Sim;
            public SafePlanner Safe;
            public FlakyPlanner Flaky;
            public List<string> Log = new List<string>();
            public List<string> Strategies = new List<string>();

            public void RunWaves(int waves)
            {
                for (int i = 0; i < 5000 && Strategies.Count < waves; i++)
                {
                    Sim.Tick();
                    foreach (SimEvent e in Sim.LastTickEvents)
                        if (e.Kind == SimEventKind.WaveResolved) Strategies.Add(Sim.State.LastOutcome.StrategyId);
                }
                Assert.AreEqual(waves, Strategies.Count, "waves resolved");
            }
        }

        private static Rig Build(Action<FlakyPlanner> setup)
        {
            SimConfig config = Config();
            MapData map = AsciiMap.Parse(Map).Map;
            var rig = new Rig { Flaky = new FlakyPlanner(Fixed()) };
            setup(rig.Flaky);
            rig.Safe = new SafePlanner(rig.Flaky, new EscalationPlanner(config, map), map, config) { OnFallback = rig.Log.Add };
            rig.Sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), 1, rig.Safe);
            return rig;
        }

        [Test]
        public void AnExceptionInPlanWaveBecomesAFallbackWave()
        {
            Rig rig = Build(f => f.ThrowOnWave = wave => wave == 2);
            rig.RunWaves(3);

            CollectionAssert.AreEqual(new[] { "fixed", EscalationPlanner.Id, "fixed" }, rig.Strategies);
            Assert.AreEqual(1, rig.Safe.FallbackCount);
            Assert.AreEqual(2, rig.Safe.LastFallbackWave);
            Assert.AreEqual(SafePlanner.ReasonException, rig.Safe.LastFallbackReason);
            StringAssert.Contains("director bug in PlanWave, wave 2", rig.Safe.LastFallbackDetail);

            Assert.AreEqual(1, rig.Log.Count);
            StringAssert.StartsWith("[Director]", rig.Log[0]);
            StringAssert.Contains("wave 2", rig.Log[0]);
            StringAssert.Contains("flaky", rig.Log[0]);
        }

        [Test]
        public void AnInvalidPlanBecomesAFallbackWave()
        {
            Rig rig = Build(f => f.WrongWaveOnWave = wave => wave == 1);
            rig.RunWaves(2);

            CollectionAssert.AreEqual(new[] { EscalationPlanner.Id, "fixed" }, rig.Strategies);
            Assert.AreEqual(SafePlanner.ReasonInvalidPlan, rig.Safe.LastFallbackReason);
            StringAssert.Contains("plan is for wave 8", rig.Safe.LastFallbackDetail);
        }

        [Test]
        public void ANullPlanBecomesAFallbackWave()
        {
            Rig rig = Build(f => f.NullOnWave = wave => wave == 1);
            rig.RunWaves(1);

            Assert.AreEqual(EscalationPlanner.Id, rig.Strategies[0]);
            Assert.AreEqual(SafePlanner.ReasonInvalidPlan, rig.Safe.LastFallbackReason);
        }

        [Test]
        public void APlannerThatAlwaysThrowsStillGivesAPlayableSession()
        {
            Rig rig = Build(f => f.ThrowOnWave = _ => true);
            rig.RunWaves(4);

            Assert.AreEqual(4, rig.Safe.FallbackCount);
            foreach (string s in rig.Strategies) Assert.AreEqual(EscalationPlanner.Id, s);
            Assert.AreEqual(4, rig.Sim.State.WaveIndex, "waves are numbered 1..4 with no gaps");
        }

        [Test]
        public void AnExceptionInOnWaveResolvedIsReportedAndTheSessionContinues()
        {
            Rig rig = Build(f => f.ResolveThrowsLeft = 1);
            rig.RunWaves(2);

            Assert.AreEqual(0, rig.Safe.FallbackCount, "no wave was planned by the fallback");
            Assert.AreEqual(SafePlanner.ReasonResolveException, rig.Safe.LastFallbackReason);
            Assert.AreEqual(1, rig.Safe.LastFallbackWave);
            Assert.AreEqual(1, rig.Log.Count);
            Assert.AreEqual(2, rig.Flaky.ResolveCalls, "one call per wave: the failed one is not repeated");
        }

        [Test]
        public void ItKeepsTheInnerPlannersName()
        {
            Rig rig = Build(_ => { });
            Assert.AreEqual("flaky", rig.Safe.Name);
            Assert.AreSame(rig.Flaky, rig.Safe.Inner);
        }

        // DirectorHost wraps every planner, the escalation baseline included. A
        // wrapped run must be the same game, tick for tick, or a session recorded in
        // Unity would not replay headless (the replay rebuilds the planner by name).
        [Test]
        public void AWrappedEscalationPlannerPlaysTheSameGameAsABareOne()
        {
            SimConfig config = Config();
            MapData map = AsciiMap.Parse(Map).Map;
            var bare = new Simulation(config, map, TestShapes.SampleSceneLibrary(), 9, new EscalationPlanner(config, map));
            var safe = new SafePlanner(new EscalationPlanner(config, map), new EscalationPlanner(config, map), map, config);
            var wrapped = new Simulation(config, map, TestShapes.SampleSceneLibrary(), 9, safe);

            Assert.AreEqual(EscalationPlanner.Id, wrapped.Planner.Name);
            for (int i = 0; i < 3000; i++)
            {
                bare.Tick();
                wrapped.Tick();
                if (bare.ComputeHash() != wrapped.ComputeHash()) Assert.Fail("diverged at tick " + bare.State.Tick);
            }
            Assert.GreaterOrEqual(bare.State.WaveIndex, 2);
            Assert.AreEqual(0, safe.FallbackCount);
        }

        // What it cannot rescue: a planner that writes to the state. The state is
        // already wrong by then, so the simulation's own guard must still stop it.
        [Test]
        public void APlannerThatWritesTheStateStillFailsLoudly()
        {
            SimConfig config = Config();
            MapData map = AsciiMap.Parse(Map).Map;
            var safe = new SafePlanner(new MutatingPlanner(MutatingPlanner.When.PlanWave), new EscalationPlanner(config, map), map, config);
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), 1, safe);

            var e = Assert.Throws<InvalidOperationException>(() => TestSims.Run(sim, 10));
            StringAssert.Contains("changed the simulation state", e.Message);
        }

        [Test]
        public void MissingPartsAreRejectedAtConstruction()
        {
            SimConfig config = Config();
            MapData map = AsciiMap.Parse(Map).Map;
            var escalation = new EscalationPlanner(config, map);
            Assert.Throws<ArgumentNullException>(() => new SafePlanner(null, escalation, map, config));
            Assert.Throws<ArgumentNullException>(() => new SafePlanner(escalation, null, map, config));
            Assert.Throws<ArgumentNullException>(() => new SafePlanner(escalation, escalation, null, config));
            Assert.Throws<ArgumentNullException>(() => new SafePlanner(escalation, escalation, map, null));
        }
    }
}
