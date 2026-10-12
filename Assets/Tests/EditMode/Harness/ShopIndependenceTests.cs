using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // CLAUDE.md I10, end to end: the shop's offers depend on the seed and the wave
    // number and on nothing else. ShopTests checks the roll itself; this plays whole
    // sessions with different players against different planners and reads what
    // each was offered from its telemetry, which is where the analysis will read it.
    public class ShopIndependenceTests
    {
        private const int Waves = 30;
        private const ulong Seed = 4;

        private static List<WaveRecord> Play(IPlayerPolicy policy, string planner)
        {
            EpisodeOptions o = TestEpisodes.Small(Seed, Waves, tickHashes: false, policy: policy, planner: planner);
            o.Config.CoreMaxHp = 1000000;   // every session must reach wave 30, the idle one too
            EpisodeResult run = EpisodeRunner.Run(o);
            Assert.AreEqual(Waves, run.WavesResolved, "rig: " + policy.Name + " against " + planner + " did not reach wave " + Waves);
            return run.Telemetry;
        }

        [Test]
        public void EveryPlayerAgainstEveryPlannerIsOfferedTheSameTowersForThirtyWaves()
        {
            TowerDef[] roster = TestTowers.Roster();
            int offers = new SimConfig().ShopOffers;

            var sessions = new List<List<WaveRecord>>();
            var labels = new List<string>();
            foreach (string planner in new[] { EscalationPlanner.Id, ClassCyclePlanner.Id })
            {
                IPlayerPolicy[] players =
                {
                    new IdlePolicy(),
                    new GreedyDetourPolicy { MaxPlacementsPerIntermission = 2, MaxOriginsPerPlacement = 12 },
                    new SentryPolicy(),
                    SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 3),
                    SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3),
                };
                foreach (IPlayerPolicy player in players)
                {
                    sessions.Add(Play(player, planner));
                    labels.Add(player.Name + " against " + planner);
                }
            }

            for (int s = 0; s < sessions.Count; s++)
            {
                Assert.AreEqual(Waves + 1, sessions[s].Count, labels[s]);
                foreach (WaveRecord row in sessions[s])
                {
                    TowerOffer[] roll = ShopRoller.Roll(Seed, row.Wave, 0, roster, offers);
                    var expected = new string[roll.Length];
                    for (int i = 0; i < roll.Length; i++) expected[i] = roster[roll[i].TowerIndex].Id;

                    CollectionAssert.AreEqual(expected, row.Shop.Offered, labels[s] + ", wave " + row.Wave);
                }
            }

            // The sessions must really have differed, or the above proves little.
            Assert.AreEqual(0, Built(sessions[0]), "the idle player builds nothing");
            Assert.Greater(Built(sessions[2]), 20, "the tower player builds a lot");
            Assert.AreNotEqual(Built(sessions[2]), Built(sessions[3]), "and differently from the one who also builds walls");
            Assert.Greater(Sold(sessions[4]), 10, "the restless player sells");
            // Wave 3 of the class cycle is all flyers; the static baseline never sends one.
            Assert.AreEqual(0.0, sessions[2][3].Outcome.SpawnedByClass["flying"], labels[2]);
            Assert.Greater(sessions[7][3].Outcome.SpawnedByClass["flying"], 0.0, labels[7] + ": the two planners sent different waves");
        }

        private static int Built(List<WaveRecord> rows)
        {
            int n = 0;
            foreach (WaveRecord row in rows)
                foreach (WaveRecord.PlacementRow p in row.Placements)
                    if (p.IsBuild) n++;
            return n;
        }

        private static int Sold(List<WaveRecord> rows)
        {
            int n = 0;
            foreach (WaveRecord row in rows)
                foreach (WaveRecord.PlacementRow p in row.Placements)
                    if (p.Kind == WaveRecord.PlacementRow.SellTower || p.Kind == WaveRecord.PlacementRow.SellWall) n++;
            return n;
        }
    }
}
