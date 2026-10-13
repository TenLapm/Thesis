namespace Thesis.Sim
{
    // Every kind of input the player (or a scripted policy) can send. Kept as one
    // closed set - ARCHITECTURE.md §4.5 - so a replay's command list is exactly
    // "what the player did" and nothing else can reach into the simulation.
    // Simulation applies them in enqueue order at the start of a tick (or at once,
    // through FlushInput).
    //
    // The numbers are hashed with pending commands: append, never renumber.
    public enum SimCommandKind
    {
        PlaceShape,   // place the current shape with its origin at (X, Y)
        Rotate,       // rotate the current shape a quarter turn
        Hold,         // swap the current shape with the hold slot
        StartWaveNow, // skip the rest of an intermission
        PlaceTower,   // buy the shop's offer in slot A and put that tower on tile (X, Y)
        SellTower,    // sell the tower on tile (X, Y)
        SellWall,     // sell the whole wall piece that tile (X, Y) belongs to
        RerollShop,   // pay for a new set of offers (a no-op while rerolls are off)
        TakeCoin,     // take the coin card in slot A
        BuyGear,      // buy the gear card in slot A onto the tower on tile (X, Y)
        MoveGear,     // move the gear in slot A of the tower on (X, Y) to the tower on (B, C)
        UpgradeTower, // raise the tower on tile (X, Y) one level
        BuyShopGear,  // buy the gear shop's item in slot A onto the tower on tile (X, Y)
        MoveTower,    // put the tower on tile (X, Y) on tile (B, C)
        PlaceCore,    // put the core on tile (X, Y), before the first wave
        BuyWalls,     // pay for a pack of wall tiles
        BuyMove,      // pay for one more move this build phase
        BenchCard,    // take the tower card in slot A onto bench slot B
        DeployTower,  // put the tower in bench slot A on tile (X, Y); a tower standing there goes to that slot
        RecallTower,  // take the tower on tile (X, Y) onto bench slot A
        SwapBench,    // swap bench slots A and B
        SellBenched,  // sell the tower in bench slot A
        StoreGearCard,   // take the gear card in slot A into the gear inventory
        StoreShopGear,   // buy the gear shop's item in slot A into the gear inventory
        EquipGear,       // put the piece in inventory slot A on the tower on (X, Y), or, with B > 0, in bench slot B - 1
        UnequipGear,     // take the piece in gear slot A off the tower on (X, Y), or, with B > 0, in bench slot B - 1, into the inventory
        RerollGearShop,  // pay for a new stock in the gear shop
        ChooseUpgrade,   // pay for the tower on (X, Y), or with B > 0 in bench slot B - 1, to gain upgrade A
        SetTargeting,    // the tower on (X, Y), or with B > 0 in bench slot B - 1, shoots by order A (a TargetingMode)
        MergeTower,      // merge the tower on (X, Y), or with A > 0 in bench slot A - 1, into the tower on (B, C), or with C < 0 in bench slot B
        EquipOverflow,   // put spare piece A on the tower on (X, Y), or, with B > 0, in bench slot B - 1
        SellOverflow,    // sell spare piece A
        FeedCard,     // merge the tower card in slot A into the tower on (X, Y), or, with B > 0, in bench slot B - 1
    }

    public readonly struct SimCommand
    {
        public readonly SimCommandKind Kind;

        // PlaceShape: the origin tile. PlaceTower, SellTower, SellWall: the tile.
        // Zero otherwise.
        public readonly int X;
        public readonly int Y;

        // PlaceTower: the shop's offer slot (an index into SimState.Shop.Offers), so
        // a player can only build what the shop is offering. Until WP-C3 this was an
        // index into the tower roster. Zero otherwise.
        public readonly int A;

        // MoveGear: the tile of the tower the gear goes to. MoveTower: the tile the tower goes to. Zero otherwise.
        public readonly int B;
        public readonly int C;

        private SimCommand(SimCommandKind kind, int x, int y, int a, int b = 0, int c = 0)
        {
            Kind = kind;
            X = x;
            Y = y;
            A = a;
            B = b;
            C = c;
        }

        public static SimCommand PlaceShape(int originX, int originY) { return new SimCommand(SimCommandKind.PlaceShape, originX, originY, 0); }

        public static SimCommand Rotate() { return new SimCommand(SimCommandKind.Rotate, 0, 0, 0); }

        public static SimCommand Hold() { return new SimCommand(SimCommandKind.Hold, 0, 0, 0); }

        public static SimCommand StartWaveNow() { return new SimCommand(SimCommandKind.StartWaveNow, 0, 0, 0); }

        public static SimCommand PlaceTower(int offerSlot, int x, int y) { return new SimCommand(SimCommandKind.PlaceTower, x, y, offerSlot); }

        public static SimCommand SellTower(int x, int y) { return new SimCommand(SimCommandKind.SellTower, x, y, 0); }

        public static SimCommand SellWall(int x, int y) { return new SimCommand(SimCommandKind.SellWall, x, y, 0); }

        public static SimCommand RerollShop() { return new SimCommand(SimCommandKind.RerollShop, 0, 0, 0); }

        public static SimCommand TakeCoin(int offerSlot) { return new SimCommand(SimCommandKind.TakeCoin, 0, 0, offerSlot); }

        public static SimCommand BuyGear(int offerSlot, int x, int y) { return new SimCommand(SimCommandKind.BuyGear, x, y, offerSlot); }

        public static SimCommand MoveGear(int fromX, int fromY, int gearSlot, int toX, int toY) { return new SimCommand(SimCommandKind.MoveGear, fromX, fromY, gearSlot, toX, toY); }

        public static SimCommand UpgradeTower(int x, int y) { return new SimCommand(SimCommandKind.UpgradeTower, x, y, 0); }

        public static SimCommand BuyShopGear(int shopSlot, int x, int y) { return new SimCommand(SimCommandKind.BuyShopGear, x, y, shopSlot); }

        public static SimCommand MoveTower(int fromX, int fromY, int toX, int toY) { return new SimCommand(SimCommandKind.MoveTower, fromX, fromY, 0, toX, toY); }

        public static SimCommand PlaceCore(int x, int y) { return new SimCommand(SimCommandKind.PlaceCore, x, y, 0); }

        public static SimCommand BuyWalls() { return new SimCommand(SimCommandKind.BuyWalls, 0, 0, 0); }

        public static SimCommand BuyMove() { return new SimCommand(SimCommandKind.BuyMove, 0, 0, 0); }

        public static SimCommand BenchCard(int offerSlot, int benchSlot) { return new SimCommand(SimCommandKind.BenchCard, 0, 0, offerSlot, benchSlot); }

        public static SimCommand DeployTower(int benchSlot, int x, int y) { return new SimCommand(SimCommandKind.DeployTower, x, y, benchSlot); }

        public static SimCommand RecallTower(int x, int y, int benchSlot) { return new SimCommand(SimCommandKind.RecallTower, x, y, benchSlot); }

        public static SimCommand SwapBench(int a, int b) { return new SimCommand(SimCommandKind.SwapBench, 0, 0, a, b); }

        public static SimCommand SellBenched(int benchSlot) { return new SimCommand(SimCommandKind.SellBenched, 0, 0, benchSlot); }

        public static SimCommand StoreGearCard(int offerSlot) { return new SimCommand(SimCommandKind.StoreGearCard, 0, 0, offerSlot); }

        public static SimCommand StoreShopGear(int shopSlot) { return new SimCommand(SimCommandKind.StoreShopGear, 0, 0, shopSlot); }

        public static SimCommand EquipGear(int inventorySlot, int x, int y) { return new SimCommand(SimCommandKind.EquipGear, x, y, inventorySlot); }

        public static SimCommand EquipGearOnBench(int inventorySlot, int benchSlot) { return new SimCommand(SimCommandKind.EquipGear, 0, 0, inventorySlot, benchSlot + 1); }

        public static SimCommand UnequipGear(int x, int y, int gearSlot) { return new SimCommand(SimCommandKind.UnequipGear, x, y, gearSlot); }

        public static SimCommand UnequipGearOnBench(int benchSlot, int gearSlot) { return new SimCommand(SimCommandKind.UnequipGear, 0, 0, gearSlot, benchSlot + 1); }

        // Source and target of a merge: each is a tile on the board or a bench slot.
        public static SimCommand MergeTower(int fromX, int fromY, int fromBench, int toX, int toY, int toBench)
        {
            return new SimCommand(SimCommandKind.MergeTower, fromBench >= 0 ? 0 : fromX, fromBench >= 0 ? 0 : fromY, fromBench >= 0 ? fromBench + 1 : 0,
                                  toBench >= 0 ? toBench : toX, toBench >= 0 ? -1 : toY);
        }

        public static SimCommand ChooseUpgrade(int x, int y, int choice) { return new SimCommand(SimCommandKind.ChooseUpgrade, x, y, choice); }

        public static SimCommand ChooseUpgradeOnBench(int benchSlot, int choice) { return new SimCommand(SimCommandKind.ChooseUpgrade, 0, 0, choice, benchSlot + 1); }

        public static SimCommand SetTargeting(int x, int y, TargetingMode mode) { return new SimCommand(SimCommandKind.SetTargeting, x, y, (int)mode); }

        public static SimCommand SetTargetingOnBench(int benchSlot, TargetingMode mode) { return new SimCommand(SimCommandKind.SetTargeting, 0, 0, (int)mode, benchSlot + 1); }

        public static SimCommand RerollGearShop() { return new SimCommand(SimCommandKind.RerollGearShop, 0, 0, 0); }

        public static SimCommand EquipOverflow(int index, int x, int y) { return new SimCommand(SimCommandKind.EquipOverflow, x, y, index); }

        public static SimCommand EquipOverflowOnBench(int index, int benchSlot) { return new SimCommand(SimCommandKind.EquipOverflow, 0, 0, index, benchSlot + 1); }

        public static SimCommand SellOverflow(int index) { return new SimCommand(SimCommandKind.SellOverflow, 0, 0, index); }

        public static SimCommand FeedCard(int offerSlot, int x, int y) { return new SimCommand(SimCommandKind.FeedCard, x, y, offerSlot); }

        public static SimCommand FeedCardOnBench(int offerSlot, int benchSlot) { return new SimCommand(SimCommandKind.FeedCard, 0, 0, offerSlot, benchSlot + 1); }

        public override string ToString()
        {
            switch (Kind)
            {
                case SimCommandKind.PlaceCore: return "PlaceCore(" + X + "," + Y + ")";
                case SimCommandKind.MergeTower: return "MergeTower(" + (A > 0 ? "bench " + (A - 1) : X + "," + Y) + " -> " + (C < 0 ? "bench " + B : B + "," + C) + ")";
                case SimCommandKind.EquipOverflow: return B > 0 ? "EquipOverflow(" + A + " -> bench " + (B - 1) + ")" : "EquipOverflow(" + A + " @" + X + "," + Y + ")";
                case SimCommandKind.SellOverflow: return "SellOverflow(" + A + ")";
                case SimCommandKind.StoreGearCard: return "StoreGearCard(slot " + A + ")";
                case SimCommandKind.StoreShopGear: return "StoreShopGear(slot " + A + ")";
                case SimCommandKind.EquipGear: return B > 0 ? "EquipGear(inventory " + A + " -> bench " + (B - 1) + ")" : "EquipGear(inventory " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.UnequipGear: return B > 0 ? "UnequipGear(bench " + (B - 1) + " gear " + A + ")" : "UnequipGear(" + X + "," + Y + " gear " + A + ")";
                case SimCommandKind.BenchCard: return "BenchCard(slot " + A + " -> bench " + B + ")";
                case SimCommandKind.DeployTower: return "DeployTower(bench " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.RecallTower: return "RecallTower(" + X + "," + Y + " -> bench " + A + ")";
                case SimCommandKind.SwapBench: return "SwapBench(" + A + "," + B + ")";
                case SimCommandKind.SellBenched: return "SellBenched(bench " + A + ")";
                case SimCommandKind.FeedCard: return B > 0 ? "FeedCard(slot " + A + " -> bench " + (B - 1) + ")" : "FeedCard(slot " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.MoveTower: return "MoveTower(" + X + "," + Y + " -> " + B + "," + C + ")";
                case SimCommandKind.PlaceShape: return "PlaceShape(" + X + "," + Y + ")";
                case SimCommandKind.PlaceTower: return "PlaceTower(slot " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.SellTower: return "SellTower(" + X + "," + Y + ")";
                case SimCommandKind.SellWall: return "SellWall(" + X + "," + Y + ")";
                case SimCommandKind.TakeCoin: return "TakeCoin(slot " + A + ")";
                case SimCommandKind.BuyGear: return "BuyGear(slot " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.MoveGear: return "MoveGear(" + X + "," + Y + " slot " + A + " -> " + B + "," + C + ")";
                case SimCommandKind.UpgradeTower: return "UpgradeTower(" + X + "," + Y + ")";
                case SimCommandKind.BuyShopGear: return "BuyShopGear(slot " + A + " @" + X + "," + Y + ")";
                default: return Kind.ToString();
            }
        }
    }
}
