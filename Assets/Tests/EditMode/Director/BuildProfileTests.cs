using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP8a: the build profile and its three cheap features (ARCHITECTURE.md §5.3, the
    // formulas the student decided on 2026-11-10). Each fixture is a small ASCII board, so a
    // test says what the number means and does not move when the game is rebalanced.
    public class BuildProfileTests
    {
        private const float Eps = 1e-5f;

        // The board as written: '#' walls are on the grid, the ground field is built, and the
        // empty board is the same map with nothing built (rocks impassable, the C# default).
        private static ProfileInput Input(string ascii, IReadOnlyList<TowerState> towers = null, float[] damageByType = null)
        {
            AsciiFixture f = AsciiMap.Parse(ascii);
            new FlowField().Generate(f.Grid, f.Map.Core);
            return new ProfileInput(f.Grid, f.Map.Spawns, EmptyBoard.For(f.Map, new SimConfig()), towers ?? new TowerState[0], damageByType);
        }

        private static TowerState Tower(int id, int x, int y, TowerDef def = null)
        {
            return new TowerState(id, def ?? TestTowers.Gun(), new TileCoord(x, y), new Vec2f(x, y), 1f, 0);
        }

        // ---- maze_length ----

        private const string OpenCorridor = @"
            S . . . . . C";

        [Test]
        public void AnOpenBoardHasAMazeLengthOfOne()
        {
            FeatureValue v = new MazeLength().Compute(Input(@"
                S . . . . .
                . . . . . .
                . . . . . .
                . . . . . C"));
            Assert.IsTrue(v.HasData);
            Assert.AreEqual(1f, v.Value, Eps);
        }

        [Test]
        public void ADetourAroundAWallLengthensTheMaze()
        {
            // The wall leaves the top row open: walking round it is cheaper than digging.
            FeatureValue v = new MazeLength().Compute(Input(@"
                . . . . . . .
                . . . # . . .
                S . . # . . C
                . . . # . . .
                X X X X X X X"));
            Assert.Greater(v.Value, 1f);
        }

        [Test]
        public void AWallAcrossTheOnlyCorridorLengthensTheMazeByItsDigCost()
        {
            // Enemies dig through, and digging is priced as distance (the field's cost model).
            FeatureValue v = new MazeLength().Compute(Input(@"
                S . . # . . C"));
            Assert.Greater(v.Value, 1f);
        }

        [Test]
        public void EveryCandidateEntryCountsNotOnlyTheOnesAWaveUses()
        {
            // Two entries the same distance from the core; a wall in front of the top one only.
            // The value is the mean of the two ratios, (top + 1) / 2.
            ProfileInput input = Input(@"
                S . . # . . .
                X X X X X X .
                . . . . . . C
                X X X X X X .
                S . . . . . .");
            float top = (float)(ProfileInput.Tiles(input.Grid.Get(input.Spawns[0]).BestCost) / input.Empty.Tiles(0));
            float bottom = (float)(ProfileInput.Tiles(input.Grid.Get(input.Spawns[1]).BestCost) / input.Empty.Tiles(1));
            Assert.AreEqual(1f, bottom, Eps, "the bottom entry is open");
            Assert.Greater(top, 1f);

            Assert.AreEqual((top + bottom) / 2f, new MazeLength().Compute(input).Value, Eps);
        }

        [Test]
        public void TheEmptyBoardIsInTilesNotFieldUnits()
        {
            // The regression guard for "divide BestCost by 10": six steps east are 6 tiles, not 60.
            ProfileInput input = Input(OpenCorridor);
            Assert.AreEqual(6f, input.Empty.Tiles(0), Eps);
            Assert.AreEqual(6f, ProfileInput.Tiles(input.Grid.Get(input.Spawns[0]).BestCost), Eps);
        }

        [Test]
        public void RocksCountAsPartOfTheBaseWhenTheyCanBeDug()
        {
            // The student, 2026-11-10: the player chose where the core stands, so the rocks round
            // it are the player's base. With breakable rocks the empty board has no rocks, and a
            // rock between the entry and the core lengthens the maze. With RockHealth 0 a rock is
            // map geometry, on the empty board as well, and changes nothing.
            const string board = @"
                . . . X . . .
                S . . X . . C
                . . . X . . .
                . . . . . . .";

            var breakable = new SimConfig { RockHealth = 60f };
            Simulation dug = TestSims.Ascii(board, new EscalationPlanner(breakable, AsciiMap.Parse(board).Map), breakable);
            ProfileInput withRocks = ProfileInput.From(dug.State, dug.Map, EmptyBoard.For(dug.Map, breakable));
            Assert.Greater(new MazeLength().Compute(withRocks).Value, 1f);

            var solid = new SimConfig();
            Simulation map = TestSims.Ascii(board, new EscalationPlanner(solid, AsciiMap.Parse(board).Map), solid);
            ProfileInput geometry = ProfileInput.From(map.State, map.Map, EmptyBoard.For(map.Map, solid));
            Assert.AreEqual(1f, new MazeLength().Compute(geometry).Value, Eps);
        }

        // ---- tower_concentration ----

        // The same built area in both cases (walls in the four corners), so only the towers differ.
        private const string Area = @"
            # . . . . . . #
            . . . . . . . .
            . . . . . . . .
            . . . . . . . .
            . . . . . . . .
            # . . . . . . C";

        [Test]
        public void AdjacentTowersAreMoreConcentratedThanTowersInOppositeCorners()
        {
            float together = new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 3, 2), Tower(2, 4, 2) })).Value;
            float apart = new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 1, 1), Tower(2, 6, 4) })).Value;
            Assert.Greater(together, apart);
        }

        [Test]
        public void ConcentrationIsMeasuredAgainstTheAreaThePlayerBuiltIn()
        {
            // The same two towers, one tile apart. In a small base they are spread out; in a base
            // spanning the whole board they are concentrated.
            TowerState[] pair = { Tower(1, 1, 1), Tower(2, 3, 1) };
            float small = new TowerConcentration().Compute(Input(@"
                . . . . . . . .
                . . . . . . . .
                . . . . . . . .
                . . . . . . . .
                . . . . . . . .
                . . . . . . . C", pair)).Value;
            float large = new TowerConcentration().Compute(Input(Area, pair)).Value;
            Assert.Greater(large, small);
        }

        [Test]
        public void TwoTowersAloneAtTheEndsOfTheirAreaScoreOneMinusHalfTheirDistanceOverTheDiagonal()
        {
            // Box 4 x 1 tiles, diagonal √17; each tower is 1.5 tiles from the centroid.
            float v = new TowerConcentration().Compute(Input(@"
                . . . . . C", new[] { Tower(1, 0, 0), Tower(2, 3, 0) })).Value;
            Assert.AreEqual(1f - 1.5f / (float)System.Math.Sqrt(17.0), v, Eps);
        }

        [Test]
        public void OneTowerIsFullyConcentrated()
        {
            Assert.AreEqual(1f, new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 3, 3) })).Value, Eps);
        }

        [Test]
        public void NoTowersIsNoData()
        {
            FeatureValue v = new TowerConcentration().Compute(Input(Area));
            Assert.IsFalse(v.HasData);
            Assert.AreEqual(FeatureValue.NoDataValue, v.Value);
        }

        [Test]
        public void PylonsAndDestroyedTowersDoNotCountButKnockedOutTowersDo()
        {
            TowerState pylon = Tower(3, 6, 4);
            pylon.PowerCapacity = 2;
            TowerState destroyed = Tower(4, 1, 4);
            destroyed.IsAlive = false;
            TowerState knockedOut = Tower(5, 4, 2);
            knockedOut.KnockedOut = true;

            float pairOnly = new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 3, 2), Tower(2, 4, 2) })).Value;
            float withPylonAndWreck = new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 3, 2), Tower(2, 4, 2), pylon, destroyed })).Value;
            Assert.AreEqual(pairOnly, withPylonAndWreck, Eps);

            float withKnockedOut = new TowerConcentration().Compute(Input(Area, new[] { Tower(1, 3, 2), Tower(2, 4, 2), knockedOut })).Value;
            Assert.AreNotEqual(pairOnly, withKnockedOut);
        }

        // ---- damage_type_mix ----

        [Test]
        public void AllDamageOfOneTypeHasNoMix()
        {
            FeatureValue v = new DamageTypeMix().Compute(Input(OpenCorridor, null, new[] { 0f, 120f, 0f }));
            Assert.IsTrue(v.HasData);
            Assert.AreEqual(0f, v.Value, Eps);
        }

        [Test]
        public void EqualDamageOfEveryTypeIsAFullMix()
        {
            Assert.AreEqual(1f, new DamageTypeMix().Compute(Input(OpenCorridor, null, new[] { 40f, 40f, 40f })).Value, Eps);
        }

        [Test]
        public void TwoTypesHalfAndHalfScoreLnTwoOverLnThree()
        {
            float expected = (float)(System.Math.Log(2.0) / System.Math.Log(3.0));
            Assert.AreEqual(expected, new DamageTypeMix().Compute(Input(OpenCorridor, null, new[] { 50f, 0f, 50f })).Value, Eps);
        }

        [Test]
        public void BeforeTheFirstWaveOrAfterAWaveWithNoDamageThereIsNoData()
        {
            FeatureValue none = new DamageTypeMix().Compute(Input(OpenCorridor));
            Assert.IsFalse(none.HasData);
            Assert.AreEqual(0.5f, none.Value);

            FeatureValue zero = new DamageTypeMix().Compute(Input(OpenCorridor, null, new[] { 0f, 0f, 0f }));
            Assert.IsFalse(zero.HasData);
            Assert.AreEqual(0.5f, zero.Value);
        }

        // ---- the profile ----

        [Test]
        public void TheProfileHoldsTheFeaturesInAFixedOrderWithTheirDataFlags()
        {
            BuildProfile p = BuildProfiler.Default().Profile(Input(Area, new[] { Tower(1, 3, 2) }));

            Assert.AreEqual(BuildProfile.SchemaVersion, p.Schema);
            CollectionAssert.AreEqual(new[] { "maze_length", "tower_concentration", "damage_type_mix" }, p.Names);
            Assert.AreEqual(p.Names.Length, p.Values.Length);
            Assert.AreEqual(p.Names.Length, p.HasData.Length);

            Assert.AreEqual(1f, p["tower_concentration"], Eps);
            Assert.IsTrue(p.Has("tower_concentration"));
            Assert.IsFalse(p.Has("damage_type_mix"), "no wave has been played");
            Assert.AreEqual(0.5f, p["damage_type_mix"]);
        }

        [Test]
        public void TwoFeaturesWithTheSameNameAreRefused()
        {
            Assert.Throws<System.ArgumentException>(() => new BuildProfiler(new IBuildFeature[] { new MazeLength(), new MazeLength() }));
        }

        [Test]
        public void AfterAWaveTheProfileReadsTheDamageTheTowersDealt()
        {
            Simulation sim = TestSims.Open38(seed: 3);
            TestTowers.BuyAny(sim, 18, 18);
            TestTowers.BuyAny(sim, 20, 20);
            TestSims.RunUntilWaveResolves(sim);

            float[] dealt = sim.State.LastOutcome.DamageByType;
            double total = 0.0, h = 0.0;
            foreach (float d in dealt) total += d;
            Assume.That(total, Is.GreaterThan(0.0), "rig: the towers must hit something");
            foreach (float d in dealt) if (d > 0f) h -= d / total * System.Math.Log(d / total);

            BuildProfile p = BuildProfiler.Default().Profile(ProfileInput.From(sim.State, sim.Map, EmptyBoard.For(sim.Map, sim.Config)));
            Assert.IsTrue(p.Has("damage_type_mix"));
            Assert.AreEqual((float)(h / System.Math.Log(3.0)), p["damage_type_mix"], 1e-4f);
            Assert.IsTrue(p.Has("tower_concentration"));
        }

        [Test]
        public void ShopOffersThePlayerDidNotBuyDoNotChangeTheProfile()
        {
            // The shop confound rule (CLAUDE.md §2): a feature describes the board the player
            // chose, never what the roll happened to offer.
            Simulation sim = TestSims.Open38(seed: 5);
            TestTowers.BuyAny(sim, 18, 18);
            EmptyBoard empty = EmptyBoard.For(sim.Map, sim.Config);
            BuildProfile before = BuildProfiler.Default().Profile(ProfileInput.From(sim.State, sim.Map, empty));

            sim.State.Shop.Offers = new TowerOffer[0];
            sim.State.Shop.RerollCount += 3;
            BuildProfile after = BuildProfiler.Default().Profile(ProfileInput.From(sim.State, sim.Map, empty));

            CollectionAssert.AreEqual(before.Values, after.Values);
            CollectionAssert.AreEqual(before.HasData, after.HasData);
        }
    }
}
