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

        public override string ToString()
        {
            switch (Kind)
            {
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
