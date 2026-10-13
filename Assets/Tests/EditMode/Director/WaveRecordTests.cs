using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Thesis.Director;

namespace Thesis.Tests.Director
{
    // The telemetry row and its file (WORKPLAN WP-C3, ARCHITECTURE.md §5.8).
    public class WaveRecordTests
    {
        // The exact bytes of a baseline row. If this fails because a field was
        // renamed, moved or removed, old telemetry files no longer read the same:
        // raise WaveRecord.CurrentSchema, do not edit the string to match.
        // (Adding a field at the end of the order is the one change that only needs
        // the string extended.)
        private const string BaselineLine =
            "{\"schema\":6,\"session\":\"hand\",\"condition\":\"escalation\",\"player\":\"test\",\"build\":\"test\",\"map\":\"hand\",\"mapSeed\":0,\"rngSeed\":1,"
            + "\"wave\":2,\"tickStart\":0,\"tickResolved\":-1,\"tickEnd\":0,"
            + "\"profile\":null,\"bin\":null,\"threatBudget\":null,\"strategies\":null,\"chosen\":null,\"fallback\":null,\"announcement\":null,"
            + "\"plan\":null,\"outcome\":null,"
            + "\"placements\":[{\"tick\":0,\"kind\":\"wall\",\"name\":\"T\",\"rot\":0,\"x\":3,\"y\":3,\"tiles\":[[3,3],[4,3]],\"cost\":4,\"refund\":0,\"piece\":0,\"tower\":-1},"
            + "{\"tick\":0,\"kind\":\"sellTower\",\"name\":\"archer\",\"rot\":0,\"x\":2,\"y\":2,\"tiles\":[[2,2]],\"cost\":0,\"refund\":7.5,\"piece\":-1,\"tower\":0}],"
            + "\"shop\":{\"offered\":[],\"bought\":[],\"sold\":[],\"rerolls\":0},"
            + "\"reward\":null,\"estimatorDigest\":null,\"directorMicros\":null,\"stateHash\":null}";

        [Test]
        public void ABaselineRowIsTheseExactBytes()
        {
            WaveRecord row = TestRows.Row(2, TestRows.Wall(3, 3, 4, 3), TestRows.SoldTower(2, 2));
            Assert.AreEqual(BaselineLine, row.ToJsonLine());
        }

        [Test]
        public void WhatOnlyADirectorKnowsIsWrittenAsNull()
        {
            // One schema for every study condition (CLAUDE.md §7).
            string line = TestRows.Row(1).ToJsonLine();
            foreach (string field in new[] { "profile", "bin", "threatBudget", "strategies", "chosen", "fallback", "announcement", "reward", "estimatorDigest", "directorMicros" })
                Assert.That(line, Does.Contain("\"" + field + "\":null"), field);
        }

        [Test]
        public void ARowIsOneLine()
        {
            WaveRecord row = TestRows.DirectorRow();
            row.Announcement = "two\nlines, a \"quote\" and a tab\t";
            string line = row.ToJsonLine();

            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(line, Does.Not.Contain("\r"));
            Assert.AreEqual(row.Announcement, WaveRecord.FromJsonLine(line).Announcement);
        }

