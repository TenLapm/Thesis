using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class InputTests
    {
        // The claim FlushInput rests on: applying input between ticks is the same as
        // applying it at the start of the next tick. If this ever fails, a replay that
        // records "(tick, command)" would no longer reproduce a live session.
        [Test]
        public void FlushInputEqualsApplyingAtTheNextTick()
        {
            var flushed = TestSims.SampleScene(seed: 21);
            var ticked = TestSims.SampleScene(seed: 21);
            var script = new CommandScript(seed: 8, ticks: 6000, every: 7, mapWidth: 38, mapHeight: 38);

            for (int i = 0; i < 6000; i++)
            {
                if (i == 40)
                {
                    flushed.Enqueue(SimCommand.StartWaveNow());
                    ticked.Enqueue(SimCommand.StartWaveNow());
                }
                script.Feed(flushed);
                script.Feed(ticked);

                flushed.FlushInput(); // the host's path: apply now, tick later
                flushed.Tick();
                ticked.Tick();        // the replay's path: apply at step 1 of the tick

                if (flushed.ComputeHash() != ticked.ComputeHash())
                    Assert.Fail("flush and tick paths diverged at tick " + ticked.State.Tick);
            }
            Assert.GreaterOrEqual(ticked.State.WaveIndex, 1);
            Assert.Greater(ticked.State.PlacementLog.Count, 0);
        }

        [Test]
        public void RotateAndHoldApplyWithoutAdvancingTime()
        {
            // Paused game = no ticks. Rotating and holding must still work (as in the
            // original, where pause blocks building but not rotate/hold).
            var sim = TestSims.SampleScene();
            int tick = sim.State.Tick;
            int remaining = sim.State.PhaseTicksRemaining;

            sim.Enqueue(SimCommand.Rotate());
            sim.FlushInput();
            Assert.AreEqual(1, sim.State.Bag.CurrentRotationTurns);

            sim.Enqueue(SimCommand.Hold());
            sim.FlushInput();
            Assert.IsNotNull(sim.State.Bag.HoldShape);

            Assert.AreEqual(tick, sim.State.Tick, "no time passed");
            Assert.AreEqual(remaining, sim.State.PhaseTicksRemaining, "the prep timer did not move");
        }

        [Test]
        public void StartWaveNowFlushedEarlyIsActedOnByTheNextTick()
        {
            var sim = TestSims.SampleScene();
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.FlushInput();
            Assert.AreEqual(0, sim.State.WaveIndex, "flushing input never starts a wave by itself");

            sim.Tick();
            Assert.AreEqual(1, sim.State.WaveIndex);
        }

        [Test]
        public void FlushedPlacementRaisesItsEventsImmediately()
        {
            var sim = TestSims.SampleScene();
            sim.Enqueue(SimCommand.PlaceShape(18, 18));
            sim.FlushInput();

            Assert.AreEqual(1, sim.State.PlacementLog.Count);
            bool placed = false;
            foreach (SimEvent e in sim.LastTickEvents) if (e.Kind == SimEventKind.WallPlaced) placed = true;
            Assert.IsTrue(placed, "views see the wall on the frame it was clicked, not a tick later");
        }
    }
}
