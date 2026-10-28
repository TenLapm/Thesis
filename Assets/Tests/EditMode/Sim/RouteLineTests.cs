using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The route lines walk the agents' own step rule (SimGrid.NextStep, 2026-10-28), lures
    // included, so the tiles a line shows are the tiles an enemy walks.
    public class RouteLineTests
    {
        private sealed class OneGroup : IWavePlanner
        {
            public AgentGroup Group;
            public string Name => "one";
            public WavePlan PlanWave(WaveContext context) { return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "one", Groups = new[] { Group.Copy() } }; }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        [Test]
        public void AnEnemyWalksTheTilesItsRouteLineShowsAlsoWhenALureBendsIt()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 100f, CoreMaxHp = 1000, WaitForStart = true, BuildMenu = true, ChoiceUpgradeCost = 0f };
            MapData map = TestMaps.Parse(@"
                . . . . . . . . .
                . . . . . . . . .
                S . . . . . . . C
                . . . . . . . . .
                . . . . . . . . .").Map;
            TowerDef lure = TestTowers.Gun(damage: 0.01f, range: 1f, cost: 5);
            lure.Upgrades = new[] { new TowerLevelMod { Id = "bonfire", Label = "Bonfire", Lure = true } };
            var planner = new OneGroup();
            var sim = new Simulation(config, map, new[] { new ShapeDef { Name = "Wall", BuildCost = 1, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } } },
                                     new[] { lure }, 1, planner);
            AgentGroup g = EnemyArchetypes.Basic().Apply(new EscalationPlanner(config, map).PlanFor(1).Groups[0], 1f);
            g.Count = 1;
            g.Hp = 100000f;
            planner.Group = g;
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.BuyTower(0, 5, 0));
            TestSims.Send(sim, SimCommand.ChooseUpgrade(5, 0, 0));
            Assert.IsTrue(s.Towers[0].Lure);
            Assert.Greater(s.Grid.LureGoals, 0);

            var line = new List<SimNode>();
            Route.CollectSteps(s.Grid, s.Grid.Get(map.Spawns[0]), line, MovementClass.Ground, false);
            Assert.AreEqual(new TileCoord(5, 0), new TileCoord(line[line.Count - 1].X, line[line.Count - 1].Y), "the line ends at the lure, off the straight way");

            TestSims.Send(sim, SimCommand.StartWaveNow());
            var walked = new List<SimNode>();
            for (int tick = 0; tick < 3000; tick++)
            {
                sim.Tick();
                if (s.Live.Count == 0) continue;
                SimNode at = s.Grid.NodeFromPosition(s.Live[0].Position);
                if (walked.Count == 0 || walked[walked.Count - 1] != at) walked.Add(at);
                if (at == line[line.Count - 1]) break;
            }
            // Every tile of the line, in order, and the walk ends where the line does. An agent
            // re-aims as it crosses into each tile, so a diagonal step passes a corner and may touch
            // a side tile on the way; those are the only tiles it walks that the line does not show.
            Assert.AreEqual(line[line.Count - 1], walked[walked.Count - 1], "it ends at the lure, as the line does");
            int k = 0;
            foreach (SimNode n in walked) if (k < line.Count && n == line[k]) k++;
            Assert.AreEqual(line.Count, k, "every tile of the line, in order: " + string.Join(" ", Coords(walked)));
            foreach (SimNode n in walked)
                if (!line.Contains(n)) Assert.IsTrue(line.Exists(m => System.Math.Abs(m.X - n.X) <= 1 && System.Math.Abs(m.Y - n.Y) <= 1), "an off-line tile is beside the line");
        }

        private static List<string> Coords(List<SimNode> nodes)
        {
            var list = new List<string>();
            foreach (SimNode n in nodes) list.Add(n.X + "," + n.Y);
            return list;
        }
    }
}
