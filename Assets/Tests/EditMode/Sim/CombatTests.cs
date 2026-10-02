using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // Towers inside a running Simulation: placing them by command, what a wave's
    // outcome records, and what happens when enemies chew through one.
    public class CombatTests
    {
        // A corridor one tile high: the only way from S to C is straight along it.
        private const string Corridor = "S . . . . . . . . C";

        // Room to stand a tower beside the route.
        private const string Field = @"
            . . . . . . . . . .
            S . . . . . . . . C
            . . . . . . . . . .";

        private static SimConfig Quick(int coreHp = 1000) { return new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = coreHp }; }

        private static List<SimEvent> RunUntilWaveResolves(Simulation sim, int maxTicks = 5000)
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

        // ---------------------------------------------------------------- placing

        [Test]
        public void PlaceTowerBuysTheTowerPutsItOnTheTileAndLogsIt()
        {
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(1)), Quick());
            TowerDef archer = sim.TowerLibrary[0];

            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 2));

            SimState s = sim.State;
            Assert.AreEqual(60f - archer.Cost, s.BuildBudget);
            Assert.AreEqual(1, s.Towers.Count);
            TowerState tower = s.Towers[0];
            Assert.AreEqual(0, tower.Id);
            Assert.AreSame(archer, tower.Def);
            Assert.AreEqual(new TileCoord(4, 2), tower.Tile);
            Assert.IsTrue(tower.IsAlive);

            SimNode node = s.Grid[4, 2];
            Assert.IsTrue(node.HasWall, "a tower is a built tile like a wall");
            Assert.AreEqual(Occupant.Tower, node.Occupant);
            Assert.AreEqual(0, node.TowerId);
            Assert.AreEqual(archer.DigCost, node.TerrainCost);
            Assert.AreEqual(archer.TowerHealth, node.WallHealth);

            Assert.AreEqual(1, s.PlacementLog.Count);
            PlacementRecord record = s.PlacementLog[0];
            Assert.AreEqual(PlacementKind.Tower, record.Kind);
            Assert.AreEqual("archer", record.ShapeName);
            Assert.AreEqual(0, record.TowerId);
            CollectionAssert.AreEqual(new[] { new TileCoord(4, 2) }, record.Tiles);

            SimEvent placed = sim.LastTickEvents.Single(e => e.Kind == SimEventKind.TowerPlaced);
            Assert.AreEqual(0, placed.IntA);
            Assert.AreEqual(4, placed.IntB);
            Assert.AreEqual(2, placed.IntC);

            Assert.AreEqual(1, s.Bag.Version, "placing a tower does not consume the wall piece in hand");
        }

        [Test]
        public void PlaceTowerIsIgnoredWhenTheTileTheBudgetOrTheTypeDoesNotAllowIt()
        {
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(1)), Quick());
            ulong before = sim.ComputeHash();

            TestSims.Send(sim, SimCommand.PlaceTower(0, 0, 1));    // the spawn
            TestSims.Send(sim, SimCommand.PlaceTower(0, 9, 1));    // the core
            TestSims.Send(sim, SimCommand.PlaceTower(0, 40, 1));   // off the map
            TestSims.Send(sim, SimCommand.PlaceTower(-1, 4, 2));   // no such tower type
            TestSims.Send(sim, SimCommand.PlaceTower(99, 4, 2));
            Assert.AreEqual(before, sim.ComputeHash(), "none of those changed anything");
            Assert.AreEqual(0, sim.State.Towers.Count);

            // A tile that already holds a wall.
            sim.State.Grid.SetWall(sim.State.Grid[6, 0], 15, 6f);
            sim.RebuildFieldForSetup();
            ulong withWall = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.PlaceTower(0, 6, 0));
            Assert.AreEqual(withWall, sim.ComputeHash(), "a tower may not stand on a wall");

            // A tile that already holds a tower.
            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 2));
            ulong withTower = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 2));
            Assert.AreEqual(withTower, sim.ComputeHash(), "nor on another tower");
            Assert.AreEqual(1, sim.State.Towers.Count);
        }

        [Test]
        public void ATowerThatCostsMoreThanTheBudgetIsNotPlaced()
        {
            var config = Quick();
            config.StartBudget = 9f; // the archer costs 10
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(1)), config);

            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 2));
            Assert.AreEqual(0, sim.State.Towers.Count);
            Assert.AreEqual(9f, sim.State.BuildBudget);
            Assert.IsFalse(sim.State.Grid[4, 2].HasWall);
        }

        [Test]
        public void ATowerOnTheRouteMakesTheFieldTreatItAsExpensiveTerrain()
        {
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(1)), Quick());
            int before = sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost;
            int version = sim.State.Grid.FieldVersion;

            TestSims.Send(sim, SimCommand.PlaceTower(0, 4, 1)); // on the straight route

            Assert.Greater(sim.State.Grid.FieldVersion, version, "the field was rebuilt");
            Assert.Greater(sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost, before, "the route now goes around it");
        }

        // ---------------------------------------------------------------- a wave with towers

        [Test]
        public void KillsAreCountedPaidAndBookedInTheOutcome()
        {
            // One strong tower beside the route kills all four slow enemies.
            var roster = new[] { TestTowers.Gun(damage: 5f, range: 20f, interval: 1, type: DamageType.Fire) };
            var planner = new FixedPlanner(FixedPlanner.Group(count: 4, hp: 10f, speed: 1f, intervalTicks: 5));
            Simulation sim = TestSims.Ascii(Field, planner, Quick(), towers: roster);

            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = RunUntilWaveResolves(sim);

            WaveOutcome o = sim.State.LastOutcome;
            Assert.AreEqual(4, o.Spawned);
            Assert.AreEqual(4, o.Killed);
            Assert.AreEqual(0, o.Leaked);
            Assert.AreEqual(0, o.Removed);
            Assert.IsFalse(o.TimedOut);
            Assert.AreEqual(40f, o.DamageByType[(int)DamageType.Fire], "all of every enemy's hit points, and no overkill");
            Assert.AreEqual(0f, o.DamageByType[(int)DamageType.Physical]);
            Assert.AreEqual(40f, o.TotalDamage());
            Assert.AreEqual(1000, o.CoreHpAfter);
            Assert.AreEqual(60f, o.BudgetBefore, 1e-4f);
            Assert.AreEqual(60.8f, o.BudgetAfter, 1e-4f, "four kills x 0.2");

            Assert.AreEqual(4, TestTowers.Count(events, SimEventKind.AgentKilled));
            Assert.AreEqual(8, TestTowers.Count(events, SimEventKind.TowerFired), "two shots of 5 per 10-HP enemy");

            TowerState tower = sim.State.Towers[0];
            Assert.AreEqual(4, tower.Kills);
            Assert.AreEqual(8, tower.Shots);
            Assert.AreEqual(40f, tower.DamageDealt);
            Assert.AreEqual(40f, sim.State.Damage.Total(), "the damage map holds the same total");

            Assert.AreEqual(1, planner.Outcomes.Count);
            Assert.AreEqual(4, planner.Outcomes[0].Killed);
            Assert.AreEqual(40f, planner.Outcomes[0].DamageByType[(int)DamageType.Fire]);
        }

        [Test]
        public void TheDamageMapIsPerWave()
        {
            var roster = new[] { TestTowers.Gun(damage: 5f, range: 20f, interval: 1) };
            var config = Quick();
            config.IntermissionSeconds = 0.1f;
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 2, hp: 10f, speed: 1f)), config, towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            RunUntilWaveResolves(sim);
            Assert.AreEqual(20f, sim.State.Damage.Total());

            RunUntilWaveResolves(sim);
            Assert.AreEqual(2, sim.State.LastOutcome.WaveIndex);
            Assert.AreEqual(20f, sim.State.Damage.Total(), "reset when wave 2 began, then wave 2's own damage");
            Assert.AreEqual(40f, sim.State.Towers[0].DamageDealt, "the tower's own total runs across waves");
        }

        [Test]
        public void AnEnemyThatOutrunsTheTowersStillLeaks()
        {
            var roster = new[] { TestTowers.Gun(damage: 1f, range: 2f, interval: 50) };
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 3, hp: 100f, speed: 10f)), Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            RunUntilWaveResolves(sim);

            WaveOutcome o = sim.State.LastOutcome;
            Assert.AreEqual(3, o.Leaked);
            Assert.AreEqual(0, o.Killed);
            Assert.AreEqual(997, o.CoreHpAfter);
            Assert.Greater(o.TotalDamage(), 0f, "it was shot at on the way");
            Assert.Less(o.TotalDamage(), 300f);
        }

        // ---------------------------------------------------------------- chewing through a tower

        // WORKPLAN WP-C1 review probe: a tower that can never hit, standing on the only
        // route. Enemies must dig through it; the tower is destroyed exactly once; the
        // flow field is rebuilt.
        [Test]
        public void EnemiesChewThroughATowerOnTheOnlyRouteAndDestroyItOnce()
        {
            var blocker = TestTowers.Gun(damage: 5f, range: 0f);
            blocker.TowerHealth = 1f;
            var planner = new FixedPlanner(FixedPlanner.Group(count: 3, hp: 10f, speed: 4f, intervalTicks: 3));
            Simulation sim = TestSims.Ascii(Corridor, planner, Quick(), towers: new[] { blocker });

            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 0));
            int versionAfterPlacing = sim.State.Grid.FieldVersion;
            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = RunUntilWaveResolves(sim);

            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.TowerDestroyed), "exactly once");
            SimEvent destroyed = events.Find(e => e.Kind == SimEventKind.TowerDestroyed);
            Assert.AreEqual(0, destroyed.IntA);
            Assert.AreEqual(5, destroyed.IntB);
            Assert.AreEqual(0, destroyed.IntC);

            TowerState tower = sim.State.Towers[0];
            Assert.IsFalse(tower.IsAlive);
            Assert.AreEqual(0, tower.Shots, "range 0 never hits");
            Assert.AreEqual(1, sim.State.Towers.Count, "a destroyed tower keeps its id and its place in the list");

            SimNode node = sim.State.Grid[5, 0];
            Assert.IsFalse(node.HasWall);
            Assert.AreEqual(Occupant.None, node.Occupant);
            Assert.AreEqual(1, node.TerrainCost);
            Assert.Greater(sim.State.Grid.FieldVersion, versionAfterPlacing, "the field was rebuilt after the breach");

            WaveOutcome o = sim.State.LastOutcome;
            Assert.AreEqual(1, o.TowersDestroyed);
            Assert.AreEqual(0, o.WallsBreached, "a tower is not a wall");
            Assert.AreEqual(3, o.Leaked);
            Assert.AreEqual(60f, o.BudgetAfter, 1e-4f, "a chewed-through tower pays nothing by default (the tower was free in this test)");
        }

        // WORKPLAN WP-C1: "...and a destroyed tower never fires on a later tick."
        [Test]
        public void ADestroyedTowerNeverFiresAgain()
        {
            // It can hit, but for too little to stop anyone chewing through it.
            var weak = TestTowers.Gun(damage: 0.001f, range: 6f, interval: 1);
            weak.TowerHealth = 0.5f;
            var planner = new FixedPlanner(FixedPlanner.Group(count: 6, hp: 10f, speed: 4f, intervalTicks: 10));
            Simulation sim = TestSims.Ascii(Corridor, planner, Quick(), towers: new[] { weak });
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 0));
            TestSims.Send(sim, SimCommand.StartWaveNow());

            TowerState tower = sim.State.Towers[0];
            int shotsWhenDestroyed = -1;
            int ticksAliveAfter = 0;
            for (int i = 0; i < 3000 && sim.State.LastOutcome == null; i++)
            {
                sim.Tick();
                if (!tower.IsAlive && shotsWhenDestroyed < 0) shotsWhenDestroyed = tower.Shots;
                else if (!tower.IsAlive && sim.State.LiveAgentCount > 0) ticksAliveAfter++;
            }

            Assert.IsFalse(tower.IsAlive);
            Assert.Greater(shotsWhenDestroyed, 0, "it did fire while it stood");
            Assert.Greater(ticksAliveAfter, 20, "enemies kept passing within its range afterwards");
            Assert.AreEqual(shotsWhenDestroyed, tower.Shots, "and it never fired again");
        }

        // ---------------------------------------------------------------- state plumbing

        [Test]
        public void ACloneCarriesItsOwnTowers()
        {
            var roster = new[] { TestTowers.Gun(damage: 1f, range: 20f, interval: 5) };
            Simulation a = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 5, hp: 50f, speed: 1f, intervalTicks: 5)), Quick(), towers: roster);
            TestSims.Send(a, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(a, SimCommand.StartWaveNow());
            TestSims.Run(a, 40);

            Simulation b = a.Clone(new FixedPlanner(FixedPlanner.Group(count: 5, hp: 50f, speed: 1f, intervalTicks: 5)));
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            Assert.AreNotSame(a.State.Towers[0], b.State.Towers[0]);
            Assert.AreSame(a.State.Towers[0].Def, b.State.Towers[0].Def, "definitions are shared: they never change");

            TestSims.Run(b, 30);
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash(), "the clone moved on alone");
            Assert.Greater(b.State.Towers[0].Shots, a.State.Towers[0].Shots);

            TestSims.Run(a, 30);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "and the original catches up to the same state");
        }

        [Test]
        public void TheStateHashSeesEveryCombatField()
        {
            var roster = new[] { TestTowers.Slower(slowTicks: 40, slowFactor: 0.5f, range: 20f, damage: 1f, interval: 5) };
            Simulation sim = TestSims.Ascii(Field, new FixedPlanner(FixedPlanner.Group(count: 3, hp: 50f, speed: 1f, intervalTicks: 5)), Quick(), towers: roster);
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 30);

            SimState s = sim.State;
            AgentState agent = s.LiveAgents[0];
            TowerState tower = s.Towers[0];
            SimNode towerTile = s.Grid[5, 2];
            Assert.Greater(agent.SlowTicks, 0, "rig: an agent is slowed");

            ulong baseline = sim.ComputeHash();
            void Changes(string what, System.Action change, System.Action undo)
            {
                change();
                Assert.AreNotEqual(baseline, sim.ComputeHash(), what + " is not in the state hash");
                undo();
                Assert.AreEqual(baseline, sim.ComputeHash(), what + ": undo did not restore the state");
            }

            Changes("agent Hp", () => agent.Hp -= 1f, () => agent.Hp += 1f);
            Changes("agent SlowTicks", () => agent.SlowTicks++, () => agent.SlowTicks--);
            Changes("agent SlowFactor", () => agent.SlowFactor = 0.75f, () => agent.SlowFactor = 0.5f);
            Changes("tower Cooldown", () => tower.Cooldown++, () => tower.Cooldown--);
            Changes("tower IsAlive", () => tower.IsAlive = false, () => tower.IsAlive = true);
            Changes("tower Shots", () => tower.Shots++, () => tower.Shots--);
            Changes("tower Kills", () => tower.Kills++, () => tower.Kills--);
            Changes("tower DamageDealt", () => tower.DamageDealt += 1f, () => tower.DamageDealt -= 1f);
            Changes("tile Occupant", () => towerTile.Occupant = Occupant.Wall, () => towerTile.Occupant = Occupant.Tower);
            Changes("tile TowerId", () => towerTile.TowerId = 7, () => towerTile.TowerId = 0);
            Changes("outcome DamageByType", () => s.CurrentOutcome.DamageByType[0] += 1f, () => s.CurrentOutcome.DamageByType[0] -= 1f);
            Changes("outcome Killed", () => s.CurrentOutcome.Killed++, () => s.CurrentOutcome.Killed--);
        }
    }
}
