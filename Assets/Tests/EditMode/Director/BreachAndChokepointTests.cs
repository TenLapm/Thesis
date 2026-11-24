using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP8b: the two remaining features (ARCHITECTURE.md §5.3, the student's formulas of
    // 2026-11-10). breach_vulnerability reads the Ground route; chokepoint_reliance reads how
    // last wave's damage was shared between the towers.
    public class BreachAndChokepointTests
    {
        private const float Eps = 1e-5f;

        private static ProfileInput Input(string ascii, IReadOnlyList<TowerState> towers = null, float[] damageByType = null)
        {
            AsciiFixture f = AsciiMap.Parse(ascii);
            new FlowField().Generate(f.Grid, f.Map.Core);
            return new ProfileInput(f.Grid, f.Map.Spawns, EmptyBoard.For(f.Map, new SimConfig()), towers ?? new TowerState[0], damageByType);
        }

        // Last wave happened (some damage by type), so the damage features have something to read.
        private static readonly float[] AWavePlayed = { 10f, 0f, 0f };

        private static TowerState Tower(int id, int x, int y, float waveDamage)
        {
            var t = new TowerState(id, TestTowers.Gun(), new TileCoord(x, y), new Vec2f(x, y), 1f, 0);
            t.WaveDamage = waveDamage;
            return t;
        }

        // ---- breach_vulnerability ----

        [Test]
        public void AnOpenBoardHasNoBreachVulnerability()
        {
            FeatureValue v = new BreachVulnerability().Compute(Input(@"
                S . . . . .
                . . . . . .
                . . . . . C"));
            Assert.IsTrue(v.HasData);
            Assert.AreEqual(0f, v.Value, Eps);
        }

        [Test]
        public void AWallAcrossTheOnlyCorridorIsTheShareOfTheRouteCostItCarries()
        {
            // Six steps of 10, one of them leaving the wall at 10 x 15: 150 of 200.
            FeatureValue v = new BreachVulnerability().Compute(Input(@"
                S . . # . . C"));
            Assert.AreEqual(150f / 200f, v.Value, Eps);
        }

        [Test]
        public void ADetourAroundAWallHasNoBreachVulnerabilityButALongerMaze()
        {
            const string board = @"
                . . . . . . .
                . . . # . . .
                S . . # . . C
                . . . # . . .
                X X X X X X X";
            Assert.AreEqual(0f, new BreachVulnerability().Compute(Input(board)).Value, Eps);
            Assert.Greater(new MazeLength().Compute(Input(board)).Value, 1f);
        }

        [Test]
        public void EveryCandidateEntryCountsAndTheValueIsTheMean()
        {
            // The top entry digs through the wall (0.75, as above); the bottom one walks (0).
            FeatureValue v = new BreachVulnerability().Compute(Input(@"
                S . . # . . .
                X X X X X X .
                X X X X X X C
                X X X X X X .
                S . . . . . ."));
            float top = new BreachVulnerability().Compute(Input(@"
                S . . # . . .
                X X X X X X .
                X X X X X X C")).Value;
            Assert.Greater(top, 0f);
            Assert.AreEqual(top / 2f, v.Value, Eps);
        }

        [Test]
        public void ABreakableRockOnTheRouteCountsAsDiggable()
        {
            // The student, 2026-11-10: rocks round the core are the player's base. The map must
            // leave a way round (MapData refuses a core cut off by rock), so the rock is made cheap
            // to dig: the route goes through it rather than round. With RockHealth 0 the same rock
            // is map geometry, the route walks round it, and nothing is diggable.
            // Three rocks tall, so the way round (48) costs more than digging through (30); one
            // rock alone is cheaper to pass diagonally (28) than to dig.
            const string board = @"
                . . . . . . .
                . . . X . . .
                S . . X . . C
                . . . X . . .
                X X X X X X X";
            var breakable = new SimConfig { RockHealth = 60f, RockDigCost = 2 };
            Simulation dug = TestSims.Ascii(board, new EscalationPlanner(breakable, AsciiMap.Parse(board).Map), breakable);
            ProfileInput through = ProfileInput.From(dug.State, dug.Map, EmptyBoard.For(dug.Map, breakable));
            Assert.Greater(new BreachVulnerability().Compute(through).Value, 0f);

            var solid = new SimConfig();
            Simulation round = TestSims.Ascii(board, new EscalationPlanner(solid, AsciiMap.Parse(board).Map), solid);
            ProfileInput geometry = ProfileInput.From(round.State, round.Map, EmptyBoard.For(round.Map, solid));
            Assert.AreEqual(0f, new BreachVulnerability().Compute(geometry).Value, Eps);
        }

        [Test]
        public void TheShareIsAShareOfCostNotOfTiles()
        {
            // One wall tile among six steps is a sixth of the tiles but most of the cost.
            float v = new BreachVulnerability().Compute(Input(@"
                S . . # . . C")).Value;
            Assert.Greater(v, 1f / 6f + 0.1f);
        }

        // ---- chokepoint_reliance ----

        [Test]
        public void OneTowerDoingAllTheDamageIsFullReliance()
        {
            FeatureValue v = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 1, 0, 90f), Tower(2, 2, 0, 0f), Tower(3, 3, 0, 0f) }, AWavePlayed));
            Assert.IsTrue(v.HasData);
            Assert.AreEqual(1f, v.Value, Eps);
        }

        [Test]
        public void TowersSharingTheDamageEquallyIsNoReliance()
        {
            FeatureValue v = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 1, 0, 30f), Tower(2, 2, 0, 30f), Tower(3, 3, 0, 30f) }, AWavePlayed));
            Assert.AreEqual(0f, v.Value, Eps);
        }

        [Test]
        public void TwoOfFourTowersSharingTheDamageIsHalfway()
        {
            // 1 − ln 2 / ln 4 = 0.5.
            FeatureValue v = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 1, 0, 50f), Tower(2, 2, 0, 50f), Tower(3, 3, 0, 0f), Tower(4, 4, 0, 0f) }, AWavePlayed));
            Assert.AreEqual(0.5f, v.Value, Eps);
        }

        [Test]
        public void ASingleTowerThatDealtDamageIsFullReliance()
        {
            Assert.AreEqual(1f, new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 2, 0, 40f) }, AWavePlayed)).Value, Eps);
        }

        [Test]
        public void ItIsAboutTheTowersNotWhereTheyStand()
        {
            // The same shares, the towers moved: the same value. Different from tower_concentration.
            float here = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 1, 0, 70f), Tower(2, 2, 0, 30f) }, AWavePlayed)).Value;
            float there = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 0, 0, 70f), Tower(2, 4, 0, 30f) }, AWavePlayed)).Value;
            Assert.AreEqual(here, there, Eps);
        }

        [Test]
        public void PylonsAndDestroyedTowersAreLeftOut()
        {
            TowerState pylon = Tower(3, 3, 0, 0f);
            pylon.PowerCapacity = 2;
            TowerState wreck = Tower(4, 4, 0, 0f);
            wreck.IsAlive = false;
            FeatureValue v = new ChokepointReliance().Compute(Input(@"
                S . . . . C", new[] { Tower(1, 1, 0, 30f), Tower(2, 2, 0, 30f), pylon, wreck }, AWavePlayed));
            Assert.AreEqual(0f, v.Value, Eps, "the two real towers share equally");
        }

        [Test]
        public void NoWaveNoTowersOrNoDamageIsNoData()
        {
            const string board = @"
                S . . . . C";
            FeatureValue beforeWaveOne = new ChokepointReliance().Compute(Input(board, new[] { Tower(1, 1, 0, 0f) }));
            FeatureValue noTowers = new ChokepointReliance().Compute(Input(board, null, AWavePlayed));
            FeatureValue nothingHit = new ChokepointReliance().Compute(Input(board, new[] { Tower(1, 1, 0, 0f), Tower(2, 2, 0, 0f) }, AWavePlayed));
            foreach (FeatureValue v in new[] { beforeWaveOne, noTowers, nothingHit })
            {
                Assert.IsFalse(v.HasData);
                Assert.AreEqual(FeatureValue.NoDataValue, v.Value);
            }
        }

        // ---- the full profile ----

        [Test]
        // The proposal's five in its order, then collector_exposure (2026-11-24, the raid).
        public void TheDefaultProfileHasTheFiveFeaturesInTheProposalsOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "maze_length", "breach_vulnerability", "chokepoint_reliance", "tower_concentration", "damage_type_mix", "collector_exposure" },
                BuildProfiler.Default().Names);
        }

        [Test]
        public void AfterAPlayedWaveTheDamageFeaturesHaveData()
        {
            Simulation sim = TestSims.Open38(seed: 3);
            TestTowers.BuyAny(sim, 18, 18);
            TestTowers.BuyAny(sim, 20, 20);
            TestSims.RunUntilWaveResolves(sim);

            BuildProfile p = BuildProfiler.Default().Profile(ProfileInput.From(sim.State, sim.Map, EmptyBoard.For(sim.Map, sim.Config)));
            Assume.That(sim.State.LastOutcome.DamageByType[0] + sim.State.LastOutcome.DamageByType[1] + sim.State.LastOutcome.DamageByType[2], Is.GreaterThan(0f), "rig: the towers must hit something");
            Assert.IsTrue(p.Has("chokepoint_reliance"));
            Assert.IsTrue(p.Has("breach_vulnerability"));
            for (int i = 0; i < p.Values.Length; i++)
                Assert.IsTrue(p.Values[i] >= 0f && (p.Names[i] == "maze_length" || p.Values[i] <= 1f), p.Names[i] + " = " + p.Values[i]);
        }
    }
}
