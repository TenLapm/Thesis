using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // The study's primary metric (gate G5), as ARCHITECTURE.md §5.9 defines it.
    // Most tests write the rows by hand, so they say what the number means and do
    // not move when the game is rebalanced.
    public class PlacementEntropyTests
    {
        // 12 x 12 open tiles; with the default 6 x 6 partition a region is 2 x 2
        // tiles and region (i, j) holds the tile (2i + 1, 2j + 1). The core is (0,0).
        private static MapData Open12() { return TestMaps.Open(12, 12, 24f, 24f, 1f); }

        private static double LnShare(params int[] counts)
        {
            int total = 0;
            foreach (int c in counts) total += c;
            double h = 0.0;
            foreach (int c in counts) if (c > 0) h -= (double)c / total * Math.Log((double)c / total);
            return h;
        }

        // ---- what the number means ----

        [Test]
        public void NothingBuiltScoresZero()
        {
            List<WaveRecord> rows = TestRows.List(TestRows.Row(0), TestRows.Row(1), TestRows.Row(2));
            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 2));
            Assert.AreEqual(0.0, PlacementEntropy.Compute(new List<WaveRecord>(), Open12(), 5));
        }

        [Test]
        public void EverythingInOneRegionScoresZero()
        {
            List<WaveRecord> rows = TestRows.List(
                TestRows.Row(1, TestRows.Wall(0, 1, 1, 1), TestRows.Tower(1, 0)),
                TestRows.Row(2, TestRows.Tower(1, 1)));

            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 2));
        }

        [Test]
        public void AnEvenSpreadOverEveryRegionScoresOne()
        {
            var towers = new List<WaveRecord.PlacementRow>();
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    towers.Add(TestRows.Tower(2 * i + 1, 2 * j + 1));

            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, towers.ToArray()));
            Assert.AreEqual(1.0, PlacementEntropy.Compute(rows, Open12(), 1), 1e-12);
        }

        [Test]
        public void TwoRegionsEquallyScoreLnTwoOverLnOfTheRegionCount()
        {
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, TestRows.Tower(1, 1), TestRows.Tower(3, 1)));

            Assert.AreEqual(0.193426403617, PlacementEntropy.Compute(rows, Open12(), 1), 1e-11);
            Assert.AreEqual(Math.Log(2.0) / Math.Log(36.0), PlacementEntropy.Compute(rows, Open12(), 1), 1e-12);
        }

        [Test]
        public void TilesAreCountedNotPieces()
        {
            // A four-tile wall piece in one region and a tower in another: 4 to 1, not 1 to 1.
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, TestRows.Wall(0, 1, 1, 1, 1, 0, 0, 0), TestRows.Tower(11, 11)));

            Assert.AreEqual(LnShare(4, 1) / Math.Log(36.0), PlacementEntropy.Compute(rows, Open12(), 1), 1e-12);
        }

        [Test]
        public void APieceAcrossARegionLineCountsInBoth()
        {
            // Tiles (1,1) and (2,1): regions (0,0) and (1,0).
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, TestRows.Wall(1, 1, 2, 1)));
            Assert.AreEqual(Math.Log(2.0) / Math.Log(36.0), PlacementEntropy.Compute(rows, Open12(), 1), 1e-12);
        }

        [Test]
        public void TheNumberIsTheSameTextOnEveryRuntime()
        {
            // Uneven shares, so every logarithm is of an awkward number. Both test
            // runners check this string: that is the cross-runtime check.
            var placed = new List<WaveRecord.PlacementRow>();
            int[] counts = { 1, 2, 3, 5, 7 };
            for (int region = 0; region < counts.Length; region++)
                for (int n = 0; n < counts[region]; n++)
                    placed.Add(TestRows.Tower(2 * region + 1, 1));
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, placed.ToArray()));

            double h = PlacementEntropy.Compute(rows, Open12(), 1);
            Assert.AreEqual(LnShare(counts) / Math.Log(36.0), h, 1e-12);
            Assert.AreEqual("0.398056898361", Json.FormatDouble(h));
        }

        // ---- what is counted ----

        [Test]
        public void TheBuildPhaseBeforeTheFirstWaveIsNotCounted()
        {
            // Nothing in row 0 can be an answer to a wave: none had been seen.
            List<WaveRecord> spreadBefore = TestRows.List(
                TestRows.Row(0, TestRows.Tower(1, 1), TestRows.Tower(5, 5), TestRows.Tower(9, 9)),
                TestRows.Row(1, TestRows.Tower(3, 3)));

            Assert.AreEqual(0.0, PlacementEntropy.Compute(spreadBefore, Open12(), 1), "only the one tower of wave 1 counts");
        }

        [Test]
        public void OnlyTheFirstWavesOfTheWindowAreCounted()
        {
            List<WaveRecord> rows = TestRows.List(
                TestRows.Row(0),
                TestRows.Row(1, TestRows.Tower(1, 1)),
                TestRows.Row(2, TestRows.Tower(3, 1)),
                TestRows.Row(3, TestRows.Tower(5, 1)));
            double ln36 = Math.Log(36.0);

            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 0));
            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 1));
            Assert.AreEqual(Math.Log(2.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 2), 1e-12);
            Assert.AreEqual(Math.Log(3.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 3), 1e-12);
            Assert.AreEqual(Math.Log(3.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 99), 1e-12, "a window longer than the session takes what there is");
        }

        [Test]
        public void SellingAndRerollingBuildNothingAndASoldTileStillCounts()
        {
            List<WaveRecord> built = TestRows.List(TestRows.Row(1, TestRows.Wall(1, 1, 3, 1), TestRows.Tower(5, 1)));
            List<WaveRecord> builtThenSold = TestRows.List(
                TestRows.Row(1, TestRows.Wall(1, 1, 3, 1), TestRows.Tower(5, 1), TestRows.Reroll()),
                TestRows.Row(2, TestRows.SoldWall(1, 1, 3, 1), TestRows.SoldTower(5, 1), TestRows.SoldTower(9, 9)));

            double h = PlacementEntropy.Compute(built, Open12(), 1);
            Assert.AreEqual(Math.Log(3.0) / Math.Log(36.0), h, 1e-12);
            Assert.AreEqual(h, PlacementEntropy.Compute(builtThenSold, Open12(), 2), "the metric is about where the player chose to build");
        }

        [Test]
        public void WallsAndTowersCanBeLookedAtApart()
        {
            // Walls in two regions, towers in three others.
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1,
                TestRows.Wall(1, 1), TestRows.Wall(3, 1),
                TestRows.Tower(5, 1), TestRows.Tower(7, 1), TestRows.Tower(9, 1)));
            double ln36 = Math.Log(36.0);

            Assert.AreEqual(Math.Log(5.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 1), 1e-12);
            Assert.AreEqual(Math.Log(5.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 1, PlacementEntropy.DefaultPartition, PlacementScope.WallsAndTowers), 1e-12);
            Assert.AreEqual(Math.Log(2.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 1, PlacementEntropy.DefaultPartition, PlacementScope.WallsOnly), 1e-12);
            Assert.AreEqual(Math.Log(3.0) / ln36, PlacementEntropy.Compute(rows, Open12(), 1, PlacementEntropy.DefaultPartition, PlacementScope.TowersOnly), 1e-12);
        }

        [Test]
        public void ItDoesNotMatterInWhichWaveOfTheWindowATileWasBuilt()
        {
            List<WaveRecord> early = TestRows.List(TestRows.Row(1, TestRows.Tower(1, 1), TestRows.Tower(3, 1), TestRows.Tower(3, 3)), TestRows.Row(2));
            List<WaveRecord> late = TestRows.List(TestRows.Row(1, TestRows.Tower(3, 3)), TestRows.Row(2, TestRows.Tower(3, 1), TestRows.Tower(1, 1)));

            Assert.AreEqual(PlacementEntropy.Compute(early, Open12(), 2), PlacementEntropy.Compute(late, Open12(), 2));
        }

        // ---- regions ----

        [Test]
        public void ATileIsInTheRegionWholeNumberDivisionPutsItIn()
        {
            MapData scene = TestSims.SampleSceneMap();      // 38 x 38: 6 * x / 38

            Assert.AreEqual(0, PlacementEntropy.RegionOf(0, 0, scene));
            Assert.AreEqual(0, PlacementEntropy.RegionOf(6, 6, scene));
            Assert.AreEqual(6, PlacementEntropy.RegionOf(7, 0, scene), "x = 7 is the second column of regions");
            Assert.AreEqual(1, PlacementEntropy.RegionOf(0, 7, scene), "y = 7 is the second row");
            Assert.AreEqual(35, PlacementEntropy.RegionOf(37, 37, scene));
            Assert.AreEqual(15, PlacementEntropy.RegionOf(37, 37, scene, 4));
            Assert.AreEqual(0, PlacementEntropy.RegionOf(37, 37, scene, 1));
        }

        [Test]
        public void EveryTileIsInOneRegionAndTheRegionsAreNearlyTheSameSize()
        {
            MapData scene = TestSims.SampleSceneMap();
            var size = new int[36];
            for (int x = 0; x < scene.Width; x++)
                for (int y = 0; y < scene.Height; y++)
                    size[PlacementEntropy.RegionOf(x, y, scene)]++;

            int total = 0, min = int.MaxValue, max = 0;
            foreach (int n in size)
            {
                total += n;
                min = Math.Min(min, n);
                max = Math.Max(max, n);
            }
            Assert.AreEqual(38 * 38, total);
            Assert.AreEqual(36, min, "38 does not divide by 6: a region is 6 or 7 tiles on a side");
            Assert.AreEqual(49, max);
        }

        [Test]
        public void TheRegionCountIsTheRegionsAPlayerCanBuildIn()
        {
            Assert.AreEqual(36, PlacementEntropy.Regions(Open12()));
            Assert.AreEqual(36, PlacementEntropy.Regions(TestSims.SampleSceneMap()));
            Assert.AreEqual(16, PlacementEntropy.Regions(TestSims.SampleSceneMap(), 4));
            Assert.AreEqual(64, PlacementEntropy.Regions(TestSims.SampleSceneMap(), 8));

            // 6 x 6 tiles cut 6 x 6: every tile is its own region. Three static
            // blockers, the spawn and the core can never be built on.
            MapData map = AsciiMap.Parse(@"
                . . . . . .
                . S . . X .
                . . . . X .
                . . . . X .
                . . . . C .
                . . . . . .").Map;
            Assert.AreEqual(31, PlacementEntropy.Regions(map, 6));
            Assert.AreEqual(9, PlacementEntropy.Regions(map, 3), "a 2 x 2 region with one open tile still counts");
        }

        [Test]
        public void AnEvenSpreadOverEverythingBuildableScoresOneWhateverIsBlocked()
        {
            MapData map = AsciiMap.Parse(@"
                . . . . . .
                . S . . X .
                . . . . X .
                . . . . X .
                . . . . C .
                . . . . . .").Map;

            var towers = new List<WaveRecord.PlacementRow>();
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 6; y++)
                    if (map.IsWalkable(x, y) && !Placement.IsProtected(map, new TileCoord(x, y))) towers.Add(TestRows.Tower(x, y));
            Assert.AreEqual(31, towers.Count);

            Assert.AreEqual(1.0, PlacementEntropy.Compute(TestRows.List(TestRows.Row(1, towers.ToArray())), map, 1, 6), 1e-12);
        }

        [Test]
        public void TheSameTilesScoreDifferentlyOnACoarserOrFinerPartition()
        {
            // (1,1) and (2,1): one region on a 4 x 4 cut of 12 tiles, two on 6 x 6.
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1, TestRows.Tower(1, 1), TestRows.Tower(2, 1)));

            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 1, 4));
            Assert.AreEqual(Math.Log(2.0) / Math.Log(36.0), PlacementEntropy.Compute(rows, Open12(), 1, 6), 1e-12);
            // Cut 12 x 12, every tile is a region, and the core's cannot be built on: 143.
            Assert.AreEqual(Math.Log(2.0) / Math.Log(143.0), PlacementEntropy.Compute(rows, Open12(), 1, 12), 1e-12);
            Assert.AreEqual(0.0, PlacementEntropy.Compute(rows, Open12(), 1, 1), "one region: nothing to be spread over");
        }

        // ---- which waves ----

        [Test]
        public void AWaveIsCompleteWhenTheNextOneBegan()
        {
            // Its window is the fight AND the build phase after it. The last wave a
            // session reached never has both.
            Assert.AreEqual(0, PlacementEntropy.CompletedWaves(new List<WaveRecord>()));
            Assert.AreEqual(0, PlacementEntropy.CompletedWaves(TestRows.List(TestRows.Row(0))));
            Assert.AreEqual(0, PlacementEntropy.CompletedWaves(TestRows.List(TestRows.Row(0), TestRows.Row(1))));
            Assert.AreEqual(2, PlacementEntropy.CompletedWaves(TestRows.List(TestRows.Row(0), TestRows.Row(1), TestRows.Row(2), TestRows.Row(3))));
        }

        [Test]
        public void TwoSessionsAreCutToTheShorter()
        {
            List<WaveRecord> longer = TestRows.List(
                TestRows.Row(0),
                TestRows.Row(1, TestRows.Tower(1, 1)),
                TestRows.Row(2, TestRows.Tower(3, 1)),
                TestRows.Row(3, TestRows.Tower(5, 1)),
                TestRows.Row(4, TestRows.Tower(7, 1)),
                TestRows.Row(5, TestRows.Tower(9, 1)),
                TestRows.Row(6));
            List<WaveRecord> shorter = TestRows.List(
                TestRows.Row(0),
                TestRows.Row(1, TestRows.Tower(1, 1)),
                TestRows.Row(2, TestRows.Tower(1, 1)),
                TestRows.Row(3, TestRows.Tower(9, 9)));     // the wave this session ended in

            int window = PlacementEntropy.ComparisonWindow(longer, shorter);
            Assert.AreEqual(2, window);
            Assert.AreEqual(window, PlacementEntropy.ComparisonWindow(shorter, longer));

            Assert.AreEqual(Math.Log(2.0) / Math.Log(36.0), PlacementEntropy.Compute(longer, Open12(), window), 1e-12, "what the longer session built afterwards is not held against the shorter");
            Assert.AreEqual(0.0, PlacementEntropy.Compute(shorter, Open12(), window));
        }

        // ---- a played session ----

        [Test]
        public void APlayedSessionGivesTheNumberAPlainCountGives()
        {
            EpisodeOptions o = TestEpisodes.Small(seed: 2, waves: 6, tickHashes: false, policy: SequencePolicy.Restless(wallsPerPhase: 2, towersPerPhase: 3));
            EpisodeResult run = EpisodeRunner.Run(o);
            int waves = PlacementEntropy.CompletedWaves(run.Telemetry);
            Assert.AreEqual(5, waves, "six waves were played; the sixth has no build phase after it");

            // The same thing worked out the long way: a dictionary and Math.Log.
            const int partition = 3;                        // 12 x 6 tiles: 4 x 2 tiles a region
            var perRegion = new Dictionary<string, int>();
            int total = 0;
            foreach (WaveRecord row in run.Telemetry)
            {
                if (row.Wave < 1 || row.Wave > waves) continue;
                foreach (WaveRecord.PlacementRow p in row.Placements)
                {
                    if (!p.IsBuild) continue;
                    foreach (int[] tile in p.Tiles)
                    {
                        string key = (partition * tile[0] / o.Map.Width) + "," + (partition * tile[1] / o.Map.Height);
                        int n;
                        perRegion.TryGetValue(key, out n);
                        perRegion[key] = n + 1;
                        total++;
                    }
                }
            }
            var counts = new List<int>(perRegion.Values);
            double expected = LnShare(counts.ToArray()) / Math.Log(9.0);

            double h = PlacementEntropy.Compute(run.Telemetry, o.Map, waves, partition);
            Assert.Greater(total, 10, "rig: the player built during the waves");
            Assert.Greater(perRegion.Count, 1, "rig: in more than one region");
            Assert.AreEqual(expected, h, 1e-12);
            Assert.Greater(h, 0.0);
            Assert.Less(h, 1.0);

            // And from the replay alone, as the `entropy` command does it.
            List<WaveRecord> again = ReplayRunner.Telemetry(TestEpisodes.Reload(run.Replay));
            Assert.AreEqual(h, PlacementEntropy.Compute(again, o.Map, PlacementEntropy.CompletedWaves(again), partition));
        }

        // ---- misuse ----

        [Test]
        public void BadArgumentsAreRefused()
        {
            List<WaveRecord> rows = TestRows.List(TestRows.Row(1));

            Assert.Throws<ArgumentNullException>(() => PlacementEntropy.Compute(null, Open12(), 1));
            Assert.Throws<ArgumentNullException>(() => PlacementEntropy.Compute(rows, null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlacementEntropy.Compute(rows, Open12(), 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlacementEntropy.Regions(Open12(), 0));
            Assert.Throws<ArgumentNullException>(() => PlacementEntropy.Regions(null));
            Assert.Throws<ArgumentNullException>(() => PlacementEntropy.CompletedWaves(null));
        }
    }
}
