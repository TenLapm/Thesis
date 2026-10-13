using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // What the player can do to a tower that already stands (2026-10-12): raise its
    // level, buy a piece of gear onto it, move gear from one tower to another, and
    // the one card that touches no tower at all, the coin card. Each is one row in
    // the build log, like building and selling, so telemetry and the build profile
    // see them; none of them puts anything on a tile or takes anything off, so none
    // rebuilds a flow field.
    //
    // Every method returns false, changing nothing, when the move is not possible.
    public static class TowerUpgrades
    {
        // What raising a tower from `level` to the next costs: its card's price at
        // level 1, half as much again at level 2, times the config's factor.
        public static int CostOf(TowerDef def, int level, SimConfig config)
        {
            float steps = (float)(0.5f + (float)(0.5f * level));
            return (int)(float)((float)(def.Cost * steps) * config.UpgradeCostFactor);
        }

        // The price of a tower card at `level`: the tower and every upgrade up to it.
        public static int CardPrice(TowerDef def, int level, SimConfig config)
        {
            int price = def.Cost;
            for (int l = 1; l < level; l++) price += CostOf(def, l, config);
            return price;
        }

        private static TowerState TowerAt(SimGrid grid, TileCoord tile, IList<TowerState> towers)
        {
            if (!grid.InBounds(tile)) return null;
            SimNode node = grid.Get(tile);
            if (node.Occupant != Occupant.Tower) return null;
            TowerState tower = towers[node.TowerId];
            return tower.IsAlive ? tower : null;
        }

        public static bool TryUpgrade(SimGrid grid, TileCoord tile, SimConfig config, GearDef[] gear, ref float buildBudget, int tick,
                                      IList<TowerState> towers, IList<PlacementRecord> log, IList<SimEvent> events)
        {
            TowerState tower = TowerAt(grid, tile, towers);
            if (tower == null || tower.Level >= TowerState.MaxLevel) return false;

            int cost = CostOf(tower.Def, tower.Level, config);
            if (buildBudget < cost) return false;

            buildBudget -= cost;
            tower.Level++;
            tower.PricePaid += cost;
            tower.Recompute(gear);
            events?.Add(SimEvent.TowerChanged(tower.Id));
            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerUpgraded, Tick = tick, ShapeName = tower.Def.Id, OriginX = tile.X, OriginY = tile.Y,
                Tiles = new[] { tile }, TowerId = tower.Id, Cost = cost,
            });
            return true;
        }

        public static bool TryBuyGear(SimGrid grid, TileCoord tile, int gearIndex, int price, GearDef[] gear, ref float buildBudget, int tick,
                                      IList<TowerState> towers, IList<PlacementRecord> log, IList<SimEvent> events)
        {
            if (gearIndex < 0 || gearIndex >= gear.Length) return false;
            TowerState tower = TowerAt(grid, tile, towers);
            if (tower == null) return false;
            int slot = tower.FreeGearSlot();
            if (slot < 0 || buildBudget < price) return false;

            buildBudget -= price;
            tower.Gear[slot] = gearIndex;
            tower.PricePaid += price;
            tower.Recompute(gear);
            events?.Add(SimEvent.TowerChanged(tower.Id));
            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.GearBought, Tick = tick, ShapeName = gear[gearIndex].Id, OriginX = tile.X, OriginY = tile.Y,
                Tiles = new[] { tile }, TowerId = tower.Id, Cost = price,
            });
            return true;
        }

        // Moves the gear in `slot` of the tower on `from` to the tower on `to`. Free.
        // What was paid for the piece moves with it, so a later sale of either tower
        // refunds the right amount.
        public static bool TryMoveGear(SimGrid grid, TileCoord from, int slot, TileCoord to, GearDef[] gear, int tick,
                                       IList<TowerState> towers, IList<PlacementRecord> log, IList<SimEvent> events)
        {
            TowerState source = TowerAt(grid, from, towers);
            TowerState target = TowerAt(grid, to, towers);
            if (source == null || target == null || source == target) return false;
            if (slot < 0 || slot >= TowerState.MaxGearSlots || source.Gear[slot] == TowerState.NoGear) return false;
            int free = target.FreeGearSlot();
            if (free < 0) return false;

            int piece = source.Gear[slot];
            int price = Math.Min(gear[piece].Price, source.PricePaid);
            source.Gear[slot] = TowerState.NoGear;
            target.Gear[free] = piece;
            source.PricePaid -= price;
            target.PricePaid += price;
            source.Recompute(gear);
            target.Recompute(gear);
            events?.Add(SimEvent.TowerChanged(source.Id));
            events?.Add(SimEvent.TowerChanged(target.Id));
            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.GearMoved, Tick = tick, ShapeName = gear[piece].Id, OriginX = to.X, OriginY = to.Y,
                Tiles = new[] { from, to }, TowerId = target.Id,
            });
            return true;
        }

        public static void TakeCoin(TowerOffer card, ref float buildBudget, int tick, IList<PlacementRecord> log)
        {
            buildBudget += card.Amount;
            log?.Add(new PlacementRecord { Kind = PlacementKind.CoinTaken, Tick = tick, ShapeName = "coin", Refund = card.Amount });
        }
    }
}
