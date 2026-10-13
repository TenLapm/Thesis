using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // The bench (2026-10-13): a few slots beside the board that hold towers the player
    // owns but has not put down. A benched tower keeps its level and its gear, does not
    // shoot, and is on no tile. These two helpers are the only places a tower crosses
    // between the bench and the board; the caller owns the bench slots, the move
    // counter and the field rebuild.
    public static class TowerBench
    {
        public const int Empty = -1;

        // The tile a benched tower carries: none.
        public static readonly TileCoord NoTile = new TileCoord(-1, -1);

        // A new tower that starts its life on the bench.
        public static TowerState Create(IList<TowerState> towers, TowerDef def, float tileSize, int tick, int price, int level, IList<PlacementRecord> log)
        {
            var tower = new TowerState(towers.Count, def, NoTile, default(Vec2f), tileSize, tick, price, level) { OnBench = true };
            towers.Add(tower);
            log?.Add(new PlacementRecord { Kind = PlacementKind.TowerBenched, Tick = tick, ShapeName = def.Id, OriginX = -1, OriginY = -1, TowerId = tower.Id, Cost = price });
            return tower;
        }

        public static void PutOnBoard(SimGrid grid, TowerState tower, TileCoord tile, int tick, IList<PlacementRecord> log)
        {
            SimNode node = grid.Get(tile);
            grid.SetTower(node, Math.Max(2, tower.Def.DigCost), Math.Max(0.5f, tower.Def.TowerHealth), tower.Id);
            tower.MoveTo(tile, node.Position);
            tower.OnBench = false;
            tower.Cooldown = 0;
            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerDeployed, Tick = tick, ShapeName = tower.Def.Id, OriginX = tile.X, OriginY = tile.Y,
                Tiles = new[] { tile }, TowerId = tower.Id,
            });
        }

        public static void TakeOffBoard(SimGrid grid, TowerState tower, int tick, IList<PlacementRecord> log)
        {
            TileCoord tile = tower.Tile;
            grid.ClearWall(grid.Get(tile));
            tower.OnBench = true;
            tower.MoveTo(NoTile, default(Vec2f));
            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerRecalled, Tick = tick, ShapeName = tower.Def.Id, OriginX = tile.X, OriginY = tile.Y,
                Tiles = new[] { tile }, TowerId = tower.Id,
            });
        }
    }
}
