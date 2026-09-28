using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    // The seven shapes SampleScene's BlockManager.shapeLibrary actually wires up,
    // copied from Assets/Scripts/BlockS/{I,J,L,O,S,T,Z}.asset (localTiles and
    // buildCost). digCost / wallHealth are not serialized on those assets, so
    // BlockShape's C# defaults (15 / 6) apply.
    public static class TestShapes
    {
        public static ShapeDef[] SampleSceneLibrary()
        {
            return new[]
            {
                Make("I", 4, (0, 0), (1, 0), (2, 0), (3, 0)),
                Make("O", 7, (0, 0), (1, 0), (0, 1), (1, 1)),
                Make("T", 5, (0, 0), (1, 0), (2, 0), (1, 1)),
                Make("S", 3, (1, 0), (2, 0), (0, 1), (1, 1)),
                Make("Z", 3, (0, 0), (1, 0), (1, 1), (2, 1)),
                Make("J", 3, (0, 0), (0, 1), (1, 1), (2, 1)),
                Make("L", 3, (2, 0), (0, 1), (1, 1), (2, 1)),
            };
        }

        private static ShapeDef Make(string name, int cost, params (int x, int y)[] tiles)
        {
            var t = new TileCoord[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) t[i] = new TileCoord(tiles[i].x, tiles[i].y);
            return new ShapeDef { Name = name, BuildCost = cost, DigCost = 15, WallHealth = 6f, LocalTiles = t };
        }
    }
}
