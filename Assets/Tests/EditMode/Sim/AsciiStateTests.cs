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
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(count: 3, life: 100f, speed: 2f, intervalTicks: 5)), new SimConfig { PrepSeconds = 0.02f });
            TestSims.Run(sim, ticks);
            return sim;
        }

        private static string[] Lines(string text) { return text.TrimEnd('\n').Split('\n'); }

        [Test]
        public void TheHeaderDescribesTheState()
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(1, 1f)));
            string[] lines = Lines(AsciiState.Render(sim, AsciiLayer.Route));

            Assert.AreEqual("wave 0  tick 0  phase Prep  budget 60.0  core 10/10  live 0  layer=route", lines[0]);
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
            Simulation sim = TestSims.AsciiWithWalls(boxed, new FixedPlanner(FixedPlanner.Group(count: 1, life: 100f, speed: 2f)), new SimConfig { PrepSeconds = 0.02f });

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
    }
}
