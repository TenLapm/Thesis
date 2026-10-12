using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // "Get rid of what is not pulling its weight." At the start of every build
    // phase after the first wave it sells the live tower that has dealt the least
    // damage, and the oldest wall piece that still has a tile standing. It builds
    // nothing itself: SequencePolicy.Restless() puts it in front of the wall and
    // tower builders, so the budget it frees is spent again in the same phase.
    //
    // It exists so that recorded episodes contain sales (WP-C3): the replay and
    // telemetry checks would otherwise never run the selling rules. As a player it
    // is crude. A tower that did little may simply have been bought last, and the
    // oldest wall is not the worst one. The ladder's Reactive policy (WP12) is the
    // one that is meant to sell for a reason.
    public sealed class SellerPolicy : IPlayerPolicy
    {
        public const string Id = "seller";

        // It keeps at least this many towers and wall pieces, so that the builders
        // behind it are adding to a defence, not replacing one.
        public int MinTowersKept = 3;
        public int MinPiecesKept = 1;

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver || s.WaveIndex == 0) return; // before the first wave nothing has been tried yet

            SellWeakestTower(s, send);
            SellOldestPiece(s, send);
        }

        // Least damage dealt; with a strict '<' a tie goes to the lowest id.
        private void SellWeakestTower(SimState s, Action<SimCommand> send)
        {
            TowerState weakest = null;
            int alive = 0;
            for (int i = 0; i < s.Towers.Count; i++)
            {
                TowerState t = s.Towers[i];
                if (!t.IsAlive) continue;
                alive++;
                if (weakest == null || t.DamageDealt < weakest.DamageDealt) weakest = t;
            }
            if (weakest == null || alive <= MinTowersKept) return;

            send(SimCommand.SellTower(weakest.Tile.X, weakest.Tile.Y));
            if (!weakest.Sold)
                throw new InvalidOperationException("[Harness] SellerPolicy tried to sell tower #" + weakest.Id + " at " + weakest.Tile + " in a build phase, and the simulation refused.");
        }

        // The build log is in order, so the first wall piece with a tile that still
        // carries its id is the oldest one standing.
        private void SellOldestPiece(SimState s, Action<SimCommand> send)
        {
            List<PlacementRecord> log = s.PlacementLog;
            int oldest = -1;
            TileCoord tile = default(TileCoord);
            int standingPieces = 0;

            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind != PlacementKind.Wall) continue;
                for (int t = 0; t < log[i].Tiles.Length; t++)
                {
                    SimNode node = s.Grid.Get(log[i].Tiles[t]);
                    if (node.Occupant != Occupant.Wall || node.PieceId != i) continue;
                    standingPieces++;
                    if (oldest < 0)
                    {
                        oldest = i;
                        tile = log[i].Tiles[t];
                    }
                    break; // one standing tile is enough to count the piece
                }
            }
            if (oldest < 0 || standingPieces <= MinPiecesKept) return;

            int before = log.Count;
            send(SimCommand.SellWall(tile.X, tile.Y));
            if (log.Count == before)
                throw new InvalidOperationException("[Harness] SellerPolicy tried to sell wall piece " + oldest + " at " + tile + " in a build phase, and the simulation refused.");
        }
    }
}
