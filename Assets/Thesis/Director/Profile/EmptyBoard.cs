using System;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // The ground cost from every candidate entry to the core on the map with nothing built:
    // what maze_length divides by. Computed once per map (and core), not once per wave.
    //
    // "Nothing built" includes the rocks when they can be dug (SimConfig.RockHealth > 0, the
    // game): the student decided on 2026-11-10 that the rocks round the core are part of the
    // player's base, because the player chose where the core stands. With RockHealth 0 a rock
    // is impassable map geometry (SimNode.IsWalkable false), here exactly as on the real board.
    public sealed class EmptyBoard
    {
        private readonly int[] costs;

        private EmptyBoard(int[] costs) { this.costs = costs; }

        public int SpawnCount => costs.Length;

        // In field units (×10), or SimNode.Infinity if the entry cannot reach the core.
        public int Cost(int spawn) { return costs[spawn]; }

        public float Tiles(int spawn) { return ProfileInput.Tiles(costs[spawn]); }

        // The core is the map's: build this again after PlaceCore moves it.
        public static EmptyBoard For(MapData map, SimConfig config)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (config == null) throw new ArgumentNullException(nameof(config));
            return For(map, config, map.Core);
        }

        public static EmptyBoard For(MapData map, SimConfig config, TileCoord core)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (config == null) throw new ArgumentNullException(nameof(config));

            // The same grid the simulation makes (Simulation's constructor), before it puts the
            // rock walls on: with breakable rocks every rock tile is open ground here.
            var grid = new SimGrid(map, config.RockHealth > 0f);
            new FlowField().Generate(grid, core);

            var costs = new int[map.Spawns.Length];
            for (int i = 0; i < costs.Length; i++) costs[i] = grid.Get(map.Spawns[i]).BestCost;
            return new EmptyBoard(costs);
        }
    }
}
