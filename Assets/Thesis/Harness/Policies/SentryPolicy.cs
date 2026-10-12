using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // "Buy what the shop offers and put it where it can see the most of the route."
    // Each build phase it goes through the shop's offers in slot order, buys every
    // one the budget covers, and puts each tower on the free tile next to the route
    // from which the most route tiles are within that tower's range. It never
    // builds ON the route, so it does not make enemies dig.
    //
    // Counting tiles in range needs no flow-field rebuild, so this policy is cheap
    // (compare GreedyDetourPolicy, which rebuilds the field for every candidate).
    //
    // A stand-in player, not a good one: it takes the offers as they come, ignores
    // damage types, never sells, and keeps stacking towers on the same stretch of
    // route. SequencePolicy.Mixed() pairs it with GreedyDetourPolicy for a player
    // that builds walls as well.
    public sealed class SentryPolicy : IPlayerPolicy
    {
        public const string Id = "sentry";

        // The shop already limits a phase to its offers (three by default); this
        // is a second, lower limit for callers that want a weaker player.
        public int MaxTowersPerPhase = 6;

        // How far from the route a tower may stand, in tiles (Chebyshev distance).
        public int MaxDistanceFromRoute = 2;

        private readonly List<SimNode> route = new List<SimNode>();
        private readonly List<TileCoord> routeTiles = new List<TileCoord>();
        private readonly TileCoord[] one = new TileCoord[1];
        private bool[] onRoute = new bool[0];
        private bool[] tried = new bool[0];

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return; // a finished game takes no input; do not mistake that for a refused placement
            TowerDef[] roster = sim.TowerLibrary;

            int placed = 0;
            for (int slot = 0; slot < s.Shop.Offers.Length && placed < MaxTowersPerPhase; slot++)
            {
                TowerOffer offer = s.Shop.Offers[slot];
                if (offer.Bought || s.BuildBudget < offer.Price) continue;

                TowerDef def = roster[offer.TowerIndex];
                TileCoord tile;
                if (!FindBestTile(sim, def, out tile)) continue;

                int before = s.Towers.Count;
                send(SimCommand.PlaceTower(slot, tile.X, tile.Y));
                if (s.Towers.Count == before)
                    throw new InvalidOperationException("[Harness] SentryPolicy chose " + def.Id + " (shop slot " + slot + ") at " + tile
                                                        + ", but the simulation refused it. The policy's legality check and Placement.CanPlace disagree.");
                placed++;
            }
        }

        private bool FindBestTile(Simulation sim, TowerDef def, out TileCoord best)
        {
            SimGrid grid = sim.State.Grid;
            MapData map = sim.Map;
            if (onRoute.Length != grid.NodeCount)
            {
                onRoute = new bool[grid.NodeCount];
                tried = new bool[grid.NodeCount];
            }
            Array.Clear(onRoute, 0, onRoute.Length);
            Array.Clear(tried, 0, tried.Length);

            routeTiles.Clear();
            for (int sp = 0; sp < map.Spawns.Length; sp++)
            {
                Route.Collect(grid, grid.Get(map.Spawns[sp]), route);
                for (int i = 0; i < route.Count; i++)
                {
                    if (onRoute[route[i].Index]) continue;
                    onRoute[route[i].Index] = true;
                    routeTiles.Add(new TileCoord(route[i].X, route[i].Y));
                }
            }

            // Tile distances are whole numbers, so "in range" is an exact comparison.
            float rangeSquared = (float)(def.RangeTiles * def.RangeTiles);

            best = default(TileCoord);
            int bestCover = 0;

            // Candidates in a fixed order: along the route, then by offset. With a
            // strict '>' the first candidate found keeps a tie.
            for (int r = 0; r < routeTiles.Count; r++)
            {
                for (int dx = -MaxDistanceFromRoute; dx <= MaxDistanceFromRoute; dx++)
                {
                    for (int dy = -MaxDistanceFromRoute; dy <= MaxDistanceFromRoute; dy++)
                    {
                        var candidate = new TileCoord(routeTiles[r].X + dx, routeTiles[r].Y + dy);
                        if (!grid.InBounds(candidate)) continue;

                        int index = grid.Get(candidate).Index;
                        if (tried[index] || onRoute[index]) continue;
                        tried[index] = true;

                        one[0] = candidate;
                        if (!Placement.CanPlace(grid, map, one)) continue;

                        int cover = 0;
                        for (int k = 0; k < routeTiles.Count; k++)
                        {
                            int ddx = routeTiles[k].X - candidate.X;
                            int ddy = routeTiles[k].Y - candidate.Y;
                            if ((float)(ddx * ddx + ddy * ddy) <= rangeSquared) cover++;
                        }

                        if (cover > bestCover)
                        {
                            bestCover = cover;
                            best = candidate;
                        }
                    }
                }
            }

            return bestCover > 0;
        }
    }
}
