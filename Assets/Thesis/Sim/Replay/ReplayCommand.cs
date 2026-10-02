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

        // PlaceShape: the origin tile. PlaceTower: the tower's tile. Zero otherwise.
        public int X;
        public int Y;

        // PlaceTower: the index of the tower type in the roster. Zero otherwise.
        public int A;

        public static ReplayCommand From(int tick, SimCommand command)
        {
            return new ReplayCommand { Tick = tick, Cmd = command.Kind.ToString(), X = command.X, Y = command.Y, A = command.A };
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
                case SimCommandKind.StartWaveNow: return SimCommand.StartWaveNow();
                case SimCommandKind.PlaceTower: return SimCommand.PlaceTower(A, X, Y);
                default: throw new InvalidDataException("[Replay] Command '" + Cmd + "' at tick " + Tick + " cannot be replayed by this version.");
            }
        }

        public override string ToString()
        {
            if (Cmd == "PlaceShape") return "tick " + Tick + ": PlaceShape(" + X + "," + Y + ")";
            if (Cmd == "PlaceTower") return "tick " + Tick + ": PlaceTower(#" + A + " @" + X + "," + Y + ")";
            return "tick " + Tick + ": " + Cmd;
        }
    }
}
