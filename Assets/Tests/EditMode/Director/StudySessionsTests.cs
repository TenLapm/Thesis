using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP14: the participant code decides the two sessions (D9, ARCHITECTURE.md §6).
    public class StudySessionsTests
    {
        [TestCase(1, 1, StudyInfo.Static, "X")]
        [TestCase(1, 2, StudyInfo.Adaptive, "Y")]
        [TestCase(2, 1, StudyInfo.Static, "Y")]
        [TestCase(2, 2, StudyInfo.Adaptive, "X")]
        [TestCase(3, 1, StudyInfo.Adaptive, "X")]
        [TestCase(3, 2, StudyInfo.Static, "Y")]
        [TestCase(4, 1, StudyInfo.Adaptive, "Y")]
        [TestCase(4, 2, StudyInfo.Static, "X")]
        public void EachGroupGetsTheSessionsOfTheTable(int participant, int session, string condition, string seedSet)
        {
            StudyInfo info = StudySessions.For("P" + participant, session);
            Assert.That(info.Group, Is.EqualTo(participant));
            Assert.That(info.SessionNumber, Is.EqualTo(session));
            Assert.That(info.Condition, Is.EqualTo(condition));
            Assert.That(info.SeedSet, Is.EqualTo(seedSet));
            Assert.That(info.Practice, Is.False);
            Assert.That(StudySessions.SeedOf(info), Is.EqualTo(seedSet == "X" ? StudySessions.SeedX : StudySessions.SeedY));
            Assert.That(StudySessions.PlannerOf(info), Is.EqualTo(condition == StudyInfo.Adaptive ? WaveDirector.FullId : SpreadStaticPlanner.Id));
        }

        [TestCase(1, 1)] [TestCase(2, 2)] [TestCase(4, 4)] [TestCase(5, 1)] [TestCase(23, 3)] [TestCase(24, 4)]
        public void TheGroupFollowsFromTheNumber(int participant, int group)
        {
            Assert.That(StudySessions.For("P" + participant, 1).Group, Is.EqualTo(group));
        }

        [Test]
        public void EveryParticipantPlaysBothConditionsOnTwoSeeds()
        {
            for (int p = 1; p <= 30; p++)
            {
                StudyInfo a = StudySessions.For("P" + p, 1), b = StudySessions.For("P" + p, 2);
                Assert.That(a.Condition, Is.Not.EqualTo(b.Condition), "P" + p);
                Assert.That(a.SeedSet, Is.Not.EqualTo(b.SeedSet), "P" + p);
            }
        }

        [Test]
        public void TwentyFourParticipantsAreBalanced()
        {
            // 48 sessions: each condition on each seed, and each condition in each position, 12 times.
            var cells = new Dictionary<string, int>();
            for (int p = 1; p <= 24; p++)
                for (int s = 1; s <= 2; s++)
                {
                    StudyInfo info = StudySessions.For("P" + p, s);
                    Count(cells, info.Condition + "/" + info.SeedSet);
                    Count(cells, info.Condition + "/session" + s);
                }
            Assert.That(cells.Count, Is.EqualTo(8));
            foreach (var kv in cells) Assert.That(kv.Value, Is.EqualTo(12), kv.Key);
        }

        private static void Count(Dictionary<string, int> cells, string key)
        {
            cells.TryGetValue(key, out int n);
            cells[key] = n + 1;
        }

        [TestCase("p7", "P07")] [TestCase("P07", "P07")] [TestCase(" P12 ", "P12")] [TestCase("P120", "P120")]
        public void ACodeIsWrittenOneWay(string typed, string written)
        {
            Assert.That(StudySessions.For(typed, 1).Participant, Is.EqualTo(written));
        }

        [TestCase("")] [TestCase(null)] [TestCase("P")] [TestCase("P0")] [TestCase("X12")] [TestCase("P-1")] [TestCase("P1a")] [TestCase("P1000")]
        public void AWrongCodeIsRefused(string code)
        {
            Assert.That(StudySessions.TryParse(code, out _), Is.False);
            Assert.Throws<ArgumentException>(() => StudySessions.For(code, 1));
        }

        [TestCase(0)] [TestCase(3)]
        public void OnlySessionsOneAndTwoExist(int session)
        {
            Assert.Throws<ArgumentException>(() => StudySessions.For("P01", session));
        }

        [Test]
        public void PracticeIsStaticOnItsOwnSeedAndShort()
        {
            StudyInfo info = StudySessions.Practice("p3");
            Assert.That(info.Practice, Is.True);
            Assert.That(info.Participant, Is.EqualTo("P03"));
            Assert.That(info.SessionNumber, Is.EqualTo(0));
            Assert.That(info.Group, Is.EqualTo(0));
            Assert.That(info.Condition, Is.EqualTo(StudyInfo.Static));
            Assert.That(info.SeedSet, Is.EqualTo("practice"));
            Assert.That(StudySessions.SeedOf(info), Is.EqualTo(StudySessions.PracticeSeed));
            Assert.That(StudySessions.PlannerOf(info), Is.EqualTo(SpreadStaticPlanner.Id));
            Assert.That(StudySessions.PracticeWaves, Is.EqualTo(3));
        }

        [Test]
        public void TheThreeSeedsDiffer()
        {
            Assert.That(new HashSet<ulong> { StudySessions.SeedX, StudySessions.SeedY, StudySessions.PracticeSeed }.Count, Is.EqualTo(3));
        }
    }
}
