using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The tower pass on its own (ARCHITECTURE.md §4.6 step 3): range, cadence,
    // damage and resistance, splash, slow, target choice, and the "paid once" rule.
    public class TowerSystemTests
    {
        // One row of 12 tiles, core at the left end. Tiles are 2 world units wide, so
        // the centre of tile x is at world X = -11 + 2x. Route cost falls to the left.
        private const string Row = "C . . . . . . . . . . S";

        private sealed class Rig
        {
            public AsciiFixture F;
            public List<TowerState> Towers = new List<TowerState>();
            public List<AgentState> Agents = new List<AgentState>();
            public List<SimEvent> Events = new List<SimEvent>();
            public DamageMap Damage;
            public float[] ByType = new float[DamageTypes.Count];
            public float Budget;
            public float MinSlow = 0.25f;

            public TowerState Tower(TowerDef def, int x) { TowerState t = TestTowers.StateOn(F, Towers.Count, def, x, 0); Towers.Add(t); return t; }

            // An agent `tiles` tile-widths to the right of a tower.
            public AgentState AgentAt(TowerState tower, float tiles, float hp = 10f, float[] resist = null)
            {
                var a = TestTowers.Agent(Agents.Count, new Vec2f(tower.Position.X + tiles * 2f, tower.Position.Y), hp, resist: resist);
                Agents.Add(a);
                return a;
            }

            public void Step(int times = 1)
            {
                for (int i = 0; i < times; i++) TowerSystem.Step(F.Grid, F.Grid.Get(F.Map.Core), Towers, Agents, Damage, ByType, MinSlow, ref Budget, Events);
            }
        }

        private static Rig NewRig()
        {
            var rig = new Rig { F = TestMaps.Parse(Row) };
            new FlowField().Generate(rig.F.Grid, rig.F.Map.Core);
            rig.Damage = new DamageMap(rig.F.Grid.NodeCount);
            return rig;
        }

        // WORKPLAN WP-C1: "a tower with range 3 hits an agent at 2.9 tiles and not at 3.1."
        [Test]
        public void RangeIsMeasuredInTilesFromTheTowersCentre()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 1f, range: 3f), 2);
            AgentState inside = rig.AgentAt(tower, 2.9f);
            AgentState outside = rig.AgentAt(tower, 3.1f);

            rig.Step();
            Assert.AreEqual(9f, inside.Hp);
            Assert.AreEqual(10f, outside.Hp);

            // Exactly on the edge counts as in range.
            Rig edge = NewRig();
            TowerState t2 = edge.Tower(TestTowers.Gun(damage: 1f, range: 3f), 2);
            AgentState onEdge = edge.AgentAt(t2, 3f);
            edge.Step();
            Assert.AreEqual(9f, onEdge.Hp);
        }

        // WORKPLAN WP-C1: "with a target always present, a tower with FireIntervalTicks 25
        // fires exactly 40 times in 1,000 ticks."
        [Test]
        public void ATowerFiresOnceEveryIntervalStartingAtOnce()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 1f, range: 3f, interval: 25), 2);
            rig.AgentAt(tower, 1f, hp: 1e9f);

            rig.Step(1);
            Assert.AreEqual(1, tower.Shots, "ready on the first tick");
            rig.Step(24);
            Assert.AreEqual(1, tower.Shots, "24 ticks of cooldown");
            rig.Step(1);
            Assert.AreEqual(2, tower.Shots, "the 26th tick is 25 ticks after the first shot");

            rig.Step(974);
            Assert.AreEqual(40, tower.Shots, "1,000 ticks");
            Assert.AreEqual(40, TestTowers.Count(rig.Events, SimEventKind.TowerFired));
            Assert.AreEqual(40f, tower.DamageDealt);
        }

        [Test]
        public void ATowerWithNothingInRangeKeepsItsShotForTheFirstTarget()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(range: 1f, interval: 25), 2);
            rig.Step(100);
            Assert.AreEqual(0, tower.Shots);
            Assert.AreEqual(0, tower.Cooldown);

            AgentState late = rig.AgentAt(tower, 0.5f);
            rig.Step(1);
            Assert.AreEqual(1, tower.Shots, "fires the tick a target appears");
            Assert.AreEqual(9f, late.Hp);
        }

        // WORKPLAN WP-C1: "Damage equals Damage x Resist[type], and Resist 0 means immune."
        [Test]
        public void DamageIsScaledByTheResistanceToItsType()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 4f, range: 5f, type: DamageType.Fire), 2);
            AgentState resistant = rig.AgentAt(tower, 1f, hp: 100f, resist: new[] { 1f, 0.5f, 1f }); // half fire damage

            rig.Step();
            Assert.AreEqual(98f, resistant.Hp);
            Assert.AreEqual(2f, rig.ByType[(int)DamageType.Fire]);
            Assert.AreEqual(0f, rig.ByType[(int)DamageType.Physical]);
            Assert.AreEqual(2f, tower.DamageDealt);

            Rig weak = NewRig();
            TowerState t2 = weak.Tower(TestTowers.Gun(damage: 4f, range: 5f, type: DamageType.Fire), 2);
            AgentState vulnerable = weak.AgentAt(t2, 1f, hp: 100f, resist: new[] { 1f, 2f, 1f });
            weak.Step();
            Assert.AreEqual(92f, vulnerable.Hp);
        }

        [Test]
        public void AnImmuneEnemyTakesNoDamageAndNoSlow()
        {
            Rig rig = NewRig();
            var def = new TowerDef { Id = "frostgun", Cost = 0, DamageType = DamageType.Frost, Damage = 5f, RangeTiles = 5f, SlowTicks = 50, SlowFactor = 0.5f };
            TowerState tower = rig.Tower(def, 2);
            AgentState immune = rig.AgentAt(tower, 1f, hp: 10f, resist: new[] { 1f, 1f, 0f });

            rig.Step();
            Assert.AreEqual(10f, immune.Hp);
            Assert.AreEqual(0, immune.SlowTicks);
            Assert.AreEqual(1f, immune.SlowFactor);
            Assert.AreEqual(0f, tower.DamageDealt);
            Assert.AreEqual(0f, rig.Damage.Total());
            Assert.AreEqual(1, tower.Shots, "the shot is still spent");
        }

        // Damage is what was actually removed: a 6-damage shot at 2 HP deals 2.
        [Test]
        public void OverkillIsNotCountedAsDamage()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 6f, range: 5f), 2);
            AgentState weak = rig.AgentAt(tower, 1f, hp: 2f);

            rig.Step();
            Assert.IsFalse(weak.IsAlive);
            Assert.AreEqual(0f, weak.Hp);
            Assert.AreEqual(2f, tower.DamageDealt);
            Assert.AreEqual(2f, rig.ByType[(int)DamageType.Physical]);
            Assert.AreEqual(2f, rig.Damage.Total());
        }

        [Test]
        public void DamageIsBookedOnTheTileUnderTheEnemyNotTheTower()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 3f, range: 5f), 2);
            rig.AgentAt(tower, 2f, hp: 100f); // two tiles to the right: tile 4

            rig.Step();
            Assert.AreEqual(3f, rig.Damage[rig.F.Grid[4, 0]]);
            Assert.AreEqual(0f, rig.Damage[rig.F.Grid[2, 0]], "not the tower's own tile");
        }

        [Test]
        public void AKillPaysTheRewardOnceAndRaisesOneEvent()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 5f, range: 5f), 2);
            AgentState target = rig.AgentAt(tower, 1f, hp: 5f);

            rig.Step();
            Assert.IsFalse(target.IsAlive);
            Assert.IsTrue(target.Killed);
            Assert.IsFalse(target.Leaked);
            Assert.AreEqual(0.2f, rig.Budget);
            Assert.AreEqual(1, tower.Kills);
            Assert.AreEqual(1, TestTowers.Count(rig.Events, SimEventKind.AgentKilled));
            SimEvent killed = rig.Events.Find(e => e.Kind == SimEventKind.AgentKilled);
            Assert.AreEqual(target.Id, killed.IntA);
            Assert.AreEqual(0.2f, killed.FloatA);
        }

        // WORKPLAN WP-C1: "the kill reward is credited once even when a direct hit and
        // splash kill the same agent in the same tick." Two towers, one victim: the
        // first kills it, and it is dead to the second.
        [Test]
        public void AnEnemyKilledByOneTowerIsDeadToEveryLaterTowerInTheSameTick()
        {
            Rig rig = NewRig();
            TowerState first = rig.Tower(TestTowers.Gun(damage: 5f, range: 6f), 2);
            TowerState second = rig.Tower(TestTowers.Splash(damage: 5f, range: 6f, splashTiles: 3f), 3);
            AgentState victim = rig.AgentAt(first, 2f, hp: 5f);

            rig.Step();
            Assert.IsFalse(victim.IsAlive);
            Assert.AreEqual(0.2f, rig.Budget, "paid once");
            Assert.AreEqual(1, TestTowers.Count(rig.Events, SimEventKind.AgentKilled));
            Assert.AreEqual(1, first.Kills);
            Assert.AreEqual(0, second.Kills);
            Assert.AreEqual(0, second.Shots, "nothing left to shoot at: it keeps its shot");
            Assert.AreEqual(5f, rig.Damage.Total(), "and no damage is booked on a dead enemy");
        }

        // WORKPLAN WP-C1: "splash hits every agent within the radius, in id order."
        [Test]
        public void SplashHitsEveryOtherEnemyWithinTheRadiusInIdOrder()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Splash(damage: 4f, range: 6f, splashTiles: 1f), 1);

            // Ids 0..3 by position. Id 1 is nearest the core, so it is the target.
            AgentState a0 = rig.AgentAt(tower, 2.5f, hp: 4f);  // 0.5 tile from the target: in the splash
            AgentState a1 = rig.AgentAt(tower, 2.0f, hp: 4f);  // the target
            AgentState a2 = rig.AgentAt(tower, 3.0f, hp: 4f);  // exactly 1 tile away: in
            AgentState a3 = rig.AgentAt(tower, 3.2f, hp: 4f);  // 1.2 tiles away: out

            rig.Step();

            Assert.IsFalse(a0.IsAlive);
            Assert.IsFalse(a1.IsAlive);
            Assert.IsFalse(a2.IsAlive);
            Assert.IsTrue(a3.IsAlive);
            Assert.AreEqual(4f, a3.Hp);

            var killed = new List<int>();
            foreach (SimEvent e in rig.Events) if (e.Kind == SimEventKind.AgentKilled) killed.Add(e.IntA);
            CollectionAssert.AreEqual(new[] { 1, 0, 2 }, killed, "the target first, then the others by id");

            SimEvent fired = rig.Events.Find(e => e.Kind == SimEventKind.TowerFired);
            Assert.AreEqual(1, fired.IntB, "the event names the target");
            Assert.AreEqual(1, tower.Shots);
            Assert.AreEqual(3, tower.Kills);
            Assert.AreEqual(12f, tower.DamageDealt);
        }

        // WORKPLAN WP-C1: "slow keeps the maximum ticks and the minimum factor."
        [Test]
        public void TwoSlowsKeepTheLongerTimeAndTheStrongerFactor()
        {
            Rig rig = NewRig();
            TowerState shortStrong = rig.Tower(TestTowers.Slower(slowTicks: 50, slowFactor: 0.5f, range: 6f), 1);
            TowerState longWeak = rig.Tower(TestTowers.Slower(slowTicks: 100, slowFactor: 0.8f, range: 6f), 2);
            AgentState agent = rig.AgentAt(shortStrong, 2f, hp: 100f);

            rig.Step();
            Assert.AreEqual(100, agent.SlowTicks);
            Assert.AreEqual(0.5f, agent.SlowFactor);
            Assert.AreEqual(100f, agent.Hp, "these towers deal no damage");
            Assert.AreEqual(1, shortStrong.Shots);
            Assert.AreEqual(1, longWeak.Shots);
        }

        // WORKPLAN WP-C1: "a slow never takes an enemy below MinSlowFactor."
        [Test]
        public void ASlowCannotGoBelowTheConfiguredFloor()
        {
            Rig rig = NewRig();
            rig.MinSlow = 0.25f;
            TowerState freezer = rig.Tower(TestTowers.Slower(slowTicks: 50, slowFactor: 0.01f, range: 6f), 1);
            AgentState agent = rig.AgentAt(freezer, 2f, hp: 100f);

            rig.Step();
            Assert.AreEqual(0.25f, agent.SlowFactor);
        }

        // WORKPLAN WP-C1: "Targeting.First picks the agent with the lowest route cost,
        // with ties going to the lowest id."
        [Test]
        public void FirstTargetsTheEnemyNearestTheCoreByRouteCost()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 1f, range: 8f), 4);
            AgentState far = rig.AgentAt(tower, 3f, hp: 10f);    // id 0, further from the core
            AgentState near = rig.AgentAt(tower, -2f, hp: 10f);  // id 1, two tiles nearer the core
            AgentState middle = rig.AgentAt(tower, 1f, hp: 10f); // id 2

            rig.Step();
            Assert.AreEqual(9f, near.Hp, "lowest route cost");
            Assert.AreEqual(10f, far.Hp);
            Assert.AreEqual(10f, middle.Hp);
        }

        [Test]
        public void ATieInRouteCostGoesToTheLowestId()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 1f, range: 8f), 4);
            // Three agents on the SAME tile (same route cost), ids 0, 1, 2.
            AgentState a0 = rig.AgentAt(tower, 2.0f);
            AgentState a1 = rig.AgentAt(tower, 2.1f);
            AgentState a2 = rig.AgentAt(tower, 1.9f);
            Assert.AreSame(rig.F.Grid.NodeFromPosition(a0.Position), rig.F.Grid.NodeFromPosition(a1.Position), "rig: same tile");
            Assert.AreSame(rig.F.Grid.NodeFromPosition(a0.Position), rig.F.Grid.NodeFromPosition(a2.Position), "rig: same tile");

            rig.Step();
            Assert.AreEqual(9f, a0.Hp);
            Assert.AreEqual(10f, a1.Hp);
            Assert.AreEqual(10f, a2.Hp);

            a0.IsAlive = false; // dead agents are never targeted
            rig.Step(25);
            Assert.AreEqual(9f, a1.Hp, "then the next lowest id");
        }

        [Test]
        public void TowersActInIdOrderAndADestroyedTowerNeverFires()
        {
            Rig rig = NewRig();
            TowerState t0 = rig.Tower(TestTowers.Gun(damage: 3f, range: 8f), 2);
            TowerState t1 = rig.Tower(TestTowers.Gun(damage: 3f, range: 8f), 3);
            TowerState t2 = rig.Tower(TestTowers.Gun(damage: 3f, range: 8f), 4);
            t1.IsAlive = false;
            AgentState agent = rig.AgentAt(t0, 3f, hp: 100f);

            rig.Step();
            Assert.AreEqual(94f, agent.Hp, "two shots, not three");
            Assert.AreEqual(0, t1.Shots);

            var shooters = new List<int>();
            foreach (SimEvent e in rig.Events) if (e.Kind == SimEventKind.TowerFired) shooters.Add(e.IntA);
            CollectionAssert.AreEqual(new[] { 0, 2 }, shooters);
        }

        [Test]
        public void ARangeOfZeroNeverHitsAnEnemyThatIsNotOnTheTowersCentre()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Gun(damage: 5f, range: 0f), 2);
            AgentState close = rig.AgentAt(tower, 0.01f);

            rig.Step(50);
            Assert.AreEqual(0, tower.Shots);
            Assert.AreEqual(10f, close.Hp);
        }

        [Test]
        public void ReachIsWorkedOutFromTilesWhenTheTowerIsPlaced()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(TestTowers.Splash(damage: 1f, range: 3.5f, splashTiles: 0.75f), 2);
            // Tiles are 2 world units: 3.5 tiles = 7 units, 0.75 tiles = 1.5 units.
            Assert.AreEqual(49f, tower.RangeSquared);
            Assert.AreEqual(2.25f, tower.SplashSquared);
        }

        [Test]
        public void ABadTowerDefinitionIsRejectedWithItsName()
        {
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = null }.Validate());
            var e = Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "broken", FireIntervalTicks = 0 }.Validate());
            StringAssert.Contains("broken", e.Message);
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "x", SlowFactor = 0f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "x", SlowFactor = 1.5f }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "x", Damage = float.NaN }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "x", DigCost = 1 }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new TowerDef { Id = "x", TowerHealth = 0f }.Validate());
            foreach (TowerDef def in TowerRoster.Placeholder()) Assert.DoesNotThrow(() => def.Validate(), def.Id);
        }
    }
}
