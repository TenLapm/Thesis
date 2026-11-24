using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The student, 2026-11-24, after an enemy crossed the brazier's outermost lit tile unshot:
    // "shoot by lit tiles". With SimConfig.TileRange a tower reaches an enemy when the tile the
    // enemy stands on has its centre within range: exactly the tiles the game lights up.
    public class TileRangeTests
    {
        private const string Open = @"
            S . . . . . . . . . . . .
            . . . . . . . . . . . . .
            . . . . . . . . . . . . .
            . . . . . . . . . . . . .
            . . . . . . . . . . . . .
            . . . . . . . . . . . . C";

        private static (TowerState tower, SimGrid grid, SimNode core) Brazier()
        {
            AsciiFixture f = AsciiMap.Parse(Open);
            var grid = new SimGrid(f.Map);
            new FlowField().Generate(grid, f.Map.Core);
            TowerState tower = TestTowers.StateOn(f, 0, TestTowers.Gun(range: 2f), 4, 2);
            return (tower, grid, grid.Get(f.Map.Core));
        }

        // Two tiles out in a straight line, but off the tile's centre line: inside the lit tile,
        // outside the circle.
        private static AgentState OffCentreOnTheTipTile(SimGrid grid)
        {
            Vec2f centre = grid[6, 2].Position;
            return TestTowers.Agent(0, new Vec2f(centre.X, (float)(centre.Y + (float)(grid.TileSize * 0.4f))));
        }

        [Test]
        public void AnEnemyAnywhereOnALitTileIsInRange()
        {
            var (tower, grid, core) = Brazier();
            var live = new List<AgentState> { OffCentreOnTheTipTile(grid) };
            Assert.AreEqual(-1, Targeting.Pick(TargetingMode.First, tower, grid, core, live), "the circle: out");
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.First, tower, grid, core, live, rangeByTile: true), "the lit tile: in");
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.Nearest, tower, grid, core, live, rangeByTile: true), "in every order");
        }

        [Test]
        public void ATileWhoseCentreIsOutOfReachIsNotLitAndNotInRange()
        {
            var (tower, grid, core) = Brazier();
            // Tile (6, 3): its centre is sqrt(5) tiles away, beyond 2. The near corner is inside the circle.
            Vec2f centre = grid[6, 3].Position;
            float back = (float)(grid.TileSize * 0.45f);
            var live = new List<AgentState> { TestTowers.Agent(0, new Vec2f((float)(centre.X - back), (float)(centre.Y - back))) };
            Assert.AreEqual(0, Targeting.Pick(TargetingMode.First, tower, grid, core, live), "the circle reaches the corner");
            Assert.AreEqual(-1, Targeting.Pick(TargetingMode.First, tower, grid, core, live, rangeByTile: true), "an unlit tile is out");
            Assert.IsFalse(Simulation.InPowerReach(tower.Tile, tower.RangeTiles, new TileCoord(6, 3)), "and the game does not light it");
        }

        [Test]
        public void TheSettingIsOffInTheDefaultsAndOnInTheGame()
        {
            Assert.IsFalse(new SimConfig().TileRange, "the tests' and pinned replays' older rule");
            SimConfig game = Json.Deserialize<SimConfig>(System.IO.File.ReadAllText(System.IO.Path.Combine(TestPaths.Maps, "SimConfig.json")));
            Assert.IsTrue(game.TileRange);
        }
    }
}
