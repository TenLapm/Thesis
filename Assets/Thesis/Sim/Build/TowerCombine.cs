using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Two rules about how many towers stand (2026-10-12, the third round after play):
    //
    //   Combining. SimConfig.CombineCount towers of one type and one level merge into
    //   one tower of the next level, the moment the last of them is placed. The one
    //   that was placed FIRST stays where it is; the others leave the board. Their
    //   gear goes into its free slots, and a piece that finds no slot is paid back.
    //   A merge that completes another set merges again (three rares into an epic).
    //
    //   The cap. Only so many towers may stand at once (SimConfig.TowerCap, growing
    //   with the waves). A tower that completes a set is placed whatever the cap
    //   says, since the set then becomes one tower.
    //
    // Both are part of placement, so the same questions are asked by the simulation,
    // by the scripted players and by the Unity preview (I9).
    public static class TowerCombine
    {
        // How many towers may stand at once after `wavesStarted` waves; 0 = no limit.
        public static int CapAt(SimConfig config, int wavesStarted)
        {
            if (config.TowerCap <= 0) return 0;
            return config.TowerCap + (config.TowerCapEveryWaves > 0 ? wavesStarted / config.TowerCapEveryWaves : 0);
        }

        public static int Standing(IList<TowerState> towers)
        {
            int n = 0;
            for (int i = 0; i < towers.Count; i++) if (towers[i].IsAlive) n++;
            return n;
        }

        // Standing towers of this type at this level.
        public static int Copies(IList<TowerState> towers, TowerDef def, int level)
        {
            int n = 0;
            for (int i = 0; i < towers.Count; i++) if (towers[i].IsAlive && towers[i].Def == def && towers[i].Level == level) n++;
            return n;
        }

        // Whether one more tower of this type and level would complete a set.
        public static bool WouldCombine(IList<TowerState> towers, TowerDef def, int level, SimConfig config)
        {
            return config.CombineCount > 1 && level < TowerState.MaxLevel && Copies(towers, def, level) >= config.CombineCount - 1;
        }

        // Whether one more tower of this type and level may be placed at all.
        public static bool HasRoomFor(IList<TowerState> towers, TowerDef def, int level, SimConfig config, int wavesStarted)
        {
            int cap = CapAt(config, wavesStarted);
            return cap <= 0 || Standing(towers) < cap || WouldCombine(towers, def, level, config);
        }

        // Merges every complete set that `placed` belongs to. Returns whether anything
        // merged; the fields are rebuilt once, at the end.
        public static bool Combine(SimGrid grid, MapData map, FlowFieldSet fields, TowerState placed, SimConfig config, GearDef[] gear,
                                   ref float buildBudget, int tick, IList<TowerState> towers, IList<PlacementRecord> log, IList<SimEvent> events)
        {
            int count = config.CombineCount;
            if (count <= 1) return false;

            bool merged = false;
            TowerState current = placed;
            while (current.IsAlive && current.Level < TowerState.MaxLevel)
            {
                // The set, lowest id first: the first is the one that stays.
                var set = new List<TowerState>(count);
                for (int i = 0; i < towers.Count && set.Count < count; i++)
                {
                    TowerState t = towers[i];
                    if (t.IsAlive && t.Def == current.Def && t.Level == current.Level) set.Add(t);
                }
                if (set.Count < count) break;

                TowerState keeper = set[0];
                keeper.Level++;
                var emptied = new TileCoord[count - 1];
                for (int i = 1; i < count; i++)
                {
                    TowerState gone = set[i];
                    for (int slot = 0; slot < TowerState.MaxGearSlots; slot++)
                    {
                        int piece = gone.Gear[slot];
                        if (piece == TowerState.NoGear) continue;
                        int price = Math.Min(gear[piece].Price, gone.PricePaid);
                        gone.Gear[slot] = TowerState.NoGear;
                        gone.PricePaid -= price;
                        int free = keeper.FreeGearSlot();
                        if (free >= 0)
                        {
                            keeper.Gear[free] = piece;
                            keeper.PricePaid += price;
                        }
                        else buildBudget += price; // no slot for it: paid back in full
                    }
                    keeper.PricePaid += gone.PricePaid;
                    gone.IsAlive = false;
                    gone.Combined = true;
                    grid.ClearWall(grid.Get(gone.Tile));
                    emptied[i - 1] = gone.Tile;
                }
                keeper.Recompute(gear);
                events?.Add(SimEvent.TowerChanged(keeper.Id));
                log?.Add(new PlacementRecord
                {
                    Kind = PlacementKind.TowerCombined, Tick = tick, ShapeName = keeper.Def.Id, OriginX = keeper.Tile.X, OriginY = keeper.Tile.Y,
                    Tiles = emptied, TowerId = keeper.Id,
                });
                merged = true;
                current = keeper;
            }

            if (merged) fields.Generate(grid, map.Core);
            return merged;
        }
    }
}
