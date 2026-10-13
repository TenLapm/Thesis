using Thesis.Core;

namespace Thesis.Sim
{
    // One row of the build log (SimState.PlacementLog): something the player did
    // with the budget, in the order it happened. Building a wall piece or a tower,
    // selling one again, or paying to reroll the shop. The primary metric
    // (ARCHITECTURE.md §5.9) and the build-profile features read this log, and it
    // becomes the "placements" array in a wave's telemetry row (§5.8).
    //
    // The log only grows. A sale does not remove the row of what was sold: it adds
    // a row of its own, because "this left the board" is as much part of the
    // player's building as "this went on".
    //
    // A wall piece has several tiles; a tower has one. ShapeName is the shape's
    // name for a wall and the TowerDef's id for a tower, also on the row that sells it.
    public sealed class PlacementRecord
    {
        public PlacementKind Kind;
        public int Tick;
        public string ShapeName;
        public int RotationTurns;
        public int OriginX;
        public int OriginY;

        // Wall, Tower: the tiles built. WallSold, TowerSold: the tiles cleared, which
        // for a wall are only those of the piece still standing. ShopReroll: none.
        public TileCoord[] Tiles = new TileCoord[0];

        // For a tower (built or sold): its id in SimState.Towers. -1 otherwise.
        public int TowerId = -1;

        // For a wall: the piece this row is about, which is the index in the log of
        // the row that BUILT it. A Wall row therefore carries its own index, and a
        // WallSold row points back at it. -1 otherwise. SimNode.PieceId holds the
        // same number on every tile of the piece.
        public int PieceId = -1;

        // Budget spent by this row (Wall, Tower, ShopReroll) and budget returned by
        // it (WallSold, TowerSold). The other one is 0.
        public float Cost;
        public float Refund;

        public bool IsPlacement => Kind == PlacementKind.Wall || Kind == PlacementKind.Tower;

        public bool IsSale => Kind == PlacementKind.WallSold || Kind == PlacementKind.TowerSold;

        // The row took things off its Tiles without a sale: the towers a merge absorbed.
        public bool IsCombine => Kind == PlacementKind.TowerCombined;

        public override string ToString()
        {
            switch (Kind)
            {
                case PlacementKind.Tower: return "tick " + Tick + ": tower " + ShapeName + " #" + TowerId + " @(" + OriginX + "," + OriginY + ") for " + Cost;
                case PlacementKind.TowerSold: return "tick " + Tick + ": sold tower " + ShapeName + " #" + TowerId + " @(" + OriginX + "," + OriginY + ") for " + Refund;
                case PlacementKind.WallSold: return "tick " + Tick + ": sold " + Tiles.Length + " tile(s) of " + ShapeName + " (piece " + PieceId + ") for " + Refund;
                case PlacementKind.ShopReroll: return "tick " + Tick + ": rerolled the shop for " + Cost;
                default: return "tick " + Tick + ": " + ShapeName + " rot" + RotationTurns + " @(" + OriginX + "," + OriginY + ") for " + Cost;
            }
        }
    }
}
