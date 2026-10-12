using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The tower shop (WP-C3; ARCHITECTURE.md §4.6): what it offers, when it rolls,
    // and what buying does. The rule under most of this file is CLAUDE.md I10: the
    // offers depend on the seed, the wave and the reroll count, and on nothing else.
    public class ShopTests
    {
        private const string Field = @"
            . . . . . . . . . .
            S . . . . . . . . C
            . . . . . . . . . .";

        private static SimConfig Quick(int coreHp = 1000) { return new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = coreHp }; }

        // Two quick enemies a wave: a wave is over in a few seconds.
        private static IWavePlanner Trickle() { return new FixedPlanner(FixedPlanner.Group(count: 2, hp: 10f, speed: 5f)); }

        private static Simulation Sim(ulong seed = 1, SimConfig config = null, IWavePlanner planner = null, TowerDef[] towers = null)
        {
            return TestSims.Ascii(Field, planner ?? Trickle(), config ?? Quick(), seed, towers);
        }

        private static string[] Ids(TowerOffer[] offers, TowerDef[] roster)
        {
            var ids = new string[offers.Length];
            for (int i = 0; i < offers.Length; i++) ids[i] = roster[offers[i].TowerIndex].Id;
            return ids;
        }

        private static string Rolled(ulong seed, int wave, int reroll, int count = 4)
        {
            TowerDef[] roster = TestTowers.Roster();
            return string.Join(",", Ids(ShopRoller.Roll(seed, wave, reroll, roster, count), roster));
        }

        // ================================================================ the roller on its own

        [Test]
        public void TheSameThreeNumbersAlwaysGiveTheSameOffers()
        {
            for (int wave = 0; wave <= 30; wave++)
                Assert.AreEqual(Rolled(7, wave, 0), Rolled(7, wave, 0), "wave " + wave);

            // Each of the three numbers matters. One roll can repeat another by chance
            // (there are only 81 different sets of four), so compare thirty of them.
            var bySeed7 = new List<string>();
            var bySeed8 = new List<string>();
            var rerolled = new List<string>();
            var nextWave = new List<string>();
            for (int wave = 0; wave < 30; wave++)
            {
                bySeed7.Add(Rolled(7, wave, 0));
                bySeed8.Add(Rolled(8, wave, 0));
                rerolled.Add(Rolled(7, wave, 1));
                nextWave.Add(Rolled(7, wave + 1, 0));
            }
            CollectionAssert.AreNotEqual(bySeed7, bySeed8, "another seed, other offers");
            CollectionAssert.AreNotEqual(bySeed7, rerolled, "a reroll gives other offers");
            CollectionAssert.AreNotEqual(bySeed7, nextWave, "another wave, other offers");
        }

        // The arithmetic behind a roll must be the same on Mono and on .NET, or the
        // two would offer different towers and no replay would survive the trip.
        // These are the first rolls of seed 1; the test runs in both runners.
        [Test]
        public void TheFirstRollsOfSeedOneArePinned()
        {
            Assert.AreEqual(PinnedSeed, ShopRoller.SeedFor(1, 0, 0).ToString("x16"));
            Assert.AreEqual(PinnedRoll0, Rolled(1, 0, 0));
            Assert.AreEqual(PinnedRoll1, Rolled(1, 1, 0));
            Assert.AreEqual(PinnedRoll2, Rolled(1, 2, 0));
            Assert.AreEqual(PinnedReroll, Rolled(1, 0, 1));
        }

        private const string PinnedSeed = "b35b1761f87fbd04";
        private const string PinnedRoll0 = "cannon,archer,frost,archer";
        private const string PinnedRoll1 = "cannon,frost,frost,archer";
        private const string PinnedRoll2 = "cannon,archer,archer,archer";
        private const string PinnedReroll = "archer,archer,cannon,archer";

        // WORKPLAN WP-C3: "across 100k rolls, each tower's offer frequency is within 1%
        // of its shop weight."
        [Test]
        public void OffersComeInProportionToTheShopWeight()
        {
            float[] weights = { 1f, 2f, 0.5f, 0f, 1.5f };
            var roster = new TowerDef[weights.Length];
            for (int i = 0; i < roster.Length; i++) roster[i] = new TowerDef { Id = "t" + i, Cost = 10 + i, ShopWeight = weights[i] };

            var seen = new int[roster.Length];
            int total = 0;
            for (int roll = 0; roll < 100000; roll++)
            {
                foreach (TowerOffer o in ShopRoller.Roll(99, roll, 0, roster, 4))
                {
                    seen[o.TowerIndex]++;
                    total++;
                }
            }

            Assert.AreEqual(400000, total);
            for (int i = 0; i < roster.Length; i++)
            {
                double share = (double)seen[i] / total;
                Assert.AreEqual(weights[i] / 5.0, share, 0.01, roster[i].Id + " (weight " + weights[i] + " of 5)");
            }
            Assert.AreEqual(0, seen[3], "a tower of weight 0 is never offered");
        }

        [Test]
        public void EachSlotIsDrawnOnItsOwnSoATowerCanFillTwo()
        {
            TowerDef[] roster = TestTowers.Roster();
            int withDuplicate = 0, missingAType = 0;
            for (int wave = 0; wave < 200; wave++)
            {
                var count = new int[roster.Length];
                foreach (TowerOffer o in ShopRoller.Roll(3, wave, 0, roster, 4)) count[o.TowerIndex]++;
                bool duplicate = false, missing = false;
                foreach (int c in count)
                {
                    if (c > 1) duplicate = true;
                    if (c == 0) missing = true;
                }
                if (duplicate) withDuplicate++;
                if (missing) missingAType++;
            }
            // Four slots and three towers: every roll repeats one. What varies is
            // whether a type is missing altogether (about 5 rolls in 9).
            Assert.AreEqual(200, withDuplicate);
            Assert.That(missingAType, Is.InRange(80, 140), "a roll does not promise one of each");
        }

        [Test]
        public void AnOfferCarriesTheTowersIndexAndItsCost()
        {
            TowerDef[] roster = TestTowers.Roster();
            foreach (TowerOffer o in ShopRoller.Roll(1, 0, 0, roster, 4))
            {
                Assert.That(o.TowerIndex, Is.InRange(0, roster.Length - 1));
                Assert.AreEqual(roster[o.TowerIndex].Cost, o.Price);
                Assert.IsFalse(o.Bought);
            }
        }

        [Test]
        public void WithNothingToOfferTheShopIsEmpty()
        {
            Assert.AreEqual(0, ShopRoller.Roll(1, 0, 0, new TowerDef[0], 4).Length, "an empty roster");
            Assert.AreEqual(0, ShopRoller.Roll(1, 0, 0, TestTowers.Roster(), 0).Length, "no slots");
            var unwanted = new[] { new TowerDef { Id = "a", ShopWeight = 0f }, new TowerDef { Id = "b", ShopWeight = 0f } };
            Assert.AreEqual(0, ShopRoller.Roll(1, 0, 0, unwanted, 4).Length, "every weight zero");
            Assert.Throws<System.ArgumentNullException>(() => ShopRoller.Roll(1, 0, 0, null, 4));

            // A simulation with no towers at all still runs; PlaceTower finds no slot.
            Simulation walls = Sim(towers: new TowerDef[0]);
            Assert.AreEqual(0, walls.State.Shop.Offers.Length);
            ulong before = walls.ComputeHash();
            TestSims.Send(walls, SimCommand.PlaceTower(0, 4, 2));
            Assert.AreEqual(before, walls.ComputeHash());

            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { ShopOffers = -1 }.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new SimConfig { ShopOffers = 17 }.Validate());
        }

        // ================================================================ when it rolls

        [Test]
        public void TheFirstOffersAreThereBeforeTheFirstTick()
        {
            Simulation sim = Sim(seed: 5);
            ShopState shop = sim.State.Shop;
            Assert.AreEqual(0, shop.WaveIndex);
            Assert.AreEqual(0, shop.RerollCount);
            Assert.AreEqual(4, shop.Offers.Length, "SimConfig.ShopOffers");
            Assert.AreEqual(Rolled(5, 0, 0), string.Join(",", Ids(shop.Offers, sim.TowerLibrary)));
        }

        [Test]
        public void ANewRollArrivesWhenAWaveResolvesAndStaysThroughTheNextWave()
        {
            Simulation sim = Sim(seed: 5);
            TestTowers.BuyAny(sim, 4, 2);
            TestSims.Send(sim, SimCommand.StartWaveNow());

            // During wave 1 the shop still shows the first offers, the bought one included.
            TestSims.Run(sim, 20);
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "rig: mid-wave");
            Assert.AreEqual(0, sim.State.Shop.WaveIndex);
            Assert.IsTrue(sim.State.Shop.Offers[0].Bought);

            List<SimEvent> events = TestSims.RunUntilWaveResolves(sim);
            SimEvent rolled = events.Find(e => e.Kind == SimEventKind.ShopRolled);
            Assert.AreEqual(1, TestTowers.Count(events, SimEventKind.ShopRolled), "one roll, at the wave's end");
            Assert.AreEqual(1, rolled.IntA, "the roll made when wave 1 resolved");
            Assert.AreEqual(0, rolled.IntB);
            Assert.Greater(events.IndexOf(rolled), events.FindIndex(e => e.Kind == SimEventKind.WaveResolved), "after the wave is closed");

            Assert.AreEqual(1, sim.State.Shop.WaveIndex);
            Assert.AreEqual(Rolled(5, 1, 0), string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)));
            foreach (TowerOffer o in sim.State.Shop.Offers) Assert.IsFalse(o.Bought, "a fresh set: nothing in it is bought");

            // Through the build phase and the whole of wave 2 it does not change.
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 20);
            Assert.AreEqual(1, sim.State.Shop.WaveIndex);
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(2, sim.State.Shop.WaveIndex);
            Assert.AreEqual(Rolled(5, 2, 0), string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)));
        }

        [Test]
        public void TheShopDoesNotRollWhenTheGameEnds()
        {
            Simulation sim = Sim(config: Quick(coreHp: 1));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = TestSims.RunUntilWaveResolves(sim);

            Assert.IsTrue(sim.State.IsGameOver);
            Assert.AreEqual(0, TestTowers.Count(events, SimEventKind.ShopRolled));
            Assert.AreEqual(0, sim.State.Shop.WaveIndex, "still the offers the game started with");
        }

        // ================================================================ buying

        // WORKPLAN WP-C3: "buying the same slot twice fails the second time."
        [Test]
        public void ASlotCanBeBoughtOnce()
        {
            Simulation sim = Sim();
            int price = sim.State.Shop.Offers[2].Price;

            TestSims.Send(sim, SimCommand.PlaceTower(2, 4, 2));
            Assert.AreEqual(1, sim.State.Towers.Count);
            Assert.AreEqual(60f - price, sim.State.BuildBudget);

            ulong after = sim.ComputeHash();
            TestSims.Send(sim, SimCommand.PlaceTower(2, 6, 2));
            Assert.AreEqual(after, sim.ComputeHash(), "the second try on the same slot changes nothing");
            Assert.AreEqual(0, sim.LastTickEvents.Count, "and raises nothing");
            Assert.IsFalse(sim.State.Grid[6, 2].HasWall);

            // The other slots are untouched and still for sale.
            TestSims.Send(sim, SimCommand.PlaceTower(0, 6, 2));
            Assert.AreEqual(2, sim.State.Towers.Count);
        }

        // WORKPLAN WP-C3: "buying without enough budget is a no-op and emits nothing."
        [Test]
        public void BuyingWithoutTheBudgetChangesNothingAndRaisesNothing()
        {
            var config = Quick();
            config.StartBudget = 9f; // the cheapest tower costs 10
            Simulation sim = Sim(config: config);
            ulong before = sim.ComputeHash();

            for (int slot = 0; slot < 4; slot++)
            {
                TestSims.Send(sim, SimCommand.PlaceTower(slot, 4, 2));
                Assert.AreEqual(0, sim.LastTickEvents.Count, "slot " + slot);
            }
            Assert.AreEqual(before, sim.ComputeHash());
            Assert.AreEqual(0, sim.State.PlacementLog.Count);
            foreach (TowerOffer o in sim.State.Shop.Offers) Assert.IsFalse(o.Bought, "a refused purchase leaves the slot for later");
        }

        [Test]
        public void TheShopStaysOpenDuringAWave()
        {
            Simulation sim = Sim();
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 10);
            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "rig: mid-wave");

            TestTowers.BuyAny(sim, 4, 2);
            Assert.AreEqual(1, sim.State.Towers.Count);
        }

        // ================================================================ I10: nothing else decides the offers

        private static List<string> OffersOverWaves(Simulation sim, int waves, System.Action<Simulation> eachBuildPhase)
        {
            var seen = new List<string> { string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)) };
            for (int w = 0; w < waves; w++)
            {
                eachBuildPhase?.Invoke(sim);
                TestSims.Send(sim, SimCommand.StartWaveNow());
                TestSims.RunUntilWaveResolves(sim);
                Assert.IsFalse(sim.State.IsGameOver, "rig: the core must outlast the test");
                seen.Add(string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)));
            }
            return seen;
        }

        // WORKPLAN WP-C3, I10: "with the same seed, offers for waves 1-30 are identical
        // across ... (c) buying everything versus buying nothing."
        [Test]
        public void WhatThePlayerBuysAndBuildsDoesNotChangeTheOffers()
        {
            const int waves = 30;
            var expected = new List<string>();
            for (int w = 0; w <= waves; w++) expected.Add(Rolled(11, w, 0));

            List<string> idle = OffersOverWaves(Sim(seed: 11), waves, null);
            CollectionAssert.AreEqual(expected, idle, "a player who buys nothing");

            // A player who cannot stop: every build phase it sells whatever it built in
            // the last one, buys all four offers again and puts down a wall piece.
            var rich = new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = 1000, StartBudget = 5000f };
            int bought = 0;
            List<string> spender = OffersOverWaves(Sim(seed: 11, config: rich), waves, sim =>
            {
                for (int x = 1; x <= 8; x++)
                {
                    for (int y = 0; y <= 2; y++)
                    {
                        TestSims.Send(sim, SimCommand.SellTower(x, y));
                        TestSims.Send(sim, SimCommand.SellWall(x, y));
                    }
                }
                int before = sim.State.Towers.Count;
                for (int slot = 0; slot < 4; slot++) TestSims.Send(sim, SimCommand.PlaceTower(slot, 1 + slot, 0));
                TestSims.Send(sim, SimCommand.PlaceShape(5, 0));
                bought += sim.State.Towers.Count - before;
            });
            Assert.AreEqual(4 * waves, bought, "rig: it really did buy every offer of every roll");
            CollectionAssert.AreEqual(expected, spender, "a player who buys everything");
        }

        // "... (b) EscalationPlanner and a random planner."
        [Test]
        public void ThePlannerDoesNotChangeTheOffers()
        {
            const int waves = 12;
            var expected = new List<string>();
            for (int w = 0; w <= waves; w++) expected.Add(Rolled(11, w, 0));

            // Small waves, so twelve of them fit in a test.
            var config = new SimConfig { PrepSeconds = 60f, IntermissionSeconds = 60f, CoreMaxHp = 100000, AgentsPerWave = 3, AgentsPerWaveIncrement = 0, BaseMoveSpeed = 5f, MaxMoveSpeed = 5f };
            MapData map = AsciiMap.Parse(Field).Map;

            CollectionAssert.AreEqual(expected, OffersOverWaves(Sim(11, config, new EscalationPlanner(config, map)), waves, null), "escalation");
            CollectionAssert.AreEqual(expected, OffersOverWaves(Sim(11, config, new ClassCyclePlanner(config, map)), waves, null), "the class cycle");

            // A planner that draws its waves at random, from a stream of its own.
            var rng = new Pcg32(4242, 0);
            var random = new FixedPlanner(wave => new[]
            {
                FixedPlanner.Group(count: 1 + rng.NextInt(4), hp: 5f + rng.NextInt(20), speed: 4f + rng.NextInt(3),
                                   movement: (MovementClass)rng.NextInt(MovementClasses.Count)),
            });
            CollectionAssert.AreEqual(expected, OffersOverWaves(Sim(11, config, random), waves, null), "random waves");
        }

        [Test]
        public void ADifferentSeedGivesDifferentOffers()
        {
            List<string> a = OffersOverWaves(Sim(seed: 11), 8, null);
            List<string> b = OffersOverWaves(Sim(seed: 12), 8, null);
            CollectionAssert.AreNotEqual(a, b);
        }

        // CLAUDE.md I11: the director shapes waves and never touches the shop. A
        // planner that tries is caught by the same guard as any other write.
        [Test]
        public void APlannerThatTouchesTheShopIsCaught()
        {
            var meddler = new ShopMeddler();
            Simulation sim = Sim(planner: meddler);
            sim.Enqueue(SimCommand.StartWaveNow());

            var e = Assert.Throws<System.InvalidOperationException>(() => sim.Tick());
            StringAssert.Contains("changed the simulation state", e.Message);
        }

        private sealed class ShopMeddler : IWavePlanner
        {
            public string Name => "meddler";

            public WavePlan PlanWave(WaveContext context)
            {
                context.State.Shop.Offers[0].Price = 1; // a "discount" for the player
                return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "meddler", Groups = new[] { FixedPlanner.Group(1) } };
            }

            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        // ================================================================ rerolls

        // WORKPLAN WP-C3: "with rerolls off, RerollShop is a no-op."
        [Test]
        public void WithRerollsOffTheCommandDoesNothing()
        {
            Simulation sim = Sim();
            Assert.Less(sim.Config.RerollCost, 0f, "rig: off by default");
            ulong before = sim.ComputeHash();

            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(before, sim.ComputeHash());
            Assert.AreEqual(0, sim.LastTickEvents.Count);
        }

        // "With rerolls on, RerollCount increases and the offers match the
        // counter-based expectation."
        [Test]
        public void WithRerollsOnTheCommandBuysTheNextRollOfTheSameWave()
        {
            var config = Quick();
            config.RerollCost = 5f;
            Simulation sim = Sim(seed: 5, config: config);
            TestTowers.BuySlot(sim, 0, 4, 2);
            float budget = sim.State.BuildBudget;

            TestSims.Send(sim, SimCommand.RerollShop());

            ShopState shop = sim.State.Shop;
            Assert.AreEqual(0, shop.WaveIndex);
            Assert.AreEqual(1, shop.RerollCount);
            Assert.AreEqual(Rolled(5, 0, 1), string.Join(",", Ids(shop.Offers, sim.TowerLibrary)));
            foreach (TowerOffer o in shop.Offers) Assert.IsFalse(o.Bought, "four new slots, the bought one included");
            Assert.AreEqual(budget - 5f, sim.State.BuildBudget);

            SimEvent rolled = default(SimEvent);
            foreach (SimEvent e in sim.LastTickEvents) if (e.Kind == SimEventKind.ShopRolled) rolled = e;
            Assert.AreEqual(SimEventKind.ShopRolled, rolled.Kind);
            Assert.AreEqual(0, rolled.IntA);
            Assert.AreEqual(1, rolled.IntB);

            PlacementRecord row = sim.State.PlacementLog[sim.State.PlacementLog.Count - 1];
            Assert.AreEqual(PlacementKind.ShopReroll, row.Kind);
            Assert.AreEqual(5f, row.Cost);
            Assert.AreEqual(0, row.Tiles.Length);
            Assert.IsFalse(row.IsPlacement);
            Assert.IsFalse(row.IsSale);

            // Again: the second reroll of the same wave.
            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(2, sim.State.Shop.RerollCount);
            Assert.AreEqual(Rolled(5, 0, 2), string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)));

            // The next wave starts its own count.
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(1, sim.State.Shop.WaveIndex);
            Assert.AreEqual(0, sim.State.Shop.RerollCount);
            Assert.AreEqual(Rolled(5, 1, 0), string.Join(",", Ids(sim.State.Shop.Offers, sim.TowerLibrary)));
        }

        [Test]
        public void ARerollThatCannotBePaidForDoesNothing()
        {
            var config = Quick();
            config.RerollCost = 5f;
            config.StartBudget = 4f;
            Simulation sim = Sim(config: config);
            ulong before = sim.ComputeHash();

            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(before, sim.ComputeHash());

            // A free reroll (cost 0) is "on": it works with an empty purse.
            var free = Quick();
            free.RerollCost = 0f;
            free.StartBudget = 0f;
            Simulation gratis = Sim(config: free);
            TestSims.Send(gratis, SimCommand.RerollShop());
            Assert.AreEqual(1, gratis.State.Shop.RerollCount);
        }

        // ================================================================ state plumbing

        [Test]
        public void TheStateHashSeesTheShopAndACloneHasItsOwn()
        {
            Simulation sim = Sim();
            ShopState shop = sim.State.Shop;
            ulong baseline = sim.ComputeHash();
            void Changes(string what, System.Action change, System.Action undo)
            {
                change();
                Assert.AreNotEqual(baseline, sim.ComputeHash(), what + " is not in the state hash");
                undo();
                Assert.AreEqual(baseline, sim.ComputeHash(), what + ": undo did not restore the state");
            }

            Changes("offer Bought", () => shop.Offers[1].Bought = true, () => shop.Offers[1].Bought = false);
            Changes("offer Price", () => shop.Offers[1].Price++, () => shop.Offers[1].Price--);
            int index = shop.Offers[1].TowerIndex;
            Changes("offer TowerIndex", () => shop.Offers[1].TowerIndex = (index + 1) % 3, () => shop.Offers[1].TowerIndex = index);
            Changes("shop WaveIndex", () => shop.WaveIndex++, () => shop.WaveIndex--);
            Changes("shop RerollCount", () => shop.RerollCount++, () => shop.RerollCount--);

            Simulation copy = sim.Clone(Trickle());
            Assert.AreEqual(sim.ComputeHash(), copy.ComputeHash());
            Assert.AreNotSame(sim.State.Shop, copy.State.Shop);
            Assert.AreNotSame(sim.State.Shop.Offers[0], copy.State.Shop.Offers[0]);

            TestTowers.BuySlot(copy, 0, 4, 2);
            Assert.IsTrue(copy.State.Shop.Offers[0].Bought);
            Assert.IsFalse(sim.State.Shop.Offers[0].Bought, "buying in the clone does not use up the original's slot");
            Assert.AreEqual(baseline, sim.ComputeHash());
        }

        [Test]
        public void TheDumpShowsTheOffers()
        {
            Simulation sim = Sim();
            TestTowers.BuySlot(sim, 0, 4, 2);
            var dump = Newtonsoft.Json.Linq.JObject.Parse(StateDump.ToJson(sim.State));
            Assert.AreEqual(4, ((Newtonsoft.Json.Linq.JArray)dump["ShopOffers"]).Count);
            StringAssert.Contains("(bought)", (string)dump["ShopOffers"][0]);
            Assert.AreEqual(0, (int)dump["ShopWave"]);
        }
    }
}
