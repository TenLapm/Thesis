using System.IO;
using NUnit.Framework;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // The standing cross-runtime check for CLAUDE.md I1.
    //
    // DeterminismTests compares two simulations inside ONE runtime, so it cannot see
    // a difference between Unity's Mono and .NET (WP4 found exactly such a
    // difference: one bit, at tick 2916). These files can. Two were recorded by
    // real play sessions in the Unity editor (Mono) and one by the headless CLI
    // (.NET). This test runs in both test runners, so each file is re-run on the
    // runtime that did NOT record it, on every test run.
    //
    // IF THIS FAILS:
    //   * You did not mean to change the game rules -> you have a determinism bug.
    //     `thesis replay <file> --per-tick` names the tick; see ARCHITECTURE.md §8
    //     and §9 rule 3 (uncast float chains are the usual cause).
    //   * You changed the rules on purpose (the WP-C packages do) -> these
    //     recordings describe the old game. Record new ones the same way (see
    //     Results/2026-10-02_determinism/README.md), replace the files, and say so
    //     in DEVLOG. Do not delete the test.
    public class PinnedReplayTests
    {
        private static string Dir => Path.Combine(TestPaths.ProjectRoot, "Results", "2026-10-02_determinism");

        [TestCase("unity-session-a.replay.json", 4709, 1, Description = "Unity editor, x1/x2/x3 and timeScale 20, pause, mid-wave builds, played to game over")]
        [TestCase("unity-session-b.replay.json", 882, 0, Description = "Unity editor, quit mid-wave while paused, with input after the last tick")]
        [TestCase("headless-greedy-5waves-seed7.replay.json", 22914, 5, Description = ".NET CLI, greedy policy, five waves with stalls, leaks and breaches")]
        public void ARecordedSessionStillReplaysExactly(string name, int ticks, int waves)
        {
            ReplayFile file = ReplayFile.Load(Path.Combine(Dir, name));
            Assert.AreEqual(ticks, file.FinalTick, "the pinned file itself changed");
            Assert.AreEqual(waves, file.WaveHashes.Count, "the pinned file itself changed");

            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, name + " (recorded by " + file.Build + ")\n" + report.Describe());
            Assert.AreEqual(ticks, report.TicksRun);
            Assert.AreEqual(waves, report.WavesVerified);
        }

        [Test]
        public void TheUnitySessionsWereRecordedByUnityAndCarryTickHashes()
        {
            foreach (string name in new[] { "unity-session-a.replay.json", "unity-session-b.replay.json" })
            {
                ReplayFile file = ReplayFile.Load(Path.Combine(Dir, name));
                StringAssert.StartsWith("Unity", file.Build);
                Assert.AreEqual("human", file.Policy);
                Assert.AreEqual(file.FinalTick, file.DecodeTickHashes().Length, "one hash per tick");
            }
        }
    }
}
