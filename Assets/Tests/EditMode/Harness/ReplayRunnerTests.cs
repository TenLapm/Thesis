using NUnit.Framework;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class ReplayRunnerTests
    {
        [Test]
        public void ARecordedEpisodeVerifies()
        {
            ReplayFile file = TestEpisodes.SmallReplay();
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);

            Assert.IsTrue(report.Ok, report.Describe());
            Assert.AreEqual(3, report.WavesVerified);
            Assert.AreEqual(file.FinalTick, report.TicksRun);
            Assert.IsTrue(report.TickHashesChecked);
            Assert.AreEqual(-1, report.FirstDivergentWave);
            Assert.AreEqual(-1, report.FirstDivergentTick);
            StringAssert.Contains("OK", report.Describe());
        }

        [Test]
        public void ItStillVerifiesAfterBeingWrittenAndReadBack()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay());
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());
        }

        // WORKPLAN WP5: "take a recorded replay, change one command's tick by +1, and
        // check that replay reports the first divergent wave and then the exact tick."
        [Test]
        public void ACommandMovedByOneTickIsPinnedToItsWaveAndItsTick()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay());
            int tick = MoveFirstLatePlacementOneTickLater(file);
            int wave = FirstWaveResolvingAfter(file, tick);

            // Wave hashes alone name the wave...
            ReplayReport coarse = ReplayRunner.Verify(file, perTick: false);
            Assert.IsFalse(coarse.Ok);
            Assert.AreEqual(wave, coarse.FirstDivergentWave, coarse.Describe());
            Assert.AreEqual(-1, coarse.FirstDivergentTick, "the tick is not known without per-tick hashes");
            StringAssert.Contains("--per-tick", coarse.Describe());

            // ...and the per-tick hashes name the tick: the first tick that ran
            // without the wall the recording had already placed.
            ReplayReport fine = ReplayRunner.Verify(file, perTick: true);
            Assert.IsFalse(fine.Ok);
            Assert.AreEqual(wave, fine.FirstDivergentWave, fine.Describe());
            Assert.AreEqual(tick, fine.FirstDivergentTick, fine.Describe());
            Assert.AreEqual(wave - 1, fine.WavesVerified, "the waves before it still match");
        }

        [Test]
        public void AFileWithoutTickHashesStillNamesTheWave()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(tickHashes: false));
            Assert.IsNull(file.TickHashes);
            Assert.IsTrue(ReplayRunner.Verify(file, perTick: true).Ok, "untampered");

            int tick = MoveFirstLatePlacementOneTickLater(file);
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);

            Assert.IsFalse(report.Ok);
            Assert.AreEqual(FirstWaveResolvingAfter(file, tick), report.FirstDivergentWave);
            Assert.AreEqual(-1, report.FirstDivergentTick);
            Assert.IsFalse(report.TickHashesChecked);
            StringAssert.Contains("no per-tick hashes", report.Describe());
        }

        [Test]
        public void ADroppedCommandDiverges()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay());
            int index = file.Commands.FindIndex(c => c.Cmd == "PlaceShape");
            int tick = file.Commands[index].Tick;
            file.Commands.RemoveAt(index);

            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsFalse(report.Ok);
            Assert.AreEqual(tick, report.FirstDivergentTick, report.Describe());
        }

        // The seed is not part of the setup hash (config, map, shapes), so a changed
        // seed loads fine and is caught by the very first state hash instead.
        [Test]
        public void ADifferentSeedIsCaughtBeforeTheFirstTick()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            file.RngSeed++;

            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsFalse(report.Ok);
            Assert.AreEqual(0, report.TicksRun);
            StringAssert.Contains("starting state", report.Describe());
        }

        [Test]
        public void AnUnknownPlannerIsNamed()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            file.Planner = "director-v9";

            var e = Assert.Throws<System.ArgumentException>(() => ReplayRunner.Verify(file, perTick: false));
            StringAssert.Contains("director-v9", e.Message);
            StringAssert.Contains(EscalationPlanner.Id, e.Message);
        }

        [Test]
        public void RunToTickLandsOnTheRecordedTickHashes()
        {
            ReplayFile file = TestEpisodes.SmallReplay(waves: 2);
            ulong[] hashes = file.DecodeTickHashes();

            foreach (int ran in new[] { 0, 1, 137, hashes.Length / 2, hashes.Length - 1 })
            {
                Simulation sim = ReplayRunner.RunToTick(file, ran + 1, includeCommandsAtTick: false);
                Assert.AreEqual(ran + 1, sim.State.Tick);
                Assert.AreEqual(hashes[ran], sim.ComputeHash(), "state after tick " + ran);
            }

            Simulation end = ReplayRunner.RunToTick(file, file.FinalTick, includeCommandsAtTick: true);
            Assert.AreEqual(file.FinalHash, ReplayFile.Hex(end.ComputeHash()));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ReplayRunner.RunToTick(file, file.FinalTick + 1, false));
        }

        // A session quit in the middle of a wave, with input applied after the last
        // tick that ran (rotating while paused, then closing the game): the final
        // hash must cover both.
        [Test]
        public void ASessionCutOffMidWaveWithTrailingInputVerifies()
        {
            Simulation sim = TestSims.Ascii(TestEpisodes.SmallMap, new EscalationPlanner(TestEpisodes.SmallConfig(), AsciiMap.Parse(TestEpisodes.SmallMap).Map), TestEpisodes.SmallConfig(), seed: 5);
            var recorder = new ReplayRecorder(sim, recordTickHashes: true);

            Send(sim, recorder, SimCommand.PlaceShape(5, 2));
            Send(sim, recorder, SimCommand.StartWaveNow());
            for (int i = 0; i < 300; i++)
            {
                if (i == 120) Send(sim, recorder, SimCommand.Hold());
                sim.Tick();
                recorder.AfterTick();
            }
            // Rotate goes last: it always changes the state, so dropping it must show.
            Send(sim, recorder, SimCommand.PlaceShape(2, 0));
            Send(sim, recorder, SimCommand.Rotate());

            Assert.AreEqual(SimPhase.Resolving, sim.State.Phase, "the wave is still running");
            ReplayFile file = TestEpisodes.Reload(recorder.Snapshot());
            Assert.AreEqual(0, file.WaveHashes.Count);
            Assert.AreEqual(300, file.FinalTick);

            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());

            // Without the trailing input the final hash must NOT match.
            file.Commands.RemoveAt(file.Commands.Count - 1);
            ReplayReport cut = ReplayRunner.Verify(file, perTick: true);
            Assert.IsFalse(cut.Ok);
            StringAssert.Contains("final state", cut.Describe());
        }

        private static void Send(Simulation sim, ReplayRecorder recorder, SimCommand command)
        {
            recorder.OnCommand(command);
            sim.Enqueue(command);
            sim.FlushInput();
        }

        // Moves the first PlaceShape sent after tick 0 one tick later, keeping the
        // command list in tick order. Returns the tick it was recorded at.
        private static int MoveFirstLatePlacementOneTickLater(ReplayFile file)
        {
            int index = file.Commands.FindIndex(c => c.Cmd == "PlaceShape" && c.Tick > 0);
            Assert.GreaterOrEqual(index, 0, "the episode must place a wall after the first wave");

            ReplayCommand moved = file.Commands[index];
            int tick = moved.Tick;
            file.Commands.RemoveAt(index);
            moved.Tick = tick + 1;

            int insert = file.Commands.FindIndex(c => c.Tick > moved.Tick);
            if (insert < 0) insert = file.Commands.Count;
            file.Commands.Insert(insert, moved);
            file.Validate();
            return tick;
        }

        private static int FirstWaveResolvingAfter(ReplayFile file, int tick)
        {
            foreach (WaveHash w in file.WaveHashes)
            {
                if (w.Tick > tick) return w.Wave;
            }
            Assert.Fail("no wave resolves after tick " + tick);
            return -1;
        }
    }
}
