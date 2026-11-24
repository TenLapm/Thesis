using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // A tester, 2026-11-24: "the pylon can be equipped with gear". A pylon powers towers; it does
    // not shoot, so gear has nothing to act on and only wasted the piece. It is refused now.
    public class PylonGearTests
    {
        private static Simulation Game()
        {
            SimConfig config = Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json")));
            MapData map = GameMaps.Generate(109);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 109UL, new EscalationPlanner(config, map));
            TestSims.Send(sim, SimCommand.PlaceCore(sim.State.Core.X, sim.State.Core.Y));
            return sim;
        }

        private static int Def(Simulation sim, bool pylon)
        {
            for (int i = 0; i < sim.TowerLibrary.Length; i++) if ((sim.TowerLibrary[i].PowerCapacity > 0) == pylon) return i;
            Assert.Fail("no such tower in the roster");
            return -1;
        }

        // A rock (towers stand on rocks) far enough from the core for a pylon, where the tower goes.
        private static TowerState Build(Simulation sim, int def, int skip)
        {
            SimState s = sim.State;
            int before = s.Towers.Count;
            for (int i = 0; i < s.Grid.NodeCount && s.Towers.Count == before; i++)
            {
                SimNode n = s.Grid.ByIndex(i);
                if (!n.IsRock || n.Occupant != Occupant.Wall) continue;
                int d = System.Math.Max(System.Math.Abs(n.X - s.Core.X), System.Math.Abs(n.Y - s.Core.Y));
                if (d < 6 + skip) continue;
                TestSims.Send(sim, SimCommand.BuyTower(def, n.X, n.Y));
            }
            Assert.AreEqual(before + 1, s.Towers.Count, "rig: the tower was built");
            return s.Towers[s.Towers.Count - 1];
        }

        private static int AnyGear(Simulation sim)
        {
            for (int g = 0; g < sim.GearLibrary.Length; g++) if (!sim.GearLibrary[g].Extracts) return g;
            Assert.Fail("no gear in the library");
            return -1;
        }

        [Test]
        public void APylonTakesNoGearAndTheGearStaysInTheInventory()
        {
            Simulation sim = Game();
            TowerState pylon = Build(sim, Def(sim, pylon: true), 0);
            int piece = AnyGear(sim);
            sim.State.GearInventory[0] = piece;
            TestSims.Send(sim, SimCommand.EquipGear(0, pylon.Tile.X, pylon.Tile.Y));
            Assert.AreEqual(piece, sim.State.GearInventory[0], "the piece is still in the inventory");
            for (int g = 0; g < TowerState.MaxGearSlots; g++) Assert.AreEqual(TowerState.NoGear, pylon.Gear[g]);
        }

        [Test]
        public void ATowerStillTakesIt()
        {
            Simulation sim = Game();
            TowerState tower = Build(sim, Def(sim, pylon: false), 0);
            int piece = AnyGear(sim);
            sim.State.GearInventory[0] = piece;
            TestSims.Send(sim, SimCommand.EquipGear(0, tower.Tile.X, tower.Tile.Y));
            Assert.AreEqual(TowerState.NoGear, sim.State.GearInventory[0]);
            Assert.AreEqual(piece, tower.Gear[0]);
        }
    }
}
