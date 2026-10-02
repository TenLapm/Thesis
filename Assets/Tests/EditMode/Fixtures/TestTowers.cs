using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    // Tower definitions for tests. Roster() is the game's placeholder roster; the
    // builders below make one-purpose towers that cost nothing, so a test can place
    // them without caring about the budget.
    public static class TestTowers
    {
        public static TowerDef[] Roster() { return TowerRoster.Placeholder(); }

        public static TowerDef Gun(float damage = 1f, float range = 3f, int interval = 25, DamageType type = DamageType.Physical, int cost = 0, string id = "gun")
        {
            return new TowerDef { Id = id, Cost = cost, DamageType = type, Damage = damage, RangeTiles = range, FireIntervalTicks = interval };
        }

        public static TowerDef Splash(float damage, float range, float splashTiles, int interval = 25)
        {
            return new TowerDef { Id = "splash", Cost = 0, DamageType = DamageType.Fire, Damage = damage, RangeTiles = range, FireIntervalTicks = interval, SplashRadiusTiles = splashTiles };
        }

        public static TowerDef Slower(int slowTicks, float slowFactor, float range = 3f, float damage = 0f, int interval = 25)
        {
            return new TowerDef { Id = "slower", Cost = 0, DamageType = DamageType.Frost, Damage = damage, RangeTiles = range, FireIntervalTicks = interval, SlowTicks = slowTicks, SlowFactor = slowFactor };
        }

        // A tower as TowerSystem sees it, without going through a Simulation: for
        // unit tests that call TowerSystem.Step directly.
        public static TowerState StateOn(AsciiFixture f, int id, TowerDef def, int x, int y)
        {
            return new TowerState(id, def, new TileCoord(x, y), f.Grid[x, y].Position, f.Map.NodeDiameter, 0);
        }

        public static AgentState Agent(int id, Vec2f position, float hp = 10f, float speed = 1f, float[] resist = null, float killReward = 0.2f)
        {
            return new AgentState(id, position, speed, hp, resist ?? DamageTypes.AllOnes(), 1f, killReward, 1f);
        }

        public static int Count(IList<SimEvent> events, SimEventKind kind)
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++) if (events[i].Kind == kind) n++;
            return n;
        }
    }
}
