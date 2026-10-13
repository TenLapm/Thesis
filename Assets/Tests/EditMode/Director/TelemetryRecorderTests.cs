using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // The recorder that turns a running simulation into telemetry rows (WORKPLAN
    // WP-C3). Most tests play a short episode on the small map and read its rows.
    public class TelemetryRecorderTests
    {
        private static EpisodeResult Play(IPlayerPolicy policy = null, int waves = 4, ulong seed = 1, string planner = EscalationPlanner.Id, Action<SimConfig> tune = null)
        {
            EpisodeOptions o = TestEpisodes.Small(seed, waves, tickHashes: false, policy: policy, planner: planner);
            if (tune != null) tune(o.Config);
            return EpisodeRunner.Run(o);
        }

        private static Simulation SmallSim(IWavePlanner planner = null, Action<SimConfig> tune = null, ulong seed = 1)
        {
            SimConfig config = TestEpisodes.SmallConfig();
            if (tune != null) tune(config);
            MapData map = AsciiMap.Parse(TestEpisodes.SmallMap, "small").Map;
            return new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), seed, planner ?? new EscalationPlanner(config, map));
        }

        // Ticks with the recorder attached, the way a host does, until `until` holds.
        private static void Run(Simulation sim, TelemetryRecorder recorder, List<WaveRecord> rows, Func<bool> until, int maxTicks = 20000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (until()) return;
                sim.Tick();
                rows.AddRange(recorder.AfterTick());
            }
            throw new InvalidOperationException("rig: the condition did not come true within " + maxTicks + " ticks.");
        }

        private static string[] Names(TowerOffer[] offers)
        {
            TowerDef[] roster = TestTowers.Roster();
            var names = new string[offers.Length];
            for (int i = 0; i < names.Length; i++) names[i] = roster[offers[i].TowerIndex].Id;
            return names;
        }

        // ---- the shape of a session ----

        [Test]
        public void ThereIsOneRowPerWaveAndOneForTheBuildPhaseBeforeTheFirst()
        {
            EpisodeResult run = Play(waves: 4);

            Assert.AreEqual(5, run.Telemetry.Count);
            for (int i = 0; i < run.Telemetry.Count; i++)
            {
                WaveRecord row = run.Telemetry[i];
                Assert.AreEqual(i, row.Wave);
                Assert.AreEqual(WaveRecord.CurrentSchema, row.Schema);
                Assert.AreEqual("test", row.Session);
                Assert.AreEqual("test", row.Build);
                Assert.AreEqual(SequencePolicy.MixedId, row.Player);
                Assert.AreEqual(EscalationPlanner.Id, row.Condition);
                Assert.AreEqual("small", row.Map);
                Assert.AreEqual(1UL, row.RngSeed);
            }
        }

        [Test]
        public void TheWindowsJoinUpFromTheFirstTickToTheLast()
        {
            EpisodeResult run = Play(waves: 4);
            List<WaveRecord> rows = run.Telemetry;

            Assert.AreEqual(0, rows[0].TickStart);
            for (int i = 1; i < rows.Count; i++)
            {
                Assert.AreEqual(rows[i - 1].TickEnd, rows[i].TickStart, "row " + i + " starts where row " + (i - 1) + " ends");
                Assert.Greater(rows[i].TickResolved, rows[i].TickStart, "row " + i);
                Assert.LessOrEqual(rows[i].TickResolved, rows[i].TickEnd, "row " + i);
            }
            Assert.AreEqual(run.Ticks, rows[rows.Count - 1].TickEnd, "the last window ends on the session's last tick");
        }

        [Test]
        public void RowZeroIsTheBuildPhaseBeforeTheFirstWave()
        {
            WaveRecord row = Play().Telemetry[0];

            Assert.AreEqual(0, row.Wave);
            Assert.IsNull(row.Plan);
            Assert.IsNull(row.Outcome);
            Assert.IsNull(row.StateHash);
            Assert.AreEqual(-1, row.TickResolved);
            Assert.Greater(row.Placements.Length, 0, "the scripted player built before the first wave");
            CollectionAssert.AreEqual(Names(ShopRoller.Roll(1UL, 0, 0, TestTowers.Roster(), new SimConfig().ShopOffers)), row.Shop.Offered, "the offers the game started with");
        }

        [Test]
        public void EveryRowOfTheBuildLogIsInExactlyOneWindowInOrder()
        {
            EpisodeResult run = Play(SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3), waves: 5);
            List<PlacementRecord> log = ReplayRunner.RunToTick(run.Replay, run.Replay.FinalTick, includeCommandsAtTick: true).State.PlacementLog;

            int at = 0;
            foreach (WaveRecord row in run.Telemetry)
            {
                foreach (WaveRecord.PlacementRow p in row.Placements)
                {
                    PlacementRecord r = log[at++];
                    Assert.AreEqual(r.Tick, p.Tick);
                    Assert.AreEqual(r.OriginX, p.X);
                    Assert.AreEqual(r.OriginY, p.Y);
                    Assert.AreEqual(r.ShapeName, p.Name);
                    Assert.AreEqual(r.Tiles.Length, p.Tiles.Length);
                    Assert.AreEqual(r.Cost, p.Cost);
                    Assert.AreEqual(r.Refund, p.Refund);
                    Assert.AreEqual(r.PieceId, p.Piece);
                    Assert.AreEqual(r.TowerId, p.TowerId);

                    // WaveRecord: tickStart < tick <= tickEnd, and row 0 from tick 0.
                    Assert.LessOrEqual(p.Tick, row.TickEnd, "wave " + row.Wave);
                    if (row.Wave > 0) Assert.Greater(p.Tick, row.TickStart, "wave " + row.Wave);
                }
            }
            Assert.AreEqual(log.Count, at, "nothing left out, nothing twice");
            Assert.Greater(at, 10);
        }

        [Test]
        public void WhatIsBuiltOnTheTickAWaveStartsBelongsToTheBuildPhaseBefore()
        {
            Simulation sim = SmallSim();
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();

            TestTowers.BuySlot(sim, 0, 5, 2);                       // tick 0, before the wave
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.Enqueue(SimCommand.PlaceTower(1, 6, 2));            // same tick, even after the start command: commands come first
            sim.Tick();
            rows.AddRange(recorder.AfterTick());

            Assert.AreEqual(1, rows.Count, "the wave's start closed row 0");
            Assert.AreEqual(0, rows[0].Wave);
            Assert.AreEqual(0, rows[0].TickEnd);
            Assert.AreEqual(2, rows[0].Placements.Length);
            Assert.AreEqual(2, rows[0].Shop.Bought.Length);

            TestTowers.BuySlot(sim, 2, 7, 2);                       // tick 1: the wave is running
            Run(sim, recorder, rows, () => sim.State.Phase == SimPhase.Intermission);
            rows.AddRange(recorder.Finish());

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(0, rows[1].TickStart);
            Assert.AreEqual(1, rows[1].Placements.Length);
            Assert.AreEqual(1, rows[1].Placements[0].Tick);
            Assert.AreEqual(7, rows[1].Placements[0].X);
        }

        // ---- the shop ----

        [Test]
        public void TheOffersOfARowAreTheRollMadeInItsWindow()
        {
            // Row N: the roll made when wave N resolved, which is what the player
            // could buy while answering wave N. A pure function of (seed, N, 0): I10.
            EpisodeResult run = Play(waves: 4, seed: 5);
            TowerDef[] roster = TestTowers.Roster();

            foreach (WaveRecord row in run.Telemetry)
                CollectionAssert.AreEqual(Names(ShopRoller.Roll(5UL, row.Wave, 0, roster, new SimConfig().ShopOffers)), row.Shop.Offered, "wave " + row.Wave);
        }

        [Test]
        public void WhatWasBoughtAndSoldIsListedWithTheRowsOfTheBuildLog()
        {
            EpisodeResult run = Play(SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3), waves: 5);

            int bought = 0, soldTowers = 0, soldWalls = 0;
            foreach (WaveRecord row in run.Telemetry)
            {
                var boughtHere = new List<string>();
                var soldHere = new List<WaveRecord.PlacementRow>();
                foreach (WaveRecord.PlacementRow p in row.Placements)
                {
                    if (p.Kind == WaveRecord.PlacementRow.Tower) boughtHere.Add(p.Name);
                    if (p.Kind == WaveRecord.PlacementRow.SellTower) soldHere.Add(p);
                    if (p.Kind == WaveRecord.PlacementRow.SellWall) soldWalls++;
                }

                CollectionAssert.AreEqual(boughtHere, row.Shop.Bought, "wave " + row.Wave);
                Assert.AreEqual(soldHere.Count, row.Shop.Sold.Length, "wave " + row.Wave);
                for (int i = 0; i < soldHere.Count; i++)
                {
                    Assert.AreEqual(soldHere[i].Name, row.Shop.Sold[i].Def);
                    Assert.AreEqual(soldHere[i].X, row.Shop.Sold[i].X);
                    Assert.AreEqual(soldHere[i].Y, row.Shop.Sold[i].Y);
                    Assert.AreEqual(soldHere[i].Refund, row.Shop.Sold[i].Refund);
                    Assert.Greater(soldHere[i].Refund, 0f);
                }
                Assert.AreEqual(0, row.Shop.Rerolls);

                // Everything bought in a window was on offer in it or in the one before
                // (offers stay through the next wave's fight).
                bought += boughtHere.Count;
                soldTowers += soldHere.Count;
            }

            Assert.Greater(bought, 4, "towers bought");
            Assert.Greater(soldTowers, 0, "towers sold");
            Assert.Greater(soldWalls, 0, "wall pieces sold");
        }

        [Test]
        public void ARerollIsCountedAndTheFirstOffersStayOnRecord()
        {
            Simulation sim = SmallSim(tune: c => c.RerollCost = 1f);
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();

            TestSims.Send(sim, SimCommand.RerollShop());
            TestSims.Send(sim, SimCommand.RerollShop());
            Assert.AreEqual(2, sim.State.Shop.RerollCount, "rig: both rerolls were paid for");
            TestSims.Send(sim, SimCommand.StartWaveNow());
            Run(sim, recorder, rows, () => rows.Count > 0);

            WaveRecord row = rows[0];
            Assert.AreEqual(2, row.Shop.Rerolls);
            Assert.AreEqual(2, row.Placements.Length);
            Assert.AreEqual(WaveRecord.PlacementRow.Reroll, row.Placements[0].Kind);
            Assert.AreEqual(1f, row.Placements[0].Cost);
            Assert.AreEqual(0, row.Placements[0].Tiles.Length);
            // The later sets need not be stored: they are ShopRoller.Roll(seed, wave, 1), (.., 2).
            CollectionAssert.AreEqual(Names(ShopRoller.Roll(1UL, 0, 0, TestTowers.Roster(), sim.Config.ShopOffers)), row.Shop.Offered);
        }

        // ---- the wave ----

        [Test]
        public void TheOutcomeIsTheSimulationsOwn()
        {
            EpisodeResult run = Play(waves: 4);

            for (int w = 1; w <= 4; w++)
            {
                WaveRecord.OutcomeRow row = run.Telemetry[w].Outcome;
                WaveOutcome o = run.Outcomes[w - 1];

                Assert.AreEqual(o.Spawned, row.Spawned);
                Assert.AreEqual(o.Killed, row.Killed);
                Assert.AreEqual(o.Leaked, row.Leaked);
                Assert.AreEqual(o.Removed, row.Removed);
                Assert.AreEqual(o.TimedOut, row.TimedOut);
                Assert.AreEqual(o.WallsBreached, row.Breaches);
                Assert.AreEqual(o.TowersDestroyed, row.TowersDestroyed);
                Assert.AreEqual(o.CoreHpBefore, row.CoreHpBefore);
                Assert.AreEqual(o.CoreHpAfter, row.CoreHpAfter);
                Assert.AreEqual(o.CoreDestroyed, row.CoreDestroyed);
                Assert.AreEqual(o.BudgetBefore, row.BudgetBefore);
                Assert.AreEqual(o.BudgetAfter, row.BudgetAfter);
                Assert.AreEqual(o.PressureShare, row.Pressure);
                Assert.AreEqual(o.TickStarted, run.Telemetry[w].TickStart);
                Assert.AreEqual(o.TickResolved, run.Telemetry[w].TickResolved);

                Assert.AreEqual(3, row.DamageByType.Count);
                Assert.AreEqual("physical", row.DamageByType.NameAt(0));
                Assert.AreEqual("fire", row.DamageByType.NameAt(1));
                Assert.AreEqual("frost", row.DamageByType.NameAt(2));
                Assert.AreEqual((double)o.DamageByType[0], row.DamageByType["physical"]);

                Assert.AreEqual("ground", row.SpawnedByClass.NameAt(0));
                Assert.AreEqual("sapper", row.SpawnedByClass.NameAt(1));
                Assert.AreEqual("flying", row.SpawnedByClass.NameAt(2));
                Assert.AreEqual(o.Spawned, (int)row.SpawnedByClass.Sum());
                Assert.AreEqual(o.Killed, (int)row.KilledByClass.Sum());
                Assert.AreEqual(o.Leaked, (int)row.LeakedByClass.Sum());
            }
        }

        [Test]
        public void TheStateHashIsTheOneTheReplayStoresForThatWave()
        {
            // The join between the two files of a session: a row can be checked
            // against the replay without running anything.
            EpisodeResult run = Play(waves: 4);

            Assert.AreEqual(4, run.Replay.WaveHashes.Count);
            for (int w = 1; w <= 4; w++)
            {
                WaveHash recorded = run.Replay.WaveHashes[w - 1];
                Assert.AreEqual(recorded.Wave, run.Telemetry[w].Wave);
                Assert.AreEqual(recorded.Hash, run.Telemetry[w].StateHash);
                Assert.AreEqual(recorded.Tick, run.Telemetry[w].TickResolved + 1, "the replay notes the tick after the one the wave resolved on");
            }
        }

        [Test]
        public void ThePlanIsWrittenOutGroupByGroup()
        {
            // The class-cycle planner's fifth wave sends all three classes at once.
            EpisodeResult run = Play(waves: 5, planner: ClassCyclePlanner.Id);
            WaveRecord row = run.Telemetry[5];

            Assert.AreEqual(ClassCyclePlanner.Id, row.Condition);
            Assert.IsNotNull(row.Plan.Strategy);
            Assert.AreEqual(3, row.Plan.Groups.Length);
            var movements = new List<string>();
            int count = 0;
            foreach (WaveRecord.GroupRow g in row.Plan.Groups)
            {
                movements.Add(g.Movement);
                count += g.Count;
                Assert.Greater(g.Hp, 0f);
                Assert.Greater(g.MoveSpeed, 0f);
                Assert.Greater(g.IntervalTicks, 0);
                Assert.AreEqual(3, g.Resist.Count);
                Assert.AreEqual("physical", g.Resist.NameAt(0));
            }
            CollectionAssert.AreEquivalent(new[] { "ground", "sapper", "flying" }, movements);
            Assert.AreEqual(row.Outcome.Spawned, count, "the plan and the outcome agree on how many came");
            Assert.Greater(row.Outcome.SpawnedByClass["sapper"], 0.0);
            Assert.Greater(row.Outcome.SpawnedByClass["flying"], 0.0);

            // The static baseline: one ground group.
            WaveRecord baseline = Play(waves: 1).Telemetry[1];
            Assert.AreEqual(EscalationPlanner.Id, baseline.Plan.Strategy);
            Assert.AreEqual(1, baseline.Plan.Groups.Length);
            Assert.AreEqual("ground", baseline.Plan.Groups[0].Movement);
            Assert.AreEqual(12, baseline.Plan.Groups[0].Count);
        }

        [Test]
        public void AWaveTheSafetyNetHadToPlanCarriesTheReason()
        {
            SimConfig config = TestEpisodes.SmallConfig();
            MapData map = AsciiMap.Parse(TestEpisodes.SmallMap, "small").Map;
            var flaky = new FlakyPlanner(new EscalationPlanner(config, map)) { ThrowOnWave = w => w == 2 };
            var safe = new SafePlanner(flaky, new EscalationPlanner(config, map), map, config);
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), 1, safe);
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();

            Run(sim, recorder, rows, () => rows.Count == 4);

            Assert.AreEqual("flaky", rows[0].Condition, "the condition is the planner that was meant to run");
            Assert.IsNull(rows[0].Fallback);
            Assert.IsNull(rows[1].Fallback);
            Assert.AreEqual(SafePlanner.ReasonException, rows[2].Fallback);
            Assert.IsNull(rows[3].Fallback, "one rescue does not mark the waves after it");
        }

        // ---- made again from the replay ----

        [TestCase("mixed", EscalationPlanner.Id, 1000)]
        [TestCase("restless", EscalationPlanner.Id, 1000)]
        [TestCase("mixed", ClassCyclePlanner.Id, 1000)]
        [TestCase("idle", EscalationPlanner.Id, 3)]       // ends with the core destroyed
        [TestCase("sentry", ClassCyclePlanner.Id, 1000)]
        public void ASessionMadeAgainFromItsReplayWritesTheSameBytes(string policy, string planner, int coreHp)
        {
            IPlayerPolicy player = policy == "mixed" ? SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 3)
                                 : policy == "restless" ? SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3)
                                 : Registry.CreatePolicy(policy);
            EpisodeResult run = Play(player, waves: 5, seed: 3, planner: planner, tune: c => c.CoreMaxHp = coreHp);

            // Through JSON, the way a file on disk is read.
            List<WaveRecord> again = ReplayRunner.Telemetry(TestEpisodes.Reload(run.Replay));

            Assert.AreEqual(TelemetryWriter.ToText(run.Telemetry), TelemetryWriter.ToText(again));
            Assert.Greater(run.Telemetry.Count, 1);
        }

        [Test]
        public void WhatIsDoneAfterTheLastTickIsInTheLastRowAndInTheRowsMadeAgain()
        {
            // A host stops between ticks: the player's last command was applied at
            // once (FlushInput) and no tick followed it. SimHost records this way.
            Simulation sim = SmallSim();
            var replay = new ReplayRecorder(sim, false) { Build = "b", Session = "s", Policy = "p" };
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();
            Action<SimCommand> send = c =>
            {
                replay.OnCommand(c);
                TestSims.Send(sim, c);
            };

            send(SimCommand.StartWaveNow());
            for (int i = 0; i < 40; i++)
            {
                sim.Tick();
                replay.AfterTick();
                rows.AddRange(recorder.AfterTick());
            }
            send(SimCommand.PlaceTower(0, 5, 2));
            rows.AddRange(recorder.Finish());
            ReplayFile file = TestEpisodes.Reload(replay.Snapshot());

            WaveRecord last = rows[rows.Count - 1];
            Assert.AreEqual(40, file.FinalTick);
            Assert.AreEqual(1, last.Placements.Length, "the purchase made after the last tick");
            Assert.AreEqual(40, last.Placements[0].Tick);
            Assert.AreEqual(40, last.TickEnd);
            Assert.AreEqual(TelemetryWriter.ToText(rows), TelemetryWriter.ToText(ReplayRunner.Telemetry(file)));
        }

        [Test]
        public void ASessionWithPaidRerollsIsMadeAgainTheSame()
        {
            // A host applies a command at once; a replay applies it at the start of
            // the next tick, so only there is the reroll's ShopRolled event among a
            // tick's events. The rows must not depend on which of the two happened.
            Simulation sim = SmallSim(tune: c => c.RerollCost = 1f);
            var replay = new ReplayRecorder(sim, false) { Build = "b", Session = "s", Policy = "p" };
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();
            Action<SimCommand> send = c =>
            {
                replay.OnCommand(c);
                TestSims.Send(sim, c);
            };
            Action tick = () =>
            {
                sim.Tick();
                replay.AfterTick();
                rows.AddRange(recorder.AfterTick());
            };

            send(SimCommand.RerollShop());                                   // before the first wave
            send(SimCommand.StartWaveNow());
            for (int i = 0; i < 20000 && sim.State.Phase != SimPhase.Intermission; i++) tick();
            Assert.AreEqual(SimPhase.Intermission, sim.State.Phase, "rig: wave 1 resolved and the shop rolled");

            send(SimCommand.RerollShop());
            send(SimCommand.RerollShop());
            send(SimCommand.PlaceTower(0, 5, 2));                            // from the third set
            Assert.AreEqual(2, sim.State.Shop.RerollCount, "rig: both rerolls were paid for");
            send(SimCommand.StartWaveNow());
            for (int i = 0; i < 10; i++) tick();
            rows.AddRange(recorder.Finish());

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual(1, rows[0].Shop.Rerolls);
            Assert.AreEqual(2, rows[1].Shop.Rerolls);
            Assert.AreEqual(1, rows[1].Shop.Bought.Length);
            CollectionAssert.AreEqual(Names(ShopRoller.Roll(1UL, 1, 0, TestTowers.Roster(), sim.Config.ShopOffers)), rows[1].Shop.Offered,
                                      "the first set of the roll, not what the last reroll gave");

            List<WaveRecord> again = ReplayRunner.Telemetry(TestEpisodes.Reload(replay.Snapshot()));
            Assert.AreEqual(TelemetryWriter.ToText(rows), TelemetryWriter.ToText(again));
        }

        // ---- how a session ends ----

        [Test]
        public void WhenTheCoreFallsTheLastRowIsClosedOnThatTick()
        {
            EpisodeResult run = Play(new IdlePolicy(), waves: 5, tune: c => c.CoreMaxHp = 3);
            Assert.IsTrue(run.GameOver, "rig: three leaks end the game");

            WaveRecord last = run.Telemetry[run.Telemetry.Count - 1];
            Assert.AreEqual(1, last.Wave);
            Assert.IsTrue(last.Outcome.CoreDestroyed);
            Assert.AreEqual(0, last.Outcome.CoreHpAfter);
            Assert.AreEqual(run.Ticks, last.TickEnd);
            Assert.AreEqual(run.Replay.WaveHashes[0].Hash, last.StateHash);
            Assert.AreEqual(0, last.Shop.Offered.Length, "the shop does not roll when the game ends");
            Assert.AreEqual(0, last.Placements.Length);
        }

        [Test]
        public void AfterTheGameIsOverTheRecorderStaysQuiet()
        {
            Simulation sim = SmallSim(tune: c => c.CoreMaxHp = 1);
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();
            TestSims.Send(sim, SimCommand.StartWaveNow());
            Run(sim, recorder, rows, () => sim.State.IsGameOver);

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(2, recorder.RowCount);

            sim.Tick();                                             // a finished game does not advance
            Assert.AreEqual(0, recorder.AfterTick().Count);
            Assert.AreEqual(0, recorder.Finish().Count, "the game-over row was already handed out");
            Assert.AreEqual(2, recorder.RowCount);
        }

        [Test]
        public void ASessionStoppedMidWaveKeepsTheOpenWindow()
        {
            Simulation sim = SmallSim();
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");
            var rows = new List<WaveRecord>();
            TestSims.Send(sim, SimCommand.StartWaveNow());
            Run(sim, recorder, rows, () => sim.State.Tick == 30);
            TestTowers.BuySlot(sim, 0, 5, 2);
            Assert.AreEqual(SimPhase.Spawning, sim.State.Phase, "rig: the wave is still coming in");

            rows.AddRange(recorder.Finish());

            Assert.AreEqual(2, rows.Count);
            WaveRecord open = rows[1];
            Assert.AreEqual(1, open.Wave);
            Assert.IsNotNull(open.Plan);
            Assert.IsNull(open.Outcome);
            Assert.IsNull(open.StateHash);
            Assert.AreEqual(-1, open.TickResolved);
            Assert.AreEqual(30, open.TickEnd);
            Assert.AreEqual(0, open.Shop.Offered.Length, "no roll yet: the wave has not resolved");
            Assert.AreEqual(1, open.Shop.Bought.Length, "bought from the offers of the window before");
            Assert.AreEqual(1, open.Placements.Length);

            Assert.AreEqual(0, recorder.Finish().Count, "finishing twice adds nothing");
            sim.Tick();
            Assert.AreEqual(0, recorder.AfterTick().Count, "and a finished recorder ignores later ticks");
        }

        // ---- how it must be used ----

        [Test]
        public void ItMustWatchFromBeforeTheFirstTickAndTheFirstCommand()
        {
            Simulation late = SmallSim();
            late.Tick();
            Assert.Throws<InvalidOperationException>(() => new TelemetryRecorder(late, "s", "b", "p"));

            Simulation built = SmallSim();
            TestTowers.BuySlot(built, 0, 5, 2);
            Assert.Throws<InvalidOperationException>(() => new TelemetryRecorder(built, "s", "b", "p"));

            Assert.Throws<ArgumentNullException>(() => new TelemetryRecorder(null, "s", "b", "p"));
        }

        [Test]
        public void ItMustBeAskedAfterEveryTick()
        {
            Simulation sim = SmallSim();
            var recorder = new TelemetryRecorder(sim, "s", "b", "p");

            sim.Tick();
            Assert.AreEqual(0, recorder.AfterTick().Count);
            Assert.AreEqual(0, recorder.AfterTick().Count, "asking twice for the same tick is harmless");

            sim.Tick();
            sim.Tick();
            Assert.Throws<InvalidOperationException>(() => recorder.AfterTick(), "a skipped tick could hide a wave's start");
        }
    }
}
