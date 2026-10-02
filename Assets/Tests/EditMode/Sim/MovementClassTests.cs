using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The three movement classes in play (WP-C2, ARCHITECTURE.md §4.6): what a
    // ground enemy, a sapper and a flyer each do on the same board, and what a
    // tower may do to them. The fields themselves are tested in FlowFieldSetTests.
    public class MovementClassTests
    {
        private static SimConfig Quick(int coreHp = 1000) { return new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = coreHp }; }

        private static List<SimEvent> RunUntilWaveResolves(Simulation sim, int maxTicks = 8000)
        {
            var all = new List<SimEvent>();
            int wave = sim.State.WaveIndex;
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Tick();
                all.AddRange(sim.LastTickEvents);
                if (sim.State.LastOutcome != null && sim.State.LastOutcome.WaveIndex > wave) return all;
                if (sim.State.IsGameOver) return all;
            }
            Assert.Fail("the wave did not resolve within " + maxTicks + " ticks");
            return all;
        }

        // ================================================================ sapper against ground

        // The corridor of FlowFieldSetTests: straight through costs a wall, the way
        // round is 2*height + 6 steps over the top of a block of static tiles.
        private static string Corridor(int height)
        {
            var sb = new StringBuilder();
            sb.Append(". . . . . . .\n");
            for (int i = 0; i < height - 1; i++) sb.Append(". X X X X X .\n");
            sb.Append("S . . # . . C");
            return sb.ToString();
        }

        // WORKPLAN WP-C2: "Ground routes around it and Sapper digs through it", played.
        [Test]
        public void AGroundWaveWalksRoundTheWallAndASapperWaveDigsThroughIt()
        {
            Simulation ground = TestSims.AsciiWithWalls(Corridor(5), new FixedPlanner(FixedPlanner.Group(count: 3, hp: 10f, speed: 5f, intervalTicks: 5)), Quick());
            TestSims.Send(ground, SimCommand.StartWaveNow());
            List<SimEvent> groundEvents = RunUntilWaveResolves(ground);

            Assert.AreEqual(0, TestTowers.Count(groundEvents, SimEventKind.WallBreached), "ground enemies never touch the wall");
            Assert.AreEqual(6f, ground.State.Grid[3, 0].WallHealth, "it is still whole");
            Assert.AreEqual(3, ground.State.LastOutcome.Leaked);
            Assert.AreEqual(0, ground.State.LastOutcome.WallsBreached);
            Assert.AreEqual(0, ground.State.Occupancy[ground.State.Grid[3, 0]], "nobody stood on the wall tile");
            Assert.Greater(ground.State.Occupancy[ground.State.Grid[0, 5]], 0, "they went over the top");

            Simulation sappers = TestSims.AsciiWithWalls(Corridor(5), new FixedPlanner(FixedPlanner.Sappers(count: 3, hp: 10f, speed: 5f, digRate: 4f, intervalTicks: 5)), Quick());
            TestSims.Send(sappers, SimCommand.StartWaveNow());
            List<SimEvent> sapperEvents = RunUntilWaveResolves(sappers);

            Assert.AreEqual(1, TestTowers.Count(sapperEvents, SimEventKind.WallBreached), "the sappers chewed through it");
            SimEvent breach = sapperEvents.Find(e => e.Kind == SimEventKind.WallBreached);
            Assert.AreEqual(3, breach.IntA);
            Assert.AreEqual(0, breach.IntB);
            Assert.IsFalse(sappers.State.Grid[3, 0].HasWall);
            WaveOutcome o = sappers.State.LastOutcome;
            Assert.AreEqual(3, o.Leaked);
            Assert.AreEqual(1, o.WallsBreached);
            Assert.AreEqual(61f, o.BudgetAfter, 1e-4f, "a breach pays the player, whoever did the digging");
            Assert.AreEqual(0, sappers.State.Occupancy[sappers.State.Grid[0, 5]], "nobody went over the top");

            Assert.Less(o.TickResolved, ground.State.LastOutcome.TickResolved, "and the short way is quicker, digging included");
        }

        [Test]
        public void ASapperAndAGroundEnemyOnTheSameTileStepDifferentWays()
        {
            AsciiFixture f = TestFields.Built(Corridor(5), 0.2f);
            SimNode spawn = f.Grid.Get(f.Map.Spawns[0]);
            AgentState walker = TestTowers.Agent(0, spawn.Position, speed: 1f);
            AgentState sapper = TestTowers.Agent(1, spawn.Position, speed: 1f, movement: MovementClass.Sapper);

            float budget = 0f;
            int coreHp = 10;
            AgentSystem.Step(f.Grid, f.Grid.Get(f.Map.Core), new[] { walker, sapper }, new TowerState[0], 0.5f, new OccupancyMap(f.Grid.NodeCount), 0f, ref budget, ref coreHp, null);

            Assert.AreEqual(spawn.Position.X, walker.Position.X, "the walker heads up, for the way round");
            Assert.AreEqual(spawn.Position.Y + 0.5f, walker.Position.Y);
            Assert.AreEqual(spawn.Position.X + 0.5f, sapper.Position.X, "the sapper heads along the corridor, for the wall");
            Assert.AreEqual(spawn.Position.Y, sapper.Position.Y);

            Assert.AreEqual(160, walker.MinCostSeen, "each is measured on its own field");
            Assert.AreEqual(80, sapper.MinCostSeen);
        }

        // ================================================================ flyers

        // A wall and a static blocker stand on the straight line from S to C.
        private const string Obstructed = @"
            . . . . . . . . .
            S . . # . X . . C
            . . . . . . . . .";

        // WORKPLAN WP-C2: "flyers ignore walls and towers, never dig, and leak at the core."
        [Test]
        public void FlyersCrossWallsTowersAndBlockersInAStraightLineAndLeakAtTheCore()
        {
            // A tower on the line too. It cannot hit flyers, so it is only terrain here.
            var roster = new[] { TestTowers.Gun(damage: 5f, range: 20f, interval: 1, canHitFlying: false) };
            var planner = new FixedPlanner(FixedPlanner.Flyers(count: 3, hp: 10f, speed: 5f, intervalTicks: 10));
            Simulation sim = TestSims.AsciiWithWalls(Obstructed, planner, Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 6, 1));
            Assert.AreEqual(1, sim.State.Towers.Count, "rig: the tower stands on the line");
            int version = sim.State.Grid.FieldVersion;

            TestSims.Send(sim, SimCommand.StartWaveNow());
            float lineY = sim.State.Grid.Get(sim.Map.Spawns[0]).Position.Y;
            var events = new List<SimEvent>();
            var tilesFlownOver = new HashSet<int>();
            int firstLeakTick = -1;
            for (int i = 0; i < 2000 && sim.State.LastOutcome == null; i++)
            {
                sim.Tick();
                events.AddRange(sim.LastTickEvents);
                foreach (AgentState a in sim.State.LiveAgents)
                {
                    Assert.AreEqual(MovementClass.Flying, a.Movement);
                    Assert.AreEqual(lineY, a.Position.Y, "tick " + sim.State.Tick + ": a flyer never leaves the straight line");
                    tilesFlownOver.Add(sim.State.Grid.NodeFromPosition(a.Position).X);
                }
                if (firstLeakTick < 0 && TestTowers.Count(events, SimEventKind.AgentLeaked) > 0) firstLeakTick = sim.State.Tick;
            }

            WaveOutcome o = sim.State.LastOutcome;
            Assert.IsNotNull(o, "the wave ended by itself");
            Assert.IsFalse(o.TimedOut);
            Assert.AreEqual(3, o.Leaked, "all three reached the core");
            Assert.AreEqual(997, o.CoreHpAfter);
            Assert.AreEqual(0, o.Killed);

            CollectionAssert.IsSupersetOf(tilesFlownOver, new[] { 3, 5, 6 }, "over the wall (3), the static blocker (5) and the tower (6)");
            Assert.AreEqual(0, TestTowers.Count(events, SimEventKind.WallBreached), "never digs");
            Assert.AreEqual(0, TestTowers.Count(events, SimEventKind.TowerDestroyed));
            Assert.AreEqual(0, o.WallsBreached);
            Assert.AreEqual(0, o.TowersDestroyed);
            Assert.AreEqual(6f, sim.State.Grid[3, 1].WallHealth, "the wall is untouched");
            Assert.IsTrue(sim.State.Towers[0].IsAlive);
            Assert.AreEqual(roster[0].TowerHealth, sim.State.Grid[6, 1].WallHealth, "and so is the tower's tile");
            Assert.AreEqual(version, sim.State.Grid.FieldVersion, "so the fields never needed rebuilding");

            Assert.AreEqual(0, sim.State.Towers[0].Shots, "a tower that cannot hit flyers had nothing to shoot");
            Assert.AreEqual(60f, o.BudgetAfter, 1e-4f, "nothing was killed, nothing was breached");

            // 8 tiles = 16 world units at 5 a second is 160 ticks to the core's centre;
            // the core tile begins a little before its centre.
            Assert.That(firstLeakTick, Is.InRange(155, 162), "straight-line flight time");
        }

        [Test]
        public void AFlyerHeadsStraightForTheCoreFromAnywhere()
        {
            // The core is up and to the right of the spawn: the line is a diagonal.
            const string diagonal = @"
                . . . . . . C
                . # # # # # .
                . # . . . # .
                S # . . . # .";
            Simulation sim = TestSims.AsciiWithWalls(diagonal, new FixedPlanner(FixedPlanner.Flyers(count: 1, hp: 10f, speed: 4f)), Quick());
            int version = sim.State.Grid.FieldVersion;
            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();

            AgentState flyer = sim.State.LiveAgents[0];
            Vec2f core = sim.State.Grid.Get(sim.Map.Core).Position;
            Vec2f start = sim.State.Grid.Get(sim.Map.Spawns[0]).Position;
            float last = Vec2f.Distance(start, core);
            int steps = 0;
            while (flyer.IsAlive && steps < 2000)
            {
                sim.Tick();
                steps++;
                if (!flyer.IsAlive) break;

                float now = Vec2f.Distance(flyer.Position, core);
                Assert.Less(now, last, "every step brings it nearer the core");
                last = now;

                // On the line from start to core: the cross product of (core - start)
                // and (position - start) stays at zero, give or take float rounding.
                // (Following the flow field round the walls would put it in the tens.)
                float cross = (core.X - start.X) * (flyer.Position.Y - start.Y) - (core.Y - start.Y) * (flyer.Position.X - start.X);
                Assert.AreEqual(0f, cross, 0.05f, "step " + steps + " left the straight line");
            }

            Assert.IsTrue(flyer.Leaked);
            Assert.Greater(steps, 100, "rig: a flight of some length");
            Assert.AreEqual(version, sim.State.Grid.FieldVersion, "no field was rebuilt: nothing was dug");
        }

        [Test]
        public void ASlowedFlyerFliesSlower()
        {
            var roster = new[] { new TowerDef { Id = "net", Cost = 0, DamageType = DamageType.Frost, Damage = 0f, RangeTiles = 30f, FireIntervalTicks = 1, SlowTicks = 1000, SlowFactor = 0.5f, CanHitFlying = true } };
            Simulation slowed = TestSims.AsciiWithWalls(Obstructed, new FixedPlanner(FixedPlanner.Flyers(count: 1, hp: 10f, speed: 5f)), Quick(), towers: roster);
            TestSims.Send(slowed, SimCommand.PlaceTower(0, 4, 0));
            TestSims.Send(slowed, SimCommand.StartWaveNow());
            RunUntilWaveResolves(slowed);

            Simulation free = TestSims.AsciiWithWalls(Obstructed, new FixedPlanner(FixedPlanner.Flyers(count: 1, hp: 10f, speed: 5f)), Quick());
            TestSims.Send(free, SimCommand.StartWaveNow());
            RunUntilWaveResolves(free);

            int freeTicks = free.State.LastOutcome.TickResolved;
            int slowedTicks = slowed.State.LastOutcome.TickResolved;
            Assert.That(slowedTicks, Is.InRange(2 * freeTicks - 4, 2 * freeTicks + 4), "half speed: twice the flight time (" + freeTicks + " ticks unslowed)");
            Assert.AreEqual(1, slowed.State.LastOutcome.Leaked, "slowed, not stopped: it still arrives");
        }

        [Test]
        public void FlyersAreNotGroundTrafficButTheirNearestApproachIsStillMeasured()
        {
            // Stopped two tiles short of the core by a kill, so MinCostSeen can be read.
            var roster = new[] { TestTowers.Gun(damage: 100f, range: 2f, interval: 1, canHitFlying: true) };
            Simulation sim = TestSims.AsciiWithWalls(Obstructed, new FixedPlanner(FixedPlanner.Flyers(count: 1, hp: 10f, speed: 5f)), Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 8, 0)); // beside the core
            TestSims.Send(sim, SimCommand.StartWaveNow());
            RunUntilWaveResolves(sim);

            AgentState flyer = sim.State.Agents[0];
            Assert.IsTrue(flyer.Killed);
            Assert.AreEqual(0, sim.State.Occupancy.Total(), "the occupancy map is traffic on the ground");

            // The tower at (8,0) reaches 2 tiles; the core is the tile above it. The
            // flyer on row 1 comes into range sqrt(2^2 - 1^2) = 1.73 tiles from the
            // core's centre and is killed on the next tower pass.
            Assert.That(flyer.MinCostSeen, Is.InRange(16, 19), "straight-line tenths of a tile");

            WaveOutcome o = sim.State.LastOutcome;
            Assert.AreEqual(1, o.PressureCount, "it came within the pressure radius, by its own measure");
            Assert.AreEqual(10f, sim.State.Damage.Total(), "damage to a flyer is booked like any other");
        }

        // ================================================================ towers and flyers

        // Two rows. The heavy wall right of the core makes the ground route swing
        // through the top row, so a tile's ground cost and its straight-line distance
        // to the core disagree.
        private const string TwoRows = @"
            . . . . . . . .
            C H . . . . . S";

        private sealed class Rig
        {
            public AsciiFixture F;
            public List<TowerState> Towers = new List<TowerState>();
            public List<AgentState> Agents = new List<AgentState>();
            public List<SimEvent> Events = new List<SimEvent>();
            public DamageMap Damage;
            public float[] ByType = new float[DamageTypes.Count];
            public float Budget;

            public TowerState Tower(TowerDef def, int x, int y) { TowerState t = TestTowers.StateOn(F, Towers.Count, def, x, y); Towers.Add(t); return t; }

            public AgentState Agent(int x, int y, MovementClass movement, float hp = 10f)
            {
                var a = TestTowers.Agent(Agents.Count, F.Grid[x, y].Position, hp, movement: movement);
                Agents.Add(a);
                return a;
            }

            public void Step(int times = 1)
            {
                for (int i = 0; i < times; i++) TowerSystem.Step(F.Grid, F.Grid.Get(F.Map.Core), Towers, Agents, Damage, ByType, 0.25f, ref Budget, Events);
            }
        }

        private static Rig NewRig(string text = TwoRows)
        {
            var rig = new Rig { F = TestFields.Built(text) };
            rig.Damage = new DamageMap(rig.F.Grid.NodeCount);
            return rig;
        }

        // WORKPLAN WP-C2: "a tower with CanHitFlying = false never targets a flyer, even
        // when it is the only agent in range."
        [Test]
        public void ATowerThatCannotHitFlyingNeverTargetsAFlyerEvenWhenItIsAlone()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 3f, range: 10f, interval: 5, canHitFlying: false), 4, 1);
            AgentState flyer = rig.Agent(3, 0, MovementClass.Flying);

            rig.Step(200);
            Assert.AreEqual(0, tower.Shots);
            Assert.AreEqual(0, tower.Cooldown, "it keeps its shot");
            Assert.AreEqual(10f, flyer.Hp);
            Assert.AreEqual(0, TestTowers.Count(rig.Events, SimEventKind.TowerFired));

            // A walker steps into range: the same tower fires at once, at the walker.
            AgentState walker = rig.Agent(6, 0, MovementClass.Ground);
            rig.Step();
            Assert.AreEqual(1, tower.Shots);
            Assert.AreEqual(7f, walker.Hp);
            Assert.AreEqual(10f, flyer.Hp, "and still not at the flyer, though the flyer is nearer the core");
        }

        [Test]
        public void ASapperIsAGroundTargetForEveryTower()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 3f, range: 10f, canHitFlying: false), 4, 1);
            AgentState sapper = rig.Agent(3, 0, MovementClass.Sapper);
            rig.Step();
            Assert.AreEqual(7f, sapper.Hp);
        }

        [Test]
        public void AnAntiAirTowerShootsFlyersAndWalkersAlike()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 3f, range: 10f, interval: 1, canHitFlying: true), 4, 1);
            AgentState flyer = rig.Agent(3, 0, MovementClass.Flying, hp: 3f);
            AgentState walker = rig.Agent(6, 0, MovementClass.Ground, hp: 3f);

            rig.Step(2);
            Assert.IsTrue(flyer.Killed);
            Assert.IsTrue(walker.Killed);
            Assert.AreEqual(2, tower.Kills);
            Assert.AreEqual(0.4f, rig.Budget, 1e-6f, "a flyer pays the same kill reward");
        }

        // First = least way left to the core, each enemy measured the way it travels.
        [Test]
        public void FirstRanksAFlyerByItsStraightLineNotByTheGroundUnderIt()
        {
            Rig rig = NewRig();
            SimNode underFlyer = rig.F.Grid[2, 0];
            SimNode underWalker = rig.F.Grid[2, 1];
            Assert.AreEqual(40, underFlyer.BestCost, "rig: the ground under the flyer is 4 tiles from the core, round the heavy wall");
            Assert.AreEqual(30, underWalker.BestCost, "rig: the walker is 3 tiles away");

            TowerState tower = rig.Tower(TestTowers.Gun(damage: 1f, range: 10f, canHitFlying: true), 5, 1);
            AgentState flyer = rig.Agent(2, 0, MovementClass.Flying);   // id 0: two tiles from the core in a straight line
            AgentState walker = rig.Agent(2, 1, MovementClass.Ground);  // id 1

            Assert.AreEqual(20, RouteCost.ToCore(flyer, underFlyer, rig.F.Grid.Get(rig.F.Map.Core), rig.F.Grid.TileSize));
            Assert.AreEqual(30, RouteCost.ToCore(walker, underWalker, rig.F.Grid.Get(rig.F.Map.Core), rig.F.Grid.TileSize));

            rig.Step();
            Assert.AreEqual(9f, flyer.Hp, "the flyer is nearer: 20 against 30");
            Assert.AreEqual(10f, walker.Hp);

            // The same two as walkers: now the one on the top row is nearer (30 against 40).
            Rig ground = NewRig();
            ground.Tower(TestTowers.Gun(damage: 1f, range: 10f), 5, 1);
            AgentState below = ground.Agent(2, 0, MovementClass.Ground);
            AgentState above = ground.Agent(2, 1, MovementClass.Ground);
            ground.Step();
            Assert.AreEqual(10f, below.Hp);
            Assert.AreEqual(9f, above.Hp);
        }

        [Test]
        public void FirstRanksASapperOnTheSapperField()
        {
            // The corridor: one tile into it, a sapper is nearly there (through the
            // wall) and a walker has the whole way round still to go. Two tiles up the
            // way round it is the other way about.
            Rig rig = NewRig(Corridor(5));
            SimNode inCorridor = rig.F.Grid[1, 0];
            SimNode onTheWayRound = rig.F.Grid[0, 2];
            Assert.AreEqual(170, inCorridor.BestCost, "rig: back to the spawn and round");
            Assert.AreEqual(70, inCorridor.SapperCost, "rig: straight on, through the wall");
            Assert.AreEqual(140, onTheWayRound.BestCost);
            Assert.AreEqual(100, onTheWayRound.SapperCost, "rig: back down and through the wall");

            rig.Tower(TestTowers.Gun(damage: 1f, range: 30f), 6, 5);
            AgentState upThere = rig.Agent(0, 2, MovementClass.Sapper);   // id 0
            AgentState inThere = rig.Agent(1, 0, MovementClass.Sapper);   // id 1
            rig.Step();
            Assert.AreEqual(9f, inThere.Hp, "70 against 100 on the sapper field");
            Assert.AreEqual(10f, upThere.Hp);

            // The same two as walkers are ranked the other way: 140 against 170.
            Rig ground = NewRig(Corridor(5));
            ground.Tower(TestTowers.Gun(damage: 1f, range: 30f), 6, 5);
            AgentState walkerUp = ground.Agent(0, 2, MovementClass.Ground);
            AgentState walkerIn = ground.Agent(1, 0, MovementClass.Ground);
            ground.Step();
            Assert.AreEqual(9f, walkerUp.Hp);
            Assert.AreEqual(10f, walkerIn.Hp);
        }

        [Test]
        public void SplashAndSlowFromAGroundOnlyTowerPassAFlyerBy()
        {
            Rig rig = NewRig();
            var def = new TowerDef { Id = "mortar", Cost = 0, DamageType = DamageType.Fire, Damage = 4f, RangeTiles = 10f, FireIntervalTicks = 25, SplashRadiusTiles = 3f, SlowTicks = 50, SlowFactor = 0.5f, CanHitFlying = false };
            TowerState tower = rig.Tower(def, 5, 1);
            AgentState walker = rig.Agent(3, 0, MovementClass.Ground);
            AgentState flyer = rig.Agent(3, 0, MovementClass.Flying);      // right on top of the target
            AgentState neighbour = rig.Agent(4, 0, MovementClass.Sapper);  // one tile away

            rig.Step();
            Assert.AreEqual(6f, walker.Hp, "the target");
            Assert.AreEqual(6f, neighbour.Hp, "caught in the splash");
            Assert.AreEqual(50, neighbour.SlowTicks);
            Assert.AreEqual(10f, flyer.Hp, "untouched, though it is at the centre of the blast");
            Assert.AreEqual(0, flyer.SlowTicks);
            Assert.AreEqual(1f, flyer.SlowFactor);
            Assert.AreEqual(8f, tower.DamageDealt);
        }

        [Test]
        public void SplashFromAnAntiAirTowerHitsWalkersAndFlyers()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Splash(damage: 4f, range: 10f, splashTiles: 3f, canHitFlying: true), 5, 1);
            AgentState flyer = rig.Agent(2, 0, MovementClass.Flying);   // nearest the core: the target
            AgentState walker = rig.Agent(3, 0, MovementClass.Ground);

            rig.Step();
            Assert.AreEqual(6f, flyer.Hp);
            Assert.AreEqual(6f, walker.Hp);
            SimEvent fired = rig.Events.Find(e => e.Kind == SimEventKind.TowerFired);
            Assert.AreEqual(flyer.Id, fired.IntB, "the flyer was the target");
        }

        [Test]
        public void CanHitIsTheOneRule()
        {
            var aa = TestTowers.Gun(canHitFlying: true);
            var groundOnly = TestTowers.Gun(canHitFlying: false);
            foreach (MovementClass movement in new[] { MovementClass.Ground, MovementClass.Sapper, MovementClass.Flying })
            {
                AgentState a = TestTowers.Agent(0, new Vec2f(0f, 0f), movement: movement);
                Assert.IsTrue(Targeting.CanHit(aa, a), movement.ToString());
                Assert.AreEqual(movement != MovementClass.Flying, Targeting.CanHit(groundOnly, a), movement.ToString());
            }
        }

        // The same thing in a running game: a wave of flyers past a ground-only tower.
        [Test]
        public void AGroundOnlyDefenceLetsEveryFlyerThroughAndAnAntiAirOneStopsThem()
        {
            System.Func<bool, WaveOutcome> play = antiAir =>
            {
                var roster = new[] { TestTowers.Gun(damage: 10f, range: 20f, interval: 1, canHitFlying: antiAir) };
                var planner = new FixedPlanner(FixedPlanner.Flyers(count: 5, hp: 10f, speed: 2f, intervalTicks: 5));
                Simulation sim = TestSims.AsciiWithWalls(Obstructed, planner, Quick(), towers: roster);
                TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 0));
                TestSims.Send(sim, SimCommand.StartWaveNow());
                RunUntilWaveResolves(sim);
                Assert.AreEqual(antiAir ? 5 : 0, sim.State.Towers[0].Shots);
                return sim.State.LastOutcome;
            };

            WaveOutcome groundOnly = play(false);
            Assert.AreEqual(5, groundOnly.Leaked);
            Assert.AreEqual(0, groundOnly.Killed);
            Assert.AreEqual(0f, groundOnly.TotalDamage());

            WaveOutcome antiAir = play(true);
            Assert.AreEqual(0, antiAir.Leaked);
            Assert.AreEqual(5, antiAir.Killed);
            Assert.AreEqual(50f, antiAir.TotalDamage());
        }

        // ================================================================ a mixed wave

        private const string Field = @"
            . . . . . . . . . .
            S . . . . . . . . C
            . . . . . . . . . .";

        [Test]
        public void TheOutcomeCountsEachClassOnItsOwn()
        {
            // The tower kills everything it may hit; it may not hit flyers.
            var roster = new[] { TestTowers.Gun(damage: 50f, range: 30f, interval: 1, canHitFlying: false) };
            var planner = new FixedPlanner(
                FixedPlanner.Group(count: 4, hp: 10f, speed: 2f, intervalTicks: 3),
                FixedPlanner.Sappers(count: 2, hp: 10f, speed: 2f, intervalTicks: 3),
                FixedPlanner.Flyers(count: 3, hp: 10f, speed: 2f, intervalTicks: 3));
            Simulation sim = TestSims.Ascii(Field, planner, Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            RunUntilWaveResolves(sim);

            WaveOutcome o = sim.State.LastOutcome;
            CollectionAssert.AreEqual(new[] { 4, 2, 3 }, o.SpawnedByClass, "ground, sapper, flying");
            CollectionAssert.AreEqual(new[] { 4, 2, 0 }, o.KilledByClass);
            CollectionAssert.AreEqual(new[] { 0, 0, 3 }, o.LeakedByClass);
            Assert.AreEqual(9, o.Spawned);
            Assert.AreEqual(6, o.Killed);
            Assert.AreEqual(3, o.Leaked);
            StringAssert.Contains("spawned=9 (g/s/f 4/2/3)", o.ToString());
            StringAssert.Contains("leaked=3 (g/s/f 0/0/3)", o.ToString());

            // What the planner was told is its own copy.
            WaveOutcome told = planner.Outcomes[0];
            CollectionAssert.AreEqual(new[] { 0, 0, 3 }, told.LeakedByClass);
            told.LeakedByClass[2] = 99;
            Assert.AreEqual(3, sim.State.LastOutcome.LeakedByClass[2], "a planner cannot edit the simulation's outcome through the array");
        }

        [Test]
        public void AGroundOnlyWaveSaysNothingAboutClasses()
        {
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 2, hp: 10f, speed: 5f)), Quick());
            TestSims.Send(sim, SimCommand.StartWaveNow());
            RunUntilWaveResolves(sim);
            WaveOutcome o = sim.State.LastOutcome;
            CollectionAssert.AreEqual(new[] { 2, 0, 0 }, o.SpawnedByClass);
            Assert.IsFalse(o.ToString().Contains("g/s/f"), "the one-line summary stays as short as it was");
        }

        [Test]
        public void GroupsOfDifferentClassesSpawnInterleavedAndEachAgentKeepsItsClass()
        {
            var planner = new FixedPlanner(
                FixedPlanner.Group(count: 2, hp: 100f, speed: 1f, intervalTicks: 4),
                FixedPlanner.Sappers(count: 2, hp: 100f, speed: 1f, intervalTicks: 4),
                FixedPlanner.Flyers(count: 2, hp: 100f, speed: 1f, intervalTicks: 4));
            Simulation sim = TestSims.Ascii(Field, planner, Quick());
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 10);

            // Ties on the spawn tick go to the lower group: ground, sapper, flyer, and again.
            var classes = new List<MovementClass>();
            foreach (AgentState a in sim.State.Agents) classes.Add(a.Movement);
            CollectionAssert.AreEqual(new[]
            {
                MovementClass.Ground, MovementClass.Sapper, MovementClass.Flying,
                MovementClass.Ground, MovementClass.Sapper, MovementClass.Flying,
            }, classes);
        }

        [Test]
        public void APlanWithAnUnknownMovementClassIsRefused()
        {
            MapData map = AsciiMap.Parse(Field).Map;
            AgentGroup bad = FixedPlanner.Group(1);
            bad.Movement = (MovementClass)7;
            string problem = PlanValidator.Check(new WavePlan { WaveIndex = 1, Groups = new[] { bad } }, 1, map, new SimConfig(), null);
            StringAssert.Contains("Movement 7", problem);
            StringAssert.Contains("not a movement class", problem);

            foreach (MovementClass ok in new[] { MovementClass.Ground, MovementClass.Sapper, MovementClass.Flying })
            {
                AgentGroup g = FixedPlanner.Group(1, movement: ok);
                Assert.IsNull(PlanValidator.Check(new WavePlan { WaveIndex = 1, Groups = new[] { g } }, 1, map, new SimConfig(), null), ok.ToString());
            }

            Assert.Throws<System.ArgumentOutOfRangeException>(() => new AgentState(0, new Vec2f(0f, 0f), 1f, 10f, DamageTypes.AllOnes(), 1f, 0f, 0f, (MovementClass)7));
            Assert.IsTrue(MovementClasses.IsDefined(MovementClass.Flying));
            Assert.IsFalse(MovementClasses.IsDefined((MovementClass)(-1)));
            Assert.IsFalse(MovementClasses.IsDefined((MovementClass)MovementClasses.Count));
        }

        // ================================================================ state plumbing

        private static Simulation MixedRunning(int ticks)
        {
            var roster = new[] { TestTowers.Gun(damage: 1f, range: 4f, interval: 7, canHitFlying: true) };
            var planner = new FixedPlanner(
                FixedPlanner.Group(count: 4, hp: 50f, speed: 1.5f, intervalTicks: 6),
                FixedPlanner.Sappers(count: 4, hp: 50f, speed: 1.5f, digRate: 3f, intervalTicks: 6),
                FixedPlanner.Flyers(count: 4, hp: 50f, speed: 1.5f, intervalTicks: 6));
            Simulation sim = TestSims.AsciiWithWalls(Obstructed, planner, Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 0));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, ticks);
            return sim;
        }

        [Test]
        public void TwoRunsWithEveryClassAgreeOnEveryTickAndACloneCarriesOn()
        {
            Simulation a = MixedRunning(0);
            Simulation b = MixedRunning(0);
            for (int i = 0; i < 400; i++)
            {
                a.Tick();
                b.Tick();
                if (a.ComputeHash() != b.ComputeHash()) Assert.Fail("hashes diverged at tick " + a.State.Tick);
            }
            Assert.Greater(a.State.LiveAgentCount, 0, "rig: still mid-wave");

            Simulation c = a.Clone(new FixedPlanner(FixedPlanner.Group(1)));
            Assert.AreEqual(a.ComputeHash(), c.ComputeHash(), "a clone starts identical");
            for (int i = 0; i < c.State.LiveAgents.Count; i++)
            {
                Assert.AreEqual(a.State.LiveAgents[i].Movement, c.State.LiveAgents[i].Movement, "the class is copied");
                Assert.AreNotSame(a.State.LiveAgents[i], c.State.LiveAgents[i]);
            }

            for (int i = 0; i < 300; i++)
            {
                a.Tick();
                c.Tick();
                if (a.ComputeHash() != c.ComputeHash()) Assert.Fail("the clone diverged at tick " + a.State.Tick);
            }
        }

        [Test]
        public void TheStateHashSeesEveryMovementField()
        {
            Simulation sim = MixedRunning(60);
            SimState s = sim.State;
            SimNode tile = s.Grid[2, 2];

            ulong baseline = sim.ComputeHash();
            void Changes(string what, System.Action change, System.Action undo)
            {
                change();
                Assert.AreNotEqual(baseline, sim.ComputeHash(), what + " is not in the state hash");
                undo();
                Assert.AreEqual(baseline, sim.ComputeHash(), what + ": undo did not restore the state");
            }

            Changes("tile SapperCost", () => tile.SapperCost += 1, () => tile.SapperCost -= 1);
            Changes("tile SapperNextIndex", () => tile.SapperNextIndex += 1, () => tile.SapperNextIndex -= 1);
            Changes("plan group Movement", () => s.CurrentPlan.Groups[0].Movement = MovementClass.Flying, () => s.CurrentPlan.Groups[0].Movement = MovementClass.Ground);
            Changes("outcome SpawnedByClass", () => s.CurrentOutcome.SpawnedByClass[1]++, () => s.CurrentOutcome.SpawnedByClass[1]--);
            Changes("outcome KilledByClass", () => s.CurrentOutcome.KilledByClass[2]++, () => s.CurrentOutcome.KilledByClass[2]--);
            Changes("outcome LeakedByClass", () => s.CurrentOutcome.LeakedByClass[0]++, () => s.CurrentOutcome.LeakedByClass[0]--);

            // An agent's class cannot be changed after it is made, so compare two
            // simulations that differ in nothing else.
            System.Func<MovementClass, ulong> spawned = movement =>
            {
                Simulation one = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 1, hp: 10f, speed: 0.001f, movement: movement)), Quick());
                TestSims.Send(one, SimCommand.StartWaveNow());
                one.Tick();
                // The plan differs too (hashed above), so take the plan out of the comparison.
                one.State.CurrentPlan.Groups[0].Movement = MovementClass.Ground;
                one.State.CurrentPlan.Groups[0].Archetype = "basic";
                one.State.CurrentOutcome.SpawnedByClass[(int)movement]--;
                one.State.CurrentOutcome.SpawnedByClass[0]++;
                return one.ComputeHash();
            };
            Assert.AreNotEqual(spawned(MovementClass.Ground), spawned(MovementClass.Sapper), "an agent's class is in the state hash");
        }

        [Test]
        public void TheSapperShareComesFromTheConfig()
        {
            // Factor 1: sappers route like walkers, so the wall is never touched.
            var config = Quick();
            config.SapperDigCostFactor = 1f;
            Simulation sim = TestSims.AsciiWithWalls(Corridor(5), new FixedPlanner(FixedPlanner.Sappers(count: 2, hp: 10f, speed: 5f, digRate: 4f)), config);
            Assert.AreEqual(160, sim.State.Grid.Get(sim.Map.Spawns[0]).SapperCost);
            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = RunUntilWaveResolves(sim);
            Assert.AreEqual(0, TestTowers.Count(events, SimEventKind.WallBreached));

            // And a tower placed by command rebuilds the sapper field too.
            Simulation live = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(1)), Quick());
            SimNode spawn = live.State.Grid.Get(live.Map.Spawns[0]);
            SimNode tile = live.State.Grid[4, 1];
            Assert.AreEqual(90, spawn.SapperCost);
            Assert.AreEqual(50, tile.SapperCost);

            TestSims.Send(live, SimCommand.PlaceTower(0, 4, 1)); // on the straight route; the archer's tile costs 20, a sapper pays 4
            Assert.AreEqual(40 + 40, tile.SapperCost, "leaving the tower's tile costs a sapper 10 x 4, then four open tiles");
            Assert.AreEqual(200 + 40, tile.BestCost, "and a walker 10 x 20");

            // On open ground one tile is cheap to step round (two diagonals for two
            // straights), so here even the sapper goes round it.
            Assert.AreEqual(98, spawn.BestCost);
            Assert.AreEqual(98, spawn.SapperCost);
        }
    }
}
