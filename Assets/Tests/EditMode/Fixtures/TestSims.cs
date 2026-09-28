using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    public static class TestSims
    {
        public static MapData SampleSceneMap() { return MapData.Load(TestPaths.MapFile("SampleScene")); }

        public static Simulation SampleScene(ulong seed = 1, SimConfig config = null, IWavePlanner planner = null)
        {
            MapData map = SampleSceneMap();
            config = config ?? new SimConfig();
            return new Simulation(config, map, TestShapes.SampleSceneLibrary(), seed, planner ?? new EscalationPlanner(config, map));
        }

        public static Simulation Ascii(string text, IWavePlanner planner, SimConfig config = null, ulong seed = 1)
        {
            AsciiFixture f = AsciiMap.Parse(text);
            return new Simulation(config ?? new SimConfig(), f.Map, TestShapes.SampleSceneLibrary(), seed, planner);
        }

        public static void Run(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Tick();
        }
    }

    // A reproducible stream of player input: at a fixed cadence, one of place /
    // rotate / hold at a pseudo-random tile. Most placements are illegal or
    // unaffordable and get ignored, which is fine - the point is identical input to
    // two simulations, including input the game rejects.
    public sealed class CommandScript
    {
        private readonly Dictionary<int, List<SimCommand>> byTick = new Dictionary<int, List<SimCommand>>();

        public CommandScript(ulong seed, int ticks, int every, int mapWidth, int mapHeight)
        {
            var rng = new Pcg32(seed, RngStreams.Policy);
            for (int t = 5; t < ticks; t += every)
            {
                int roll = rng.NextInt(10);
                SimCommand c = roll < 7 ? SimCommand.PlaceShape(rng.NextInt(mapWidth), rng.NextInt(mapHeight))
                             : roll < 9 ? SimCommand.Rotate()
                             : SimCommand.Hold();
                byTick[t] = new List<SimCommand> { c };
            }
        }

        public void Feed(Simulation sim)
        {
            if (byTick.TryGetValue(sim.State.Tick, out List<SimCommand> list))
            {
                foreach (SimCommand c in list) sim.Enqueue(c);
            }
        }
    }
}
