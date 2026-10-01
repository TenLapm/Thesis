using System;
using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class GreedyDetourPolicyTests
    {
        // A corridor: every route from S to C passes the gap in the static wall, so
        // one piece in the gap forces a dig and must raise the route cost at once.
        private const string Corridor = @"
            . . . . X . . . .
            . . . . X . . . .
            S . . . . . . . C
            . . . . X . . . .
            . . . . X . . . .";

        [Test]
        public void ItRaisesTheRouteCostWhenAPieceCan()
        {
            Simulation sim = Sim(Corridor);
            int before = SpawnCost(sim);

            List<SimCommand> sent = Build(sim, new GreedyDetourPolicy { MaxPlacementsPerIntermission = 1 });

            Assert.AreEqual(1, sim.State.PlacementLog.Count);
            Assert.Greater(SpawnCost(sim), before, "the gap is the only way through");
            Assert.AreEqual(SimCommandKind.PlaceShape, sent[sent.Count - 1].Kind);
        }

        // On open ground no single piece lengthens the route (there are too many
        // equally short ones). The policy must still build - by closing off routes -
        // and the route cost must end up higher once enough of them are closed.
        [Test]
        public void OnOpenGroundItKeepsBuildingUntilTheRouteGetsLonger()
        {
            Simulation sim = Sim(TestEpisodes.SmallMap);
            int before = SpawnCost(sim);

            Build(sim, new GreedyDetourPolicy { MaxPlacementsPerIntermission = 12 });

            Assert.Greater(sim.State.PlacementLog.Count, 2);
            Assert.Greater(SpawnCost(sim), before);
        }

        [Test]
        public void EveryCommandItSendsIsAccepted()
        {
            Simulation sim = Sim(TestEpisodes.SmallMap);
            int placeCommands = 0;
            foreach (SimCommand c in Build(sim, new GreedyDetourPolicy()))
            {
                if (c.Kind == SimCommandKind.PlaceShape) placeCommands++;
            }
            Assert.Greater(placeCommands, 0);
            Assert.AreEqual(placeCommands, sim.State.PlacementLog.Count, "no placement was refused");
            Assert.GreaterOrEqual(sim.State.BuildBudget, 0f);
        }

        [Test]
        public void ItStopsWhenThePieceInHandIsTooExpensive()
        {
            var config = new SimConfig { StartBudget = 2f }; // cheapest piece costs 3
            Simulation sim = Sim(TestEpisodes.SmallMap, config);

            List<SimCommand> sent = Build(sim, new GreedyDetourPolicy());

            Assert.AreEqual(0, sent.Count);
            Assert.AreEqual(0, sim.State.PlacementLog.Count);
        }

        [Test]
        public void ItRespectsThePlacementCap()
        {
            Simulation sim = Sim(TestEpisodes.SmallMap);
            Build(sim, new GreedyDetourPolicy { MaxPlacementsPerIntermission = 2 });
            Assert.AreEqual(2, sim.State.PlacementLog.Count);
        }

        [Test]
        public void TheSameBoardGivesTheSameChoices()
        {
            Simulation a = Sim(TestEpisodes.SmallMap);
            Simulation b = Sim(TestEpisodes.SmallMap);
            Build(a, new GreedyDetourPolicy());
            Build(b, new GreedyDetourPolicy());
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        private static Simulation Sim(string ascii, SimConfig config = null)
        {
            config = config ?? new SimConfig();
            AsciiFixture f = AsciiMap.Parse(ascii);
            return new Simulation(config, f.Map, TestShapes.SampleSceneLibrary(), 1, new EscalationPlanner(config, f.Map));
        }

        private static int SpawnCost(Simulation sim) { return sim.State.Grid.Get(sim.Map.Spawns[0]).BestCost; }

        private static List<SimCommand> Build(Simulation sim, IPlayerPolicy policy)
        {
            var sent = new List<SimCommand>();
            Action<SimCommand> send = c =>
            {
                sent.Add(c);
                sim.Enqueue(c);
                sim.FlushInput();
            };
            policy.OnIntermission(sim, send, new Pcg32(1, RngStreams.Policy));
            return sent;
        }
    }
}
