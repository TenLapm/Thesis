using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class AgentSystemTests
    {
        // Common rig: "C # S", a wall tile between the core and a single spawn, one
        // straight row so the agent can sit exactly on the wall tile without
        // needing the flow field generated (dig logic never reads BestCost).
        private static AsciiFixture Rig()
        {
            return TestMaps.Parse("C # S");
        }

        private static AgentState Digger(AsciiFixture f, int id, float digRate = 1f, float wallBreakReward = 1f)
        {
            return new AgentState(id, f.Grid[1, 0].Position, moveSpeed: 0f, lifeTime: 999f, digRate: digRate, deathReward: 0.2f, wallBreakReward: wallBreakReward);
        }

        private static int TicksUntilBreach(SimGrid grid, List<AgentState> agents)
        {
            var occ = new OccupancyMap(grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;
            int ticks = 0;
            int guard = 1000;
            while (guard-- > 0)
            {
                ticks++;
                bool dirty = AgentSystem.Step(grid, agents, 1f, occ, ref budget, ref coreHp, null);
                if (dirty) return ticks;
            }
            Assert.Fail("wall never breached");
            return -1;
        }

        [Test]
        public void TwoAgentsBreachInHalfTheTicksOfOne()
        {
            var f1 = Rig();
            f1.Grid.SetWall(f1.Grid[1, 0], 15, 6f);
            int oneAgentTicks = TicksUntilBreach(f1.Grid, new List<AgentState> { Digger(f1, 0) });

            var f2 = Rig();
            f2.Grid.SetWall(f2.Grid[1, 0], 15, 6f);
            int twoAgentTicks = TicksUntilBreach(f2.Grid, new List<AgentState> { Digger(f2, 0), Digger(f2, 1) });

            Assert.AreEqual(6, oneAgentTicks, "1 agent at digRate 1 on 6s health should take 6 ticks");
            Assert.AreEqual(3, twoAgentTicks, "2 agents should exactly halve the time");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void BreachRewardIsCreditedExactlyOnceRegardlessOfAgentCount(int agentCount)
        {
            var f = Rig();
            f.Grid.SetWall(f.Grid[1, 0], 15, 0.01f); // breaks on the first tick no matter how many agents share it

            var agents = new List<AgentState>();
            for (int i = 0; i < agentCount; i++) agents.Add(Digger(f, i, digRate: 1f, wallBreakReward: 1f));

            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            AgentSystem.Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.AreEqual(1f, budget, "reward must be credited once, not " + agentCount + " times");
            int breaches = 0;
            foreach (SimEvent e in events) if (e.Kind == SimEventKind.WallBreached) breaches++;
            Assert.AreEqual(1, breaches);
            Assert.IsFalse(f.Grid[1, 0].HasWall);
        }

        [Test]
        public void StalledAgentCreditsDeathRewardAndEmitsEvent()
        {
            var f = TestMaps.Parse("C . S");
            var agent = new AgentState(id: 7, f.Grid[2, 0].Position, moveSpeed: 0f, lifeTime: 0.3f, digRate: 0f, deathReward: 0.2f, wallBreakReward: 1f);
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            AgentSystem.Step(f.Grid, agents, 0.5f, occ, ref budget, ref coreHp, events); // 0.3 - 0.5 <= 0

            Assert.IsFalse(agent.IsAlive);
            Assert.AreEqual(0.2f, budget, 1e-6f);
            Assert.AreEqual(10, coreHp, "a stall must never touch core HP");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(SimEventKind.AgentStalled, events[0].Kind);
            Assert.AreEqual(7, events[0].IntA);
            Assert.AreEqual(0.2f, events[0].FloatA, 1e-6f);
        }

        [Test]
        public void LeakedAgentDamagesCoreAndEmitsEvent()
        {
            var f = TestMaps.Parse("C . S");
            new FlowField().Generate(f.Grid, f.Map.Core); // needed so the core tile's BestCost is actually 0

            var agent = new AgentState(id: 3, f.Grid.Get(f.Map.Core).Position, moveSpeed: 0f, lifeTime: 100f, digRate: 0f, deathReward: 0.2f, wallBreakReward: 1f);
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            AgentSystem.Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.IsFalse(agent.IsAlive);
            Assert.AreEqual(9, coreHp);
            Assert.AreEqual(0f, budget, "reaching the core pays no reward");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(SimEventKind.AgentLeaked, events[0].Kind);
            Assert.AreEqual(3, events[0].IntA);
        }

        [Test]
        public void MovementNeverOvershootsPastTheNextTileEvenAtHighSpeedTimesDt()
        {
            var f = TestMaps.Parse("C . . S");
            new FlowField().Generate(f.Grid, f.Map.Core);

            SimNode spawnNode = f.Grid.Get(f.Map.Spawns[0]);
            SimNode expectedNext = f.Grid.NextOf(spawnNode);
            Assert.IsNotNull(expectedNext, "test rig assumption: spawn must have a next tile");

            // Tile spacing is 2 world units (nodeRadius 1); moveSpeed*dt = 10*0.5 = 5,
            // more than double that spacing, so an unclamped step would blow straight
            // through the next tile and beyond.
            var agent = new AgentState(id: 0, spawnNode.Position, moveSpeed: 10f, lifeTime: 100f, digRate: 0f, deathReward: 0f, wallBreakReward: 0f);
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;

            AgentSystem.Step(f.Grid, agents, 0.5f, occ, ref budget, ref coreHp, null);

            Assert.AreEqual(expectedNext.Position, agent.Position);
        }

        [Test]
        public void DeadAgentsAreSkippedEntirely()
        {
            var f = Rig();
            var agent = Digger(f, 0);
            agent.IsAlive = false;
            float lifeBefore = agent.LifeTime;
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            bool dirty = AgentSystem.Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.IsFalse(dirty);
            Assert.AreEqual(lifeBefore, agent.LifeTime);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0f, budget);
        }

        [Test]
        public void OccupancyAndMinCostSeenAreTrackedForLiveNonLeakingAgents()
        {
            var f = TestMaps.Parse("C . S");
            new FlowField().Generate(f.Grid, f.Map.Core);

            SimNode spawnNode = f.Grid.Get(f.Map.Spawns[0]);
            var agent = new AgentState(id: 0, spawnNode.Position, moveSpeed: 0f, lifeTime: 100f, digRate: 0f, deathReward: 0f, wallBreakReward: 0f);
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;

            Assert.AreEqual(SimNode.Infinity, agent.MinCostSeen);
            AgentSystem.Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, null);

            Assert.AreEqual(1, occ[spawnNode]);
            Assert.AreEqual(spawnNode.BestCost, agent.MinCostSeen);

            // A leaking agent must not be counted - it already exited before the
            // occupancy/min-cost line in AgentSystem.Step runs.
            var leaker = new AgentState(id: 1, f.Grid.Get(f.Map.Core).Position, moveSpeed: 0f, lifeTime: 100f, digRate: 0f, deathReward: 0f, wallBreakReward: 0f);
            AgentSystem.Step(f.Grid, new List<AgentState> { leaker }, 1f, occ, ref budget, ref coreHp, null);
            Assert.AreEqual(SimNode.Infinity, leaker.MinCostSeen);
        }
    }
}
