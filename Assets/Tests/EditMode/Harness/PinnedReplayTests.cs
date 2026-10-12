using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Thesis.Director;
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
    // Since WP-C3 every recording has a twin, <name>.telemetry.jsonl: the telemetry
    // the same session wrote while it ran. The test makes that file again from the
    // replay and compares bytes, so telemetry is under the same cross-runtime check.
    //
    // IF THIS FAILS:
    //   * You did not mean to change the game rules -> you have a determinism bug.
    //     `thesis replay <file> --per-tick` names the tick; see ARCHITECTURE.md §8
    //     and §9 rule 3 (an uncast float chain or a Math.* call is the usual cause).
    //   * You changed the rules on purpose (the WP-C packages do) -> the recordings
    //     describe the old game. Re-record both halves: `thesis pin` under .NET and
    //     the menu Thesis > Replay > Record Pinned Episodes (Mono) in Unity. Delete
    //     the unity-session files (or play and copy in new ones, each with the
    //     telemetry.jsonl its session wrote). Commit, and say so in DEVLOG. Do not
    //     delete the test.
    //   * Only the telemetry tests fail and the replays still verify -> the row
    //     changed, not the game. If that was meant (a new field), re-record the same
    //     way; `thesis telemetry <replay> --check <file>` shows the first line that
    //     differs.
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

        // WP-C3: telemetry is a pure function of the replay. The file a session wrote
        // while it ran and the rows made again here, on this runtime, are the same
        // bytes. For a unity-session file the first was written by SimHost's frame
        // loop, live, and the second by a plain loop.
        [TestCaseSource(nameof(Recordings))]
        public void ARecordedSessionsTelemetryIsMadeAgainByteForByte(string name)
        {
            string replayPath = Path.Combine(Dir, name);
            string telemetryPath = PinnedEpisodes.TelemetryPathFor(replayPath);
            Assert.IsTrue(File.Exists(telemetryPath), name + " has no " + Path.GetFileName(telemetryPath) + " beside it");

            byte[] bytes = File.ReadAllBytes(telemetryPath);
            Assert.AreEqual(-1, Array.IndexOf(bytes, (byte)'\r'), Path.GetFileName(telemetryPath) + " has Windows line ends: .gitattributes must keep *.jsonl as LF");
            string recorded = new UTF8Encoding(false).GetString(bytes);

            ReplayFile file = ReplayFile.Load(replayPath);
            List<WaveRecord> rows = ReplayRunner.Telemetry(file);
            string again = TelemetryWriter.ToText(rows);

            if (recorded != again)
            {
                string[] a = recorded.Split('\n');
                string[] b = again.Split('\n');
                int line = 0;
                while (line < a.Length && line < b.Length && a[line] == b[line]) line++;
                string fileLine = line < a.Length ? a[line] : "";
                string madeLine = line < b.Length ? b[line] : "";
                int at = 0;
                while (at < fileLine.Length && at < madeLine.Length && fileLine[at] == madeLine[at]) at++;
                int from = Math.Max(0, at - 60);
                Assert.Fail(name + " (recorded by " + file.Build + "): line " + (line + 1) + " differs at character " + at
                            + "\n  file  : ..." + fileLine.Substring(Math.Min(from, fileLine.Length), Math.Min(160, Math.Max(0, fileLine.Length - from)))
                            + "\n  replay: ..." + madeLine.Substring(Math.Min(from, madeLine.Length), Math.Min(160, Math.Max(0, madeLine.Length - from))));
            }

            Assert.Greater(rows.Count, 1, "row 0 and at least one wave");
            Assert.AreEqual(file.WaveHashes.Count, WavesWithAHash(rows), "one state hash per resolved wave, as in the replay");
        }

        private static int WavesWithAHash(List<WaveRecord> rows)
        {
            int n = 0;
            foreach (WaveRecord row in rows) if (row.StateHash != null) n++;
            return n;
        }

        // The same episode's telemetry from the two runtimes: the same file but for
        // the two labels that say who recorded it.
        [Test]
        public void TheTwoRuntimesWroteTheSameTelemetry()
        {
            int compared = 0;
            foreach (string name in Recordings())
            {
                if (!name.StartsWith(PinnedEpisodes.DotnetTag + "-", StringComparison.Ordinal)) continue;
                string dotnetPath = PinnedEpisodes.TelemetryPathFor(Path.Combine(Dir, name));
                string monoPath = PinnedEpisodes.TelemetryPathFor(Path.Combine(Dir, PinnedEpisodes.MonoTag + name.Substring(PinnedEpisodes.DotnetTag.Length)));
                if (!File.Exists(dotnetPath) || !File.Exists(monoPath)) continue;

                List<WaveRecord> a = TelemetryWriter.Load(dotnetPath);
                List<WaveRecord> b = TelemetryWriter.Load(monoPath);
                Assert.AreNotEqual(a[0].Build, b[0].Build, name + ": the two files must come from two runtimes");
                foreach (WaveRecord row in a) row.Session = row.Build = "-";
                foreach (WaveRecord row in b) row.Session = row.Build = "-";

                Assert.AreEqual(TelemetryWriter.ToText(a), TelemetryWriter.ToText(b), name);
                compared++;
            }
            Assert.Greater(compared, 0);
        }

        // Guards what the set covers: if no recording bought, built and sold, the
        // tests above would pass without the shop and the selling rules ever running
        // on the other runtime.
        [Test]
        public void TheRecordingsCoverBuildingBuyingAndSelling()
        {
            var seen = new Dictionary<string, int>();
            foreach (string name in Recordings())
            {
                string telemetryPath = PinnedEpisodes.TelemetryPathFor(Path.Combine(Dir, name));
                if (!File.Exists(telemetryPath)) continue;
                foreach (WaveRecord row in TelemetryWriter.Load(telemetryPath))
                {
                    foreach (WaveRecord.PlacementRow p in row.Placements)
                    {
                        int n;
                        seen.TryGetValue(p.Kind, out n);
                        seen[p.Kind] = n + 1;
                    }
                }
            }

            foreach (string kind in new[] { WaveRecord.PlacementRow.Wall, WaveRecord.PlacementRow.Tower, WaveRecord.PlacementRow.SellWall, WaveRecord.PlacementRow.SellTower })
                Assert.IsTrue(seen.ContainsKey(kind) && seen[kind] > 0, "no recording has a '" + kind + "' row");
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
