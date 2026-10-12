using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class ShapeLibraryFileTests
    {
        // Maps/Shapes.json is exported from the scene (menu Thesis/Export Shapes) and is
        // what a headless run builds with. Since WP-C4 a wall is one tile (GDD 4.1): the
        // library is one shape of one tile, so every piece the bag deals is that tile.
        [Test]
        public void TheGamesLibraryIsOneWallOfOneTile()
        {
            ShapeDef[] exported = TestSims.GameShapes();

            Assert.AreEqual(1, exported.Length, "one shape");
            Assert.AreEqual("Wall", exported[0].Name);
            Assert.AreEqual(1, exported[0].BuildCost, "one price a tile");
            Assert.AreEqual(15, exported[0].DigCost);
            Assert.AreEqual(6f, exported[0].WallHealth);
            CollectionAssert.AreEqual(new[] { new TileCoord(0, 0) }, exported[0].LocalTiles, "the tile under the mouse");
        }

        // The file format carries a library of several shapes exactly: the tetrominoes
        // the game had until WP-C4, which the fixtures still build with.
        [Test]
        public void ALibraryWrittenToAFileIsReadBackTheSame()
        {
            ShapeDef[] expected = TestShapes.SampleSceneLibrary();
            ShapeDef[] exported = ShapeLibraryFile.FromJson(new ShapeLibraryFile { Source = "test", Shapes = expected }.ToJson()).Shapes;

            Assert.AreEqual(expected.Length, exported.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].Name, exported[i].Name, "order");
                Assert.AreEqual(expected[i].BuildCost, exported[i].BuildCost, expected[i].Name);
                Assert.AreEqual(expected[i].DigCost, exported[i].DigCost, expected[i].Name);
                Assert.AreEqual(expected[i].WallHealth, exported[i].WallHealth, expected[i].Name);
                CollectionAssert.AreEqual(expected[i].LocalTiles, exported[i].LocalTiles, expected[i].Name);
            }

            // Stated as the one number a replay cares about.
            MapData map = TestSims.Open38Map();
            Assert.AreEqual(ReplaySetup.Hash(new SimConfig(), map, expected, TestTowers.Roster()), ReplaySetup.Hash(new SimConfig(), map, exported, TestTowers.Roster()));
        }

        [Test]
        public void ItRoundTripsThroughJson()
        {
            var file = new ShapeLibraryFile { Source = "test", Shapes = TestShapes.SampleSceneLibrary() };
            ShapeLibraryFile back = ShapeLibraryFile.FromJson(file.ToJson());

            Assert.AreEqual("test", back.Source);
            Assert.AreEqual(7, back.Shapes.Length);
            CollectionAssert.AreEqual(file.Shapes[3].LocalTiles, back.Shapes[3].LocalTiles);
        }

        [Test]
        public void BadFilesAreRefusedWithAReason()
        {
            Assert.Throws<InvalidDataException>(() => ShapeLibraryFile.FromJson("{\"Schema\":1,\"Shapes\":[]}"));
            Assert.Throws<InvalidDataException>(() => ShapeLibraryFile.FromJson("{\"Schema\":99,\"Shapes\":[{\"Name\":\"I\",\"LocalTiles\":[{\"X\":0,\"Y\":0}]}]}"));
            Assert.Throws<InvalidDataException>(() => ShapeLibraryFile.FromJson("{\"Schema\":1,\"Shapes\":[{\"Name\":\"I\",\"LocalTiles\":[]}]}"));
            Assert.Throws<FileNotFoundException>(() => ShapeLibraryFile.Load(Path.Combine(TestPaths.Maps, "no-such-shapes.json")));
        }
    }
}
