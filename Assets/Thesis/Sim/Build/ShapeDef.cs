using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Pure-data port of BlockShape (a ScriptableObject in the original). A ShapeDef
    // is mutable by design, exactly like BlockShape: Rotate() mutates LocalTiles in
    // place. ShapeBag enforces the rule that made this safe in the original -
    // never rotate a library master, only a Clone() of one (BlockManager only ever
    // handed out Instantiate() copies, for the same reason).
    public sealed class ShapeDef
    {
        public string Name;
        public int BuildCost = 4;

        // Pathfinding weight of one tile of this shape's wall material, and the
        // seconds a single agent takes to chew through one tile. See BlockShape's
        // tooltips: digging is "as bad as a detour of this many open tiles".
        public int DigCost = 15;
        public float WallHealth = 6f;

        // Tile offsets relative to the click/origin point (0, 0).
        public TileCoord[] LocalTiles;

        public ShapeDef Clone()
        {
            var tiles = new TileCoord[LocalTiles.Length];
            Array.Copy(LocalTiles, tiles, tiles.Length);
            return new ShapeDef { Name = Name, BuildCost = BuildCost, DigCost = DigCost, WallHealth = WallHealth, LocalTiles = tiles };
        }

        // Port of BlockShape.Rotate: rotates 90 degrees clockwise by remapping every
        // tile offset, (x, y) -> (y, -x). Mutates this instance.
        public void Rotate()
        {
            for (int i = 0; i < LocalTiles.Length; i++)
            {
                TileCoord t = LocalTiles[i];
                LocalTiles[i] = new TileCoord(t.Y, -t.X);
            }
        }

        public override string ToString() { return Name + " (" + LocalTiles.Length + " tiles, buildCost " + BuildCost + ")"; }
    }
}
