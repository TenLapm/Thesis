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

        // 2026-10-13, the fourth round. WallsGained: wall tiles added to the stock (a
        // card, or a pack bought with Cost). MoveBought: an extra move paid for.
        // TowerBenched: a card taken onto the bench. TowerDeployed / TowerRecalled: a
        // tower put on its tile from the bench, or taken off it onto the bench.
        // TowerFed: a duplicate card merged into the tower, which gained a level.
        // CorePlaced: the player chose the core's tile.
        WallsGained = 11,
        MoveBought = 12,
        TowerBenched = 13,
        TowerDeployed = 14,
        TowerRecalled = 15,
        TowerFed = 16,
        CorePlaced = 17,

        // 2026-10-14, the gear inventory. GearStored: a piece taken into the inventory
        // (a card, or bought with Cost). GearEquipped / GearUnequipped: a piece put on
        // the row's tower from the inventory, or taken off it into the inventory.
        GearStored = 18,
        GearEquipped = 19,
        GearUnequipped = 20,

        // A spare piece (SimState.GearOverflow) sold for Refund.
        GearSold = 21,

        // The gear shop's stock rolled again for Cost.
        GearShopReroll = 22,
    }
}
