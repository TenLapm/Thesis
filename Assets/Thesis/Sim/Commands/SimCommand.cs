namespace Thesis.Sim
{
    // Every kind of input the player (or a scripted policy) can send. Kept as one
    // closed set - ARCHITECTURE.md §4.5 - so a replay's command list is exactly
    // "what the player did" and nothing else can reach into the simulation.
    //
    // WP2 only defines the shape; nothing dispatches these yet. Simulation.Tick
    // (WP3) applies them in enqueue order at the start of a tick.
    public enum SimCommandKind
    {
        PlaceShape,   // place the current shape with its origin at (X, Y)
        Rotate,       // rotate the current shape a quarter turn
        Hold,         // swap the current shape with the hold slot
        StartWaveNow, // skip the rest of an intermission
    }

    public readonly struct SimCommand
    {
        public readonly SimCommandKind Kind;

        // Only meaningful for PlaceShape (the origin tile); zero otherwise.
        public readonly int X;
        public readonly int Y;

        private SimCommand(SimCommandKind kind, int x, int y)
        {
            Kind = kind;
            X = x;
            Y = y;
        }

        public static SimCommand PlaceShape(int originX, int originY) { return new SimCommand(SimCommandKind.PlaceShape, originX, originY); }

        public static SimCommand Rotate() { return new SimCommand(SimCommandKind.Rotate, 0, 0); }

        public static SimCommand Hold() { return new SimCommand(SimCommandKind.Hold, 0, 0); }

        public static SimCommand StartWaveNow() { return new SimCommand(SimCommandKind.StartWaveNow, 0, 0); }

        public override string ToString()
        {
            return Kind == SimCommandKind.PlaceShape ? "PlaceShape(" + X + "," + Y + ")" : Kind.ToString();
        }
    }
}
