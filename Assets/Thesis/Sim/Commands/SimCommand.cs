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
        PlaceTower,   // buy tower type A from the roster and put it on tile (X, Y)
    }

    public readonly struct SimCommand
    {
        public readonly SimCommandKind Kind;

        // PlaceShape: the origin tile. PlaceTower: the tower's tile. Zero otherwise.
        public readonly int X;
        public readonly int Y;

        // PlaceTower: the index of the tower type in the simulation's tower roster.
        // (From WP-C3 this becomes the shop's offer slot.) Zero otherwise.
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

        public static SimCommand PlaceTower(int towerIndex, int x, int y) { return new SimCommand(SimCommandKind.PlaceTower, x, y, towerIndex); }

        public override string ToString()
        {
            switch (Kind)
            {
                case SimCommandKind.PlaceShape: return "PlaceShape(" + X + "," + Y + ")";
                case SimCommandKind.PlaceTower: return "PlaceTower(#" + A + " @" + X + "," + Y + ")";
                default: return Kind.ToString();
            }
        }
    }
}