        [Test]
        public void ADirectorsRowGoesThroughTheSameClass()
        {
            WaveRecord row = TestRows.DirectorRow();
            string line = row.ToJsonLine();
            WaveRecord back = WaveRecord.FromJsonLine(line);

            Assert.AreEqual(line, back.ToJsonLine(), "read and written again, it is the same text");

            Assert.AreEqual(3, back.Bin);
            Assert.AreEqual(140f, back.ThreatBudget);
            Assert.AreEqual("breach_thin_wall", back.Chosen);
            Assert.AreEqual(5, back.Profile.Count);
            Assert.AreEqual("maze_length", back.Profile.NameAt(0));
            Assert.AreEqual(0.62, back.Profile["maze_length"], 1e-6);

            Assert.AreEqual(3, back.Strategies.Length);
            Assert.AreEqual("Uncounterable", back.Strategies[2].Veto);
            Assert.AreEqual(float.NegativeInfinity, back.Strategies[2].Score, "a vetoed strategy's score (CLAUDE.md I5)");
            Assert.IsNull(back.Strategies[2].Heuristic);
            Assert.AreEqual(true, back.Strategies[1].Cold);
            Assert.That(line, Does.Contain("\"score\":\"-Infinity\""));

            Assert.AreEqual("sapper", back.Plan.Groups[0].Movement);
            Assert.AreEqual(0.5, back.Plan.Groups[0].Resist["frost"]);
            Assert.AreEqual(31, back.Outcome.Killed);
            Assert.AreEqual(40.0, back.Outcome.SpawnedByClass["sapper"]);
            Assert.AreEqual(1, back.Shop.Sold.Length);
            Assert.AreEqual(7.5f, back.Shop.Sold[0].Refund);
            Assert.AreEqual(2, back.Reward.Credited.Length);
            Assert.AreEqual(0.5f, back.Reward.Credited[1].Weight);
            Assert.AreEqual(412, back.DirectorMicros);
        }

        [Test]
        public void TheFieldsComeInTheWrittenOrderNotTheOrderOfTheClass()
        {
            string line = TestRows.DirectorRow().ToJsonLine();
            string[] order =
            {
                "\"schema\":", "\"session\":", "\"condition\":", "\"player\":", "\"build\":", "\"map\":", "\"mapSeed\":", "\"rngSeed\":", "\"wave\":",
                "\"tickStart\":", "\"tickResolved\":", "\"tickEnd\":", "\"profile\":", "\"bin\":", "\"threatBudget\":", "\"strategies\":", "\"chosen\":",
                "\"fallback\":", "\"announcement\":", "\"plan\":", "\"outcome\":", "\"placements\":", "\"shop\":", "\"reward\":", "\"estimatorDigest\":",
                "\"directorMicros\":", "\"stateHash\":",
            };

            int at = -1;
            foreach (string field in order)
            {
                int found = line.IndexOf(field, at + 1, System.StringComparison.Ordinal);
                Assert.Greater(found, at, field + " is missing or out of order");
                at = found;
            }
        }

        [Test]
        public void AFieldALaterVersionAddsIsSkippedWhenReading()
        {
            string line = BaselineLine.Substring(0, BaselineLine.Length - 1) + ",\"somethingNew\":{\"a\":[1,2]}}";
            WaveRecord row = WaveRecord.FromJsonLine(line);

            Assert.AreEqual(2, row.Wave);
            Assert.AreEqual(BaselineLine, row.ToJsonLine());
        }

        [Test]
        public void OnlyBuildingCountsAsBuilding()
        {
            Assert.IsTrue(TestRows.Wall(1, 1).IsBuild);
            Assert.IsTrue(TestRows.Tower(1, 1).IsBuild);
            Assert.IsFalse(TestRows.SoldWall(1, 1).IsBuild);
            Assert.IsFalse(TestRows.SoldTower(1, 1).IsBuild);
            Assert.IsFalse(TestRows.Reroll().IsBuild);
            Assert.That(TestRows.Wall(1, 1).ToJsonLineOfRow(), Does.Not.Contain("IsBuild").And.Not.Contain("isBuild"), "a convenience, not a field of the file");
        }

        // ---- the file ----

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "thesis-telemetry-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        public void TheFileIsUtf8WithoutAMarkOneRowPerLineEndingInALineFeed()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "sub", "telemetry.jsonl");
                WaveRecord a = TestRows.Row(0);
                WaveRecord b = TestRows.DirectorRow();
                b.Announcement = "Sie kommen \u00fcber die Mauer \u2014 \u58c1";   // not ASCII, on purpose

                using (var writer = new TelemetryWriter(path))
                {
                    writer.Write(a);
                    writer.Write(b);
                    Assert.AreEqual(2, writer.RowsWritten);
                    Assert.AreEqual(Path.GetFullPath(path), writer.Path);
                }

