using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // WP14: participant, session, condition and seed set go into the replay and every telemetry row
    // (D9), and nowhere when there is no study (development), so older files stay byte for byte.
    public class StudyLabelTests
    {
        private static EpisodeOptions Options(StudyInfo study)
        {
            return new EpisodeOptions
            {
                Config = new SimConfig { CoreMaxHp = 100000 },
                Map = TestSims.Open38Map(),
                Shapes = TestShapes.SampleSceneLibrary(),
                Towers = TestTowers.Roster(),
                Seed = 3,
                Policy = new SentryPolicy(),
                MaxWaves = 3,
                Session = "study-label",
                Study = study,
            };
        }

        [Test]
        public void ADevelopmentSessionWritesNoStudyField()
        {
            EpisodeResult run = EpisodeRunner.Run(Options(null));
            Assert.That(run.Replay.Study, Is.Null);
            StringAssert.DoesNotContain("\"Study\"", run.Replay.ToJson());
            foreach (WaveRecord row in run.Telemetry) StringAssert.DoesNotContain("\"study\"", row.ToJsonLine());
        }

        [Test]
        public void AStudySessionLabelsTheReplayAndEveryRow()
        {
            StudyInfo study = StudySessions.For("P07", 2);
            EpisodeResult run = EpisodeRunner.Run(Options(study));

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            Assert.That(file.Study, Is.Not.Null);
            Assert.That(file.Study.Participant, Is.EqualTo("P07"));
            Assert.That(file.Study.Group, Is.EqualTo(3));
            Assert.That(file.Study.SessionNumber, Is.EqualTo(2));
            Assert.That(file.Study.Condition, Is.EqualTo(StudyInfo.Static));
            Assert.That(file.Study.SeedSet, Is.EqualTo("Y"));

            Assert.That(run.Telemetry.Count, Is.GreaterThan(1));
            foreach (WaveRecord row in run.Telemetry)
            {
                Assert.That(row.Study, Is.Not.Null, "row " + row.Wave);
                Assert.That(row.Study.Participant, Is.EqualTo("P07"));
                StringAssert.Contains("\"study\":{\"participant\":\"P07\",\"group\":3,\"session\":2,\"condition\":\"static\",\"seedSet\":\"Y\",\"practice\":false}", row.ToJsonLine());
            }

            // The rows made again from the replay are the same, byte for byte.
            List<WaveRecord> again = ReplayRunner.Telemetry(file);
            Assert.That(again.Count, Is.EqualTo(run.Telemetry.Count));
            for (int i = 0; i < again.Count; i++) Assert.That(again[i].ToJsonLine(), Is.EqualTo(run.Telemetry[i].ToJsonLine()), "row " + i);
        }

        [Test]
        public void TheLabelNeverReachesTheSimulation()
        {
            EpisodeResult plain = EpisodeRunner.Run(Options(null));
            EpisodeResult labelled = EpisodeRunner.Run(Options(StudySessions.For("P01", 1)));
            Assert.That(labelled.Replay.FinalHash, Is.EqualTo(plain.Replay.FinalHash));
        }
    }
}
