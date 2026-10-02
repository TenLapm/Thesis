using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The agent pass on its own: leaking, digging, moving, the slow countdown. Kills
    // happen in the tower pass and are tested in TowerSystemTests.
    public class AgentSystemTests
    {
        // Common rig: "C # S", a wall tile between the core and a single spawn, one
        // straight row so the agent can sit exactly on the wall tile without
        // needing the flow field generated (dig logic never reads BestCost).
        private static AsciiFixture Rig()
        {
            return TestMaps.Parse("C # S");
        }

        private static readonly List<TowerState> NoTowers = new List<TowerState>();

        private static AgentState Agent(int id, Vec2f position, float speed = 1f, float digRate = 1f, float wallBreakReward = 1f, float hp = 10f)
        {
            return new AgentState(id, position, speed, hp, DamageTypes.AllOnes(), digRate, 0.2f, wallBreakReward);
        }

        private static AgentState Digger(AsciiFixture f, int id, float digRate = 1f, float wallBreakReward = 1f)
        {
            return Agent(id, f.Grid[1, 0].Position, speed: 1f, digRate: digRate, wallBreakReward: wallBreakReward);
        }

        private static bool Step(SimGrid grid, IList<AgentState> agents, float dt, OccupancyMap occ, ref float budget, ref int coreHp,
                                 IList<SimEvent> events, IList<TowerState> towers = null, float towerBreachReward = 0f)
        {
            return AgentSystem.Step(grid, agents, towers ?? NoTowers, dt, occ, towerBreachReward, ref budget, ref coreHp, events);
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
                if (Step(grid, agents, 1f, occ, ref budget, ref coreHp, null)) return ticks;
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

            Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.AreEqual(1f, budget, "reward must be credited once, not " + agentCount + " times");
            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.WallBreached));
            Assert.IsFalse(f.Grid[1, 0].HasWall);
            Assert.AreEqual(Occupant.None, f.Grid[1, 0].Occupant);
        }

        [Test]
        public void ChewingThroughATowerTileDestroysTheTowerAndPaysNothingByDefault()
        {
            var f = Rig();
            var tower = TestTowers.StateOn(f, 0, TestTowers.Gun(), 1, 0);
            f.Grid.SetTower(f.Grid[1, 0], 20, 0.01f, tower.Id);
            var towers = new List<TowerState> { tower };

            var agents = new List<AgentState> { Digger(f, 0), Digger(f, 1) };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            bool dirty = Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events, towers);

            Assert.IsTrue(dirty, "the field must be rebuilt");
            Assert.IsFalse(tower.IsAlive);
            Assert.IsFalse(f.Grid[1, 0].HasWall);
            Assert.AreEqual(-1, f.Grid[1, 0].TowerId);
            Assert.AreEqual(0f, budget, "no reward for a destroyed tower by default");
            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.TowerDestroyed), "once, although two agents shared the tile");
            Assert.AreEqual(0, TestTowers.Count(events, SimEventKind.WallBreached));
            SimEvent e = events[0];
            Assert.AreEqual(0, e.IntA, "tower id");
            Assert.AreEqual(1, e.IntB, "tile x");
            Assert.AreEqual(0, e.IntC, "tile y");
        }

        [Test]
        public void ATowerBreachRewardIsPaidWhenConfigured()
        {
            var f = Rig();
            var tower = TestTowers.StateOn(f, 0, TestTowers.Gun(), 1, 0);
            f.Grid.SetTower(f.Grid[1, 0], 20, 0.01f, tower.Id);

            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;
            Step(f.Grid, new List<AgentState> { Digger(f, 0) }, 1f, occ, ref budget, ref coreHp, null, new List<TowerState> { tower }, towerBreachReward: 3f);

            Assert.AreEqual(3f, budget);
        }

        [Test]
        public void LeakedAgentDamagesCoreAndEmitsEvent()
        {
            var f = TestMaps.Parse("C . S");
            new FlowField().Generate(f.Grid, f.Map.Core); // needed so the core tile's BestCost is actually 0

            var agent = Agent(3, f.Grid.Get(f.Map.Core).Position);
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.IsFalse(agent.IsAlive);
            Assert.IsTrue(agent.Leaked);
            Assert.IsFalse(agent.Killed);
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
            var agent = Agent(0, spawnNode.Position, speed: 10f);
            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;

            Step(f.Grid, new List<AgentState> { agent }, 0.5f, occ, ref budget, ref coreHp, null);

            Assert.AreEqual(expectedNext.Position, agent.Position);
        }

        // A slow of N ticks slows exactly N steps, then the agent is back to full speed.
        [Test]
        public void ASlowedAgentCoversLessGroundUntilTheSlowWearsOff()
        {
            var f = TestMaps.Parse("C . . . . . . . . S");
            new FlowField().Generate(f.Grid, f.Map.Core);
            Vec2f start = f.Grid.Get(f.Map.Spawns[0]).Position;

            var normal = Agent(0, start, speed: 1f);
            var slowed = Agent(1, start, speed: 1f);
            slowed.SlowTicks = 3;
            slowed.SlowFactor = 0.5f;

            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;
            var agents = new List<AgentState> { normal, slowed };

            // dt 0.25: a full step is 0.25 world units, a slowed one 0.125.
            for (int i = 0; i < 3; i++) Step(f.Grid, agents, 0.25f, occ, ref budget, ref coreHp, null);
            Assert.AreEqual(0.75f, start.X - normal.Position.X, 1e-6f);
            Assert.AreEqual(0.375f, start.X - slowed.Position.X, 1e-6f, "three slowed steps");
            Assert.AreEqual(0, slowed.SlowTicks);
            Assert.AreEqual(1f, slowed.SlowFactor, "the factor resets when the slow ends");

            for (int i = 0; i < 2; i++) Step(f.Grid, agents, 0.25f, occ, ref budget, ref coreHp, null);
            Assert.AreEqual(0.375f + 0.5f, start.X - slowed.Position.X, 1e-6f, "then two full steps");
        }

        [Test]
        public void TheSlowCountsDownWhileDiggingToo()
        {
            var f = Rig();
            f.Grid.SetWall(f.Grid[1, 0], 15, 100f);
            AgentState digger = Digger(f, 0);
            digger.SlowTicks = 2;
            digger.SlowFactor = 0.5f;

            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;
            var agents = new List<AgentState> { digger };
            Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, null);
            Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, null);

            Assert.AreEqual(0, digger.SlowTicks);
            Assert.AreEqual(98f, f.Grid[1, 0].WallHealth, "a slow does not change the dig rate");
        }

        [Test]
        public void DeadAgentsAreSkippedEntirely()
        {
            var f = Rig();
            f.Grid.SetWall(f.Grid[1, 0], 15, 6f);
            var agent = Digger(f, 0);
            agent.IsAlive = false;
            agent.SlowTicks = 5;
            var agents = new List<AgentState> { agent };
            var occ = new OccupancyMap(f.Grid.NodeCount);
            var events = new List<SimEvent>();
            float budget = 0f;
            int coreHp = 10;

            bool dirty = Step(f.Grid, agents, 1f, occ, ref budget, ref coreHp, events);

            Assert.IsFalse(dirty);
            Assert.AreEqual(5, agent.SlowTicks, "nothing about a dead agent changes");
            Assert.AreEqual(6f, f.Grid[1, 0].WallHealth);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0f, budget);
            Assert.AreEqual(0, occ.Total());
        }

        [Test]
        public void OccupancyAndMinCostSeenAreTrackedForLiveNonLeakingAgents()
        {
            var f = TestMaps.Parse("C . S");
            new FlowField().Generate(f.Grid, f.Map.Core);

            SimNode spawnNode = f.Grid.Get(f.Map.Spawns[0]);
            var agent = Agent(0, spawnNode.Position, speed: 0.001f);
            var occ = new OccupancyMap(f.Grid.NodeCount);
            float budget = 0f;
            int coreHp = 10;

            Assert.AreEqual(SimNode.Infinity, agent.MinCostSeen);
            Step(f.Grid, new List<AgentState> { agent }, 1f, occ, ref budget, ref coreHp, null);

            Assert.AreEqual(1, occ[spawnNode]);
            Assert.AreEqual(spawnNode.BestCost, agent.MinCostSeen);

            // A leaking agent must not be counted - it already exited before the
            // occupancy/min-cost line in AgentSystem.Step runs.
            var leaker = Agent(1, f.Grid.Get(f.Map.Core).Position);
            Step(f.Grid, new List<AgentState> { leaker }, 1f, occ, ref budget, ref coreHp, null);
            Assert.AreEqual(SimNode.Infinity, leaker.MinCostSeen);
        }

        [Test]
        public void AnAgentNeedsOneResistancePerDamageType()
        {
            Assert.Throws<System.ArgumentException>(() => new AgentState(0, new Vec2f(0f, 0f), 1f, 10f, null, 1f, 0f, 0f));
            Assert.Throws<System.ArgumentException>(() => new AgentState(0, new Vec2f(0f, 0f), 1f, 10f, new float[1], 1f, 0f, 0f));
        }
    }
}
