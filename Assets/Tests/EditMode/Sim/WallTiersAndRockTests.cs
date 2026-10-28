using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // Wall tiers and mountains enemies can break (2026-10-28).
    public class WallTiersAndRockTests
    {
        private sealed class OneGroup : IWavePlanner
        {
            public AgentGroup Group;
            public string Name => "one";
            public WavePlan PlanWave(WaveContext context) { return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "one", Groups = new[] { Group.Copy() } }; }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        private static ShapeDef[] OneTile()
        {
            return new[] { new ShapeDef { Name = "Wall", BuildCost = 1, DigCost = 15, WallHealth = 6f, LocalTiles = new[] { new TileCoord(0, 0) } } };
        }

        private static AgentGroup Walker(SimConfig config, MapData map)
        {
            AgentGroup g = EnemyArchetypes.Basic().Apply(new EscalationPlanner(config, map).PlanFor(1).Groups[0], 1f);
            g.Count = 1;
            g.Hp = 100000f;
            return g;
        }

        [Test]
        public void AStoneAndAFortWallCostMoreAndAreTougherAndWithoutTiersTheTierIsIgnored()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 100f, WaitForStart = true, WallTiers = true };
            MapData map = TestMaps.Parse("S . . . . C").Map;
            var sim = new Simulation(config, map, OneTile(), new[] { TestTowers.Gun() }, 1, new EscalationPlanner(config, map));
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.PlaceShape(1, 0, 0));
            TestSims.Send(sim, SimCommand.PlaceShape(2, 0, 1));
            TestSims.Send(sim, SimCommand.PlaceShape(3, 0, 2));
            Assert.AreEqual(100f - 1f - 3f - 5f, s.BuildBudget);
            Assert.AreEqual(6f, s.Grid[1, 0].MaxWallHealth);
            Assert.AreEqual(config.StoneWallHealth, s.Grid[2, 0].MaxWallHealth);
            Assert.AreEqual(config.StoneWallDigCost, s.Grid[2, 0].TerrainCost);
            Assert.AreEqual(config.FortWallHealth, s.Grid[3, 0].MaxWallHealth);
            Assert.AreEqual(config.FortWallDigCost, s.Grid[3, 0].TerrainCost);

            config = new SimConfig { ShopOffers = 0, StartBudget = 100f, WaitForStart = true };
            sim = new Simulation(config, map, OneTile(), new[] { TestTowers.Gun() }, 1, new EscalationPlanner(config, map));
            TestSims.Send(sim, SimCommand.PlaceShape(2, 0, 2));
            Assert.AreEqual(99f, sim.State.BuildBudget, "no tiers: the plain wall");
            Assert.AreEqual(6f, sim.State.Grid[2, 0].MaxWallHealth);
        }

        [Test]
        public void ASapperPaysFullPriceForAMountainAndLessForAWall()
        {
            // 2026-10-28, the student: sappers dig walls, but must not always tunnel the same mountain.
            var config = new SimConfig { ShopOffers = 0, StartBudget = 100f, WaitForStart = true, RockHealth = 60f, RockDigCost = 60 };
            MapData map = TestMaps.Parse(@"
                S . X . . C
                . . . . . .").Map;
            var sim = new Simulation(config, map, OneTile(), new[] { TestTowers.Gun() }, 1, new EscalationPlanner(config, map));
            TestSims.Send(sim, SimCommand.PlaceShape(3, 1)); // the top row is y = 1
            SimNode rock = sim.State.Grid[2, 1], wall = sim.State.Grid[3, 1];
            Assert.IsTrue(rock.IsRock);
            Assert.AreEqual(60, FlowField.SapperTerrainCost(rock, config.SapperDigCostFactor), "a mountain: full price");
            Assert.Less(FlowField.SapperTerrainCost(wall, config.SapperDigCostFactor), wall.TerrainCost, "a wall: the sapper's discount");
        }

        // A ridge of rock between the entry and the core, open only at the bottom: the walk round
        // is long, so with a cheap rock (RockDigCost 2) the enemy digs through (3,3).
        private const string Ridge = @"
            . . . X . . .
            S . . X . . C
            . . . X . . .
            . . . X . . .
            . . . . . . .";

        [Test]
        public void ARockIsAToughWallThatCannotBeSoldAndPaysNothingWhenBroken()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 50f, CoreMaxHp = 1000, WaitForStart = true, RockHealth = 4f, RockDigCost = 2, SellRefund = 0.5f, StipendPerWave = 0 };
            MapData map = TestMaps.Parse(Ridge).Map;
            var planner = new OneGroup();
            var sim = new Simulation(config, map, OneTile(), new[] { TestTowers.Gun() }, 1, planner);
            planner.Group = Walker(config, map);
            SimState s = sim.State;
            SimNode rock = s.Grid[3, 3];
            Assert.IsTrue(rock.IsWalkable, "walkable, with a wall on it");
            Assert.IsTrue(rock.HasWall);
            Assert.IsTrue(rock.IsRock);
            Assert.AreEqual(2, rock.TerrainCost);
            Assert.AreNotEqual(SimNode.Infinity, s.Grid.Get(map.Spawns[0]).BestCost, "the core can be reached through rock");

            TestSims.Send(sim, SimCommand.SellWall(3, 3));
            Assert.IsTrue(s.Grid[3, 3].HasWall, "a mountain is nobody's to sell");
            Assert.AreEqual(50f, s.BuildBudget);

            TestSims.Send(sim, SimCommand.StartWaveNow());
            List<SimEvent> events = TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(1, events.FindAll(e => e.Kind == SimEventKind.WallBreached).Count, "it dug through");
            Assert.IsFalse(s.Grid[3, 3].HasWall);
            Assert.IsFalse(s.Grid[3, 3].IsRock, "the mountain is gone");
            Assert.AreEqual(1, s.LastOutcome.Leaked);
            Assert.AreEqual(50f, s.BuildBudget, 0.001f, "and a broken mountain pays nothing");
        }

        [Test]
        public void ATowerOnARockLeavesTheRockWhenItIsChewedThrough()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 50f, CoreMaxHp = 1000, WaitForStart = true, RockHealth = 4f, RockDigCost = 2, BuildMenu = true, TowersOnWalls = true };
            MapData map = TestMaps.Parse(Ridge).Map;
            var planner = new OneGroup();
            TowerDef gun = TestTowers.Gun(cost: 5);
            gun.TowerHealth = 2f;
            gun.DigCost = 2; // cheaper than the walk round, so the enemy digs it
            var sim = new Simulation(config, map, OneTile(), new[] { gun }, 1, planner);
            planner.Group = Walker(config, map);
            SimState s = sim.State;
            TestSims.Send(sim, SimCommand.BuyTower(0, 3, 3));
            Assert.AreEqual(1, s.Towers.Count, "a tower stands on a rock");
            Assert.IsTrue(s.Grid[3, 3].IsRock);

            TestSims.Send(sim, SimCommand.StartWaveNow());
            bool rockLeft = false;
            for (int tick = 0; tick < 4000 && !rockLeft; tick++)
            {
                sim.Tick();
                if (!s.Towers[0].IsAlive) rockLeft = s.Grid[3, 3].HasWall && s.Grid[3, 3].PieceId == SimNode.RockPiece;
            }
            Assert.IsTrue(rockLeft, "the tower went and the rock under it stayed");
            TestSims.RunUntilWaveResolves(sim);
            Assert.AreEqual(1, s.LastOutcome.Leaked, "then the rock too");
        }
    }
}
