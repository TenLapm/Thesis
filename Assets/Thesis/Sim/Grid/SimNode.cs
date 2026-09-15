using Thesis.Core;

namespace Thesis.Sim
{
    // Port of Node.cs with the Unity-only fields removed (visualObject lives in the
    // view; bestDirection is derivable from NextIndex). Field meanings are unchanged:
    //
    // Player walls are NOT unwalkable - they are expensive terrain (high TerrainCost)
    // with health, so a path to the goal ALWAYS exists. Agents route around a wall
    // when the detour is cheap and chew through it when it isn't. That one design
    // choice is what removed the old chunk/sinkhole/validate/revert machinery.
    public sealed class SimNode
    {
        public const int Infinity = int.MaxValue;

        public readonly int X;
        public readonly int Y;

        // Flat index into SimGrid: X * Height + Y (same layout as the A* benchmark).
        public readonly int Index;

        // Static geometry only (the map's 'X' tiles). Player walls never touch this.
        public readonly bool IsWalkable;

        // World XZ of the tile centre, computed exactly as GridManager.CreateGrid does.
        public readonly Vec2f Position;

        // Pathfinding weight for ENTERING this tile. 1 = open ground.
        public int TerrainCost = 1;

        // > 0 means a player wall stands here, in seconds of single-agent chewing.
        public float WallHealth;
        public float MaxWallHealth;

        // Integer cost x10 to the goal (so diagonals can cost ~sqrt(2) without floats).
        public int BestCost = Infinity;

        // Index of the next node toward the goal, or -1. An int rather than a node
        // reference so SimGrid.Clone() and state hashing need no pointer remapping.
        public int NextIndex = -1;

        public SimNode(int x, int y, int index, bool isWalkable, Vec2f position)
        {
            X = x;
            Y = y;
            Index = index;
            IsWalkable = isWalkable;
            Position = position;
        }

        public bool HasWall => WallHealth > 0f;

        // Corner-cut rule input: agents may enter a wall tile head-on (that is how
        // digging starts) but may not slip diagonally BETWEEN two solid tiles.
        public bool BlocksCorner => !IsWalkable || HasWall;

        public override string ToString()
        {
            return "SimNode(" + X + "," + Y + " cost=" + (BestCost == Infinity ? "inf" : BestCost.ToString()) + " terrain=" + TerrainCost + (HasWall ? " wall=" + WallHealth : "") + ")";
        }
    }
}
