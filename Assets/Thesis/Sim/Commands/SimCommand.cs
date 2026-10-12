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

        private SimCommand(SimCommandKind kind, int x, int y, int a)
        {
            Kind = kind;
            X = x;
            Y = y;
            A = a;
        }

        public static SimCommand PlaceShape(int originX, int originY) { return new SimCommand(SimCommandKind.PlaceShape, originX, originY, 0); }

        public static SimCommand Rotate() { return new SimCommand(SimCommandKind.Rotate, 0, 0, 0); }

        public static SimCommand Hold() { return new SimCommand(SimCommandKind.Hold, 0, 0, 0); }

        public static SimCommand StartWaveNow() { return new SimCommand(SimCommandKind.StartWaveNow, 0, 0, 0); }

        public static SimCommand PlaceTower(int offerSlot, int x, int y) { return new SimCommand(SimCommandKind.PlaceTower, x, y, offerSlot); }

        public static SimCommand SellTower(int x, int y) { return new SimCommand(SimCommandKind.SellTower, x, y, 0); }

        public static SimCommand SellWall(int x, int y) { return new SimCommand(SimCommandKind.SellWall, x, y, 0); }

        public static SimCommand RerollShop() { return new SimCommand(SimCommandKind.RerollShop, 0, 0, 0); }

        public override string ToString()
        {
            switch (Kind)
            {
                case SimCommandKind.PlaceShape: return "PlaceShape(" + X + "," + Y + ")";
                case SimCommandKind.PlaceTower: return "PlaceTower(slot " + A + " @" + X + "," + Y + ")";
                case SimCommandKind.SellTower: return "SellTower(" + X + "," + Y + ")";
                case SimCommandKind.SellWall: return "SellWall(" + X + "," + Y + ")";
                default: return Kind.ToString();
            }
        }
    }
}
