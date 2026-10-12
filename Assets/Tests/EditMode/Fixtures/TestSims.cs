using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    public static class TestSims
    {
        // The open 38 x 38 board, spawn (3,3) and core (34,34): the game's own map until
        // WP-C4, kept as a fixture (Maps/Fixture_Open38.map.json) because the rigs of many
        // tests are written in its coordinates. With TestShapes' tetrominoes it is the
        // game as it was; the rules it exercises are the rules the game still has.
        public static MapData Open38Map() { return MapData.Load(TestPaths.MapFile("Fixture_Open38")); }

        // The map and the wall shapes the game is played on today, as exported from
        // SampleScene: 24 x 24 with rocks, and one shape of one tile (WP-C4).
        public static MapData GameMap() { return MapData.Load(TestPaths.MapFile("SampleScene")); }

        public static ShapeDef[] GameShapes() { return ShapeLibraryFile.Load(TestPaths.ShapesFile); }

        public static Simulation Game(ulong seed = 1, SimConfig config = null, IWavePlanner planner = null)
        {
            MapData map = GameMap();
            config = config ?? new SimConfig();
            return new Simulation(config, map, GameShapes(), TestTowers.Roster(), seed, planner ?? new EscalationPlanner(config, map));
        }

        public static Simulation Open38(ulong seed = 1, SimConfig config = null, IWavePlanner planner = null)
        {
            MapData map = Open38Map();
            config = config ?? new SimConfig();
            return new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), seed, planner ?? new EscalationPlanner(config, map));
        }

        // towers: the roster PlaceTower commands choose from; the game's placeholder
        // roster when not given.
        public static Simulation Ascii(string text, IWavePlanner planner, SimConfig config = null, ulong seed = 1, TowerDef[] towers = null)
        {
            AsciiFixture f = AsciiMap.Parse(text);
            return new Simulation(config ?? new SimConfig(), f.Map, TestShapes.SampleSceneLibrary(), towers ?? TestTowers.Roster(), seed, planner);
        }

        // Sends a command the way a host does: applied at once, without a tick.
        public static void Send(Simulation sim, SimCommand command)
        {
            sim.Enqueue(command);
            sim.FlushInput();
        }

        // Ascii() gives the simulation the fixture's MAP only: '#' and 'H' walls are
        // run-time state, not map data, so a fresh simulation starts without them.
        // This copies them onto the simulation's grid as scenario setup.
        public static Simulation AsciiWithWalls(string text, IWavePlanner planner, SimConfig config = null, ulong seed = 1, TowerDef[] towers = null)
        {
            AsciiFixture f = AsciiMap.Parse(text);
            var sim = new Simulation(config ?? new SimConfig(), f.Map, TestShapes.SampleSceneLibrary(), towers ?? TestTowers.Roster(), seed, planner);
            SimGrid grid = sim.State.Grid;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode from = f.Grid.ByIndex(i);
                if (from.HasWall) grid.SetWall(grid.ByIndex(i), from.TerrainCost, from.WallHealth);
            }
            sim.RebuildFieldForSetup();
            return sim;
        }

        public static void Run(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Tick();
        }

        // Ticks until the next wave resolves (or the game ends) and returns every
        // event raised on the way. Throws if that takes longer than maxTicks.
        public static List<SimEvent> RunUntilWaveResolves(Simulation sim, int maxTicks = 8000)
        {
            var all = new List<SimEvent>();
            int resolved = sim.State.LastOutcome == null ? 0 : sim.State.LastOutcome.WaveIndex;
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Tick();
                all.AddRange(sim.LastTickEvents);
                if (sim.State.LastOutcome != null && sim.State.LastOutcome.WaveIndex > resolved) return all;
                if (sim.State.IsGameOver) return all;
            }
            throw new System.InvalidOperationException("rig: no wave resolved within " + maxTicks + " ticks (wave " + sim.State.WaveIndex + ", phase " + sim.State.Phase + ").");
        }
    }

    // A reproducible stream of player input: at a fixed cadence, one of place a wall
    // piece / buy a shop offer / rotate / hold / sell the newest thing still standing
    // / reroll the shop. Many of them are illegal, unaffordable or out of phase and
    // get ignored, which is fine - the point is identical input to two simulations,
    // including input the game rejects.
    //
    // A sale needs something to aim at, so that one is worked out when it is fed,
    // from the simulation's own build log. Two simulations in the same state get
    // the same command.
    public sealed class CommandScript
    {
        private enum Op { PlaceShape, PlaceTower, Rotate, Hold, SellNewest, Reroll }

        private struct Step
        {
            public Op Op;
            public int A, X, Y;
        }

        private readonly Dictionary<int, Step> byTick = new Dictionary<int, Step>();

        public CommandScript(ulong seed, int ticks, int every, int mapWidth, int mapHeight)
        {
            var rng = new Pcg32(seed, RngStreams.Policy);
            for (int t = 5; t < ticks; t += every)
            {
                int roll = rng.NextInt(12);
                var step = new Step { X = rng.NextInt(mapWidth), Y = rng.NextInt(mapHeight), A = rng.NextInt(4) };
                step.Op = roll < 4 ? Op.PlaceShape
                        : roll < 7 ? Op.PlaceTower
                        : roll < 9 ? Op.Rotate
                        : roll < 10 ? Op.Hold
                        : roll < 11 ? Op.SellNewest
                        : Op.Reroll;
                byTick[t] = step;
            }
        }

        public void Feed(Simulation sim)
        {
            Step step;
            if (!byTick.TryGetValue(sim.State.Tick, out step)) return;

            switch (step.Op)
            {
                case Op.PlaceShape: sim.Enqueue(SimCommand.PlaceShape(step.X, step.Y)); break;
                case Op.PlaceTower: sim.Enqueue(SimCommand.PlaceTower(step.A, step.X, step.Y)); break;
                case Op.Rotate: sim.Enqueue(SimCommand.Rotate()); break;
                case Op.Hold: sim.Enqueue(SimCommand.Hold()); break;
                case Op.Reroll: sim.Enqueue(SimCommand.RerollShop()); break;
                case Op.SellNewest:
                    // The most recent wall piece or tower with a tile still standing.
                    List<PlacementRecord> log = sim.State.PlacementLog;
                    for (int i = log.Count - 1; i >= 0; i--)
                    {
                        if (!log[i].IsPlacement) continue;
                        SimNode node = sim.State.Grid.Get(log[i].Tiles[0]);
                        if (log[i].Kind == PlacementKind.Tower && node.Occupant == Occupant.Tower && node.TowerId == log[i].TowerId)
                        {
                            sim.Enqueue(SimCommand.SellTower(node.X, node.Y));
                            break;
                        }
                        if (log[i].Kind == PlacementKind.Wall && node.Occupant == Occupant.Wall && node.PieceId == i)
                        {
                            sim.Enqueue(SimCommand.SellWall(node.X, node.Y));
                            break;
                        }
                    }
                    break;
            }
        }
    }
}
