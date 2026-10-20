using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of PlayerBuilder's placement rules. CanPlace is CLAUDE.md's I9: the
    // single source of truth for placement legality, for walls and for towers
    // (both occupy a tile as diggable terrain the same way). Everything
    // that decides "can I click here" - PlayerBuilder, GhostPreviewer, the
    // harness - must call this, never re-derive it.
    //
    // Placement is fully synchronous: because walls are expensive terrain rather
    // than absolute blockers, no placement can ever disconnect the map, so there
    // is nothing to validate asynchronously and nothing to revert.
    //
    // Selling (WP-C3) lives here too: it is placement run backwards. Every change
    // to the board, in either direction, rebuilds BOTH flow fields at once and adds
    // one row to the build log.
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
                if (map.InBuildMargin(t)) return false; // the ring enemies enter through
                if (map.DepositAt(t) >= 0) return false; // an ore deposit is for a collector
            }
            return true;
        }

        // Where a TOWER may stand. Without SimConfig.TowersOnWalls that is CanPlace (I9).
        // With it, only on a wall (one with no tower yet) or on a rock of the map; never on
        // open ground, the core, an entry point or the ring along the edge.
        public static bool CanPlaceTower(SimGrid grid, MapData map, TileCoord tile, bool onWalls)
        {
            if (!onWalls) return CanPlace(grid, map, new[] { tile });
            if (!grid.InBounds(tile) || IsProtected(map, tile) || map.InBuildMargin(tile)) return false;
            SimNode node = grid.Get(tile);
            if (node.Occupant == Occupant.Wall) return true;
            return !node.IsWalkable && node.Occupant == Occupant.None;
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

        // What one tile of this shape becomes on the grid. PlayerBuilder clamped both
        // (a wall must cost more than open ground and must have health to be a wall);
        // a scripted policy that tries a placement on a scratch grid uses the same two
        // values, so its preview and the real placement cannot disagree.
        public static int WallTerrainCost(ShapeDef shape) { return Math.Max(2, shape.DigCost); }

        public static float WallHealth(ShapeDef shape) { return Math.Max(0.5f, shape.WallHealth); }

        // Port of PlayerBuilder.HandlePlacement's check-and-spend. Deduct BEFORE
        // building so the budget can never go negative (this is what fixed the
        // original's double-click negative-budget exploit), then rebuild the field
        // synchronously so this tick's agents already see the new wall
        // (ARCHITECTURE.md §4.2 step 1 - unlike a mid-tick breach, a player's own
        // placement command still rebuilds immediately).
        //
        // Every tile of the piece is stamped with the piece's id, which is the index
        // its row gets in the log. That is how SellWall later finds the rest of a
        // piece from any one of its tiles. Without a log there is no id, and such a
        // wall cannot be sold.
        public static bool TryPlace(SimGrid grid, MapData map, FlowFieldSet fields, ShapeDef shape, int rotationTurns,
                                     TileCoord origin, ref float buildBudget, int tick,
                                     IList<PlacementRecord> log, IList<SimEvent> events, bool free = false)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            // `free`: the piece comes out of the wall stock (SimConfig.WallStock) and
            // costs no budget; the caller counts the tiles.
            int cost = free ? 0 : shape.BuildCost;
            TileCoord[] tiles = ToWorldTiles(shape.LocalTiles, origin);
            if (!CanPlace(grid, map, tiles)) return false;
            if (buildBudget < cost) return false;

            buildBudget -= cost;

            int pieceId = log != null ? log.Count : -1;
            int terrainCost = WallTerrainCost(shape);
            float wallHealth = WallHealth(shape);
            for (int i = 0; i < tiles.Length; i++)
            {
                SimNode node = grid.Get(tiles[i]);
                grid.SetWall(node, terrainCost, wallHealth, pieceId);
                events?.Add(SimEvent.WallPlaced(node.X, node.Y));
            }

            fields.Generate(grid, map.Core);

            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.Wall,
                Tick = tick,
                ShapeName = shape.Name,
                RotationTurns = rotationTurns,
                OriginX = origin.X,
                OriginY = origin.Y,
                Tiles = tiles,
                PieceId = pieceId,
                Cost = cost,
            });
            return true;
        }

        // A tower is bought and placed in one step, the same way a wall piece is:
        // check legality (CanPlace, the same rule as for walls - I9), check the
        // budget, spend, build, rebuild the fields. It occupies ONE tile as diggable
        // terrain. Returns false, changing nothing, when the tile or the budget
        // does not allow it.
        //
        // `price` is what the shop is asking for this offer. It is remembered on the
        // tower, because a later sale refunds a share of what was actually paid.
        public static bool TryPlaceTower(SimGrid grid, MapData map, FlowFieldSet fields, TowerDef def, int price, TileCoord tile,
                                         ref float buildBudget, int tick, IList<TowerState> towers,
                                         IList<PlacementRecord> log, IList<SimEvent> events, int level = 1, bool onWalls = false)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (towers == null) throw new ArgumentNullException(nameof(towers));

            var tiles = new[] { tile };
            if (!CanPlaceTower(grid, map, tile, onWalls)) return false;
            if (buildBudget < price) return false;

            buildBudget -= price;

            SimNode node = grid.Get(tile);
            var tower = new TowerState(towers.Count, def, tile, node.Position, map.NodeDiameter, tick, price, level);
            towers.Add(tower);
            grid.SetTower(node, Math.Max(2, def.DigCost), Math.Max(0.5f, def.TowerHealth), tower.Id);
            events?.Add(SimEvent.TowerPlaced(tower.Id, tile.X, tile.Y));

            fields.Generate(grid, map.Core);

            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.Tower,
                Tick = tick,
                ShapeName = def.Id,
                RotationTurns = 0,
                OriginX = tile.X,
                OriginY = tile.Y,
                Tiles = tiles,
                TowerId = tower.Id,
                Cost = price,
            });
            return true;
        }

        // Sells the tower standing on `tile` for a share of what was paid for it.
        // The tile goes back to open ground and the fields are rebuilt. Returns
        // false, changing nothing, when there is no tower there.
        //
        // The tower stays in the list, like one that was chewed through: its id and
        // its totals still mean something to telemetry. Sold tells the two apart.
        public static bool TrySellTower(SimGrid grid, MapData map, FlowFieldSet fields, TileCoord tile, float refundRate,
                                        ref float buildBudget, int tick, IList<TowerState> towers,
                                        IList<PlacementRecord> log, IList<SimEvent> events)
        {
            if (towers == null) throw new ArgumentNullException(nameof(towers));
            if (!grid.InBounds(tile)) return false;

            SimNode node = grid.Get(tile);
            if (node.Occupant != Occupant.Tower) return false;

            TowerState tower = towers[node.TowerId];
            float refund = (float)(tower.PricePaid * refundRate);

            tower.IsAlive = false;
            tower.Sold = true;
            grid.LiftTower(node);
            buildBudget += refund;
            events?.Add(SimEvent.TowerSold(tower.Id, tile.X, tile.Y, refund));

            fields.Generate(grid, map.Core);

            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerSold,
                Tick = tick,
                ShapeName = tower.Def.Id,
                OriginX = tile.X,
                OriginY = tile.Y,
                Tiles = new[] { tile },
                TowerId = tower.Id,
                Refund = refund,
            });
            return true;
        }

        // Puts the tower standing on `from` on `to`, for nothing. The tower keeps its id,
        // its level, its gear, its cooldown and whatever has been chewed off it; only
        // the tile changes. `to` must be a tile a tower could be built on (CanPlace,
        // I9). Returns false, changing nothing, otherwise.
        public static bool TryMoveTower(SimGrid grid, MapData map, FlowFieldSet fields, TileCoord from, TileCoord to, int tick,
                                        IList<TowerState> towers, IList<PlacementRecord> log, IList<SimEvent> events, bool onWalls = false)
        {
            if (towers == null) throw new ArgumentNullException(nameof(towers));
            if (!grid.InBounds(from)) return false;

            SimNode source = grid.Get(from);
            if (source.Occupant != Occupant.Tower) return false;
            if (!CanPlaceTower(grid, map, to, onWalls)) return false;

            TowerState tower = towers[source.TowerId];
            int terrainCost = source.TerrainCost;
            float health = source.WallHealth;
            float fullHealth = source.MaxWallHealth;

            grid.LiftTower(source);
            SimNode target = grid.Get(to);
            grid.SetTower(target, terrainCost, fullHealth, tower.Id);
            target.WallHealth = health;
            tower.MoveTo(to, target.Position);
            events?.Add(SimEvent.TowerChanged(tower.Id));

            fields.Generate(grid, map.Core);

            log?.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerMoved,
                Tick = tick,
                ShapeName = tower.Def.Id,
                OriginX = to.X,
                OriginY = to.Y,
                Tiles = new[] { from, to },
                TowerId = tower.Id,
            });
            return true;
        }

        // Sells the whole wall PIECE that `tile` belongs to (decision D6): every tile
        // of that placement still standing is cleared, and the player gets
        //
        //     piece cost x refundRate x (tiles still standing / tiles placed)
        //
        // so a piece that enemies have half chewed away pays half. "Still standing"
        // means the tile is a wall AND still carries this piece's id: a tile that was
        // breached and later built over by another piece belongs to that one now and
        // is left alone. Returns false, changing nothing, when `tile` is not part of
        // a piece (open ground, a tower, or a wall put there by a test or a scenario,
        // which never went through the log).
        public static bool TrySellWall(SimGrid grid, MapData map, FlowFieldSet fields, TileCoord tile, float refundRate,
                                       ref float buildBudget, int tick,
                                       IList<PlacementRecord> log, IList<SimEvent> events)
        {
            if (log == null) throw new ArgumentNullException(nameof(log), "A wall is sold through the log that recorded its piece.");
            if (!grid.InBounds(tile)) return false;

            SimNode clicked = grid.Get(tile);
            int pieceId = clicked.PieceId;
            if (clicked.Occupant != Occupant.Wall || pieceId < 0 || pieceId >= log.Count) return false;

            PlacementRecord piece = log[pieceId];
            if (piece.Kind != PlacementKind.Wall) return false;

            int standing = 0;
            for (int i = 0; i < piece.Tiles.Length; i++)
            {
                if (BelongsTo(grid.Get(piece.Tiles[i]), pieceId)) standing++;
            }

            var cleared = new TileCoord[standing];
            int k = 0;
            for (int i = 0; i < piece.Tiles.Length; i++)
            {
                SimNode node = grid.Get(piece.Tiles[i]);
                if (!BelongsTo(node, pieceId)) continue;
                grid.ClearWall(node);
                cleared[k++] = piece.Tiles[i];
                events?.Add(SimEvent.WallSold(node.X, node.Y, pieceId));
            }

            // Explicit casts round every float intermediate (ARCHITECTURE.md §9 rule 3).
            float whole = (float)(piece.Cost * refundRate);
            float refund = (float)((float)(whole * standing) / piece.Tiles.Length);
            buildBudget += refund;

            fields.Generate(grid, map.Core);

            log.Add(new PlacementRecord
            {
                Kind = PlacementKind.WallSold,
                Tick = tick,
                ShapeName = piece.ShapeName,
                RotationTurns = piece.RotationTurns,
                OriginX = piece.OriginX,
                OriginY = piece.OriginY,
                Tiles = cleared,
                PieceId = pieceId,
                Refund = refund,
            });
            return true;
        }

        private static bool BelongsTo(SimNode node, int pieceId)
        {
            return node.Occupant == Occupant.Wall && node.PieceId == pieceId;
        }
    }
}
