using System;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // A planner that throws, or returns a plan the simulation refuses, must not
    // damage the game state: the failed Tick() changes nothing and the next one
    // tries again. Found in the 2026-10-02 review: WaveIndex was incremented before
    // the planner was asked, so three failures made the first real wave "wave 4",
    // and one throw in OnWaveResolved made every later tick crash.
    public class PlannerFailureTests
    {
        private const string Map = @"
            S . . . . .
            . . . . . .
            . . . . . C";

        private static SimConfig Config() { return new SimConfig { PrepSeconds = 0.1f, IntermissionSeconds = 0.1f, CoreMaxHp = 1000 }; }

        // Two fast agents: a wave ends, by both reaching the core, in under a second.
        private static FixedPlanner Fixed() { return new FixedPlanner(FixedPlanner.Group(count: 2, hp: 5f, speed: 20f)); }

        private static int FailingTicks(Simulation sim, int attempts)
        {
            int failures = 0;
            for (int i = 0; i < attempts; i++)
            {
                try { sim.Tick(); }
                catch (InvalidOperationException) { failures++; }
            }
            return failures;
        }

        [Test]
        public void AThrowInPlanWaveChangesNothingAndTheNextTickTriesAgain()
        {
            var flaky = new FlakyPlanner(Fixed()) { PlanThrowsLeft = 3 };
            Simulation sim = TestSims.Ascii(Map, flaky, Config());
            Simulation reference = TestSims.Ascii(Map, Fixed(), Config());

            // The countdown runs out on tick 4 (0.1 s = 5 ticks; the wave starts on the
            // tick it reaches zero). Until then both runs are identical.
            TestSims.Run(sim, 4);
            TestSims.Run(reference, 4);
            Assert.AreEqual(reference.ComputeHash(), sim.ComputeHash());
            ulong before = sim.ComputeHash();

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Assert.Throws<InvalidOperationException>(() => sim.Tick(), "attempt " + attempt);
                Assert.AreEqual(before, sim.ComputeHash(), "a failed tick must leave the state exactly as it was (attempt " + attempt + ")");
                Assert.AreEqual(0, sim.State.WaveIndex);
                Assert.AreEqual(4, sim.State.Tick);
            }

            // The fourth attempt works, and from here the run is the one that never failed.
            sim.Tick();
            reference.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex, "the first wave is wave 1, however many attempts it took");
            for (int i = 0; i < 300; i++)
            {
                Assert.AreEqual(reference.ComputeHash(), sim.ComputeHash(), "tick " + sim.State.Tick);
                sim.Tick();
                reference.Tick();
            }
            Assert.AreEqual(3 + sim.State.WaveIndex, flaky.PlanCalls, "three failed calls, then one per wave");
        }

        [Test]
        public void AThrowAfterStartWaveNowKeepsTheRequest()
        {
            var flaky = new FlakyPlanner(Fixed()) { PlanThrowsLeft = 2 };
            Simulation sim = TestSims.Ascii(Map, flaky, new SimConfig { PrepSeconds = 60f });

            sim.Enqueue(SimCommand.StartWaveNow());
            Assert.AreEqual(2, FailingTicks(sim, 2));
            Assert.AreEqual(0, sim.State.WaveIndex);

            sim.Tick(); // no new command: the request from before must still stand
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.AreEqual(SimPhase.Spawning, sim.State.Phase);
        }

        [Test]
        public void AnInvalidPlanIsRefusedWithoutChangingTheState()
        {
            var flaky = new FlakyPlanner(Fixed()) { WrongWaveOnWave = wave => wave == 1 };
            Simulation sim = TestSims.Ascii(Map, flaky, Config());
            TestSims.Run(sim, 4);
            ulong before = sim.ComputeHash();

            var e = Assert.Throws<InvalidOperationException>(() => sim.Tick());
            StringAssert.Contains("plan is for wave 8", e.Message);
            Assert.AreEqual(before, sim.ComputeHash());
            Assert.AreEqual(0, sim.State.WaveIndex);
            Assert.IsNull(sim.State.CurrentPlan);
        }

        [Test]
        public void AThrowInOnWaveResolvedLeavesTheWaveOpenAndTheNextTickClosesIt()
        {
            var flaky = new FlakyPlanner(Fixed()) { ResolveThrowsLeft = 1 };
            Simulation sim = TestSims.Ascii(Map, flaky, Config());

            int resolvedEvents = 0;
            bool threw = false;
            for (int i = 0; i < 200 && sim.State.LastOutcome == null; i++)
            {
                try
                {
                    sim.Tick();
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                    Assert.IsNotNull(sim.State.CurrentOutcome, "the wave is still open");
                    Assert.IsNull(sim.State.LastOutcome);
                    Assert.AreEqual(SimPhase.Resolving, sim.State.Phase);
                    continue;
                }
                foreach (SimEvent e in sim.LastTickEvents) if (e.Kind == SimEventKind.WaveResolved) resolvedEvents++;
            }

            Assert.IsTrue(threw);
            Assert.AreEqual(1, resolvedEvents, "WaveResolved is raised once, on the tick that succeeds");
            Assert.AreEqual(1, sim.State.LastOutcome.WaveIndex);
            Assert.IsNull(sim.State.CurrentOutcome);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase);
            Assert.AreEqual(2, flaky.ResolveCalls, "asked again on the retry");

            // And the game goes on: the next wave starts and resolves normally.
            TestSims.Run(sim, 200);
            Assert.GreaterOrEqual(sim.State.WaveIndex, 2);
        }

        [Test]
        public void DuringPlanWaveTheStateStillShowsThePreviousWave()
        {
            int seenInState = -1, seenInContext = -1;
            var planner = new ProbePlanner(Fixed(), ctx =>
            {
                seenInState = ctx.State.WaveIndex;
                seenInContext = ctx.WaveIndex;
            });
            Simulation sim = TestSims.Ascii(Map, planner, Config());
            TestSims.Run(sim, 5);

            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.AreEqual(1, seenInContext, "the wave being planned");
            Assert.AreEqual(0, seenInState, "nothing is written until the plan is accepted");
        }

        private sealed class ProbePlanner : IWavePlanner
        {
            private readonly IWavePlanner inner;
            private readonly Action<WaveContext> onPlan;

            public ProbePlanner(IWavePlanner inner, Action<WaveContext> onPlan)
            {
                this.inner = inner;
                this.onPlan = onPlan;
            }

            public string Name => "probe";

            public WavePlan PlanWave(WaveContext context)
            {
                onPlan(context);
                return inner.PlanWave(context);
            }

            public void OnWaveResolved(WaveOutcome outcome) { inner.OnWaveResolved(outcome); }
        }
    }
}
