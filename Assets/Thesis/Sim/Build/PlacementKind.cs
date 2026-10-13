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

        // 2026-10-12: things done to a standing tower, and the coin card. None of them
        // puts anything on a tile or takes anything off.
        TowerUpgraded = 5,
        GearBought = 6,
        GearMoved = 7,
        CoinTaken = 8,

        // 2026-10-12, third round. TowerCombined: towers of one type merged; the row's
        // tower is the one that stays and its Tiles are the tiles that were emptied.
        // TowerMoved: a tower was put on another tile; Tiles are { from, to }.
        TowerCombined = 9,
        TowerMoved = 10,
    }
}
