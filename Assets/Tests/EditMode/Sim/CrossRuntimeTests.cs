using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class CrossRuntimeTests
    {
        // The WP4 smoke session, played inside SampleScene in the Unity editor, replayed
        // here from the same seed and the same input at the same ticks. Must give the
        // same game-over tick and the same state hash under Unity's Mono AND .NET's
        // CoreCLR - an early look at WP5's cross-check, before replay files exist.
        //
        // History: the live session and Unity's test runner both gave 305650b7869ff480
        // while dotnet gave d88b7dfd957f0dcb. The first divergent tick was 2916: one
        // bit of one agent's position, from Mono evaluating a float chain in double.
        // After the explicit-cast rule (ARCHITECTURE.md §9 rule 3) both give the value
        // pinned below.
        //
        // Explicit, because it pins one exact run: any intended change to the rules
        // changes this hash. Re-record it (and say so in DEVLOG) when that happens.
        [Test, Explicit("Pins one recorded session; run under Unity and dotnet, both must pass")]
        public void Wp4SmokeSessionFromUnityReplaysToTheSameHash()
        {
            Simulation sim = TestSims.SampleScene(seed: 1);

            TestSims.Run(sim, 652);
            // One editor call at tick 652: rotate, hold, hold (ignored), five placements.
            sim.Enqueue(SimCommand.Rotate());
            sim.Enqueue(SimCommand.Hold());
            sim.Enqueue(SimCommand.Hold());
            sim.Enqueue(SimCommand.PlaceShape(16, 17));
            sim.Enqueue(SimCommand.PlaceShape(19, 15));
            sim.Enqueue(SimCommand.PlaceShape(13, 20));
            sim.Enqueue(SimCommand.PlaceShape(21, 12));
            sim.Enqueue(SimCommand.PlaceShape(10, 23));
            sim.FlushInput();
            Assert.AreEqual(5, sim.State.PlacementLog.Count, "all five placements succeeded in Unity");
            Assert.AreEqual(40f, sim.State.BuildBudget, "Unity showed budget 40 after them");

            TestSims.Run(sim, 1345 - 652);
            sim.Enqueue(SimCommand.StartWaveNow()); // the Start button, at tick 1345
            sim.FlushInput();

            int guard = 100000;
            while (!sim.State.IsGameOver && guard-- > 0) sim.Tick();

            TestContext.WriteLine("headless: game over at tick " + sim.State.Tick + ", hash " + sim.ComputeHash().ToString("x16") + ", " + sim.State.LastOutcome);
            Assert.AreEqual(5036, sim.State.Tick, "game-over tick");
            Assert.AreEqual(0xd88b7dfd957f0dcbUL, sim.ComputeHash(), "state hash at game over (same under Mono and CoreCLR)");
        }
    }
}
