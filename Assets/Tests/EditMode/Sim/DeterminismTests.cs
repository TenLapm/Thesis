using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // CLAUDE.md I1: same {map, seed, input} -> identical run, checked on every tick.
    public class DeterminismTests
    {
        // A long-lived core so the run keeps going through several waves instead
        // of ending in wave 1 (an idle board loses wave 1 on SampleScene).
        private static SimConfig LongRun() { return new SimConfig { CoreMaxHp = 100000 }; }

        [Test]
        public void SameSeedAndCommandsGiveIdenticalHashesOnEveryTickFor20000Ticks()
        {
            const int ticks = 20000;
            var a = TestSims.SampleScene(seed: 7, config: LongRun());
            var b = TestSims.SampleScene(seed: 7, config: LongRun());
            var script = new CommandScript(seed: 99, ticks: ticks, every: 23, mapWidth: 38, mapHeight: 38);

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "identical before the first tick");
            for (int i = 0; i < ticks; i++)
            {
                script.Feed(a);
                script.Feed(b);
                a.Tick();
                b.Tick();
                if (a.ComputeHash() != b.ComputeHash())
                    Assert.Fail("hashes diverged at tick " + a.State.Tick + " (wave " + a.State.WaveIndex + ", phase " + a.State.Phase + ")");
            }

            // The run must actually have exercised the rules, or this proves little.
            int killed = 0;
            foreach (AgentState agent in a.State.Agents) if (agent.Killed) killed++;
            float damage = 0f;
            foreach (TowerState tower in a.State.Towers) damage += tower.DamageDealt;

            Assert.GreaterOrEqual(a.State.WaveIndex, 3, "waves played");
            Assert.Greater(a.State.PlacementLog.Count, 5, "walls and towers placed");
            Assert.Greater(a.State.Towers.Count, 2, "towers placed");
            Assert.Greater(damage, 0f, "towers hit something");
            Assert.Greater(a.State.Agents.Count, 200, "agents spawned");
            TestContext.WriteLine("20k ticks: waves=" + a.State.WaveIndex + " placements=" + a.State.PlacementLog.Count + " towers=" + a.State.Towers.Count
                                  + " agents=" + a.State.Agents.Count + " killed=" + killed + " tower damage=" + damage + " last=" + a.State.LastOutcome);
        }

        // The run above never digs: random walls leave detours open. Digging is where
        // several agents mutate one tile in the same tick and the field is rebuilt
        // mid-run - the likeliest place for an order dependence - so force it with a
        // full wall line across the map that every agent has to chew through.
        [Test]
        public void DeterminismHoldsWhenEveryAgentMustDig()
        {
            const int ticks = 20000;
            Simulation a = WallLineRig(seed: 4);
            Simulation b = WallLineRig(seed: 4);
            var script = new CommandScript(seed: 17, ticks: ticks, every: 29, mapWidth: 38, mapHeight: 38);

            int breaches = 0;
            for (int i = 0; i < ticks; i++)
            {
                script.Feed(a);
                script.Feed(b);
                a.Tick();
                b.Tick();
                foreach (SimEvent e in a.LastTickEvents) if (e.Kind == SimEventKind.WallBreached) breaches++;
                if (a.ComputeHash() != b.ComputeHash())
                    Assert.Fail("hashes diverged at tick " + a.State.Tick + " (wave " + a.State.WaveIndex + ")");
            }

            // Once a line is breached the field routes everyone through that hole, so
            // each line breaks exactly once (found the hard way: one line gave 1 breach).
            // Five lines -> at least five crowd breaches and five mid-run rebuilds.
            Assert.GreaterOrEqual(breaches, WallRows.Length, "every wall line must have been dug through");
            int killed = 0;
            foreach (AgentState agent in a.State.Agents) if (agent.Killed) killed++;
            TestContext.WriteLine("dig run: breaches=" + breaches + " killed=" + killed + " towers=" + a.State.Towers.Count + " waves=" + a.State.WaveIndex + " agents=" + a.State.Agents.Count);
        }

        // Rows clear of the spawn (3,3) and core (34,34) tiles.
        private static readonly int[] WallRows = { 8, 14, 20, 26, 31 };

        private static Simulation WallLineRig(ulong seed)
        {
            Simulation sim = TestSims.SampleScene(seed: seed, config: LongRun());
            SimGrid g = sim.State.Grid;
            foreach (int y in WallRows)
                for (int x = 0; x < g.Width; x++) g.SetWall(g[x, y], 15, 6f);
            sim.RebuildFieldForSetup();
            return sim;
        }

        // Every movement class in one long run (WP-C2). The class-cycle planner sends a
        // ground wave, a sapper wave, a flyer wave and a mixed one, over and over; a
        // long wall with a gap at one end gives sappers something to dig through that
        // everyone else walks round; walls and towers come from the script.
        [Test]
        public void DeterminismHoldsWithSappersAndFlyers()
        {
            const int ticks = 20000;
            Simulation a = ClassCycleRig(seed: 9);
            Simulation b = ClassCycleRig(seed: 9);
            var script = new CommandScript(seed: 41, ticks: ticks, every: 19, mapWidth: 38, mapHeight: 38);

            int breaches = 0;
            var spawned = new int[MovementClasses.Count];
            var killed = new int[MovementClasses.Count];
            var leaked = new int[MovementClasses.Count];
            for (int i = 0; i < ticks; i++)
            {
                script.Feed(a);
                script.Feed(b);
                a.Tick();
                b.Tick();
                foreach (SimEvent e in a.LastTickEvents)
                {
                    if (e.Kind == SimEventKind.WallBreached) breaches++;
                    if (e.Kind != SimEventKind.WaveResolved) continue;
                    for (int c = 0; c < MovementClasses.Count; c++)
                    {
                        spawned[c] += a.State.LastOutcome.SpawnedByClass[c];
                        killed[c] += a.State.LastOutcome.KilledByClass[c];
                        leaked[c] += a.State.LastOutcome.LeakedByClass[c];
                    }
                }
                if (a.ComputeHash() != b.ComputeHash())
                    Assert.Fail("hashes diverged at tick " + a.State.Tick + " (wave " + a.State.WaveIndex + ", phase " + a.State.Phase + ")");
            }

            // The run must actually have exercised the rules, or this proves little.
            Assert.GreaterOrEqual(a.State.WaveIndex, 5, "a full cycle and the start of the next");
            for (int c = 0; c < MovementClasses.Count; c++) Assert.Greater(spawned[c], 30, (MovementClass)c + " enemies spawned");
            Assert.Greater(breaches, 0, "something was dug through");
            Assert.Greater(leaked[(int)MovementClass.Flying], 0, "flyers reached the core");
            Assert.Greater(killed[(int)MovementClass.Flying], 0, "and anti-air towers shot some down");
            Assert.Greater(killed[(int)MovementClass.Sapper], 0, "sappers were shot");
            TestContext.WriteLine("class-cycle run: waves=" + a.State.WaveIndex + " breaches=" + breaches + " towers=" + a.State.Towers.Count
                                  + " spawned g/s/f=" + string.Join("/", spawned) + " killed=" + string.Join("/", killed) + " leaked=" + string.Join("/", leaked));
        }

        private static Simulation ClassCycleRig(ulong seed)
        {
            // Short build phases and small waves: a wave on this map is about 4,000
            // ticks (the walk alone is 70 seconds), so 20,000 ticks hold five of them.
            var config = new SimConfig { CoreMaxHp = 100000, PrepSeconds = 5f, IntermissionSeconds = 2f, AgentsPerWave = 40 };
            MapData map = TestSims.SampleSceneMap();
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), seed, new ClassCyclePlanner(config, map));

            // The straight line from the spawn (3,3) to the core (34,34) crosses row 18
            // near x = 18. The gap at x >= 28 is about six tiles out of the way: worth
            // it to a walker (a wall is 14), not to a sapper (to whom it is 2).
            SimGrid g = sim.State.Grid;
            for (int x = 0; x < 28; x++) g.SetWall(g[x, 18], 15, 6f);
            sim.RebuildFieldForSetup();

            // One archer (the roster's anti-air tower) beside that same line, which is
            // also the line the flyers take: enough to shoot some of them down and let
            // the rest through. The script's own towers land anywhere.
            TestSims.Send(sim, SimCommand.PlaceTower(0, 22, 24));
            if (sim.State.Towers.Count != 1) throw new System.InvalidOperationException("rig: the archer was not placed");
            return sim;
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var a = TestSims.SampleScene(seed: 1);
            var b = TestSims.SampleScene(seed: 2);
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash(), "the shape bag is seeded differently from tick 0");
        }

        [Test]
        public void CloneThenTickBothGivesEqualHashesAndStaysIndependent()
        {
            var script = new CommandScript(seed: 5, ticks: 9000, every: 31, mapWidth: 38, mapHeight: 38);
            var a = TestSims.SampleScene(seed: 3, config: LongRun());
            for (int i = 0; i < 3000; i++) { script.Feed(a); a.Tick(); }

            Simulation b = a.Clone(new EscalationPlanner(a.Config, a.Map));
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "a clone starts identical");

            for (int i = 0; i < 5000; i++)
            {
                script.Feed(a);
                script.Feed(b);
                a.Tick();
                b.Tick();
                if (a.ComputeHash() != b.ComputeHash())
                    Assert.Fail("clone diverged from its source at tick " + a.State.Tick);
            }

            ulong before = a.ComputeHash();
            b.Enqueue(SimCommand.Rotate());
            b.Tick();
            b.Tick();
            Assert.AreEqual(before, a.ComputeHash(), "ticking the clone must not touch the original");
        }

        [TestCase("grid cost")]
        [TestCase("agent position")]
        [TestCase("budget")]
        [TestCase("rng state")]
        public void MutatingOneFieldOfACloneChangesItsHash(string field)
        {
            var a = TestSims.SampleScene(seed: 11);
            // Into wave 1 so live agents exist.
            a.Enqueue(SimCommand.StartWaveNow());
            TestSims.Run(a, 50);
            Assert.Greater(a.State.LiveAgentCount, 0, "rig: need live agents");

            Simulation b = a.Clone(new EscalationPlanner(a.Config, a.Map));
            ulong original = a.ComputeHash();
            Assert.AreEqual(original, b.ComputeHash());

            switch (field)
            {
                case "grid cost": b.State.Grid[10, 10].TerrainCost += 1; break;
                case "agent position":
                    AgentState agent = b.State.LiveAgents[0];
                    agent.Position = new Vec2f(agent.Position.X + 0.001f, agent.Position.Y);
                    break;
                case "budget": b.State.BuildBudget += 0.001f; break;
                case "rng state": b.State.BagRng.NextUInt(); break;
            }

            Assert.AreNotEqual(original, b.ComputeHash(), "the hash must notice a change to " + field);
            Assert.AreEqual(original, a.ComputeHash(), "and the original must be untouched");
        }
    }
}
