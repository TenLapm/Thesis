using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // "Every wave must end" (ARCHITECTURE.md §4.6). The lifetime clock used to
    // guarantee it. Since WP-C1 four things do: enemies must be able to move, a slow
    // has a floor, every spawn must reach the core, and a backstop ends a wave that
    // has run too long. (The slow floor is tested in TowerSystemTests.)
    public class WaveTerminationTests
    {
        private const string Corridor = "S . . . . . . . . C";

        [Test]
        public void AnEnemyThatCannotMoveIsRefusedAtPlanningTime()
        {
            var sim = TestSims.Ascii(Corridor, new FixedPlanner(FixedPlanner.Group(count: 1, hp: 10f, speed: 0f)));
            sim.Enqueue(SimCommand.StartWaveNow());

            var e = Assert.Throws<System.InvalidOperationException>(() => sim.Tick());
            StringAssert.Contains("MoveSpeed 0", e.Message);
            StringAssert.Contains("could never end", e.Message);
            Assert.AreEqual(0, sim.State.WaveIndex);
        }

        [Test]
        public void TheValidatorNamesEachWayAPlanCanBeWrong()
        {
            MapData map = AsciiMap.Parse(Corridor).Map;
            var config = new SimConfig();
            System.Func<AgentGroup, string> check = g => PlanValidator.Check(new WavePlan { WaveIndex = 1, Groups = new[] { g } }, 1, map, config, null);

            Assert.IsNull(check(FixedPlanner.Group(3)), "a normal group is fine");
            Assert.IsNull(check(FixedPlanner.Group(0, hp: 0f, speed: 0f)), "an empty group is not checked for stats");

            StringAssert.Contains("Hp 0", check(FixedPlanner.Group(1, hp: 0f)));
            StringAssert.Contains("Hp -3", check(FixedPlanner.Group(1, hp: -3f)));
            StringAssert.Contains("Hp", check(FixedPlanner.Group(1, hp: float.NaN)));
            StringAssert.Contains("Hp", check(FixedPlanner.Group(1, hp: float.PositiveInfinity)));
            StringAssert.Contains("MoveSpeed", check(FixedPlanner.Group(1, speed: float.NaN)));
            StringAssert.Contains("MoveSpeed", check(FixedPlanner.Group(1, speed: -1f)));

            StringAssert.Contains("resistances", check(FixedPlanner.Group(1, resist: new float[2])));
            StringAssert.Contains("Resist[Fire]", check(FixedPlanner.Group(1, resist: new[] { 1f, -1f, 1f })));
            StringAssert.Contains("Resist[Frost]", check(FixedPlanner.Group(1, resist: new[] { 1f, 1f, float.NaN })));
            Assert.IsNull(check(FixedPlanner.Group(1, resist: new[] { 0f, 2f, 1f })), "0 (immune) and above 1 (vulnerable) are both allowed");
        }

        // The backstop itself. An enemy that can neither move nor die cannot exist
        // through the rules, so the test reaches into the state to make one.
        [Test]
        public void AWaveThatWouldNeverEndIsEndedByTheBackstop()
        {
            var config = new SimConfig { MaxWaveSeconds = 2f, PrepSeconds = 60f, IntermissionSeconds = 60f };
            var planner = new FixedPlanner(FixedPlanner.Group(count: 3, hp: 10f, speed: 1f, intervalTicks: 4));
            Simulation sim = TestSims.Ascii(Corridor, planner, config);
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);

            var events = new List<SimEvent>();
            int resolvedAtTick = -1;
            for (int i = 0; i < 400 && resolvedAtTick < 0; i++)
            {
                foreach (AgentState a in sim.State.LiveAgents) a.MoveSpeed = 0f; // frozen for good
                sim.Tick();
                events.AddRange(sim.LastTickEvents);
                if (sim.State.LastOutcome != null) resolvedAtTick = sim.State.Tick;
            }

            Assert.AreEqual(101, resolvedAtTick, "2 s is 100 ticks after the wave began at tick 0; the state is read one tick later");
            WaveOutcome o = sim.State.LastOutcome;
            Assert.IsTrue(o.TimedOut);
            Assert.AreEqual(3, o.Spawned);
            Assert.AreEqual(3, o.Removed);
            Assert.AreEqual(0, o.Killed);
            Assert.AreEqual(0, o.Leaked);
            Assert.IsFalse(o.CoreDestroyed);
            Assert.AreEqual(10, sim.State.CoreHp, "a removed enemy does no damage");
            Assert.AreEqual(60f, o.BudgetAfter, "and pays nothing");

            Assert.AreEqual(3, TestTowers.Count(events, SimEventKind.AgentRemoved));
            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.WaveTimedOut));
            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.WaveResolved));
            Assert.AreEqual(0, sim.State.LiveAgentCount);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase, "the game goes on");
            Assert.AreEqual(1, planner.Outcomes.Count);
            Assert.IsTrue(planner.Outcomes[0].TimedOut, "the planner is told");
            StringAssert.Contains("TIMED OUT", o.ToString());
        }

        [Test]
        public void TheBackstopAlsoStopsAgentsThatHaveNotSpawnedYet()
        {
            // 50 agents one second apart would take 50 s to spawn; the wave is cut at 1 s.
            var config = new SimConfig { MaxWaveSeconds = 1f, PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = 1000 };
            var planner = new FixedPlanner(FixedPlanner.Group(count: 50, hp: 10f, speed: 0.01f, intervalTicks: 50));
            Simulation sim = TestSims.Ascii(Corridor, planner, config);
            sim.Enqueue(SimCommand.StartWaveNow());
            for (int i = 0; i < 200 && sim.State.LastOutcome == null; i++) sim.Tick();

            WaveOutcome o = sim.State.LastOutcome;
            Assert.IsNotNull(o);
            Assert.IsTrue(o.TimedOut);
            Assert.AreEqual(2, o.Spawned, "the ones due at ticks 0 and 50");
            Assert.AreEqual(2, o.Removed);
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase);

            TestSims.Run(sim, 100);
            Assert.AreEqual(2, sim.State.Agents.Count, "none of the other 48 ever appears");
        }

        [Test]
        public void AHealthyWaveNeverTouchesTheBackstop()
        {
            var planner = new FixedPlanner(FixedPlanner.Group(count: 5, hp: 10f, speed: 5f));
            Simulation sim = TestSims.Ascii(Corridor, planner, new SimConfig { CoreMaxHp = 100 });
            sim.Enqueue(SimCommand.StartWaveNow());
            for (int i = 0; i < 2000 && sim.State.LastOutcome == null; i++) sim.Tick();

            Assert.IsFalse(sim.State.LastOutcome.TimedOut);
            Assert.AreEqual(0, sim.State.LastOutcome.Removed);
            Assert.AreEqual(5, sim.State.LastOutcome.Leaked);
        }

        // ---------------------------------------------------------------- maps

        [Test]
        public void AMapWhoseSpawnCannotReachTheCoreIsRefusedAndNamesTheSpawn()
        {
            var e = Assert.Throws<InvalidDataException>(() => AsciiMap.Parse("S . X . C"));
            StringAssert.Contains("Spawns[0]", e.Message);
            StringAssert.Contains("cannot reach the core", e.Message);
        }

        [Test]
        public void ADiagonalGapBetweenTwoBlockersIsNotAWayThrough()
        {
            // The spawn and the core touch only at a corner, with a blocker on each
            // side of it: the corner-cut rule forbids that step.
            const string pinched = @"
                S X
                X C";
            Assert.Throws<InvalidDataException>(() => AsciiMap.Parse(pinched));

            // Open one side and there is a way round.
            const string open = @"
                S .
                X C";
            Assert.DoesNotThrow(() => AsciiMap.Parse(open));
        }

        [Test]
        public void WallsAndTowersNeverCutTheCoreOff()
        {
            // Player structures are diggable, so a spawn sealed in by them still has a
            // route (through them). Only static blockers can cut it off.
            Simulation sim = TestSims.Ascii(Corridor, new FixedPlanner(FixedPlanner.Group(1)), new SimConfig { PrepSeconds = 60f });
            sim.State.Grid.SetWall(sim.State.Grid[3, 0], 15, 6f);
            sim.RebuildFieldForSetup();
            TestSims.Send(sim, SimCommand.PlaceTower(0, 6, 0));

            Assert.AreEqual(1, sim.State.Towers.Count);
            Assert.AreNotEqual(SimNode.Infinity, sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost);
        }

        [Test]
        public void EveryCommittedMapPassesTheReachabilityCheck()
        {
            foreach (string name in new[] { "SampleScene", "Bench_Open", "Bench_Maze", "Bench_Choke" })
            {
                Assert.DoesNotThrow(() => MapData.Load(TestPaths.MapFile(name)), name);
            }
        }

        [Test]
        public void TheConfigRefusesValuesThatWouldBreakTermination()
        {
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { MinSlowFactor = 0f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { MinSlowFactor = 1.5f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { MaxWaveSeconds = 0f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { BaseMoveSpeed = 0f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { BaseHp = 0f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { HpIncrementPerWave = -1f }.Validate());
            Assert.DoesNotThrow(() => new SimConfig().Validate());
        }
    }
}
