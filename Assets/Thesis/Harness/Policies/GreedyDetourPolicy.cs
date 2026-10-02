using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // "Make the route as long as possible, one piece at a time." For the piece in
    // hand it tries a set of positions and rotations on a scratch copy of the grid,
    // rebuilds the flow field for each, and plays the one that raises the cost of
    // the spawn-to-core route the most. It repeats until the budget, the placement
    // cap or the useful positions run out.
    //
    // On open ground one piece almost never lengthens the route: there are millions
    // of equally short routes and a piece blocks only some of them. Judged by route
    // cost alone, the policy would place almost nothing (the first version did
    // exactly that and stopped after six pieces). So ties are broken by the NUMBER
    // of shortest routes left: when no piece adds cost, it plays the piece that
    // closes off the most of them, and keeps doing so until the cost has to rise.
    //
    // The piece is whatever the bag deals, so its price is fixed for each decision
    // and "most route cost" is the same as "most route cost per unit of budget".
    // It never uses the hold slot.
    //
    // This is a stand-in player, not a good one. It looks one piece ahead, so it
    // happily builds a wall that a later wall makes pointless.
    public sealed class GreedyDetourPolicy : IPlayerPolicy
    {
        public const string Id = "greedy";

        // Each candidate costs one full flow-field rebuild plus a route count (about
        // 0.6 ms together on the 38x38 map), so both limits exist to keep a 25-wave
        // episode in seconds. Candidates are spread evenly along the route, not
        // taken from its start, so the far end of a long maze is still considered.
        public int MaxPlacementsPerIntermission = 8;
        public int MaxOriginsPerPlacement = 24;

        private readonly FlowField field = new FlowField();
        private readonly List<SimNode> route = new List<SimNode>();
        private readonly List<TileCoord> anchors = new List<TileCoord>();
        private readonly SimNode[] neighbors = new SimNode[8];
        private int[] order = new int[0];
        private int[] costs = new int[0];
        private double[] routes = new double[0];

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return; // a finished game takes no input; do not mistake that for a refused placement
            for (int placed = 0; placed < MaxPlacementsPerIntermission; placed++)
            {
                ShapeDef shape = s.Bag.CurrentShape;
                if (shape == null || s.BuildBudget < shape.BuildCost) return;

                int turns;
                TileCoord origin;
                if (!FindBest(sim, shape, out turns, out origin)) return;

                for (int i = 0; i < turns; i++) send(SimCommand.Rotate());

                int before = s.PlacementLog.Count;
                send(SimCommand.PlaceShape(origin.X, origin.Y));
                if (s.PlacementLog.Count == before)
                    throw new InvalidOperationException("[Harness] GreedyDetourPolicy chose " + shape.Name + " at " + origin + " after " + turns
                                                        + " turns, but the simulation refused it. The policy's legality check and Placement.CanPlace disagree.");
            }
        }

        private bool FindBest(Simulation sim, ShapeDef shape, out int bestTurns, out TileCoord bestOrigin)
        {
            MapData map = sim.Map;
            SimGrid scratch = sim.State.Grid.Clone();
            int baseline = SpawnCost(scratch, map);
            double baselineRoutes = ShortestRouteCount(scratch, map);

            CollectAnchors(scratch, map);

            int terrainCost = Placement.WallTerrainCost(shape);
            float wallHealth = Placement.WallHealth(shape);
            ShapeDef rotated = shape.Clone();

            bool found = false;
            int bestGain = 0;
            double bestRoutes = baselineRoutes;
            bestTurns = 0;
            bestOrigin = default(TileCoord);

            for (int turns = 0; turns < 4; turns++)
            {
                if (turns > 0) rotated.Rotate();
                TileCoord first = rotated.LocalTiles[0];

                for (int a = 0; a < anchors.Count; a++)
                {
                    // Put the shape's first tile on the route tile, so every
                    // candidate actually touches the route.
                    var origin = new TileCoord(anchors[a].X - first.X, anchors[a].Y - first.Y);
                    TileCoord[] tiles = Placement.ToWorldTiles(rotated.LocalTiles, origin);
                    if (!Placement.CanPlace(scratch, map, tiles)) continue;

                    for (int i = 0; i < tiles.Length; i++) scratch.SetWall(scratch.Get(tiles[i]), terrainCost, wallHealth);
                    field.Generate(scratch, map.Core);
                    int gain = SpawnCost(scratch, map) - baseline;
                    double routesLeft = ShortestRouteCount(scratch, map);
                    for (int i = 0; i < tiles.Length; i++) scratch.ClearWall(scratch.Get(tiles[i]));

                    // More cost wins; at equal cost, fewer shortest routes wins.
                    // Strict comparisons: on a full tie the first candidate found
                    // stays, so the choice depends on nothing but the search order.
                    if (gain > bestGain || (gain == bestGain && routesLeft < bestRoutes))
                    {
                        found = true;
                        bestGain = gain;
                        bestRoutes = routesLeft;
                        bestTurns = turns;
                        bestOrigin = origin;
                    }
                }
            }

            return found;
        }

        // How many different cheapest routes lead from the spawns to the core, under
        // the flow field's own cost model: a step is charged for the tile being
        // LEFT (FlowField prices "neighbour -> current" by the neighbour's terrain).
        // Counted by walking the tiles from the core outward in cost order: a tile's
        // count is the sum over the neighbours it can step to at exactly its own cost.
        //
        // A double, because the count on open ground is already in the millions and
        // can pass 2^53 in a large maze. It is only ever compared, never stored.
        private double ShortestRouteCount(SimGrid grid, MapData map)
        {
            int n = grid.NodeCount;
            if (order.Length != n)
            {
                order = new int[n];
                costs = new int[n];
                routes = new double[n];
            }
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                costs[i] = grid.ByIndex(i).BestCost;
            }
            // Ties sort in no fixed order, which is harmless: two tiles at the same
            // cost can never feed each other, because every step costs at least 10.
            Array.Sort(costs, order);

            for (int k = 0; k < n; k++)
            {
                SimNode node = grid.ByIndex(order[k]);
                if (node.BestCost == SimNode.Infinity) break; // sorted: the rest are unreachable
                if (node.BestCost == 0)
                {
                    routes[node.Index] = 1.0;
                    continue;
                }

                double sum = 0.0;
                int count = grid.GetNeighbors(node, neighbors);
                for (int i = 0; i < count; i++)
                {
                    SimNode next = neighbors[i];
                    if (!next.IsWalkable || next.BestCost == SimNode.Infinity) continue;
                    bool diagonal = next.X != node.X && next.Y != node.Y;
                    int step = (diagonal ? FlowField.DiagonalCost : FlowField.CardinalCost) * node.TerrainCost;
                    if (next.BestCost + step == node.BestCost) sum += routes[next.Index];
                }
                routes[node.Index] = sum;
            }

            double total = 0.0;
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                SimNode spawn = grid.Get(map.Spawns[i]);
                if (spawn.BestCost != SimNode.Infinity) total += routes[spawn.Index];
            }
            return total;
        }

        // Route tiles from every spawn, thinned to at most MaxOriginsPerPlacement.
        // Must run before the scratch grid's field is regenerated: it walks NextIndex.
        private void CollectAnchors(SimGrid grid, MapData map)
        {
            anchors.Clear();
            for (int sp = 0; sp < map.Spawns.Length; sp++)
            {
                Route.Collect(grid, grid.Get(map.Spawns[sp]), route);
                for (int i = 0; i < route.Count; i++) anchors.Add(new TileCoord(route[i].X, route[i].Y));
            }

            if (anchors.Count <= MaxOriginsPerPlacement) return;

            int total = anchors.Count;
            for (int k = 0; k < MaxOriginsPerPlacement; k++) anchors[k] = anchors[(int)((long)k * total / MaxOriginsPerPlacement)];
            anchors.RemoveRange(MaxOriginsPerPlacement, total - MaxOriginsPerPlacement);
        }

        // Sum over spawns of the flow-field cost to the core. A spawn with no route
        // cannot happen here (walls are diggable), but guard the sum anyway.
        private static int SpawnCost(SimGrid grid, MapData map)
        {
            long sum = 0;
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                int cost = grid.Get(map.Spawns[i]).BestCost;
                if (cost == SimNode.Infinity) return int.MaxValue / 2;
                sum += cost;
            }
            return sum > int.MaxValue / 2 ? int.MaxValue / 2 : (int)sum;
        }
    }
}
