using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The siege enemy and the rough director (2026-10-26): a siege stops outside most
    // towers' reach and knocks the most crowded ones out for the rest of the wave; the
    // director answers towers stacked at the core with an outpost raid, or with siege
    // when there is nothing bare to raid.
    public class SiegeDirectorTests
    {
        // 20 x 3, open. S is (0,1), C is (19,1).
        private const string Lane = @"
            . . . . . . . . . . . . . . . . . . . .
            S . . . . . . . . . . . . . . . . . . C
            . . . . . . . . . . . . . . . . . . . .";

        // 9 x 5, open. S is (0,2), C is (8,2). Deposits at (4,0) and (4,4).
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        private sealed class OneGroup : IWavePlanner
        {
            public AgentGroup Group;
            public string Name => "one";
            public WavePlan PlanWave(WaveContext context) { return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "one", Groups = new[] { Group.Copy() } }; }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        private static SimConfig Config()
        {
            return new SimConfig
            {
                ShopOffers = 0, StartBudget = 100f, CoreMaxHp = 100000, AgentsPerWave = 1, WaitForStart = true, BuildMenu = true,
                SiegeSetupTicks = 10, SiegeIntervalTicks = 5, SiegeDamage = 5f, TowerSiegeHp = 10f,
            };
        }

        private static ShapeDef[] OneTile()
        {
            return new[] { new ShapeDef { Name = "Wall", BuildCost = 1, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } } };
        }

        private static Simulation New(SimConfig config, MapData map, IWavePlanner planner, params TowerDef[] towers)
        {
            return new Simulation(config, map, OneTile(), towers.Length > 0 ? towers : new[] { TestTowers.Gun(cost: 5) }, 1, planner);
        }

        private static AgentGroup Siege(SimConfig config, MapData map, float hp)
        {
            AgentGroup g = EnemyArchetypes.Siege().Apply(new EscalationPlanner(config, map).PlanFor(1).Groups[0], 1f);
            g.Count = 1;
            g.Hp = hp;
            return g;
        }

        // ------------------------------------------------------------------ the siege enemy

        [Test]
        public void ASiegeStopsOutsideTheTowersReachKnocksTheCrowdedOnesOutFirstAndTheyAreWholeAfterTheWave()
        {
            SimConfig config = Config();
            MapData map = TestMaps.Parse(Lane).Map;
            var planner = new OneGroup();
            Simulation sim = New(config, map, planner);
            planner.Group = Siege(config, map, 1000000f);
            TestSims.Send(sim, SimCommand.BuyTower(0, 17, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 18, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 17, 2));
            SimState s = sim.State;
            Assert.AreEqual(3, s.Towers.Count);

            TestSims.Send(sim, SimCommand.StartWaveNow());
            var knocked = new List<int>();
            float firstShotX = float.NaN;
            for (int guard = 0; guard < 5000 && knocked.Count < 3; guard++)
            {
                sim.Tick();
                foreach (SimEvent e in sim.LastTickEvents)
                {
                    if (e.Kind == SimEventKind.TowerSieged && float.IsNaN(firstShotX)) firstShotX = s.Agents[e.IntA].Position.X;
                    if (e.Kind == SimEventKind.TowerKnockedOut) knocked.Add(e.IntA);
                }
            }
            Assert.AreEqual(3, knocked.Count, "every tower knocked out in the end");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, knocked.GetRange(0, 2), "the two side by side first");
            Assert.Less(firstShotX, s.Grid[14, 1].Position.X, "it fires from outside the guns' reach of 3 tiles");
            Assert.AreEqual(0, s.Towers[0].Shots, "so they never hit it");

            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(1, s.LastOutcome.Leaked, "then it walks on to the core");
            foreach (TowerState t in s.Towers)
            {
                Assert.IsTrue(t.IsAlive, "a siege never destroys a tower");
                Assert.IsFalse(t.KnockedOut, "whole again for the build phase");
                Assert.AreEqual(0f, t.SiegeDamage);
            }
        }

        [Test]
        public void ATowerThatOutrangesTheSiegeKillsItWhileItSetsUp()
        {
            SimConfig config = Config();
            config.SiegeSetupTicks = 100;
            MapData map = TestMaps.Parse(Lane).Map;
            var planner = new OneGroup();
            Simulation sim = New(config, map, planner, TestTowers.Gun(damage: 50f, range: 6f, interval: 10, cost: 5));
            planner.Group = Siege(config, map, 120f);
            TestSims.Send(sim, SimCommand.BuyTower(0, 17, 0));

            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(1, sim.State.LastOutcome.Killed);
            Assert.IsFalse(events.Exists(e => e.Kind == SimEventKind.TowerSieged), "dead before its first shot");
        }

        [Test]
        public void ASlowStretchesTheSetUp()
        {
            int SetUpTicks(bool slowed)
            {
                SimConfig config = Config();
                config.SiegeSetupTicks = 60;
                MapData map = TestMaps.Parse(Lane).Map;
                var planner = new OneGroup();
                TowerDef slower = TestTowers.Slower(slowTicks: 40, slowFactor: 0.5f, range: 6f, interval: 10);
                slower.Cost = 5;
                Simulation sim = New(config, map, planner, TestTowers.Gun(cost: 5), slower);
                planner.Group = Siege(config, map, 1000000f);
                TestSims.Send(sim, SimCommand.BuyTower(slowed ? 1 : 0, 17, 0));
                TestSims.Send(sim, SimCommand.StartWaveNow());
                int stoodFrom = -1;
                for (int tick = 0; tick < 5000; tick++)
                {
                    sim.Tick();
                    AgentState siege = sim.State.Agents.Count > 0 ? sim.State.Agents[0] : null;
                    if (stoodFrom < 0 && siege != null && siege.SiegeSetup > 0f) stoodFrom = tick;
                    foreach (SimEvent e in sim.LastTickEvents) if (e.Kind == SimEventKind.TowerSieged) return tick - stoodFrom;
                }
                Assert.Fail("never fired");
                return -1;
            }

            int plain = SetUpTicks(false), slowed = SetUpTicks(true);
            Assert.AreEqual(60, plain, 1);
            Assert.Greater(slowed, plain + 40, "half speed while slowed: nearly twice as long");
        }

        // ------------------------------------------------------------------ the rough director

        [Test]
        public void TheDirectorAnswersEachWeaknessOnItsOwn()
        {
            // 2026-10-28: a raid needs bare outposts, not a stack; a stack is the densest cluster
            // anywhere; one damage type relied on brings resistant enemies; weak anti-air, flyers.
            var stackedWithBareOutposts = new RoughDirector.Board { Towers = 6, StackLean = 1f, Collectors = 2, BareCollectors = 2 };
            var stackedNoOutposts = new RoughDirector.Board { Towers = 6, StackLean = 1f };
            var stackedGuardedOutposts = new RoughDirector.Board { Towers = 6, StackLean = 1f, Collectors = 2 };
            var spread = new RoughDirector.Board { Towers = 6, StackLean = 0.2f, Collectors = 2, BareCollectors = 2 };

            Assert.AreEqual(RoughDirector.Raid, RoughDirector.Choose(stackedWithBareOutposts, 8, null));
            Assert.AreEqual(RoughDirector.SiegeStack, RoughDirector.Choose(stackedNoOutposts, 8, null));
            Assert.AreEqual(RoughDirector.SiegeStack, RoughDirector.Choose(stackedGuardedOutposts, 8, null));
            Assert.AreEqual(RoughDirector.Raid, RoughDirector.Choose(spread, 8, null), "bare outposts are raided, stacked or not");
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(new RoughDirector.Board { Towers = 6, StackLean = 0.2f, Collectors = 2 }, 8, null), "nothing to answer");
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(stackedNoOutposts, 4, null), "no siege before wave 5");
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(stackedWithBareOutposts, 3, null), "no raid before wave 4");

            // A close call goes the other way after the same strategy last wave.
            var lean = new RoughDirector.Board { Towers = 6, StackLean = 0.4f, Collectors = 1, BareCollectors = 1 };
            Assert.AreEqual(RoughDirector.Raid, RoughDirector.Choose(lean, 8, null));
            Assert.AreEqual(RoughDirector.SiegeStack, RoughDirector.Choose(lean, 8, RoughDirector.Raid));

            var fire = new RoughDirector.Board { Towers = 6, Dominant = DamageType.Fire, DominantShare = 0.95f, AirShare = 0.5f };
            Assert.AreEqual(RoughDirector.ResistMix, RoughDirector.Choose(fire, 8, null));
            var weakAir = new RoughDirector.Board { Towers = 6, DominantShare = 0.5f, AirShare = 0.05f };
            Assert.AreEqual(RoughDirector.AirRaid, RoughDirector.Choose(weakAir, 8, null));
            var noAir = new RoughDirector.Board { Towers = 6, DominantShare = 0.5f, AirShare = 0f };
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(noAir, 8, null), "no flyers against a board that cannot hit them");
        }

        [Test]
        public void SappersComeOnlyAsAMazeBreachAgainstALongWallMaze()
        {
            var maze = new RoughDirector.Board { Towers = 6, StackLean = 0f, Walls = 20, Stretch = 2.2f };
            Assert.AreEqual(RoughDirector.MazeBreach, RoughDirector.Choose(maze, 8, null));
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(maze, 5, null), "not before wave 6");
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(new RoughDirector.Board { Towers = 6, Walls = 20, Stretch = 1.3f }, 8, null), "a short route is no maze");
            Assert.AreEqual(RoughDirector.Baseline, RoughDirector.Choose(new RoughDirector.Board { Towers = 6, Walls = 4, Stretch = 2.2f }, 8, null), "a few walls are no maze");

            // On an open board the composer's sapper waves (3 and 8 of its cycle) come as walkers.
            SimConfig config = Config();
            config.AgentsPerWave = 20;
            MapData map = MapWithDeposits();
            var director = new RoughDirector(config, map);
            Simulation sim = New(config, map, director);
            foreach (int wave in new[] { 3, 8, 15 })
            {
                WavePlan plan = director.PlanWave(new WaveContext(wave, sim.State, map, config));
                Assert.AreEqual(RoughDirector.Baseline, plan.StrategyId);
                Assert.Greater(plan.TotalAgents(), 0);
                foreach (AgentGroup g in plan.Groups) Assert.AreNotEqual(MovementClass.Sapper, g.Movement, "wave " + wave);
            }
        }

        [Test]
        public void ACollectorOnAnEmptiedDepositIsNoOutpostToRaid()
        {
            SimConfig config = Config();
            config.DepositOre = 50;
            MapData map = MapWithDeposits();
            Simulation sim = New(config, map, new RoughDirector(config, map));
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            Assert.AreEqual(2, RoughDirector.Read(new WaveContext(6, sim.State, map, config)).BareCollectors);
            sim.State.DepositLeft[0] = 0;
            RoughDirector.Board b = RoughDirector.Read(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(1, b.Collectors, "the emptied one does not count");
            Assert.AreEqual(1, b.BareCollectors);
        }

        private static MapData MapWithDeposits()
        {
            MapData map = TestMaps.Parse(Open).Map;
            map.Deposits = new[]
            {
                new Deposit { Tile = new TileCoord(4, 0), Yield = 3 },
                new Deposit { Tile = new TileCoord(4, 4), Yield = 2 },
            };
            return map;
        }

        [Test]
        public void TheDirectorReadsTheBoardAndKeepsAtLeastHalfOfTheWaveOnTheCore()
        {
            SimConfig config = Config();
            config.AgentsPerWave = 20;
            MapData map = MapWithDeposits();
            var director = new RoughDirector(config, map);
            Simulation sim = New(config, map, director);
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 3));
            TestSims.Send(sim, SimCommand.BuyTower(0, 8, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 8, 3));

            // Stacked, nothing to raid: siege.
            WavePlan siege = director.PlanWave(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(RoughDirector.SiegeStack, siege.StrategyId);
            Assert.IsNotNull(RoughDirector.Line(siege.AnnouncementId));
            AgentGroup engines = System.Array.Find(siege.Groups, g => g.Siege);
            Assert.IsNotNull(engines);
            Assert.That(engines.Count, Is.InRange(1, 3));

            // Bare collectors beside the stack: a raid, and most of the wave still for the core.
            TestSims.Send(sim, SimCommand.BuildCollector(4, 0));
            TestSims.Send(sim, SimCommand.BuildCollector(4, 4));
            RoughDirector.Board b = RoughDirector.Read(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(1f, b.StackShare);
            Assert.AreEqual(2, b.BareCollectors, "no tower reaches (4,0) or (4,4)");
            WavePlan raid = director.PlanWave(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(RoughDirector.Raid, raid.StrategyId);
            int raiders = 0;
            foreach (AgentGroup g in raid.Groups) if (g.TargetsCollectors) raiders += g.Count;
            Assert.Greater(raiders, 0);
            Assert.LessOrEqual(raiders * 2, raid.TotalAgents());

            // The same board gives the same plan: the director keeps nothing between calls.
            WavePlan again = director.PlanWave(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(raid.TotalAgents(), again.TotalAgents());
            Assert.AreEqual(raid.StrategyId, again.StrategyId);
        }

        [Test]
        public void AClusterFarFromTheCoreIsAStackToo()
        {
            SimConfig config = Config();
            MapData map = MapWithDeposits(); // the core is (8,2)
            Simulation sim = New(config, map, new RoughDirector(config, map));
            TestSims.Send(sim, SimCommand.BuyTower(0, 1, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 1, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 0));
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 4));
            RoughDirector.Board b = RoughDirector.Read(new WaveContext(6, sim.State, map, config));
            Assert.AreEqual(0.8f, b.StackShare, 1e-5f, "four of the five towers stand together, far from the core");
            Assert.AreEqual(1f, b.StackLean, 1e-5f);
            Assert.AreEqual(DamageType.Physical, b.Dominant);
            Assert.AreEqual(1f, b.DominantShare);
        }

        [Test]
        public void TheDirectorRunsInsideTheSimulationWithoutTouchingTheState()
        {
            SimConfig config = Config();
            config.AgentsPerWave = 4;
            MapData map = MapWithDeposits();
            Simulation sim = New(config, map, new RoughDirector(config, map));
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 7, 3));
            TestSims.Send(sim, SimCommand.BuyTower(0, 8, 1));
            TestSims.Send(sim, SimCommand.BuyTower(0, 8, 3));
            var seen = new HashSet<string>();
            for (int wave = 0; wave < 8; wave++)
            {
                TestSims.Send(sim, SimCommand.StartWaveNow());
                TestSims.RunUntilWaveResolves(sim); // Simulation throws if the planner changes the state
                seen.Add(sim.State.CurrentPlan.StrategyId);
            }
            Assert.Contains(RoughDirector.SiegeStack, new List<string>(seen));
        }
    }
}
