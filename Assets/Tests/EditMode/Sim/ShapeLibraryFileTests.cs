using System.IO;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class ShapeLibraryFileTests
    {
        // Maps/Shapes.json is exported from the scene (menu Thesis/Export Shapes);
        // TestShapes was copied by hand from the same assets. They must agree, shape
        // for shape and in the same order, or headless runs and tests would be
        // playing with a different bag from the game.
        [Test]
        public void TheExportedLibraryMatchesTheHandCopiedOne()
        {
            ShapeDef[] exported = ShapeLibraryFile.Load(TestPaths.ShapesFile);
            ShapeDef[] expected = TestShapes.SampleSceneLibrary();

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
            MapData map = TestSims.SampleSceneMap();
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
