namespace Thesis.Sim
{
    // What one row of the build log records. The first two put something on the
    // board; the next two take it off again for a refund (WP-C3); the last is the
    // one way of spending budget that touches no tile. Hashed with the log: append,
    // never renumber.
    public enum PlacementKind
    {
        Wall = 0,
        Tower = 1,
        WallSold = 2,
        TowerSold = 3,
        ShopReroll = 4,
    }
}
