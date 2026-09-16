using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class PlacementTests
    {
        // Layout (see AsciiMap.Parse doc for orientation):
        //   y=5  . . . . . .
        //   y=4  . . . . . .
        //   y=3  . . X . . .
        //   y=2  S . # . . C
        //   y=1  . . . . . .
        //   y=0  . . . . . .
        private const string Map = @"
            . . . . . .
            . . . . . .
            . . X . . .
            S . # . . C
            . . . . . .
            . . . . . .";

        // A 2-tile domino, origin + one step in +x. Rotating (x,y) -> (y,-x):
        //   turns 0: (0,0)(1,0)   [east]
        //   turns 1: (0,0)(0,-1)  [south]
        //   turns 2: (0,0)(-1,0)  [west]
        //   turns 3: (0,0)(0,1)   [north]
        private static ShapeDef Domino()
        {
            return new ShapeDef
            {
                Name = "Domino",
                BuildCost = 4,
                DigCost = 15,
                WallHealth = 6f,
                LocalTiles = new[] { new TileCoord(0, 0), new TileCoord(1, 0) },
            };
        }

        private static ShapeDef Rotated(int turns)
        {
            ShapeDef s = Domino();
            for (int i = 0; i < turns; i++) s.Rotate();
            return s;
        }

        public static IEnumerable<TestCaseData> LegalityCases()
        {
            yield return new TestCaseData(0, new TileCoord(5, 5), false).SetName("OffMap_t0");
            yield return new TestCaseData(1, new TileCoord(2, 4), false).SetName("StaticBlocker_t1");
            yield return new TestCaseData(2, new TileCoord(3, 2), false).SetName("ExistingWall_t2");
            yield return new TestCaseData(3, new TileCoord(0, 1), false).SetName("Spawn_t3");
            yield return new TestCaseData(0, new TileCoord(4, 2), false).SetName("Core_t0");
            yield return new TestCaseData(1, new TileCoord(3, 4), true).SetName("Valid_t1");
        }

        [TestCaseSource(nameof(LegalityCases))]
        public void CanPlaceCoversEveryCategoryAcrossAllFourRotations(int turns, TileCoord origin, bool expected)
        {
            var f = TestMaps.Parse(Map);
            ShapeDef shape = Rotated(turns);
            TileCoord[] tiles = Placement.ToWorldTiles(shape.LocalTiles, origin);

            Assert.AreEqual(expected, Placement.CanPlace(f.Grid, f.Map, tiles), "turns=" + turns + " origin=" + origin + " tiles=" + string.Join(" ", tiles));
        }

        [Test]
        public void CanPlaceRejectsEveryProtectedTileNotJustTheFirstSpawn()
        {
            var f = TestMaps.Parse(@"
                . . .
                S . S
                . C .");
            Assert.IsTrue(f.Map.Spawns.Length == 2, "fixture must have two spawns");

            foreach (TileCoord spawn in f.Map.Spawns)
                Assert.IsFalse(Placement.CanPlace(f.Grid, f.Map, new[] { spawn }), "spawn " + spawn + " must be protected");
        }

        [Test]
        public void TryPlaceSpendsBudgetSetsTerrainAndRebuildsTheField()
        {
            var f = TestMaps.Parse(Map);
            ShapeDef shape = Domino();
            float budget = 10f;
            var log = new List<PlacementRecord>();
            var events = new List<SimEvent>();
            var field = new FlowField();
            field.Generate(f.Grid, f.Map.Core);
            int versionBefore = f.Grid.FieldVersion;

            bool placed = Placement.TryPlace(f.Grid, f.Map, field, shape, rotationTurns: 0, origin: new TileCoord(3, 4),
                ref budget, tick: 42, log, events);

            Assert.IsTrue(placed);
            Assert.AreEqual(6f, budget, "10 - buildCost(4)");
            Assert.Greater(f.Grid.FieldVersion, versionBefore, "placement must rebuild the field");

            SimNode a = f.Grid[3, 4];
            SimNode b = f.Grid[4, 4];
            Assert.IsTrue(a.HasWall);
            Assert.IsTrue(b.HasWall);
            Assert.AreEqual(15, a.TerrainCost);
            Assert.AreEqual(6f, a.MaxWallHealth);

            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(42, log[0].Tick);
            Assert.AreEqual("Domino", log[0].ShapeName);
            Assert.AreEqual(3, log[0].OriginX);
            Assert.AreEqual(4, log[0].OriginY);
            CollectionAssert.AreEqual(new[] { new TileCoord(3, 4), new TileCoord(4, 4) }, log[0].Tiles);

            int wallPlacedEvents = 0;
            foreach (SimEvent e in events) if (e.Kind == SimEventKind.WallPlaced) wallPlacedEvents++;
            Assert.AreEqual(2, wallPlacedEvents, "one WallPlaced event per tile");
        }

        [Test]
        public void TryPlaceClampsLowDigCostAndWallHealthToTheirFloors()
        {
            // Mirrors PlayerBuilder.HandlePlacement: Mathf.Max(2, digCost) and
            // Mathf.Max(0.5f, wallHealth), so a misconfigured near-zero shape can
            // never become a free pass-through tile or an instant breach.
            var f = TestMaps.Parse(Map);
            var shape = new ShapeDef { Name = "Weak", BuildCost = 1, DigCost = 0, WallHealth = 0f, LocalTiles = new[] { new TileCoord(0, 0) } };
            float budget = 10f;
            var field = new FlowField();
            field.Generate(f.Grid, f.Map.Core);

            Placement.TryPlace(f.Grid, f.Map, field, shape, 0, new TileCoord(3, 4), ref budget, 0, null, null);

            SimNode node = f.Grid[3, 4];
            Assert.AreEqual(2, node.TerrainCost);
            Assert.AreEqual(0.5f, node.WallHealth);
        }

        [Test]
        public void TryPlaceFailsWithoutSideEffectsWhenIllegalOrUnaffordable()
        {
            var f = TestMaps.Parse(Map);
            var field = new FlowField();
            field.Generate(f.Grid, f.Map.Core);

            // Illegal (lands on the static blocker).
            float budget = 100f;
            bool placedIllegal = Placement.TryPlace(f.Grid, f.Map, field, Rotated(1), 0, new TileCoord(2, 4), ref budget, 0, null, null);
            Assert.IsFalse(placedIllegal);
            Assert.AreEqual(100f, budget);

            // Legal tile, but the budget is too low.
            budget = 1f;
            var log = new List<PlacementRecord>();
            bool placedPoor = Placement.TryPlace(f.Grid, f.Map, field, Domino(), 0, new TileCoord(3, 4), ref budget, 0, log, null);
            Assert.IsFalse(placedPoor);
            Assert.AreEqual(1f, budget, "the budget must never be touched on a failed placement");
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(f.Grid[3, 4].HasWall);
        }

        [Test]
        public void BudgetNeverGoesNegativeAcrossRepeatedPlacements()
        {
            var f = TestMaps.Parse(@"
                . . . . . . . . . .
                . . . . . . . . . .
                S . . . . . . . . C");
            var field = new FlowField();
            field.Generate(f.Grid, f.Map.Core);
            float budget = 9f; // exactly two 4-cost placements, with 1 left over

            var single = new ShapeDef { Name = "Single", BuildCost = 4, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } };

            Assert.IsTrue(Placement.TryPlace(f.Grid, f.Map, field, single, 0, new TileCoord(2, 1), ref budget, 0, null, null));
            Assert.IsTrue(Placement.TryPlace(f.Grid, f.Map, field, single, 0, new TileCoord(4, 1), ref budget, 0, null, null));
            Assert.AreEqual(1f, budget);

            bool third = Placement.TryPlace(f.Grid, f.Map, field, single, 0, new TileCoord(6, 1), ref budget, 0, null, null);
            Assert.IsFalse(third);
            Assert.AreEqual(1f, budget, "must never dip below zero");
        }
    }
}
