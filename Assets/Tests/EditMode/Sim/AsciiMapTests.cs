using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class AsciiMapTests
    {
        private const string Sample = @"
            . . . . X
            S # . H .
            . . . . C";

        [Test]
        public void ParseReadsTilesWithTopLineAsHighestY()
        {
            var f = TestMaps.Parse(Sample);

            Assert.AreEqual(5, f.Map.Width);
            Assert.AreEqual(3, f.Map.Height);
            Assert.IsFalse(f.Map.IsWalkable(4, 2));
            Assert.AreEqual(new TileCoord(0, 1), f.Map.Spawns[0]);
            Assert.AreEqual(new TileCoord(4, 0), f.Map.Core);

            Assert.IsTrue(f.Grid[1, 1].HasWall);
            Assert.AreEqual(AsciiMap.WallCost, f.Grid[1, 1].TerrainCost);
            Assert.AreEqual(AsciiMap.HeavyWallCost, f.Grid[3, 1].TerrainCost);
        }

        [Test]
        public void RenderWithoutLabelsRoundTripsThroughParse()
        {
            var f = TestMaps.Parse(Sample);
            string rendered = AsciiMap.Render(f.Grid, f.Map, AsciiLayer.Terrain, labels: false);
            var again = TestMaps.Parse(rendered);

            CollectionAssert.AreEqual(f.Map.Rows, again.Map.Rows);
            Assert.AreEqual(f.Map.Core, again.Map.Core);
            CollectionAssert.AreEqual(f.Map.Spawns, again.Map.Spawns);
            for (int i = 0; i < f.Grid.NodeCount; i++)
            {
                Assert.AreEqual(f.Grid.ByIndex(i).TerrainCost, again.Grid.ByIndex(i).TerrainCost);
                Assert.AreEqual(f.Grid.ByIndex(i).WallHealth, again.Grid.ByIndex(i).WallHealth);
            }
        }

        [Test]
        public void RouteLayerMarksTheSpawnToCorePath()
        {
            var f = TestMaps.Parse(@"
                . . . . .
                S # # # C
                . . . . .");
            new FlowField().Generate(f.Grid, f.Map.Core);
            string rendered = AsciiMap.Render(f.Grid, f.Map, AsciiLayer.Route, labels: false);

            // Digging the wall row would cost 460; the detour costs 60. Top and bottom
            // detours tie, and the bottom one is relaxed first (neighbour order + FIFO),
            // so the route is S -> (0,0) ... (4,0) -> C and never crosses a '#'.
            Assert.AreEqual(
                ".....\n" +
                "S###C\n" +
                "*****\n",
                rendered.Replace("\r", ""),
                "rendered:\n" + rendered);
        }

        [Test]
        public void RenderWithLabelsShowsRulers()
        {
            var f = TestMaps.Parse(Sample);
            string rendered = AsciiMap.Render(f.Grid, f.Map);
            StringAssert.Contains("01234", rendered);
            StringAssert.Contains("  2  ....X", rendered);
        }

        [Test]
        public void ParseRejectsBadInput()
        {
            Assert.Throws<InvalidDataException>(() => AsciiMap.Parse(". . ."), "no core");
            Assert.Throws<InvalidDataException>(() => AsciiMap.Parse("C . C"), "two cores");
            Assert.Throws<InvalidDataException>(() => AsciiMap.Parse("C . .\n. ."), "ragged");
            Assert.Throws<InvalidDataException>(() => AsciiMap.Parse("C ? ."), "unknown tile");
        }
    }
}
