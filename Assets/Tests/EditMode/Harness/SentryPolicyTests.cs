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

        // The shop decides what can be bought (WP-C3): the policy takes the offers in
        // slot order and buys every one the budget still covers.
        [Test]
        public void ItBuysEveryOfferTheBudgetCoversInSlotOrder()
        {
            Simulation sim = Sim();
            var offered = new List<string>();
            int total = 0;
            foreach (TowerOffer o in sim.State.Shop.Offers)
            {
                offered.Add(sim.TowerLibrary[o.TowerIndex].Id);
                total += o.Price;
            }
            Assert.AreEqual(4, offered.Count, "rig: four offers");
            Assert.LessOrEqual(total, 60, "rig: the starting budget covers all four (the dearest tower is 18)");

            List<SimCommand> sent = Build(sim, new SentryPolicy());

            var bought = new List<string>();
            foreach (TowerState t in sim.State.Towers) bought.Add(t.Def.Id);
            CollectionAssert.AreEqual(offered, bought, "one of each offer, in slot order");
            Assert.AreEqual(60f - total, sim.State.BuildBudget);
            Assert.AreEqual(4, sent.Count);
            for (int i = 0; i < sent.Count; i++)
            {
                Assert.AreEqual(SimCommandKind.PlaceTower, sent[i].Kind);
                Assert.AreEqual(i, sent[i].A, "the command names the slot");
            }
            foreach (TowerOffer o in sim.State.Shop.Offers) Assert.IsTrue(o.Bought);

            // Nothing is left to buy: a second pass in the same build phase sends nothing.
            Assert.AreEqual(0, Build(sim, new SentryPolicy()).Count);
        }

        [Test]
        public void WithATightBudgetItSkipsWhatItCannotAffordAndBuysWhatItCan()
        {
            Simulation sim = Sim(new SimConfig { StartBudget = 25f });

            // What the rule should do, worked out from the offers themselves.
            var expected = new List<int>();
            float budget = 25f;
            for (int slot = 0; slot < sim.State.Shop.Offers.Length; slot++)
            {
                int price = sim.State.Shop.Offers[slot].Price;
                if (budget < price) continue;
                budget -= price;
                expected.Add(slot);
            }
            Assert.Less(expected.Count, 4, "rig: 25 cannot buy all four");
            Assert.Greater(expected.Count, 0);

            List<SimCommand> sent = Build(sim, new SentryPolicy());
            var slots = new List<int>();
            foreach (SimCommand c in sent) slots.Add(c.A);
            CollectionAssert.AreEqual(expected, slots);
            Assert.AreEqual(budget, sim.State.BuildBudget);
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
