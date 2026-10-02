using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class AsciiStateTests
    {
        private const string Map = @"
            . . . . . . .
            S . . # . . C
            . . . . . . .";

        // Slow agents with a long clock, so they are still on the board when rendered.
        private static Simulation Running(int ticks)
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(count: 3, hp: 100f, speed: 2f, intervalTicks: 5)), new SimConfig { PrepSeconds = 0.02f });
            TestSims.Run(sim, ticks);
            return sim;
        }

        private static string[] Lines(string text) { return text.TrimEnd('\n').Split('\n'); }

        [Test]
        public void TheHeaderDescribesTheState()
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(1, 1f)));
            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Route));

            Assert.AreEqual("wave 0  tick 0  phase Prep  budget 60.0  core 10/10  live 0  towers 0  layer=route", lines[0]);
            Assert.AreEqual(1 + 2 + 3 + 1, lines.Length, "header, two rulers, three rows, legend");
            StringAssert.StartsWith("legend", lines[lines.Length - 1]);
        }

        [Test]
        public void RowsAreLabelledTopDownAndShowTheRoute()
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(1, 1f)));
            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Route));

            StringAssert.StartsWith("  2  ", lines[3]);
            StringAssert.StartsWith("  1  S", lines[4]);
            StringAssert.StartsWith("  0  ", lines[5]);
            StringAssert.EndsWith("C", lines[4]);
            StringAssert.Contains("#", lines[4]);
            StringAssert.Contains("*", lines[3] + lines[4] + lines[5]);

            string terrain = AsciiState.Render(sim, AsciiLayer.Terrain);
            Assert.IsFalse(terrain.Contains("*"), "the terrain layer draws no route");
        }

        [Test]
        public void WalkingAgentsAreDrawnOnTheirTile()
        {
            Simulation sim = Running(30);
            Assert.Greater(sim.State.LiveAgentCount, 0);

            string text = AsciiState.Render(sim, AsciiLayer.Route);
            StringAssert.Contains("a", string.Join("", Lines(text), 3, 3));
            StringAssert.Contains("live " + sim.State.LiveAgentCount, text);
        }

        [Test]
        public void AnAgentChewingAWallIsDrawnAsDigging()
        {
            // The spawn is boxed in, so the agent has to chew: it steps onto the wall
            // tile and stays there while the wall's health runs down.
            const string boxed = @"
                # # . . .
                S # . . C
                # # . . .";
            Simulation sim = TestSims.AsciiWithWalls(boxed, new FixedPlanner(FixedPlanner.Group(count: 1, hp: 100f, speed: 2f)), new SimConfig { PrepSeconds = 0.02f });

            bool sawDigging = false;
            for (int i = 0; i < 400 && !sawDigging; i++)
            {
                sim.Tick();
                string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Terrain));
                sawDigging = (lines[3] + lines[4] + lines[5]).Contains("d");
            }
            Assert.IsTrue(sawDigging);
        }

        [Test]
        public void TheOccupancyLayerMarksWhereAgentsHaveBeen()
        {
            Simulation sim = Running(120);
            Assert.Greater(sim.State.Occupancy.Total(), 0);

            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Occupancy));
            string rows = lines[3] + lines[4] + lines[5];
            StringAssert.Contains("9", rows, "the busiest tile is always 9");
            StringAssert.Contains("agent-ticks", lines[lines.Length - 1]);
        }

        [Test]
        public void TheCostLayerRunsFromZeroAtTheCoreSide()
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(1, 1f)));
            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Cost));

            // Row y=1: "S . . # . . C" -> the tile next to the core is the cheapest band.
            string row = lines[4].Substring(5);
            Assert.AreEqual('S', row[0]);
            Assert.AreEqual('#', row[3]);
            Assert.AreEqual('0', row[5]);
            Assert.AreEqual('C', row[6]);
            Assert.Greater(row[1], row[5], "cost falls toward the core");
        }

        // WORKPLAN WP-C2 review probe: the ground route and the sapper route of the same
        // board, side by side.
        [Test]
        public void TheSapperRouteCanBeDrawnInsteadOfTheGroundRoute()
        {
            // Straight through costs a wall; the way round goes over the top.
            const string corridor = @"
                . . . . . . .
                . X X X X X .
                . X X X X X .
                S . . # . . C";
            Simulation sim = TestSims.AsciiWithWalls(corridor, new FixedPlanner(FixedPlanner.Group(1, 1f)));

            string[] ground = Lines(AsciiState.Render(sim, AsciiLayer.Route));
            Assert.AreEqual("  3  *******", ground[3], "the ground route runs along the top");
            Assert.AreEqual("  0  S..#..C", ground[6], "and not through the corridor");
            Assert.IsFalse(ground[0].Contains("class="), "a ground render reads as it always did");
            StringAssert.Contains("* route", ground[ground.Length - 1]);

            string[] sapper = Lines(AsciiState.Render(sim, AsciiLayer.Route, MovementClass.Sapper));
            Assert.AreEqual("  3  .......", sapper[3]);
            Assert.AreEqual("  0  S**+**C", sapper[6], "the sapper route goes through the wall: '+' marks where");
            StringAssert.EndsWith("layer=route  class=sapper", sapper[0]);
            StringAssert.Contains("* sapper route", sapper[sapper.Length - 1]);

            // The cost layer follows the class too. Its scale tops out at the dearest
            // tile: for a walker the wall itself (15 tiles to leave it, 2 more to the
            // core), for a sapper the far corner of the way round.
            string[] groundCost = Lines(AsciiState.Render(sim, AsciiLayer.Cost));
            string[] sapperCost = Lines(AsciiState.Render(sim, AsciiLayer.Cost, MovementClass.Sapper));
            StringAssert.Contains("9 = 17 tiles", groundCost[groundCost.Length - 1]);
            StringAssert.Contains("9 = 10 tiles", sapperCost[sapperCost.Length - 1]);
            Assert.AreEqual('7', groundCost[6].Substring(5)[2], "two tiles into the corridor a walker is 14 tiles from the core (back out and round)");
            Assert.AreEqual('5', sapperCost[6].Substring(5)[2], "and a sapper 6 (on through the wall)");

            Assert.Throws<System.ArgumentException>(() => AsciiState.Render(sim, AsciiLayer.Route, MovementClass.Flying));
        }

        [Test]
        public void AFlyerIsDrawnAsF()
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Flyers(count: 1, hp: 100f, speed: 2f)), new SimConfig { PrepSeconds = 0.02f });
            TestSims.Run(sim, 150);
            Assert.AreEqual(1, sim.State.LiveAgentCount, "rig: the flyer is still on its way");

            // 150 steps of 0.04 is 6 world units: three tiles along the middle row,
            // which is where the wall stands. It is over the wall, not digging it.
            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Route));
            Assert.AreEqual('f', lines[4].Substring(5)[3], lines[4]);
            Assert.IsFalse((lines[3] + lines[4] + lines[5]).Contains("d"), "a flyer over a wall is not digging");
            StringAssert.Contains("f flying agent", lines[lines.Length - 1]);
        }

        [Test]
        public void ATowerIsDrawnAsTAndCountedInTheHeader()
        {
            Simulation sim = TestSims.Ascii(Map, new FixedPlanner(FixedPlanner.Group(1)), new SimConfig { PrepSeconds = 60f });
            TestSims.Send(sim, SimCommand.PlaceTower(0, 5, 2));

            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Route));
            StringAssert.Contains("towers 1", lines[0]);
            Assert.AreEqual('T', lines[3].Substring(5)[5], "row y=2, x=5");
            StringAssert.Contains("T tower", lines[lines.Length - 1]);

            foreach (AsciiLayer layer in new[] { AsciiLayer.Terrain, AsciiLayer.Occupancy, AsciiLayer.Cost })
            {
                string[] other = Lines(AsciiState.Render(sim, layer));
                Assert.AreEqual('T', other[3].Substring(5)[5], layer.ToString());
            }
        }
    }
}
