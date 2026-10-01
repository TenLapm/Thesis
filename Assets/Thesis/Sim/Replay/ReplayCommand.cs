using System;
using System.IO;

namespace Thesis.Sim
{
    // One recorded player input: "at State.Tick == Tick, this command was applied".
    // Commands that share a tick are applied in file order.
    //
    // A plain class with a string kind instead of SimCommand itself, so the file
    // reads as "PlaceShape 12 7" rather than an enum number that would silently
    // change meaning if SimCommandKind were ever reordered.
    public sealed class ReplayCommand
    {
        public int Tick;
        public string Cmd;

        // Only PlaceShape uses these (the origin tile); zero otherwise.
        public int X;
        public int Y;

        public static ReplayCommand From(int tick, SimCommand command)
        {
            return new ReplayCommand { Tick = tick, Cmd = command.Kind.ToString(), X = command.X, Y = command.Y };
        }

        public SimCommand ToCommand()
        {
            SimCommandKind kind;
            if (Cmd == null || !Enum.TryParse(Cmd, out kind) || !Enum.IsDefined(typeof(SimCommandKind), kind))
                throw new InvalidDataException("[Replay] Unknown command '" + Cmd + "' at tick " + Tick + ".");

            switch (kind)
            {
                case SimCommandKind.PlaceShape: return SimCommand.PlaceShape(X, Y);
                case SimCommandKind.Rotate: return SimCommand.Rotate();
                case SimCommandKind.Hold: return SimCommand.Hold();
                default: return SimCommand.StartWaveNow();
            }
        }

        public override string ToString() { return "tick " + Tick + ": " + (Cmd == "PlaceShape" ? "PlaceShape(" + X + "," + Y + ")" : Cmd); }
    }
}
