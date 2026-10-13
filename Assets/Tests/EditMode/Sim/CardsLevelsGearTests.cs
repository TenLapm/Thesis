using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The rules of 2026-10-12: a shop slot holds a card (a tower that may be rare or
    // epic, a coin card, a piece of gear), a tower has a level and gear, and the
    // build phase can wait for the player. All of it is OFF in a default SimConfig,
    // which the first test pins: the hundreds of tests written before it, and the
    // offers of every seed, must not have moved.
    public class CardsLevelsGearTests
    {
        // 9 x 5, open. S is (0,2), C is (8,2).
        private const string Open = @"
            . . . . . . . . .
            . . . . . . . . .
            S . . . . . . . C
            . . . . . . . . .
            . . . . . . . . .";

        private static SimConfig Cards()
        {
            return new SimConfig { StartBudget = 500f, CoinCardWeight = 0.5f, GearCardWeight = 0.5f, RareTowerChance = 0.25f, EpicTowerChance = 0.1f, CoreMaxHp = 100000 };
        }

        private static Simulation New(SimConfig config, ulong seed = 1, TowerDef[] towers = null)
        {
            MapData map = TestMaps.Parse(Open).Map;
            return TestSims.Ascii(Open, new EscalationPlanner(config, map), config, seed, towers);
        }

        // The first slot holding a card of this kind (and, for a tower, this level), on
        // the first seed that has one. Rolls are cheap, so a test asks for what it needs.
        private static Simulation WithCard(SimConfig config, CardKind kind, out int slot, int level = 0)
        {
            for (ulong seed = 1; seed < 400; seed++)
            {
                Simulation sim = New(config, seed);
                TowerOffer[] offers = sim.State.Shop.Offers;
                for (int i = 0; i < offers.Length; i++)
                {
                    if (offers[i].Kind == kind && (level == 0 || offers[i].Level == level))
                    {
                        slot = i;
                        return sim;
                    }
                }
            }
            Assert.Fail("no seed below 400 offers a " + kind + " card at game start");
            slot = -1;
            return null;
        }

        private static TowerState Bought(Simulation sim, int x, int y)
        {
            TowerOffer[] offers = sim.State.Shop.Offers;
            for (int i = 0; i < offers.Length; i++)
            {
                if (offers[i].Bought || offers[i].Kind != CardKind.Tower) continue;
                return TestTowers.BuySlot(sim, i, x, y);
            }
            Assert.Fail("no tower card on offer: " + TestTowers.Offered(sim));
            return null;
        }

        // ------------------------------------------------------------------ the shop

        [Test]
        public void WithEverythingOffARollIsThePlainRollOfTowers()
        {
            TowerDef[] roster = TestTowers.Roster();
            GearDef[] gear = GearRoster.Default();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                for (int wave = 0; wave < 6; wave++)
                {
                    TowerOffer[] plain = ShopRoller.Roll(seed, wave, 0, roster, 4);
                    TowerOffer[] withConfig = ShopRoller.Roll(seed, wave, 0, roster, 4, new SimConfig(), gear);
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.AreEqual(CardKind.Tower, withConfig[i].Kind);
                        Assert.AreEqual(1, withConfig[i].Level);
                        Assert.AreEqual(plain[i].TowerIndex, withConfig[i].TowerIndex, "seed " + seed + " wave " + wave + " slot " + i);
                        Assert.AreEqual(plain[i].Price, withConfig[i].Price);
                    }
                }
            }
        }

        [Test]
        public void WithCardsOnARollHoldsEveryKindAndIsStillAFunctionOfSeedWaveAndReroll()
        {
            SimConfig config = Cards();
            TowerDef[] roster = TestTowers.Roster();
            GearDef[] gear = GearRoster.Default();
            var kinds = new int[3];
            var levels = new int[4];
            var pieces = new HashSet<int>();

            for (int wave = 0; wave < 300; wave++)
            {
                TowerOffer[] a = ShopRoller.Roll(7, wave, 0, roster, 4, config, gear);
                TowerOffer[] b = ShopRoller.Roll(7, wave, 0, roster, 4, config, gear);
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.AreEqual(a[i].ToString() + a[i].Kind + a[i].Level + a[i].GearIndex + a[i].Amount, b[i].ToString() + b[i].Kind + b[i].Level + b[i].GearIndex + b[i].Amount, "the same roll twice");
                    kinds[(int)a[i].Kind]++;
                    switch (a[i].Kind)
                    {
                        case CardKind.Coin:
                            Assert.AreEqual(config.CoinCardAmount, a[i].Amount);
                            Assert.AreEqual(0, a[i].Price, "a coin card is free");
                            break;
                        case CardKind.Gear:
                            pieces.Add(a[i].GearIndex);
                            Assert.AreEqual(gear[a[i].GearIndex].Price, a[i].Price);
                            break;
                        default:
                            levels[a[i].Level]++;
                            Assert.AreEqual(TowerUpgrades.CardPrice(roster[a[i].TowerIndex], a[i].Level, config), a[i].Price, "a rare card costs the tower and its upgrade");
                            break;
                    }
                }
            }

            Assert.Greater(kinds[(int)CardKind.Tower], kinds[(int)CardKind.Coin], "towers are the most common card");
            Assert.Greater(kinds[(int)CardKind.Coin], 100);
            Assert.Greater(kinds[(int)CardKind.Gear], 100);
            Assert.AreEqual(gear.Length, pieces.Count, "every piece of gear turns up");
            Assert.Greater(levels[1], levels[2]);
            Assert.Greater(levels[2], levels[3]);
            Assert.Greater(levels[3], 0, "an epic card exists");
        }

        [Test]
        public void ACoinCardPaysOnceAndIsNotATower()
        {
            SimConfig config = Cards();
            Simulation sim = WithCard(config, CardKind.Coin, out int slot);
            float before = sim.State.BuildBudget;

            TestSims.Send(sim, SimCommand.PlaceTower(slot, 3, 1));
            Assert.AreEqual(0, sim.State.Towers.Count, "a coin card cannot be put on the board");
            Assert.AreEqual(before, sim.State.BuildBudget);

            TestSims.Send(sim, SimCommand.TakeCoin(slot));
            TestSims.Send(sim, SimCommand.TakeCoin(slot));
            Assert.AreEqual(before + config.CoinCardAmount, sim.State.BuildBudget, "paid once");
            Assert.IsTrue(sim.State.Shop.Offers[slot].Bought);
            Assert.AreEqual(PlacementKind.CoinTaken, sim.State.PlacementLog[0].Kind);
            Assert.AreEqual(1, sim.State.PlacementLog.Count);
        }

        [Test]
        public void ARareCardPutsARareTowerOnTheBoardForTheTowerAndItsUpgrade()
        {
            SimConfig config = Cards();
            Simulation sim = WithCard(config, CardKind.Tower, out int slot, level: 2);
            TowerOffer card = sim.State.Shop.Offers[slot];
            TowerDef def = sim.TowerLibrary[card.TowerIndex];
            float before = sim.State.BuildBudget;

            TowerState tower = TestTowers.BuySlot(sim, slot, 3, 1);

            Assert.AreEqual(2, tower.Level);
            Assert.AreEqual(2, tower.GearSlots, "a rare tower has two gear slots");
            Assert.AreEqual(def.Cost + TowerUpgrades.CostOf(def, 1, config), card.Price);
            Assert.AreEqual(before - card.Price, sim.State.BuildBudget);
            Assert.AreEqual(def.Damage * 1.5f, tower.Damage, 1e-5f, "a roster tower with no levels of its own gains half its damage a level");
        }

        // ------------------------------------------------------------------ levels

        [Test]
        public void ATowerIsUpgradedTwiceAndNoFurtherAndASaleRefundsHalfOfAllOfIt()
        {
            var config = new SimConfig { StartBudget = 500f };
            Simulation sim = New(config);
            TowerState tower = Bought(sim, 3, 1);
            TowerDef def = tower.Def;
            float afterBuying = sim.State.BuildBudget;

            Assert.AreEqual(1, tower.Level);
            Assert.AreEqual(1, tower.GearSlots);
            Assert.AreEqual(def.Cost, TowerUpgrades.CostOf(def, 1, config), "the first upgrade costs the card's price");
            Assert.AreEqual(def.Cost + def.Cost / 2, TowerUpgrades.CostOf(def, 2, config), "the second half as much again");

            TestSims.Send(sim, SimCommand.UpgradeTower(3, 1));
            Assert.AreEqual(2, tower.Level);
            TestSims.Send(sim, SimCommand.UpgradeTower(3, 1));
            Assert.AreEqual(3, tower.Level);
            float afterTwo = sim.State.BuildBudget;
            TestSims.Send(sim, SimCommand.UpgradeTower(3, 1));
            Assert.AreEqual(3, tower.Level, "epic is the top");
            Assert.AreEqual(afterTwo, sim.State.BuildBudget, "and a refused upgrade costs nothing");

            int paid = def.Cost + TowerUpgrades.CostOf(def, 1, config) + TowerUpgrades.CostOf(def, 2, config);
            Assert.AreEqual(afterBuying - (paid - def.Cost), afterTwo);
            Assert.AreEqual(paid, tower.PricePaid);
            Assert.AreEqual(def.Damage * 1.5f * 1.5f, tower.Damage, 1e-4f);

            TestSims.Send(sim, SimCommand.UpgradeTower(5, 1));
            Assert.AreEqual(afterTwo, sim.State.BuildBudget, "open ground cannot be upgraded");

            TestSims.Send(sim, SimCommand.SellTower(3, 1));
            Assert.AreEqual(afterTwo + paid * config.SellRefund, sim.State.BuildBudget, 1e-4f);

            var kinds = new List<PlacementKind>();
            foreach (PlacementRecord r in sim.State.PlacementLog) kinds.Add(r.Kind);
            CollectionAssert.AreEqual(new[] { PlacementKind.Tower, PlacementKind.TowerUpgraded, PlacementKind.TowerUpgraded, PlacementKind.TowerSold }, kinds);
        }

        [Test]
        public void ALevelChangesWhatATowerDoesByItsOwnModifier()
        {
            var def = new TowerDef
            {
                Id = "ballista", Damage = 20f, RangeTiles = 5f, FireIntervalTicks = 100,
                Levels = new[]
                {
                    new TowerLevelMod { Label = "two bolts", ExtraTargets = 1 },
                    new TowerLevelMod { Label = "three bolts, faster", ExtraTargets = 1, IntervalMul = 0.5f, RangeAdd = 1f },
                },
            };
            var tower = new TowerState(0, def, new TileCoord(0, 0), new Vec2f(0f, 0f), 2f, 0);
            Assert.AreEqual(1, tower.Targets);

            tower.Level = 2;
            tower.Recompute(null);
            Assert.AreEqual(2, tower.Targets);
            Assert.AreEqual(20f, tower.Damage, "this level adds a bolt, not damage");
            Assert.AreEqual(100, tower.FireIntervalTicks);

            tower.Level = 3;
            tower.Recompute(null);
            Assert.AreEqual(3, tower.Targets);
            Assert.AreEqual(50, tower.FireIntervalTicks);
            Assert.AreEqual(6f, tower.RangeTiles);
            Assert.AreEqual(144f, tower.RangeSquared, "6 tiles of 2 units, squared");
        }

        [Test]
        public void TwoBoltsHitTheTwoEnemiesNearestTheCoreAndOneBoltIsKeptWhenThereIsOneEnemy()
        {
            AsciiFixture f = TestFields.Built(Open);
            var def = new TowerDef { Id = "ballista", Damage = 4f, RangeTiles = 20f, FireIntervalTicks = 10, Levels = new[] { new TowerLevelMod { Label = "two bolts", ExtraTargets = 1 } } };
            var tower = new TowerState(0, def, new TileCoord(4, 1), f.Grid[4, 1].Position, f.Map.NodeDiameter, 0, level: 2);
            var towers = new List<TowerState> { tower };
            var agents = new List<AgentState>
            {
                TestTowers.Agent(0, f.Grid[2, 2].Position),
                TestTowers.Agent(1, f.Grid[6, 2].Position),   // nearest the core
                TestTowers.Agent(2, f.Grid[5, 2].Position),   // second nearest
            };
            var events = new List<SimEvent>();
            var damage = new DamageMap(f.Grid.NodeCount);
            float budget = 0f;

            TowerSystem.Step(f.Grid, f.Grid.Get(f.Map.Core), towers, agents, damage, new float[DamageTypes.Count], 0.25f, ref budget, events);

            Assert.AreEqual(10f, agents[0].Hp, "the furthest is left alone");
            Assert.AreEqual(6f, agents[1].Hp);
            Assert.AreEqual(6f, agents[2].Hp);
            Assert.AreEqual(1, tower.Shots, "one shot, two bolts");
            Assert.AreEqual(2, TestTowers.Count(events, SimEventKind.TowerFired), "one event a bolt, so the view can draw both");

            agents.RemoveRange(0, 2);
            tower.Cooldown = 0;
            TowerSystem.Step(f.Grid, f.Grid.Get(f.Map.Core), towers, agents, damage, new float[DamageTypes.Count], 0.25f, ref budget, events);
            Assert.AreEqual(2f, agents[0].Hp, "the same enemy is not hit twice by one shot");
        }

        // ------------------------------------------------------------------ gear

        [Test]
        public void GearIsBoughtOntoATowerIntoAFreeSlotAndChangesIt()
        {
            SimConfig config = Cards();
            Simulation sim = WithCard(config, CardKind.Gear, out int slot);
            GearDef[] gear = sim.GearLibrary;
            TowerOffer card = sim.State.Shop.Offers[slot];

            float before = sim.State.BuildBudget;
            TestSims.Send(sim, SimCommand.BuyGear(slot, 3, 1));
            Assert.AreEqual(before, sim.State.BuildBudget, "there is no tower there yet");
            Assert.IsFalse(card.Bought);

            TowerState tower = Bought(sim, 3, 1);
            if (tower.Level > 1) Assert.Inconclusive("this seed's first tower card is not common");
            int paidForTower = tower.PricePaid;
            before = sim.State.BuildBudget;

            TestSims.Send(sim, SimCommand.BuyGear(slot, 3, 1));
            Assert.IsTrue(card.Bought);
            Assert.AreEqual(card.GearIndex, tower.Gear[0]);
            Assert.AreEqual(before - gear[card.GearIndex].Price, sim.State.BuildBudget);
            Assert.AreEqual(paidForTower + gear[card.GearIndex].Price, tower.PricePaid, "a sale refunds the gear with the tower");
            Assert.AreEqual(-1, tower.FreeGearSlot(), "a common tower has one slot");
            Assert.AreEqual(PlacementKind.GearBought, sim.State.PlacementLog[sim.State.PlacementLog.Count - 1].Kind);
        }

        [Test]
        public void EachPieceOfGearDoesWhatItsLabelSays()
        {
            GearDef[] gear = GearRoster.Default();
            var names = new List<string>();
            foreach (GearDef g in gear) names.Add(g.Id);
            CollectionAssert.AreEqual(new[] { "scope", "whetstone", "sky-sight", "powder", "chill" }, names, "order is part of a replay");

            TowerDef def = TestTowers.Gun(damage: 10f, range: 3f, interval: 20);
            System.Func<int, TowerState> with = piece =>
            {
                var t = new TowerState(0, def, new TileCoord(0, 0), new Vec2f(0f, 0f), 2f, 0);
                t.Gear[0] = piece;
                t.Recompute(gear);
                return t;
            };

            Assert.AreEqual(4f, with(0).RangeTiles, "scope");
            Assert.AreEqual(13.5f, with(1).Damage, 1e-5f, "whetstone");
            Assert.IsTrue(with(2).CanHitFlying, "sky-sight");
            Assert.IsFalse(with(0).CanHitFlying);
            Assert.AreEqual(1f, with(3).SplashSquared, 1e-5f, "powder: half a tile of 2 units, squared");
            Assert.AreEqual(50, with(4).SlowTicks, "chill");
            Assert.AreEqual(0.7f, with(4).SlowFactor);
        }

        [Test]
        public void GearMovesBetweenTowersBetweenWavesAndTakesItsPriceAlong()
        {
            var config = new SimConfig { StartBudget = 500f, PrepSeconds = 600f };
            Simulation sim = New(config);
            TowerState a = Bought(sim, 3, 1);
            TowerState b = Bought(sim, 5, 1);
            GearDef[] gear = sim.GearLibrary;
            int scope = 0;

            // Put a scope on A the way the shop would, without waiting for a gear card.
            float budget = sim.State.BuildBudget;
            Assert.IsTrue(TowerUpgrades.TryBuyGear(sim.State.Grid, a.Tile, scope, gear[scope].Price, gear, ref budget, 0, sim.State.Towers, sim.State.PlacementLog, null));
            int aPaid = a.PricePaid, bPaid = b.PricePaid;
            float aRange = a.RangeTiles;

            TestSims.Send(sim, SimCommand.MoveGear(3, 1, 0, 5, 1));

            Assert.AreEqual(TowerState.NoGear, a.Gear[0]);
            Assert.AreEqual(scope, b.Gear[0]);
            Assert.AreEqual(aRange - 1f, a.RangeTiles, 1e-5f, "A is back to what it was");
            Assert.AreEqual(b.Def.RangeTiles + 1f, b.RangeTiles, 1e-5f);
            Assert.AreEqual(aPaid - gear[scope].Price, a.PricePaid);
            Assert.AreEqual(bPaid + gear[scope].Price, b.PricePaid);
            Assert.AreEqual(PlacementKind.GearMoved, sim.State.PlacementLog[sim.State.PlacementLog.Count - 1].Kind);

            // B (common) is full now; nothing fits, and moving onto itself is nothing.
            int rows = sim.State.PlacementLog.Count;
            TestSims.Send(sim, SimCommand.MoveGear(5, 1, 0, 5, 1));
            TestSims.Send(sim, SimCommand.MoveGear(3, 1, 0, 5, 1));
            Assert.AreEqual(rows, sim.State.PlacementLog.Count);

            // During a wave gear stays where it is.
            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            TestSims.Send(sim, SimCommand.MoveGear(5, 1, 0, 3, 1));
            Assert.AreEqual(scope, b.Gear[0], "not during a wave");
        }

        // ------------------------------------------------------------------ the build phase

        [Test]
        public void WithWaitForStartABuildPhaseEndsOnlyWhenThePlayerSaysSo()
        {
            var config = new SimConfig { WaitForStart = true, PrepSeconds = 1f, IntermissionSeconds = 1f };
            Simulation sim = New(config);

            for (int i = 0; i < 5000; i++) sim.Tick();
            Assert.AreEqual(SimPhase.Prep, sim.State.Phase, "a hundred seconds on a one-second clock");
            Assert.AreEqual(0, sim.State.WaveIndex);

            TestSims.Send(sim, SimCommand.StartWaveNow());
            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.AreEqual(SimPhase.Spawning, sim.State.Phase);
        }

        // ------------------------------------------------------------------ a whole game

        [Test]
        public void AGameWithCardsLevelsAndGearReplaysOnEveryTickAndItsTelemetryIsMadeAgain()
        {
            SimConfig config = Cards();
            config.StartBudget = 80f;
            EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
            {
                Config = config,
                Map = TestSims.GameMap(),
                Shapes = TestSims.GameShapes(),
                Towers = TestSims.GameTowers(),
                Seed = 5,
                Planner = ClassCyclePlanner.Id,
                Policy = Registry.CreatePolicy(SentryPolicy.UpgraderId),
                MaxWaves = 6,
                RecordTickHashes = true,
            });

            var kinds = new HashSet<PlacementKind>();
            foreach (WaveRecord row in run.Telemetry)
                foreach (WaveRecord.PlacementRow p in row.Placements) Assert.IsNotEmpty(p.Kind);
            ReplayFile file = TestEpisodes.Reload(run.Replay);
            Simulation again = ReplayRunner.NewSimulation(file);
            foreach (ReplayCommand c in file.Commands) kinds.Add(KindOf(c.Cmd));

            CollectionAssert.Contains(kinds, PlacementKind.Tower);
            CollectionAssert.Contains(kinds, PlacementKind.TowerUpgraded, "the scripted player upgraded");
            CollectionAssert.Contains(kinds, PlacementKind.CoinTaken, "took a coin card");
            CollectionAssert.Contains(kinds, PlacementKind.GearBought, "and bought gear");

            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());

            List<WaveRecord> rebuilt = ReplayRunner.Telemetry(file);
            Assert.AreEqual(run.Telemetry.Count, rebuilt.Count);
            for (int i = 0; i < rebuilt.Count; i++) Assert.AreEqual(run.Telemetry[i].ToJsonLine(), rebuilt[i].ToJsonLine(), "row " + i);
            Assert.IsNotNull(again);
        }

        private static PlacementKind KindOf(string command)
        {
            switch (command)
            {
                case "UpgradeTower": return PlacementKind.TowerUpgraded;
                case "TakeCoin": return PlacementKind.CoinTaken;
                case "BuyGear": return PlacementKind.GearBought;
                case "MoveGear": return PlacementKind.GearMoved;
                case "PlaceTower": return PlacementKind.Tower;
                default: return PlacementKind.Wall;
            }
        }
    }
}
