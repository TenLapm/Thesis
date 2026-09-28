using System.Linq;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class SimulationPhaseTests
    {
        // A short corridor. Agents with speed 0 never move and simply stall.
        private const string Corridor = "S . . . . C";

        private static bool Raised(Simulation sim, SimEventKind kind)
        {
            return sim.LastTickEvents.Any(e => e.Kind == kind);
        }

        [Test]
        public void PrepLastsExactly45SecondsThenWaveOneStarts()
        {
            var sim = TestSims.Ascii(Corridor, new FixedPlanner(FixedPlanner.Group(1, 100f)));
            Assert.AreEqual(SimPhase.Prep, sim.State.Phase);
            Assert.AreEqual(2250, sim.State.PhaseTicksRemaining, "45 s at 50 Hz");

            TestSims.Run(sim, 2249);
            Assert.AreEqual(SimPhase.Prep, sim.State.Phase);
            Assert.AreEqual(0, sim.State.WaveIndex);
            Assert.AreEqual(60f, sim.State.BuildBudget, "no stipend before the first wave");

            sim.Tick(); // the 2250th tick
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.IsTrue(Raised(sim, SimEventKind.WaveStarted));
            Assert.IsTrue(Raised(sim, SimEventKind.AgentSpawned), "the first agent enters on the wave's first tick, as StartWave did");
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "a one-agent wave is fully spawned immediately");
        }

        [Test]
        public void IntermissionStartsOnlyWhenTheLastAgentResolvesThenLasts10Seconds()
        {
            // Two agents: one stalls after 0.1 s, the other lives 2 s. D4: the wave must
            // stay in Resolving until the long-lived one is gone.
            var planner = new FixedPlanner(FixedPlanner.Group(1, 0.1f), FixedPlanner.Group(1, 2f));
            var sim = TestSims.Ascii(Corridor, planner);
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);

            TestSims.Run(sim, 20); // 0.4 s: the short one has stalled, the long one hasn't
            Assert.AreEqual(1, sim.State.LiveAgentCount);
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "one agent still alive -> no intermission yet");

            int guard = 1000;
            while (sim.State.Phase == SimPhase.Resolving && guard-- > 0) sim.Tick();

            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase);
            Assert.AreEqual(0, sim.State.LiveAgentCount);
            Assert.AreEqual(500, sim.State.PhaseTicksRemaining, "10 s at 50 Hz");
            Assert.IsTrue(Raised(sim, SimEventKind.WaveResolved));

            WaveOutcome o = sim.State.LastOutcome;
            Assert.AreEqual(2, o.Spawned);
            Assert.AreEqual(2, o.Stalled);
            Assert.AreEqual(0, o.Leaked);
            Assert.IsFalse(o.CoreDestroyed);
            Assert.AreEqual(60f, o.BudgetBefore);
            Assert.AreEqual(60.4f, o.BudgetAfter, 1e-4f, "two stalls x 0.2");
            Assert.AreEqual(80.4f, sim.State.BuildBudget, 1e-4f, "stipend of 20 paid at intermission start, after the outcome is closed");

            Assert.AreEqual(1, planner.Outcomes.Count, "planner told exactly once");
            Assert.AreEqual(2, planner.Outcomes[0].Stalled);
        }

        [Test]
        public void IntermissionTimerStartsTheNextWave()
        {
            var sim = TestSims.Ascii(Corridor, new FixedPlanner(FixedPlanner.Group(1, 0.05f)));
            sim.Enqueue(SimCommand.StartWaveNow());
            int guard = 5000;
            while (sim.State.Phase != SimPhase.Intermission && guard-- > 0) sim.Tick();

            TestSims.Run(sim, 499);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase);
            sim.Tick();
            Assert.AreEqual(2, sim.State.WaveIndex);
        }

        [Test]
        public void StartWaveNowIsIgnoredWhileAWaveIsRunning()
        {
            var sim = TestSims.Ascii(Corridor, new FixedPlanner(FixedPlanner.Group(1, 100f)));
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);

            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase);
        }

        [Test]
        public void GroupsInterleaveBySpawnTickAndHonourStartDelay()
        {
            // Group 0: 3 agents every 4 ticks from tick 0. Group 1: 2 agents every 4 ticks from tick 2.
            var planner = new FixedPlanner(FixedPlanner.Group(3, 100f, intervalTicks: 4), FixedPlanner.Group(2, 100f, intervalTicks: 4, startDelayTicks: 2));
            var sim = TestSims.Ascii(Corridor, planner);
            sim.Enqueue(SimCommand.StartWaveNow());

            var spawnTicks = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 20; i++)
            {
                int tick = sim.State.Tick;
                sim.Tick();
                foreach (SimEvent e in sim.LastTickEvents)
                    if (e.Kind == SimEventKind.AgentSpawned) spawnTicks.Add(tick);
            }

            CollectionAssert.AreEqual(new[] { 0, 2, 4, 6, 8 }, spawnTicks);
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase);
        }

        [Test]
        public void LosingTheCoreClosesTheWaveAndFreezesTheGame()
        {
            // Agents that walk straight into the core; the core has only 2 HP.
            var config = new SimConfig { CoreMaxHp = 2 };
            var planner = new FixedPlanner(FixedPlanner.Group(5, 100f, speed: 20f, intervalTicks: 1));
            var sim = TestSims.Ascii("S . C", planner, config);
            sim.Enqueue(SimCommand.StartWaveNow());

            int guard = 2000;
            while (!sim.State.IsGameOver && guard-- > 0) sim.Tick();

            Assert.IsTrue(sim.State.IsGameOver);
            Assert.IsTrue(Raised(sim, SimEventKind.GameOver));
            Assert.IsTrue(Raised(sim, SimEventKind.WaveResolved), "the fatal wave is still closed and reported");
            Assert.IsTrue(sim.State.LastOutcome.CoreDestroyed);
            Assert.GreaterOrEqual(sim.State.LastOutcome.Leaked, 2);
            Assert.AreEqual(1, planner.Outcomes.Count);
            Assert.IsTrue(planner.Outcomes[0].CoreDestroyed);

            int tick = sim.State.Tick;
            ulong hash = sim.ComputeHash();
            sim.Enqueue(SimCommand.PlaceShape(1, 0));
            TestSims.Run(sim, 10);
            Assert.AreEqual(tick, sim.State.Tick, "a finished game never advances");
            Assert.AreEqual(hash, sim.ComputeHash(), "nothing changes after game over, input included");
        }

        [Test]
        public void PlacingAShapeSpendsBudgetConsumesThePieceAndLogsIt()
        {
            var sim = TestSims.SampleScene();
            string current = sim.State.Bag.CurrentShape.Name;
            int cost = sim.State.Bag.CurrentShape.BuildCost;

            sim.Enqueue(SimCommand.PlaceShape(18, 18));
            sim.Tick();

            Assert.AreEqual(60f - cost, sim.State.BuildBudget);
            Assert.AreEqual(1, sim.State.PlacementLog.Count);
            Assert.AreEqual(current, sim.State.PlacementLog[0].ShapeName);
            Assert.AreEqual(0, sim.State.PlacementLog[0].Tick);
            Assert.IsTrue(sim.LastTickEvents.Any(e => e.Kind == SimEventKind.WallPlaced));
            Assert.AreEqual(2, sim.State.Bag.Version, "construction pull (1) + post-placement pull (2): the placed piece was consumed");
        }
    }
}
