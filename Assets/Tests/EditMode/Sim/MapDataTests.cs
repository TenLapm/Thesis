using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class MapDataTests
    {
        [Test]
        public void JsonRoundTripPreservesEverything()
        {
            var f = TestMaps.Parse(@"
                S . X
                . . .
                X . C");
            MapData back = MapData.FromJson(f.Map.ToJson());

            Assert.AreEqual(f.Map.Width, back.Width);
            Assert.AreEqual(f.Map.Height, back.Height);
            Assert.AreEqual(f.Map.WorldSizeX, back.WorldSizeX);
            Assert.AreEqual(f.Map.NodeRadius, back.NodeRadius);
            CollectionAssert.AreEqual(f.Map.Rows, back.Rows);
            Assert.AreEqual(f.Map.Core, back.Core);
            CollectionAssert.AreEqual(f.Map.Spawns, back.Spawns);
            Assert.AreEqual(f.Map.CoreWorld.X, back.CoreWorld.X);
            Assert.AreEqual(f.Map.SpawnWorlds[0].Z, back.SpawnWorlds[0].Z);
        }

        [Test]
        public void JsonKeepsRowsReadable()
        {
            var f = TestMaps.Parse(@"
                S X
                . C");
            string json = f.Map.ToJson();
            Assert.That(json, Does.Contain("\".X\""));
            Assert.That(json, Does.Contain("\"..\""));
            Assert.That(json, Does.Not.Contain("Magnitude"), "computed properties must not leak into map files");
        }

        [Test]
        public void RowsAreStoredTopDown()
        {
            var f = TestMaps.Parse(@"
                X .
                . C");
            // First text line is y = 1, so the X is at (0, 1).
            Assert.IsFalse(f.Map.IsWalkable(0, 1));
            Assert.IsTrue(f.Map.IsWalkable(0, 0));
            Assert.AreEqual(new TileCoord(1, 0), f.Map.Core);
        }

        [Test]
        public void ValidateRejectsRaggedRows()
        {
            MapData map = TestMaps.Open(3, 2, 6f, 4f, 1f);
            map.Rows[1] = "..";
            Assert.Throws<InvalidDataException>(() => map.Validate());
        }

        [Test]
        public void ValidateRejectsUnknownCharacters()
        {
            MapData map = TestMaps.Open(3, 2, 6f, 4f, 1f);
            map.Rows[0] = ".#.";
            Assert.Throws<InvalidDataException>(() => map.Validate());
        }

        [Test]
        public void ValidateRejectsCoreOnABlocker()
        {
            MapData map = TestMaps.Open(3, 2, 6f, 4f, 1f);
            map.Rows[1] = "X.."; // bottom row, y = 0; the core is at (0, 0)
            Assert.Throws<InvalidDataException>(() => map.Validate());
        }

        [Test]
        public void ValidateRejectsMissingSpawnWorldPositions()
        {
            MapData map = TestMaps.Open(3, 2, 6f, 4f, 1f);
            map.Spawns = new[] { new TileCoord(1, 1) };
            Assert.Throws<InvalidDataException>(() => map.Validate());
        }

        [Test]
        public void LoadOfMissingFileSaysHowToCreateIt()
        {
            var e = Assert.Throws<FileNotFoundException>(() => MapData.Load(Path.Combine(TestPaths.Maps, "does-not-exist.map.json")));
            Assert.That(e.Message, Does.Contain("Export All Maps"));
        }
    }
}