                byte[] bytes = File.ReadAllBytes(path);
                byte[] expected = new UTF8Encoding(false).GetBytes(a.ToJsonLine() + "\n" + b.ToJsonLine() + "\n");
                CollectionAssert.AreEqual(expected, bytes);
                Assert.AreNotEqual(0xEF, bytes[0], "no byte-order mark");
                Assert.AreEqual((byte)'{', bytes[0]);
                Assert.AreEqual((byte)'\n', bytes[bytes.Length - 1]);
                Assert.AreEqual(-1, System.Array.IndexOf(bytes, (byte)'\r'), "no carriage returns, on Windows either");

                Assert.AreEqual(TelemetryWriter.ToText(TestRows.List(a, b)), new UTF8Encoding(false).GetString(bytes));
                Assert.AreEqual(b.Announcement, TelemetryWriter.Load(path)[1].Announcement);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ARowIsOnDiskAsSoonAsItIsWritten()
        {
            // A session that crashes keeps every wave it finished.
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "telemetry.jsonl");
                using (var writer = new TelemetryWriter(path))
                {
                    writer.Write(TestRows.Row(0));
                    writer.Write(TestRows.Row(1));

                    string soFar;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                        soFar = reader.ReadToEnd();

                    Assert.AreEqual(2, TelemetryWriter.Parse(soFar).Count);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ANewWriterStartsANewFile()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "telemetry.jsonl");
                using (var writer = new TelemetryWriter(path)) writer.Write(TestRows.List(TestRows.Row(0), TestRows.Row(1), TestRows.Row(2)));
                using (var writer = new TelemetryWriter(path)) writer.Write(TestRows.Row(0));

                Assert.AreEqual(1, TelemetryWriter.Load(path).Count, "the second run's rows are not added to the first run's");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ReadingTakesBlankLinesAndWindowsLineEnds()
        {
            string text = "\r\n" + TestRows.Row(0).ToJsonLine() + "\r\n\r\n" + TestRows.Row(1).ToJsonLine() + "\r\n";
            List<WaveRecord> rows = TelemetryWriter.Parse(text);

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(1, rows[1].Wave);
            Assert.AreEqual(0, TelemetryWriter.Parse("").Count);
            Assert.AreEqual(0, TelemetryWriter.Parse(null).Count);
        }

        [Test]
        public void ARowOfAnotherSchemaIsRefusedWithItsLineNumber()
        {
            string text = TestRows.Row(0).ToJsonLine() + "\n" + TestRows.Row(1).ToJsonLine().Replace("\"schema\":6,", "\"schema\":99,") + "\n";

            var e = Assert.Throws<InvalidDataException>(() => TelemetryWriter.Parse(text));
            Assert.That(e.Message, Does.Contain("Line 2").And.Contain("schema 99"));
        }

        [Test]
        public void WhatIsNotARowIsRefusedWithItsLineNumber()
        {
            var e = Assert.Throws<InvalidDataException>(() => TelemetryWriter.Parse(TestRows.Row(0).ToJsonLine() + "\n\n{\"schema\":1,\"wave\":\n"));
            Assert.That(e.Message, Does.Contain("Line 3"));

            Assert.Throws<InvalidDataException>(() => TelemetryWriter.Parse("not json at all\n"));
            Assert.Throws<InvalidDataException>(() => TelemetryWriter.Parse("null\n"));
            Assert.Throws<FileNotFoundException>(() => TelemetryWriter.Load(Path.Combine(Path.GetTempPath(), "thesis-no-such-file.jsonl")));
        }

        [Test]
        public void AWriterNeedsAPathAndARow()
        {
            Assert.Throws<System.ArgumentException>(() => new TelemetryWriter(""));
            string dir = NewDir();
            try
            {
                using (var writer = new TelemetryWriter(Path.Combine(dir, "t.jsonl")))
                    Assert.Throws<System.ArgumentNullException>(() => writer.Write((WaveRecord)null));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    internal static class PlacementRowText
    {
        // A placement row on its own, as it appears inside a line.
        public static string ToJsonLineOfRow(this WaveRecord.PlacementRow row) { return Thesis.Core.Json.SerializeLine(row); }
    }
}
