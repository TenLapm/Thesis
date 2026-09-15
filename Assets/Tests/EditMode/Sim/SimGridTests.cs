using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class SimGridTests
    {
        private static string Coords(SimNode[] buffer, int count)
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++) parts[i] = "(" + buffer[i].X + "," + buffer[i].Y + ")";
            return string.Join(" ", parts);
        }

        [Test]
        public void NeighbourOrderIsXOuterThenYInner()
        {
            // SPFA tie-breaks depend on this exact order (see SimGrid.GetNeighbors).
            var f = TestMaps.Parse(@"
                . . .
                . . .
                C . .");
            var buffer = new SimNode[8];
            int count = f.Grid.GetNeighbors(f.Grid[1, 1], buffer);

            Assert.AreEqual("(0,0) (0,1) (0,2) (1,0) (1,2) (2,0) (2,1) (2,2)", Coords(buffer, count));
        }

        [Test]
        public void DiagonalIsBlockedWhenEitherCornerHasAWall()
        {
            var f = TestMaps.Parse(@"
                . . .
                # . .
                C . .");
            var buffer = new SimNode[8];
            int count = f.Grid.GetNeighbors(f.Grid[0, 0], buffer);

            // (0,1) is a wall entered head-on: allowed (that's how digging starts).
            // (1,1) would squeeze past the wall's corner: not allowed.
            Assert.AreEqual("(0,1) (1,0)", Coords(buffer, count));
        }

        [Test]
        public void DiagonalIsBlockedByStaticGeometryToo()
        {
            var f = TestMaps.Parse(@"
                . . .
                . . .
                C X .");
            var buffer = new SimNode[8];
            int count = f.Grid.GetNeighbors(f.Grid[0, 0], buffer);

            // GetNeighbors only filters bounds and corners; unwalkable tiles are
            // skipped later by the flow field, exactly as in the original.
            Assert.AreEqual("(0,1) (1,0)", Coords(buffer, count));
        }

        [Test]
        public void DiagonalBetweenTwoOpenCornersIsAllowed()
        {
            var f = TestMaps.Parse(@"
                . .
                C .");
            var buffer = new SimNode[8];
            int count = f.Grid.GetNeighbors(f.Grid[0, 0], buffer);

            Assert.AreEqual("(0,1) (1,0) (1,1)", Coords(buffer, count));
        }

        [Test]
        public void NeighbourBufferMustHoldEight()
        {
            var f = TestMaps.Parse("C .");
            Assert.Throws<ArgumentException>(() => f.Grid.GetNeighbors(f.Grid[0, 0], new SimNode[7]));
        }

        [Test]
        public void TileCentresMatchCreateGridFormula()
        {
            // SampleScene: gridWorldSize 75, radius 1 -> 38x38 (RoundToInt(37.5) = 38).
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));

            // worldBottomLeft.x = -37.5; centre x = -37.5 + (x * 2 + 1)
            Assert.AreEqual(-36.5f, grid[0, 0].Position.X);
            Assert.AreEqual(-36.5f, grid[0, 0].Position.Y);
            Assert.AreEqual(37.5f, grid[37, 37].Position.X);
            Assert.AreEqual(1.5f, grid[19, 0].Position.X);
        }

        [Test]
        public void EveryTileCentreMapsBackToItsOwnTile()
        {
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));
            for (int x = 0; x < 38; x++)
            {
                for (int y = 0; y < 38; y++)
                {
                    SimNode n = grid.NodeFromPosition(grid[x, y].Position);
                    Assert.AreEqual(x, n.X, "x of tile (" + x + "," + y + ")");
                    Assert.AreEqual(y, n.Y, "y of tile (" + x + "," + y + ")");
                }
            }
        }

        [Test]
        public void NodeFromPositionKeepsTheOriginalBoundarySkew()
        {
            // Hand-computed from NodeFromWorldPoint with gridWorldSize 75, 38 tiles:
            //   index = round(37 * (wx + 37.5) / 75)
            // Tile 0's centre is -36.5, but tile 1 already starts at wx ~ -36.4865,
            // i.e. only 0.0135 past tile 0's centre - not at the midpoint (-35.5).
            // This is the original game's behaviour and is kept on purpose.
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));

            Assert.AreEqual(0, grid.NodeFromPosition(new Vec2f(-36.5f, 0f)).X);
            Assert.AreEqual(1, grid.NodeFromPosition(new Vec2f(-36.4f, 0f)).X);
            Assert.AreEqual(1, grid.NodeFromPosition(new Vec2f(-35.5f, 0f)).X);

            // Near the high edge the boundary sits close to the midpoint instead.
            Assert.AreEqual(36, grid.NodeFromPosition(new Vec2f(36.4f, 0f)).X);
            Assert.AreEqual(37, grid.NodeFromPosition(new Vec2f(36.6f, 0f)).X);
        }

        [Test]
        public void NodeFromPositionClampsOffMapPositions()
        {
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));
            SimNode low = grid.NodeFromPosition(new Vec2f(-1000f, -1000f));
            SimNode high = grid.NodeFromPosition(new Vec2f(1000f, 1000f));

            Assert.AreEqual(new TileCoord(0, 0), new TileCoord(low.X, low.Y));
            Assert.AreEqual(new TileCoord(37, 37), new TileCoord(high.X, high.Y));
        }

        [Test]
        public void SetWallValidatesItsInputs()
        {
            var f = TestMaps.Parse("C . X");
            Assert.Throws<InvalidOperationException>(() => f.Grid.SetWall(f.Grid[2, 0], 15, 6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Grid.SetWall(f.Grid[1, 0], 1, 6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Grid.SetWall(f.Grid[1, 0], 15, 0f));

            f.Grid.SetWall(f.Grid[1, 0], 15, 6f);
            Assert.IsTrue(f.Grid[1, 0].HasWall);
            Assert.AreEqual(6f, f.Grid[1, 0].MaxWallHealth);

            f.Grid.ClearWall(f.Grid[1, 0]);
            Assert.IsFalse(f.Grid[1, 0].HasWall);
            Assert.AreEqual(1, f.Grid[1, 0].TerrainCost);
        }

        [Test]
        public void CloneIsIndependent()
        {
            var f = TestMaps.Parse(@"
                . . .
                C # .");
            new FlowField().Generate(f.Grid, f.Map.Core);

            SimGrid clone = f.Grid.Clone();
            Assert.AreEqual(f.Grid[2, 0].BestCost, clone[2, 0].BestCost);
            Assert.AreEqual(f.Grid[2, 0].NextIndex, clone[2, 0].NextIndex);
            Assert.AreEqual(f.Grid.FieldVersion, clone.FieldVersion);

            clone.ClearWall(clone[1, 0]);
            clone[2, 0].BestCost = 12345;

            Assert.IsTrue(f.Grid[1, 0].HasWall, "original wall must survive a change to the clone");
            Assert.AreNotEqual(12345, f.Grid[2, 0].BestCost);
        }
    }
}
