using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    public static class TestMaps
    {
        public static AsciiFixture Parse(string text) { return AsciiMap.Parse(text); }

        // An all-open map with explicit world size, for NodeFromPosition tests that
        // need the real scene numbers (SampleScene: 38x38, gridWorldSize 75, radius 1).
        public static MapData Open(int width, int height, float worldSizeX, float worldSizeY, float nodeRadius)
        {
            var rows = new string[height];
            for (int r = 0; r < height; r++) rows[r] = new string('.', width);
            var map = new MapData
            {
                Name = "open-" + width + "x" + height,
                Width = width,
                Height = height,
                WorldSizeX = worldSizeX,
                WorldSizeY = worldSizeY,
                NodeRadius = nodeRadius,
                Rows = rows,
                Core = new TileCoord(0, 0),
                CoreWorld = new WorldPoint(0f, 0f, 0f),
            };
            map.Validate();
            return map;
        }
    }
}
