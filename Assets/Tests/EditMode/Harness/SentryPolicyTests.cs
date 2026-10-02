using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class SentryPolicyTests
    {
        private const string Field = @"
            . . . . . . . . . . . .
            . . . . . . . . . . . .
            S . . . . . . . . . . C
            . . . . . . . . . . . .
            . . . . . . . . . . . .";

        private static Simulation Sim(SimConfig config = null, TowerDef[] towers = null)
        {
            config = config ?? new SimConfig();
            AsciiFixture f = AsciiMap.Parse(Field);
            return new Simulation(config, f.Map, TestShapes.SampleSceneLibrary(), towers ?? TestTowers.Roster(), 1, new EscalationPlanner(config, f.Map));
        }

        private static List<SimCommand> Build(Simulation sim, IPlayerPolicy policy)
        {
            var sent = new List<SimCommand>();
            Action<SimCommand> send = c =>
            {
                sent.Add(c);
                TestSims.Send(sim, c);
            };
            policy.OnIntermission(sim, send, new Pcg32(1, RngStreams.Policy));
            return sent;
        }

        [Test]
        public void ItBuysTowersUntilTheBudgetRunsOutCyclingThroughTheRoster()
        {
            Simulation sim = Sim();
            List<SimCommand> sent = Build(sim, new SentryPolicy());

            // Budget 60: archer 10, cannon 18, frost 12, archer 10, then the cannon is
            // too dear, so the cheapest that still fits: archer 10. Nothing is left.
            var types = new List<string>();
            foreach (TowerState t in sim.State.Towers) types.Add(t.Def.Id);
            CollectionAssert.AreEqual(new[] { "archer", "cannon", "frost", "archer", "archer" }, types);
            Assert.AreEqual(0f, sim.State.BuildBudget);
            Assert.AreEqual(5, sent.Count);
            foreach (SimCommand c in sent) Assert.AreEqual(SimCommandKind.PlaceTower, c.Kind);
        }

        [Test]
        public void TowersStandBesideTheRouteNeverOnIt()
        {
            Simulation sim = Sim();
            var routeBefore = new List<SimNode>();
            Route.Collect(sim.State.Grid, sim.State.Grid.Get(sim.Map.Spawns[0]), routeBefore);
            int costBefore = sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost;

            Build(sim, new SentryPolicy());

            foreach (TowerState t in sim.State.Towers)
            {
                foreach (SimNode n in routeBefore)
                    Assert.IsFalse(n.X == t.Tile.X && n.Y == t.Tile.Y, "tower " + t.Id + " at " + t.Tile + " stands on the route");

                int nearest = int.MaxValue;
                foreach (SimNode n in routeBefore)
                    nearest = Math.Min(nearest, Math.Max(Math.Abs(n.X - t.Tile.X), Math.Abs(n.Y - t.Tile.Y)));
                Assert.LessOrEqual(nearest, 2, "tower " + t.Id + " is within two tiles of the route");
            }
            Assert.AreEqual(costBefore, sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost, "so the route is no longer than before");
        }

        [Test]
        public void EachTowerCoversAsMuchOfTheRouteAsAnyFreeTileWould()
        {
            // One tower type with a short reach, so coverage differs clearly by tile.
            var roster = new[] { TestTowers.Gun(range: 1.5f, cost: 10) };
            Simulation sim = Sim(towers: roster);
            Build(sim, new SentryPolicy { MaxTowersPerPhase = 1 });

            TowerState tower = sim.State.Towers[0];
            // On a straight row, a tile right next to the row covers three route tiles
            // (range 1.5 reaches the diagonal neighbours); a tile two rows away covers none.
            Assert.AreEqual(1, Math.Abs(tower.Tile.Y - 2), "directly above or below the row");
        }

        [Test]
        public void ItRespectsTheCapAndStopsWhenNothingIsAffordable()
        {
            Simulation capped = Sim();
            Build(capped, new SentryPolicy { MaxTowersPerPhase = 2 });
            Assert.AreEqual(2, capped.State.Towers.Count);

            Simulation poor = Sim(new SimConfig { StartBudget = 9f });
            List<SimCommand> sent = Build(poor, new SentryPolicy());
            Assert.AreEqual(0, sent.Count, "the cheapest tower costs 10");
        }

        [Test]
        public void TheMixedPlayerBuildsWallsFirstThenTowers()
        {
            Simulation sim = Sim();
            SequencePolicy policy = SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 3);
            Assert.AreEqual(SequencePolicy.MixedId, policy.Name);
            Assert.AreEqual(SentryPolicy.Id, new SentryPolicy().Name);

            Build(sim, policy);

            Assert.AreEqual(3, sim.State.Towers.Count);
            int walls = 0;
            bool towerSeen = false;
            foreach (PlacementRecord r in sim.State.PlacementLog)
            {
                if (r.Kind == PlacementKind.Tower)
                {
                    towerSeen = true;
                }
                else
                {
                    walls++;
                    Assert.IsFalse(towerSeen, "every wall comes before the first tower: towers are placed against the route the walls leave");
                }
            }
            Assert.Greater(walls, 0);
        }

        [Test]
        public void ASequenceRunsItsStepsInOrderAndNeedsAtLeastOne()
        {
            var order = new List<string>();
            var policy = new SequencePolicy("abc", new Marker("a", order), new Marker("b", order), new Marker("c", order));
            Assert.AreEqual("abc", policy.Name);

            Build(Sim(), policy);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, order);

            Assert.Throws<ArgumentException>(() => new SequencePolicy("empty"));
            Assert.Throws<ArgumentException>(() => new SequencePolicy(null, new IdlePolicy()));
            Assert.Throws<ArgumentException>(() => new SequencePolicy("null step", new IdlePolicy(), null));
        }

        private sealed class Marker : IPlayerPolicy
        {
            private readonly List<string> order;

            public Marker(string name, List<string> order)
            {
                Name = name;
                this.order = order;
            }

            public string Name { get; }

            public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng) { order.Add(Name); }
        }

        [Test]
        public void TheSameBoardGivesTheSameChoices()
        {
            Simulation a = Sim();
            Simulation b = Sim();
            Build(a, new SentryPolicy());
            Build(b, new SentryPolicy());
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void TheRegistryKnowsBothPlayers()
        {
            Assert.AreEqual(SentryPolicy.Id, Registry.CreatePolicy("sentry").Name);
            Assert.AreEqual(SequencePolicy.MixedId, Registry.CreatePolicy("mixed").Name);
            CollectionAssert.Contains(Registry.PolicyNames, "sentry");
            CollectionAssert.Contains(Registry.PolicyNames, "mixed");
        }
    }
}
