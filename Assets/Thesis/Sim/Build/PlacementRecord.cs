using Thesis.Core;

namespace Thesis.Sim
{
    // One row of the placement log: something the player built. The primary metric
    // (ARCHITECTURE.md §5.9) and the build-profile features read this log, and it
    // becomes the "placements" array in a wave's telemetry row (§5.8).
    //
    // A wall piece has several tiles; a tower has one. Name is the shape's name for
    // a wall and the TowerDef's id for a tower.
    public sealed class PlacementRecord
    {
        public PlacementKind Kind;
        public int Tick;
        public string ShapeName;
        public int RotationTurns;
        public int OriginX;
        public int OriginY;
        public TileCoord[] Tiles;

        // For a tower: its id in SimState.Towers. -1 for a wall.
        public int TowerId = -1;

        public override string ToString()
        {
            return Kind == PlacementKind.Tower
                ? "tick " + Tick + ": tower " + ShapeName + " #" + TowerId + " @(" + OriginX + "," + OriginY + ")"
                : "tick " + Tick + ": " + ShapeName + " rot" + RotationTurns + " @(" + OriginX + "," + OriginY + ")";
        }
    }
}
