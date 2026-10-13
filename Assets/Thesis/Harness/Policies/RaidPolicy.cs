using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // A stand-in player for the rules of 2026-10-13: few, heavy actions. It exists so
    // that every one of those rules runs in a recorded episode on both runtimes, and
    // to give a first number for how a simple player fares. It is not a good player.
    //
    // Each build phase:
    //   the core    once, before the first wave, a few tiles from where the map put it
    //   the card    merged into a tower of its type if it has one; else onto the board
    //               (SentryPolicy's tile: where the most of the announced route is in
    //               range); else onto the bench; else walls, coin or gear
    //   the moves   a benched tower goes onto the board while there is room; on even
    //               waves it changes places with the tower that has done the least; every
    //               third wave the weakest tower is moved to a better tile
    //   the walls   a few tiles from the stock onto the announced route; on even waves
    //               the oldest wall is taken up first
    //   the budget  a piece from the gear shop, a pack of walls, an extra move
    public sealed class RaidPolicy : IPlayerPolicy
    {
        public const string Id = "raider";

        private static readonly int[] CoreDx = { -3, 3, 0, 0, -2, 2 };
        private static readonly int[] CoreDy = { 2, -2, 3, -3, -2, 2 };

        private readonly SentryPolicy sentry = new SentryPolicy();
        private readonly List<SimNode> route = new List<SimNode>();
        private readonly TileCoord[] one = new TileCoord[1];
        private bool coreDone;

        public int WallsPerPhase = 4;

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return;
            SimConfig config = sim.Config;

            if (config.CorePlacement && !coreDone && s.WaveIndex == 0)
            {
                coreDone = true;
                for (int k = 0; k < CoreDx.Length; k++)
                {
                    TileCoord before = s.Core;
                    send(SimCommand.PlaceCore(s.Core.X + CoreDx[k], s.Core.Y + CoreDy[k]));
                    if (s.Core != before) return; // the wave is planned again; the runner calls back
                }
            }

            // Walls first: they bend the route, and the towers then go where it runs.
            LayWalls(sim, send);
            TakeCards(sim, send);
            MergeSets(sim, send);
            UseMoves(sim, send);
            Spend(sim, send);
        }

        // ------------------------------------------------------------------ the card

        private void TakeCards(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;
            for (int guard = 0; guard < 8 && s.Shop.MayPick; guard++)
            {
                int log = s.PlacementLog.Count;
                TowerOffer[] offers = s.Shop.Offers;

                // A duplicate first: it is a level.
                for (int slot = 0; slot < offers.Length && s.PlacementLog.Count == log && config.FeedDuplicates; slot++)
                {
                    if (offers[slot].Bought || offers[slot].Kind != CardKind.Tower || s.BuildBudget < offers[slot].Price) continue;
                    TowerDef def = sim.TowerLibrary[offers[slot].TowerIndex];
                    for (int t = 0; t < s.Towers.Count && s.PlacementLog.Count == log; t++)
                    {
                        TowerState tower = s.Towers[t];
                        if (!tower.IsAlive || tower.Def != def) continue;
                        if (tower.OnBench) send(SimCommand.FeedCardOnBench(slot, Array.IndexOf(s.Bench, tower.Id)));
                        else send(SimCommand.FeedCard(slot, tower.Tile.X, tower.Tile.Y));
                    }
                }

                // A tower onto the board, or onto the bench when the board is full.
                for (int slot = 0; slot < offers.Length && s.PlacementLog.Count == log; slot++)
                {
                    if (offers[slot].Bought || offers[slot].Kind != CardKind.Tower || s.BuildBudget < offers[slot].Price) continue;
                    TowerDef def = sim.TowerLibrary[offers[slot].TowerIndex];
                    TileCoord tile;
                    if (TowerCombine.HasRoomFor(s.Towers, def, offers[slot].Level, config, s.WaveIndex) && sentry.FindBestTile(sim, def, out tile))
                    {
                        send(SimCommand.PlaceTower(slot, tile.X, tile.Y));
                        continue;
                    }
                    int free = Array.IndexOf(s.Bench, TowerBench.Empty);
                    if (free >= 0) send(SimCommand.BenchCard(slot, free));
                }

                // Walls, then a coin, then gear.
                for (int slot = 0; slot < offers.Length && s.PlacementLog.Count == log; slot++)
                    if (!offers[slot].Bought && (offers[slot].Kind == CardKind.Wall || offers[slot].Kind == CardKind.Coin)) send(SimCommand.TakeCoin(slot));
                for (int slot = 0; slot < offers.Length && s.PlacementLog.Count == log; slot++)
                {
                    if (offers[slot].Bought || offers[slot].Kind != CardKind.Gear || s.BuildBudget < offers[slot].Price) continue;
                    TowerState tower = FirstWithFreeGearSlot(s);
                    if (config.GearInventorySlots > 0) send(SimCommand.StoreGearCard(slot));
                    else if (tower != null) send(SimCommand.BuyGear(slot, tower.Tile.X, tower.Tile.Y));
                }

                if (s.PlacementLog.Count == log) break; // nothing on offer could be taken
            }
        }

        // ------------------------------------------------------------------ merging

        // Every complete set it owns is merged: the newest copy onto the oldest.
        private static void MergeSets(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            if (!sim.Config.FeedDuplicates) return;
            for (int guard = 0; guard < 8; guard++)
            {
                int log = s.PlacementLog.Count;
                for (int i = 0; i < s.Towers.Count && s.PlacementLog.Count == log; i++)
                {
                    for (int j = s.Towers.Count - 1; j > i && s.PlacementLog.Count == log; j--)
                    {
                        TowerState target = s.Towers[i], source = s.Towers[j];
                        if (!TowerMerge.CanMerge(s, sim.Config, source, target)) continue;
                        send(SimCommand.MergeTower(source.Tile.X, source.Tile.Y, source.OnBench ? Array.IndexOf(s.Bench, source.Id) : -1,
                                                   target.Tile.X, target.Tile.Y, target.OnBench ? Array.IndexOf(s.Bench, target.Id) : -1));
                    }
                }
                if (s.PlacementLog.Count == log) break;
            }
        }

        // ------------------------------------------------------------------ the moves

        private void UseMoves(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;
            bool swapped = false, moved = false;
            for (int guard = 0; guard < 6; guard++)
            {
                if (config.MovesPerWave > 0 && s.MovesLeft <= 0) break;
                int log = s.PlacementLog.Count;
                int benchSlot = FirstBenched(s);
                TowerState weakest = Weakest(s);
                int cap = sim.TowerCap;

                if (benchSlot >= 0 && (cap <= 0 || TowerCombine.Standing(s.Towers) < cap))
                {
                    TileCoord tile;
                    if (sentry.FindBestTile(sim, s.Towers[s.Bench[benchSlot]].Def, out tile)) send(SimCommand.DeployTower(benchSlot, tile.X, tile.Y));
                }
                else if (benchSlot >= 0 && weakest != null && s.WaveIndex % 2 == 0 && !swapped)
                {
                    swapped = true;
                    send(SimCommand.DeployTower(benchSlot, weakest.Tile.X, weakest.Tile.Y));
                }
                else if (config.MoveTowers && weakest != null && s.WaveIndex % 3 == 0 && s.WaveIndex > 0 && !moved)
                {
                    moved = true;
                    TileCoord tile;
                    if (sentry.FindBestTile(sim, weakest.Def, out tile)) send(SimCommand.MoveTower(weakest.Tile.X, weakest.Tile.Y, tile.X, tile.Y));
                }
                if (s.PlacementLog.Count == log) break;
            }
        }

        // ------------------------------------------------------------------ the walls

        private void LayWalls(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            if (sim.Config.WallStock <= 0) return;

            // On even waves the oldest wall still standing is taken up first.
            if (s.WaveIndex > 0 && s.WaveIndex % 2 == 0 && s.RelaysLeft > 0)
            {
                SimNode oldest = null;
                for (int i = 0; i < s.Grid.NodeCount; i++)
                {
                    SimNode n = s.Grid.ByIndex(i);
                    if (n.Occupant == Occupant.Wall && n.PieceId >= 0 && n.PieceId < s.PhaseLogStart && (oldest == null || n.PieceId < oldest.PieceId)) oldest = n;
                }
                if (oldest != null) send(SimCommand.SellWall(oldest.X, oldest.Y));
            }

            WavePlan next = s.NextPlan;
            int entry = next != null && next.Groups.Length > 0 ? next.Groups[0].SpawnIndex : 0;
            int laid = 0;
            for (int guard = 0; guard < WallsPerPhase && s.WallsLeft > 0; guard++)
            {
                // The route changes with every tile laid, so it is read again each time.
                Route.Collect(s.Grid, s.Grid.Get(sim.Map.Spawns[entry]), route);
                bool placed = false;
                for (int i = route.Count - 5 - 2 * laid; i >= 3 && !placed; i -= 3)
                {
                    if (i >= route.Count) continue;
                    one[0] = new TileCoord(route[i].X, route[i].Y);
                    if (!Placement.CanPlace(s.Grid, sim.Map, one)) continue;
                    send(SimCommand.PlaceShape(one[0].X, one[0].Y));
                    placed = true;
                }
                if (!placed) break;
                laid++;
            }
        }

        // ------------------------------------------------------------------ the budget

        private void Spend(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;

            for (int slot = 0; slot < s.GearShop.Offers.Length; slot++)
            {
                TowerOffer item = s.GearShop.Offers[slot];
                TowerState tower = FirstWithFreeGearSlot(s);
                if (item.Bought || tower == null || s.BuildBudget < item.Price) continue;
                if (config.GearInventorySlots > 0) send(SimCommand.StoreShopGear(slot));
                else send(SimCommand.BuyShopGear(slot, tower.Tile.X, tower.Tile.Y));
            }

            // Spare gear from sold and merged towers: onto a tower with room, else sold.
            for (int guard = 0; guard < 16 && s.GearOverflow.Count > 0; guard++)
            {
                TowerState room = FirstWithFreeGearSlot(s);
                if (room != null) send(SimCommand.EquipOverflow(0, room.Tile.X, room.Tile.Y));
                else send(SimCommand.SellOverflow(0));
            }

            // The inventory: every fourth wave the oldest tower's first piece comes off
            // again; then whatever is in the inventory goes onto towers with room.
            if (config.GearInventorySlots > 0)
            {
                TowerState first = FirstWithGear(s);
                if (first != null && s.WaveIndex % 4 == 0 && s.WaveIndex > 0) send(SimCommand.UnequipGear(first.Tile.X, first.Tile.Y, 0));
                for (int i = 0; i < s.GearInventory.Length; i++)
                {
                    TowerState tower = FirstWithFreeGearSlot(s);
                    if (s.GearInventory[i] == TowerState.NoGear || tower == null) continue;
                    send(SimCommand.EquipGear(i, tower.Tile.X, tower.Tile.Y));
                }
            }
            if (config.WallStock > 0 && config.WallPackSize > 0 && s.BuildBudget >= config.WallPackPrice + 10f) send(SimCommand.BuyWalls());
            if (config.MovesPerWave > 0 && config.ExtraMovePrice >= 0f && s.WaveIndex % 4 == 0 && s.WaveIndex > 0 && s.BuildBudget >= sim.ExtraMovePrice + 10f)
            {
                send(SimCommand.BuyMove());
                UseMoves(sim, send);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static int FirstBenched(SimState s)
        {
            for (int i = 0; i < s.Bench.Length; i++) if (s.Bench[i] != TowerBench.Empty) return i;
            return -1;
        }

        // The tower on the board that has done the least damage (ties: the oldest).
        private static TowerState Weakest(SimState s)
        {
            TowerState weakest = null;
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!tower.IsAlive || tower.OnBench) continue;
                if (weakest == null || tower.DamageDealt < weakest.DamageDealt) weakest = tower;
            }
            return weakest;
        }

        private static TowerState FirstWithGear(SimState s)
        {
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (tower.IsAlive && !tower.OnBench && tower.Gear[0] != TowerState.NoGear) return tower;
            }
            return null;
        }

        private static TowerState FirstWithFreeGearSlot(SimState s)
        {
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (tower.IsAlive && !tower.OnBench && tower.FreeGearSlot() >= 0) return tower;
            }
            return null;
        }
    }
}
