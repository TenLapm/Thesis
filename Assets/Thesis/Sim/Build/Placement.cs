using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of PlayerBuilder's placement rules. CanPlace is CLAUDE.md's I9: the
    // single source of truth for placement legality, for walls (and later,
    // towers - both occupy a tile as diggable terrain the same way). Everything
    // that decides "can I click here" - PlayerBuilder, GhostPreviewer, the
    // harness - must call this, never re-derive it.
    //
    // Placement is fully synchronous: because walls are expensive terrain rather
    // than absolute blockers, no placement can ever disconnect the map, so there
    // is nothing to validate asynchronously and nothing to revert.
    public static class Placement
    {
        // Off-map, on static geometry, on an existing wall, or on a protected tile
        // (any spawn, or the core) are all illegal - matches AreTilesPlaceable,
        // generalised from one spawn to map.Spawns.
        public static bool CanPlace(SimGrid grid, MapData map, IReadOnlyList<TileCoord> tiles)
        {
            if (tiles == null || tiles.Count == 0) return false;

            for (int i = 0; i < tiles.Count; i++)
            {
                TileCoord t = tiles[i];
                if (!grid.InBounds(t)) return false;

                SimNode node = grid.Get(t);
                if (!node.IsWalkable) return false; // static geometry
                if (node.HasWall) return false;      // existing wall
                if (IsProtected(map, t)) return false; // spawn / core
            }
            return true;
        }

        public static bool IsProtected(MapData map, TileCoord t)
        {
            if (t == map.Core) return true;
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                if (t == map.Spawns[i]) return true;
            }
            return false;
        }

        public static TileCoord[] ToWorldTiles(TileCoord[] localTiles, TileCoord origin)
        {
            var result = new TileCoord[localTiles.Length];
            for (int i = 0; i < localTiles.Length; i++)
            {
                result[i] = new TileCoord(origin.X + localTiles[i].X, origin.Y + localTiles[i].Y);
            }
            return result;
        }

        // Port of PlayerBuilder.HandlePlacement's check-and-spend. Deduct BEFORE
        // building so the budget can never go negative (this is what fixed the
        // original's double-click negative-budget exploit), then rebuild the field
        // synchronously so this tick's agents already see the new wall
        // (ARCHITECTURE.md §4.2 step 1 - unlike a mid-tick breach, a player's own
        // placement command still rebuilds immediately).
        public static bool TryPlace(SimGrid grid, MapData map, FlowField field, ShapeDef shape, int rotationTurns,
                                     TileCoord origin, ref float buildBudget, int tick,
                                     IList<PlacementRecord> log, IList<SimEvent> events)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            TileCoord[] tiles = ToWorldTiles(shape.LocalTiles, origin);
            if (!CanPlace(grid, map, tiles)) return false;
            if (buildBudget < shape.BuildCost) return false;

            buildBudget -= shape.BuildCost;

            int terrainCost = Math.Max(2, shape.DigCost);
            float wallHealth = Math.Max(0.5f, shape.WallHealth);
            for (int i = 0; i < tiles.Length; i++)
            {
                SimNode node = grid.Get(tiles[i]);
                grid.SetWall(node, terrainCost, wallHealth);
                events?.Add(SimEvent.WallPlaced(node.X, node.Y));
            }

            field.Generate(grid, map.Core);

            log?.Add(new PlacementRecord
            {
                Tick = tick,
                ShapeName = shape.Name,
                RotationTurns = rotationTurns,
                OriginX = origin.X,
                OriginY = origin.Y,
                Tiles = tiles,
            });
            return true;
        }
    }
}
