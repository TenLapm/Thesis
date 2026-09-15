using System;

namespace Thesis.Core
{
    // Integer grid coordinate. X matches GridManager.gridX, Y matches gridY.
    public readonly struct TileCoord : IEquatable<TileCoord>
    {
        public readonly int X;
        public readonly int Y;

        public TileCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(TileCoord other) { return X == other.X && Y == other.Y; }

        public override bool Equals(object obj) { return obj is TileCoord other && Equals(other); }

        // Only for hashed lookups. Never iterate a Dictionary/HashSet keyed by this
        // where the order affects a result (ARCHITECTURE.md §9 rule 3).
        public override int GetHashCode() { return unchecked((X * 73856093) ^ (Y * 19349663)); }

        public static bool operator ==(TileCoord a, TileCoord b) { return a.Equals(b); }

        public static bool operator !=(TileCoord a, TileCoord b) { return !a.Equals(b); }

        public override string ToString() { return "(" + X + "," + Y + ")"; }
    }
}
