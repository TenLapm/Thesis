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

        // PlaceShape: the origin tile. PlaceTower, SellTower, SellWall: the tile.
        // Zero otherwise.
        public int X;
        public int Y;

        // PlaceTower, TakeCoin, BuyGear: the shop's slot. MoveGear: the gear slot. Zero otherwise.
        public int A;

        // MoveGear, MoveTower: the tile the gear or the tower goes to. Zero otherwise.
        public int B;
        public int C;

        public static ReplayCommand From(int tick, SimCommand command)
        {
            return new ReplayCommand { Tick = tick, Cmd = command.Kind.ToString(), X = command.X, Y = command.Y, A = command.A, B = command.B, C = command.C };
        }

        public SimCommand ToCommand()
        {
            // The name must be exactly a command's name. Enum.TryParse alone also takes a
            // number ("7"), which would silently mean whatever command has that number today.
            SimCommandKind kind;
            if (Cmd == null || !Enum.TryParse(Cmd, out kind) || !Enum.IsDefined(typeof(SimCommandKind), kind) || kind.ToString() != Cmd)
                throw new InvalidDataException("[Replay] Unknown command '" + Cmd + "' at tick " + Tick + ".");

            switch (kind)
            {
                case SimCommandKind.PlaceShape: return SimCommand.PlaceShape(X, Y, A);
                case SimCommandKind.Rotate: return SimCommand.Rotate();
                case SimCommandKind.Hold: return SimCommand.Hold();
                case SimCommandKind.StartWaveNow: return SimCommand.StartWaveNow();
                case SimCommandKind.PlaceTower: return SimCommand.PlaceTower(A, X, Y);
                case SimCommandKind.SellTower: return SimCommand.SellTower(X, Y);
                case SimCommandKind.SellWall: return SimCommand.SellWall(X, Y);
                case SimCommandKind.RerollShop: return SimCommand.RerollShop();
                case SimCommandKind.TakeCoin: return SimCommand.TakeCoin(A);
                case SimCommandKind.BuyGear: return SimCommand.BuyGear(A, X, Y);
                case SimCommandKind.MoveGear: return SimCommand.MoveGear(X, Y, A, B, C);
                case SimCommandKind.UpgradeTower: return SimCommand.UpgradeTower(X, Y);
                case SimCommandKind.BuyShopGear: return SimCommand.BuyShopGear(A, X, Y);
                case SimCommandKind.MoveTower: return SimCommand.MoveTower(X, Y, B, C);
                case SimCommandKind.PlaceCore: return SimCommand.PlaceCore(X, Y);
                case SimCommandKind.BuyWalls: return SimCommand.BuyWalls();
                case SimCommandKind.BuyMove: return SimCommand.BuyMove();
                case SimCommandKind.BenchCard: return SimCommand.BenchCard(A, B);
                case SimCommandKind.DeployTower: return SimCommand.DeployTower(A, X, Y);
                case SimCommandKind.RecallTower: return SimCommand.RecallTower(X, Y, A);
                case SimCommandKind.SwapBench: return SimCommand.SwapBench(A, B);
                case SimCommandKind.SellBenched: return SimCommand.SellBenched(A);
                case SimCommandKind.StoreGearCard: return SimCommand.StoreGearCard(A);
                case SimCommandKind.StoreShopGear: return SimCommand.StoreShopGear(A);
                case SimCommandKind.EquipGear: return B > 0 ? SimCommand.EquipGearOnBench(A, B - 1) : SimCommand.EquipGear(A, X, Y);
                case SimCommandKind.UnequipGear: return B > 0 ? SimCommand.UnequipGearOnBench(B - 1, A) : SimCommand.UnequipGear(X, Y, A);
                case SimCommandKind.MergeTower: return SimCommand.MergeTower(X, Y, A - 1, B, C, C < 0 ? B : -1);
                case SimCommandKind.EquipOverflow: return B > 0 ? SimCommand.EquipOverflowOnBench(A, B - 1) : SimCommand.EquipOverflow(A, X, Y);
                case SimCommandKind.SellOverflow: return SimCommand.SellOverflow(A);
                case SimCommandKind.RerollGearShop: return SimCommand.RerollGearShop();
                case SimCommandKind.ChooseUpgrade: return B > 0 ? SimCommand.ChooseUpgradeOnBench(B - 1, A) : SimCommand.ChooseUpgrade(X, Y, A);
                case SimCommandKind.SetTargeting: return B > 0 ? SimCommand.SetTargetingOnBench(B - 1, (TargetingMode)A) : SimCommand.SetTargeting(X, Y, (TargetingMode)A);
                case SimCommandKind.FeedCard: return B > 0 ? SimCommand.FeedCardOnBench(A, B - 1) : SimCommand.FeedCard(A, X, Y);
                case SimCommandKind.BuildCollector: return SimCommand.BuildCollector(X, Y);
                case SimCommandKind.BuyTower: return SimCommand.BuyTower(A, X, Y);
                case SimCommandKind.Research: return SimCommand.Research();
                case SimCommandKind.CraftGear: return SimCommand.CraftGear(A);
                case SimCommandKind.RepairCore: return SimCommand.RepairCore();
                case SimCommandKind.Undo: return SimCommand.Undo();
                case SimCommandKind.Redo: return SimCommand.Redo();
                case SimCommandKind.RetryWave: return SimCommand.RetryWave();
                case SimCommandKind.SetPowerSource: return SimCommand.SetPowerSource(X, Y, A, B);
                default: throw new InvalidDataException("[Replay] Command '" + Cmd + "' at tick " + Tick + " cannot be replayed by this version.");
            }
        }

        public override string ToString()
        {
            if (Cmd == "PlaceShape") return "tick " + Tick + ": PlaceShape(" + X + "," + Y + ")";
            if (Cmd == "PlaceTower") return "tick " + Tick + ": PlaceTower(slot " + A + " @" + X + "," + Y + ")";
            if (Cmd == "SellTower" || Cmd == "SellWall") return "tick " + Tick + ": " + Cmd + "(" + X + "," + Y + ")";
            return "tick " + Tick + ": " + Cmd;
        }
    }
}
