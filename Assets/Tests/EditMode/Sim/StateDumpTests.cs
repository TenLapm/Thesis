using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class StateDumpTests
    {
        private const string Map = @"
            . . . . . . .
            S . . # . . C
            . . . . . . .";

        private static Simulation Running(int ticks)
        {
            Simulation sim = TestSims.AsciiWithWalls(Map, new FixedPlanner(FixedPlanner.Group(count: 3, hp: 100f, speed: 2f, intervalTicks: 5)), new SimConfig { PrepSeconds = 0.02f });
            TestSims.Run(sim, ticks);
            return sim;
        }

        [Test]
        public void TheDumpIsJsonAndCarriesTheStateHash()
        {
            Simulation sim = Running(30);
            JObject dump = JObject.Parse(StateDump.ToJson(sim.State));

            Assert.AreEqual(ReplayFile.Hex(sim.ComputeHash()), (string)dump["Hash"]);
            Assert.AreEqual(30, (int)dump["Tick"]);
            Assert.AreEqual(sim.State.Phase.ToString(), (string)dump["Phase"]);
            Assert.AreEqual(sim.State.LiveAgentCount, ((JArray)dump["LiveAgents"]).Count);
            Assert.AreEqual(sim.State.Bag.CurrentShape.Name, (string)dump["Bag"]["Current"]);
        }

        [Test]
        public void WallsAndAgentsCarryExactFloatBits()
        {
            Simulation sim = Running(30);
            JObject dump = JObject.Parse(StateDump.ToJson(sim.State));

            JArray walls = (JArray)dump["Walls"];
            Assert.AreEqual(1, walls.Count);
            Assert.AreEqual(3, (int)walls[0]["X"]);
            Assert.AreEqual(1, (int)walls[0]["Y"]);
            Assert.AreEqual("40c00000", (string)walls[0]["HealthBits"], "6.0f");

            JToken agent = dump["LiveAgents"][0];
            AgentState a = sim.State.LiveAgents[0];
            Assert.AreEqual(a.Id, (int)agent["Id"]);
            Assert.AreEqual(System.BitConverter.SingleToInt32Bits(a.Position.X).ToString("x8"), (string)agent["XBits"]);
            Assert.AreEqual(System.BitConverter.SingleToInt32Bits(a.Hp).ToString("x8"), (string)agent["HpBits"]);
            Assert.AreEqual("Wall", (string)walls[0]["Occupant"]);
        }

        [Test]
        public void TwoRunsOfTheSameTickDumpIdenticallyAndTheNextTickDoesNot()
        {
            string a = StateDump.ToJson(Running(40).State);
            string b = StateDump.ToJson(Running(40).State);
            string later = StateDump.ToJson(Running(41).State);

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, later);
        }
    }
}
