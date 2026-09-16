using Thesis.Core;

namespace Thesis.Sim
{
    // One row of the placement log (ARCHITECTURE.md §4.1: "PlacementLog is needed
    // from the very first WP" - three of the five build-profile features and the
    // G5 entropy metric read it). Also becomes the "placements" array in a wave's
    // telemetry row (§5.8), which is why Tick and RotationTurns are recorded even
    // though nothing reads them back within WP2 itself.
    public sealed class PlacementRecord
    {
        public int Tick;
        public string ShapeName;
        public int RotationTurns;
        public int OriginX;
        public int OriginY;
        public TileCoord[] Tiles;

        public override string ToString() { return "tick " + Tick + ": " + ShapeName + " rot" + RotationTurns + " @(" + OriginX + "," + OriginY + ")"; }
    }
}
