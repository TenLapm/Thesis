using System;
using System.Collections.Generic;
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
    // difference: one bit, at tick 2916). The files in Results/pinned-replays can:
    //
    //   dotnet-*.replay.json   scripted episodes recorded by the CLI under .NET
    //   mono-*.replay.json     the same episodes recorded in the Unity editor (Mono)
    //   unity-session-*.json   sessions played through SimHost's real frame loop
    //
    // This test replays every file there, and it runs in both test runners, so each
    // recording is re-run on the runtime that did NOT make it, on every test run.
    //
    // IF THIS FAILS:
    //   * You did not mean to change the game rules -> you have a determinism bug.
    //     `thesis replay <file> --per-tick` names the tick; see ARCHITECTURE.md §8
    //     and §9 rule 3 (an uncast float chain or a Math.* call is the usual cause).
    //   * You changed the rules on purpose (the WP-C packages do) -> the recordings
    //     describe the old game. Re-record both halves: `thesis pin` under .NET and
    //     the menu Thesis > Replay > Record Pinned Episodes (Mono) in Unity. Delete
    //     the unity-session files (or play and copy in new ones). Commit, and say
    //     so in DEVLOG. Do not delete the test.
    public class PinnedReplayTests
    {
        private static string Dir => Path.Combine(TestPaths.ProjectRoot, PinnedEpisodes.Folder.Replace('/', Path.DirectorySeparatorChar));

        // File names only, so a test is named after its recording.
        private static IEnumerable<string> Recordings()
        {
            if (!Directory.Exists(Dir)) yield break;
            string[] files = Directory.GetFiles(Dir, "*.replay.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files) yield return Path.GetFileName(f);
        }

        [TestCaseSource(nameof(Recordings))]
        public void ARecordedSessionStillReplaysExactly(string name)
        {
            ReplayFile file = ReplayFile.Load(Path.Combine(Dir, name));
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);

            Assert.IsTrue(report.Ok, name + " (recorded by " + file.Build + ")\n" + report.Describe());
            Assert.AreEqual(file.FinalTick, report.TicksRun);
            Assert.AreEqual(file.WaveHashes.Count, report.WavesVerified);
        }

        // Guards the set itself: with one half missing the test above would still
        // pass, while checking only one direction.
        [Test]
        public void BothRuntimesHaveRecordedTheSameEpisodes()
        {
            var dotnet = new List<string>();
            var mono = new List<string>();
            foreach (string name in Recordings())
            {
                if (name.StartsWith(PinnedEpisodes.DotnetTag + "-", StringComparison.Ordinal)) dotnet.Add(name.Substring(PinnedEpisodes.DotnetTag.Length));
                if (name.StartsWith(PinnedEpisodes.MonoTag + "-", StringComparison.Ordinal)) mono.Add(name.Substring(PinnedEpisodes.MonoTag.Length));
            }

            Assert.Greater(dotnet.Count, 0, "no dotnet-* recordings in " + Dir + ": run `thesis pin`");
            Assert.Greater(mono.Count, 0, "no mono-* recordings in " + Dir + ": run Thesis > Replay > Record Pinned Episodes (Mono) in Unity");
            CollectionAssert.AreEqual(dotnet, mono, "the two halves must be the same episodes");
        }

        // The same episode recorded on the two runtimes must be the same file in
        // every field that describes the game: same commands, same hashes.
        [Test]
        public void TheTwoRuntimesRecordedIdenticalRuns()
        {
            int compared = 0;
            foreach (string name in Recordings())
            {
                if (!name.StartsWith(PinnedEpisodes.DotnetTag + "-", StringComparison.Ordinal)) continue;
                string twin = Path.Combine(Dir, PinnedEpisodes.MonoTag + name.Substring(PinnedEpisodes.DotnetTag.Length));
                if (!File.Exists(twin)) continue;

                ReplayFile a = ReplayFile.Load(Path.Combine(Dir, name));
                ReplayFile b = ReplayFile.Load(twin);
                Assert.AreEqual(a.SetupHash, b.SetupHash, name + ": setup");
                Assert.AreEqual(a.InitialHash, b.InitialHash, name + ": initial state");
                Assert.AreEqual(a.FinalTick, b.FinalTick, name + ": length");
                Assert.AreEqual(a.FinalHash, b.FinalHash, name + ": final state");
                Assert.AreEqual(a.Commands.Count, b.Commands.Count, name + ": the scripted player made the same moves");
                for (int i = 0; i < a.Commands.Count; i++) Assert.AreEqual(a.Commands[i].ToString(), b.Commands[i].ToString(), name + ": command " + i);
                Assert.AreEqual(a.TickHashes, b.TickHashes, name + ": every tick hash");
                compared++;
            }
            Assert.Greater(compared, 0);
        }
    }
}
