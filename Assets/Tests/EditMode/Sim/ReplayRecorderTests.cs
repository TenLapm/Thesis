using System;
using System.IO;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class ReplayRecorderTests
    {
        private const string Map = @"
            S . . . . .
            . . . . . .
            . . . . . C";

        private static Simulation NewSim(SimConfig config = null)
        {
            return TestSims.Ascii(Map, new FixedPlanner(FixedPlanner.Group(count: 2, hp: 5f, speed: 20f)), config ?? new SimConfig { PrepSeconds = 0.1f, IntermissionSeconds = 0.1f });
        }

        [Test]
        public void ItMustBeAttachedBeforeTheFirstTick()
        {
            Simulation sim = NewSim();
            sim.Tick();
            Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(sim, false));
        }

        [Test]
        public void EveryTickMustBeReported()
        {
            Simulation sim = NewSim();
            var recorder = new ReplayRecorder(sim, true);
            sim.Tick();
            sim.Tick(); // AfterTick was skipped once
            Assert.Throws<InvalidOperationException>(() => recorder.AfterTick());
        }

        [Test]
        public void CommandsAreStampedWithTheTickTheyApplyAt()
        {
            Simulation sim = NewSim(new SimConfig { PrepSeconds = 5f });
            var recorder = new ReplayRecorder(sim, false);

            recorder.OnCommand(SimCommand.Rotate());
            sim.Enqueue(SimCommand.Rotate());
            for (int i = 0; i < 3; i++)
            {
                sim.Tick();
                recorder.AfterTick();
            }
            recorder.OnCommand(SimCommand.PlaceShape(2, 1));
            sim.Enqueue(SimCommand.PlaceShape(2, 1));
            sim.FlushInput();

            ReplayFile file = recorder.Snapshot();
            Assert.AreEqual(2, file.Commands.Count);
            Assert.AreEqual("tick 0: Rotate", file.Commands[0].ToString());
            Assert.AreEqual("tick 3: PlaceShape(2,1)", file.Commands[1].ToString());
            Assert.AreEqual(3, file.FinalTick);
            Assert.AreEqual(ReplayFile.Hex(sim.ComputeHash()), file.FinalHash);
        }

        [Test]
        public void AWaveHashIsTakenOnTheTickTheWaveResolves()
        {
            Simulation sim = NewSim();
            var recorder = new ReplayRecorder(sim, false);

            bool sawBoundary = false;
            ulong hashAtResolve = 0;
            int tickAtResolve = -1;
            for (int i = 0; i < 200 && recorder.WaveCount == 0; i++)
            {
                sim.Tick();
                if (recorder.AfterTick())
                {
                    sawBoundary = true;
                    hashAtResolve = sim.ComputeHash();
                    tickAtResolve = sim.State.Tick;
                }
            }

            Assert.IsTrue(sawBoundary, "AfterTick reports the wave boundary");
            ReplayFile file = recorder.Snapshot();
            Assert.AreEqual(1, file.WaveHashes.Count);
            Assert.AreEqual(1, file.WaveHashes[0].Wave);
            Assert.AreEqual(tickAtResolve, file.WaveHashes[0].Tick);
            Assert.AreEqual(ReplayFile.Hex(hashAtResolve), file.WaveHashes[0].Hash);
            Assert.IsNull(file.TickHashes, "per-tick hashes were not asked for");
        }

        [Test]
        public void NothingIsRecordedAfterGameOver()
        {
            // Both agents leak into a 1-HP core: the wave ends the game.
            Simulation sim = TestSims.Ascii(Map, new FixedPlanner(FixedPlanner.Group(count: 2, hp: 60f, speed: 4f)), new SimConfig { PrepSeconds = 0.1f, CoreMaxHp = 1 });
            var recorder = new ReplayRecorder(sim, true);
            for (int i = 0; i < 2000 && !sim.State.IsGameOver; i++)
            {
                sim.Tick();
                recorder.AfterTick();
            }
            Assert.IsTrue(sim.State.IsGameOver);
            int finalTick = sim.State.Tick;

            recorder.OnCommand(SimCommand.Rotate());
            sim.Enqueue(SimCommand.Rotate());
            sim.Tick();
            Assert.IsFalse(recorder.AfterTick());

            ReplayFile file = recorder.Snapshot();
            Assert.AreEqual(0, file.Commands.Count);
            Assert.AreEqual(finalTick, file.FinalTick);
            Assert.AreEqual(finalTick, file.DecodeTickHashes().Length);
            Assert.AreEqual(1, file.WaveHashes.Count, "the wave the core died in is still recorded");
        }

        [Test]
        public void SnapshotsDoNotChangeAsTheSessionContinues()
        {
            Simulation sim = NewSim(new SimConfig { PrepSeconds = 5f });
            var recorder = new ReplayRecorder(sim, true);
            sim.Tick();
            recorder.AfterTick();
            ReplayFile early = recorder.Snapshot();

            recorder.OnCommand(SimCommand.Hold());
            sim.Enqueue(SimCommand.Hold());
            sim.Tick();
            recorder.AfterTick();

            Assert.AreEqual(0, early.Commands.Count);
            Assert.AreEqual(1, early.FinalTick);
            Assert.AreEqual(1, recorder.Snapshot().Commands.Count);
        }

        // In the Unity editor the config asset can be edited while the game runs.
        // Such a session cannot be reproduced; the file must say so when loaded.
        [Test]
        public void AConfigEditedMidSessionMakesTheFileInvalid()
        {
            var config = new SimConfig { PrepSeconds = 5f };
            Simulation sim = NewSim(config);
            var recorder = new ReplayRecorder(sim, false);
            recorder.Snapshot().Validate();

            config.DigRate = 3f;
            Assert.Throws<InvalidDataException>(() => recorder.Snapshot().Validate());
        }

        [Test]
        public void LabelsAndSetupAreCopiedIntoTheFile()
        {
            Simulation sim = NewSim();
            var recorder = new ReplayRecorder(sim, false) { Build = "b", Session = "s", Policy = "p" };
            ReplayFile file = recorder.Snapshot();

            Assert.AreEqual("b", file.Build);
            Assert.AreEqual("s", file.Session);
            Assert.AreEqual("p", file.Policy);
            Assert.AreEqual("fixed", file.Planner);
            Assert.AreEqual(sim.RngSeed, file.RngSeed);
            Assert.AreSame(sim.Map, file.MapData);
            Assert.AreEqual(7, file.Shapes.Length);
            Assert.AreEqual(ReplayFile.Hex(sim.ComputeHash()), file.InitialHash);
        }
    }
}
