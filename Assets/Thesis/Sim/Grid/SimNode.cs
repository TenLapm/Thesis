using System;
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

        // > 0 means a player structure stands here (a wall or a tower), in seconds of
        // single-agent chewing.
        public float WallHealth;
        public float MaxWallHealth;

        // Which kind of structure, and for a tower its id in SimState.Towers (-1 otherwise).
        public Occupant Occupant;
        public int TowerId = -1;

        // For a wall tile: the piece it belongs to, which is the index of the
        // placement that built it in SimState.PlacementLog. Selling any tile of a
        // piece sells the whole piece (WP-C3). -1 for open ground, for a tower, and
        // for a wall that was put here without going through the log.
        public int PieceId = -1;

        // The wall a tower stands on (SimConfig.TowersOnWalls): its cost, health and piece,
        // kept while the tower is there and put back when the tower leaves the tile.
        // FoundationCost 0 = no wall under it (open ground, a rock, or no tower).
        public int FoundationCost;
        public float FoundationHealth;
        public float FoundationMaxHealth;
        public int FoundationPiece = -1;

        // The GROUND flow field. Integer cost x10 to the goal (so diagonals can cost
        // ~sqrt(2) without floats).
        public int BestCost = Infinity;

        // Index of the next node toward the goal, or -1. An int rather than a node
        // reference so SimGrid.Clone() and state hashing need no pointer remapping.
        public int NextIndex = -1;

        // The SAPPER flow field (WP-C2): the same two values when a built tile costs
        // only SimConfig.SapperDigCostFactor of its price. FlowFieldSet fills it
        // together with the ground field; a grid that only ever went through
        // FlowField.Generate (a policy's scratch copy, a map check) leaves it unset.
        public int SapperCost = Infinity;
        public int SapperNextIndex = -1;

        // The RAID field (2026-10-14): the same costs as the ground field, toward the
        // nearest standing collector instead of the core. Raiders follow it while any
        // collector stands (SimGrid.RaidGoals > 0).
        public int RaidCost = Infinity;
        public int RaidNextIndex = -1;

        // A tower with lure gear stands here (2026-10-20), and the LURE field toward the
        // nearest such tower: an enemy on foot within GearDef.LureRadius tiles of one
        // follows it there (SimGrid.LureGoals > 0).
        public bool Lure;
        public int LureCost = Infinity;
        public int LureNextIndex = -1;

        public SimNode(int x, int y, int index, bool isWalkable, Vec2f position)
        {
            X = x;
            Y = y;
            Index = index;
            IsWalkable = isWalkable;
            Position = position;
        }

        // True for ANY standing structure, a tower as much as a wall: both are diggable
        // terrain, and everything that asks "is something built here?" (placement,
        // digging, the corner-cut rule) means both. Occupant says which.
        public bool HasWall => WallHealth > 0f;

        // Corner-cut rule input: agents may enter a wall tile head-on (that is how
        // digging starts) but may not slip diagonally BETWEEN two solid tiles.
        public bool BlocksCorner => !IsWalkable || HasWall;

        // The cost to the goal on the field a movement class follows. Flying follows
        // none: its distance is a straight line (FlyingMovement), so asking for it
        // here is a bug in the caller.
        public int CostFor(MovementClass movement)
        {
            switch (movement)
            {
                case MovementClass.Ground: return BestCost;
                case MovementClass.Sapper: return SapperCost;
                default: throw new InvalidOperationException("[Sim] " + movement + " has no flow field.");
            }
        }

        public int NextIndexFor(MovementClass movement)
        {
            switch (movement)
            {
                case MovementClass.Ground: return NextIndex;
                case MovementClass.Sapper: return SapperNextIndex;
                default: throw new InvalidOperationException("[Sim] " + movement + " has no flow field.");
            }
        }

        public override string ToString()
        {
            return "SimNode(" + X + "," + Y + " cost=" + (BestCost == Infinity ? "inf" : BestCost.ToString()) + " terrain=" + TerrainCost + (HasWall ? " wall=" + WallHealth : "") + ")";
        }
    }
}
