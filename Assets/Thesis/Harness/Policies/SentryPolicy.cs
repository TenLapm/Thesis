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

        // Above 0: only the last so many tiles of each route count, so the towers gather
        // round the core, where every route ends whichever side it starts from (the
        // "turtle", 2026-10-13). 0 = the whole route.
        public int RouteTail;

        public const string TurtleId = "turtle";

        private readonly List<SimNode> route = new List<SimNode>();
        private readonly List<TileCoord> routeTiles = new List<TileCoord>();
        private readonly TileCoord[] one = new TileCoord[1];
        private bool[] onRoute = new bool[0];
        private bool[] tried = new bool[0];

        // Spend what the cards leave on tower levels. Off by default: the pinned
        // episodes and the older tests describe a player who only buys.
        public bool UpgradeWithWhatIsLeft;

        public const string UpgraderId = "upgrader";

        public string Name => RouteTail > 0 ? TurtleId : UpgradeWithWhatIsLeft ? UpgraderId : Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return; // a finished game takes no input; do not mistake that for a refused placement
            TowerDef[] roster = sim.TowerLibrary;

            // Cards that are not towers (2026-10-12). A coin card is free budget: take it
            // first. Gear goes onto the first tower with a free slot.
            // In a draft (a limited number of picks) a coin is what is taken when no
            // tower is left to take, so there it comes last.
            bool draft = s.Shop.PicksLeft >= 0;
            SimConfig config = sim.Config;

            // With rerolls on, a draft that offers nothing this player can take (every
            // card a tower the cap refuses) is rolled again, a few times at most.
            for (int round = 0; ; round++)
            {
                TakeCards(sim, send, roster, draft);
                bool stuck = draft && s.Shop.MayPick && config.RerollCost >= 0f && s.BuildBudget >= sim.RerollPrice;
                if (!stuck || round >= MaxRerollsPerPhase) break;
                send(SimCommand.RerollShop());
            }

            // The gear shop: whatever is on sale and fits on a tower.
            for (int slot = 0; slot < s.GearShop.Offers.Length; slot++)
            {
                TowerOffer item = s.GearShop.Offers[slot];
                if (item.Bought || s.BuildBudget < item.Price) continue;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (!tower.IsAlive || tower.OnBench || tower.FreeGearSlot() < 0) continue;
                    send(SimCommand.BuyShopGear(slot, tower.Tile.X, tower.Tile.Y));
                    break;
                }
            }

            // What is left goes into levels, cheapest upgrade first, while it lasts.
            if (!UpgradeWithWhatIsLeft || !(config.UpgradeCostFactor > 0f)) return;
            for (int guard = 0; guard < 64; guard++)
            {
                TowerState best = null;
                int bestCost = int.MaxValue;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (!tower.IsAlive || tower.Level >= TowerState.MaxLevel) continue;
                    int cost = TowerUpgrades.CostOf(tower.Def, tower.Level, sim.Config);
                    if (cost < bestCost) { best = tower; bestCost = cost; }
                }
                if (best == null || s.BuildBudget < bestCost) break;
                send(SimCommand.UpgradeTower(best.Tile.X, best.Tile.Y));
            }
        }

        public int MaxRerollsPerPhase = 2;

        // The cards of the current roll: towers, then gear, and coins first or last.
        private void TakeCards(Simulation sim, Action<SimCommand> send, TowerDef[] roster, bool draft)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;
            if (!draft) TakeCoins(s, send);

            // With combining on, a card of a tower already on the board comes first: it
            // is a step towards a level. Otherwise one pass, in slot order, as before.
            int placed = 0;
            for (int pass = config.CombineCount > 1 ? 0 : 1; pass < 2; pass++)
            for (int slot = 0; slot < s.Shop.Offers.Length && placed < MaxTowersPerPhase; slot++)
            {
                TowerOffer offer = s.Shop.Offers[slot];
                if (!s.Shop.MayPick) break;
                if (offer.Bought || offer.Kind != CardKind.Tower || s.BuildBudget < offer.Price) continue;

                TowerDef def = roster[offer.TowerIndex];
                if (pass == 0 && TowerCombine.Copies(s.Towers, def, offer.Level) == 0) continue;
                if (!TowerCombine.HasRoomFor(s.Towers, def, offer.Level, config, s.WaveIndex)) continue;
                TileCoord tile;
                if (!FindBestTile(sim, def, out tile)) continue;

                int before = s.Towers.Count;
                send(SimCommand.PlaceTower(slot, tile.X, tile.Y));
                if (s.Towers.Count == before)
                    throw new InvalidOperationException("[Harness] SentryPolicy chose " + def.Id + " (shop slot " + slot + ") at " + tile
                                                        + ", but the simulation refused it. The policy's legality check and Placement.CanPlace disagree.");
                placed++;
            }

            for (int slot = 0; slot < s.Shop.Offers.Length; slot++)
            {
                TowerOffer card = s.Shop.Offers[slot];
                if (!s.Shop.MayPick) break;
                if (card.Bought || card.Kind != CardKind.Gear || s.BuildBudget < card.Price) continue;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (!tower.IsAlive || tower.OnBench || tower.FreeGearSlot() < 0) continue;
                    send(SimCommand.BuyGear(slot, tower.Tile.X, tower.Tile.Y));
                    break;
                }
            }
            if (draft) TakeCoins(s, send);
        }

        private static void TakeCoins(SimState s, Action<SimCommand> send)
        {
            for (int slot = 0; slot < s.Shop.Offers.Length; slot++)
            {
                TowerOffer card = s.Shop.Offers[slot];
                if (!card.Bought && card.Kind == CardKind.Coin && s.Shop.MayPick) send(SimCommand.TakeCoin(slot));
            }
        }

        public bool FindBestTile(Simulation sim, TowerDef def, out TileCoord best)
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

            // Where enemies will walk: from the entries of the wave that was announced
            // (SimConfig.PlanAhead), else from every spawn of a map with a few, else from
            // the first (a generated map has dozens of entry points).
            routeTiles.Clear();
            WavePlan next = sim.State.NextPlan;
            int sources = next != null ? next.Groups.Length : map.Spawns.Length <= 4 ? map.Spawns.Length : 1;
            for (int k = 0; k < sources; k++)
            {
                int sp = next != null ? next.Groups[k].SpawnIndex : k;
                Route.Collect(grid, grid.Get(map.Spawns[sp]), route);
                for (int i = RouteTail > 0 ? Math.Max(0, route.Count - RouteTail) : 0; i < route.Count; i++)
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
