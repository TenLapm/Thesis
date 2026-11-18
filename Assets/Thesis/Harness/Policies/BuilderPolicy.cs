using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // A stand-in player for today's rules (2026-11-16): the build menu, towers only on a wall or
    // a rock (TowersOnWalls), towers fire only with power (PowerRule), ore from collectors. The
    // older players were written for the random shop; under these rules they build nothing that
    // fires and lose wave 1, so nothing measured against them meant anything.
    //
    // Each build phase, while the ore lasts:
    //   the core        before wave 1, confirmed where the map put it
    //   a repair        when the core is at half or below
    //   income          collectors on the free deposits nearest the core, 1 + wave/2 of them
    //   towers          into a free place of a power source (the core, a collector): of the tiles
    //                   such a source reaches, the one from which the tower sees the most of the
    //                   announced route; a wall is laid first if the tile is open ground
    //   anti-air        at least a third of its towers can hit flyers
    //   more power      when every place is taken, another collector, which is two more places
    //
    // It never sells, never researches, never mazes. It is not a good player; it is one that
    // plays the game, which is what a measurement or a gate needs. It draws nothing from rng,
    // so its choices depend on the board alone.
    public sealed class BuilderPolicy : IPlayerPolicy
    {
        public const string Id = "builder";

        // The order towers are bought in, by roster id; an id the roster lacks is skipped.
        // Archers first: cheap, and they hit flyers.
        public string[] Rotation = { "archer", "cannon", "archer", "ballista", "flak", "cannon" };

        public float AntiAirShare = 1f / 3f;

        private readonly List<SimNode> route = new List<SimNode>();
        private readonly TileCoord[] one = new TileCoord[1];
        private bool[] onRoute = new bool[0];
        private readonly List<TileCoord> routeTiles = new List<TileCoord>();
        private bool coreDone;
        private int bought;

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return;
            SimConfig config = sim.Config;

            if (config.CorePlacement && !coreDone && s.WaveIndex == 0)
            {
                coreDone = true;
                send(SimCommand.PlaceCore(s.Core.X, s.Core.Y));
                s = sim.State;
            }

            if (s.CoreHp * 2 <= config.CoreMaxHp && s.BuildBudget >= config.RepairPrice) send(SimCommand.RepairCore());

            for (int guard = 0; guard < 64; guard++)
            {
                s = sim.State;
                if (Collectors(sim) < 1 + s.WaveIndex / 2 && TryCollector(sim, send)) continue;
                if (TryTower(sim, send)) continue;
                if (s.BuildBudget >= config.CollectorPrice + Cheapest(sim) && TryCollector(sim, send)) continue; // two more power places
                break;
            }
        }

        // ------------------------------------------------------------------ collectors

        private static int Collectors(Simulation sim)
        {
            int n = 0;
            foreach (Deposit d in sim.Map.Deposits) if (sim.State.Grid.Get(d.Tile).Occupant == Occupant.Collector) n++;
            return n;
        }

        // The free deposit nearest the core, one with ore left first.
        private static bool TryCollector(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            if (!sim.Config.BuildMenu || s.BuildBudget < sim.Config.CollectorPrice) return false;
            int best = -1, bestKey = int.MaxValue;
            for (int d = 0; d < sim.Map.Deposits.Length; d++)
            {
                TileCoord t = sim.Map.Deposits[d].Tile;
                if (s.Grid.Get(t).Occupant != Occupant.None) continue;
                bool dry = s.DepositLeft != null && d < s.DepositLeft.Length && s.DepositLeft[d] <= 0;
                int key = (dry ? 100000 : 0) + Math.Abs(t.X - s.Core.X) + Math.Abs(t.Y - s.Core.Y);
                if (key < bestKey) { bestKey = key; best = d; }
            }
            if (best < 0) return false;
            int before = Collectors(sim);
            send(SimCommand.BuildCollector(sim.Map.Deposits[best].Tile.X, sim.Map.Deposits[best].Tile.Y));
            return Collectors(sim) > before;
        }

        // ------------------------------------------------------------------ towers

        private int Cheapest(Simulation sim)
        {
            int cheapest = int.MaxValue;
            foreach (string id in Rotation)
            {
                int i = IndexOf(sim.TowerLibrary, id);
                if (i >= 0) cheapest = Math.Min(cheapest, sim.TowerLibrary[i].Cost);
            }
            return cheapest == int.MaxValue ? 0 : cheapest;
        }

        private static int IndexOf(TowerDef[] roster, string id)
        {
            for (int i = 0; i < roster.Length; i++) if (roster[i].Id == id && roster[i].PowerCapacity == 0) return i;
            return -1;
        }

        // What the simulation's BuyTower allows (Simulation.BuyTower): in rounds where research
        // unlocks tower types (ResearchUnlocksUpgrades off) a type above the research tier is shut,
        // and a tower cap may be full. In today's game every type is open.
        private static bool MayBuy(Simulation sim, TowerDef def)
        {
            SimConfig config = sim.Config;
            if (!config.ResearchUnlocksUpgrades && def.Tier > sim.State.ResearchLevel) return false;
            return TowerCombine.HasRoomFor(sim.State.Towers, def, 1, config, sim.State.WaveIndex);
        }

        // The next tower: an anti-air one while they are below their share, else the rotation.
        private int Choose(Simulation sim)
        {
            TowerDef[] roster = sim.TowerLibrary;
            int towers = 0, antiAir = 0;
            foreach (TowerState t in sim.State.Towers)
            {
                if (!t.IsAlive || t.OnBench || t.PowerCapacity > 0) continue;
                towers++;
                if (t.CanHitFlying) antiAir++;
            }
            if ((float)antiAir < (float)((towers + 1) * AntiAirShare))
            {
                foreach (string id in Rotation)
                {
                    int i = IndexOf(roster, id);
                    if (i >= 0 && roster[i].CanHitFlying && MayBuy(sim, roster[i])) return i;
                }
            }
            for (int k = 0; k < Rotation.Length; k++)
            {
                int i = IndexOf(roster, Rotation[(bought + k) % Rotation.Length]);
                if (i >= 0 && MayBuy(sim, roster[i])) return i;
            }
            return -1;
        }

        private bool TryTower(Simulation sim, Action<SimCommand> send)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;
            int index = Choose(sim);
            if (index < 0) return false;
            TowerDef def = sim.TowerLibrary[index];
            bool foundation = false;

            TileCoord tile;
            if (!BestTile(sim, def, out tile, out foundation)) return false;
            if (s.BuildBudget < def.Cost + (foundation ? 1f : 0f)) return false;

            if (foundation)
            {
                send(SimCommand.PlaceShape(tile.X, tile.Y));
                // The plain wall's price is the shape library's; if it could not be paid or laid,
                // there is no tower here this phase.
                if (!Placement.CanPlaceTower(sim.State.Grid, sim.Map, tile, config.TowersOnWalls)) return false;
                if (sim.State.BuildBudget < def.Cost) return false;
            }
            int before = sim.State.Towers.Count;
            send(SimCommand.BuyTower(index, tile.X, tile.Y));
            if (sim.State.Towers.Count == before)
                throw new InvalidOperationException("[Harness] BuilderPolicy chose " + def.Id + " at " + tile + ", but the simulation refused it. Its legality check and Placement disagree.");
            bought++;
            return true;
        }

        // Of the tiles a power source with a free place reaches, the one where a tower of this
        // type sees the most of the announced route. A wall or a rock first (no foundation to
        // pay for), then open ground a wall can go on. Ties: the lower tile index.
        private bool BestTile(Simulation sim, TowerDef def, out TileCoord best, out bool foundation)
        {
            SimState s = sim.State;
            SimGrid grid = s.Grid;
            MapData map = sim.Map;
            SimConfig config = sim.Config;
            MarkRoute(sim);

            best = default(TileCoord);
            foundation = false;
            int bestScore = 0;
            bool bestNeedsWall = true;

            var sources = new List<TileCoord>();
            var radii = new List<float>();
            Sources(sim, sources, radii);
            if (sources.Count == 0) return false;

            float reach = (float)(def.RangeTiles * def.RangeTiles);
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode node = grid.ByIndex(i);
                var tile = new TileCoord(node.X, node.Y);
                bool powered = false;
                for (int k = 0; k < sources.Count && !powered; k++) powered = Simulation.InPowerReach(sources[k], radii[k], tile);
                if (!powered) continue;

                bool needsWall;
                if (Placement.CanPlaceTower(grid, map, tile, config.TowersOnWalls)) needsWall = false;
                else if (config.TowersOnWalls && node.IsWalkable && node.Occupant == Occupant.None && WallFits(grid, map, tile)) needsWall = true;
                else continue;

                int score = 0;
                for (int r = 0; r < routeTiles.Count; r++)
                {
                    int dx = routeTiles[r].X - tile.X, dy = routeTiles[r].Y - tile.Y;
                    if ((float)(dx * dx + dy * dy) <= reach) score++;
                }
                if (score == 0) continue;
                bool better = score > bestScore || (score == bestScore && bestNeedsWall && !needsWall);
                if (!better) continue;
                best = tile;
                bestScore = score;
                bestNeedsWall = needsWall;
            }
            foundation = bestNeedsWall;
            return bestScore > 0;
        }

        private bool WallFits(SimGrid grid, MapData map, TileCoord tile)
        {
            one[0] = tile;
            return Placement.CanPlace(grid, map, one);
        }

        // The power sources with a free place: the core, then every standing collector, each with
        // its capacity less the towers it powers now. Power towers (pylons) the same way.
        private static void Sources(Simulation sim, List<TileCoord> sources, List<float> radii)
        {
            SimState s = sim.State;
            SimConfig config = sim.Config;
            if (!config.PowerRule)
            {
                // Without the power rule every tile is in reach: one source covering the board.
                sources.Add(s.Core);
                radii.Add(sim.Map.Width + sim.Map.Height);
                return;
            }
            Add(sim, s.Core, config.CorePowerRadius, config.CorePowerCapacity, sources, radii);
            foreach (Deposit d in sim.Map.Deposits)
                if (s.Grid.Get(d.Tile).Occupant == Occupant.Collector) Add(sim, d.Tile, config.CollectorPowerRadius, config.CollectorPowerCapacity, sources, radii);
            foreach (TowerState t in s.Towers)
                if (t.IsAlive && !t.OnBench && t.PowerCapacity > 0) Add(sim, t.Tile, t.PowerRadius, t.PowerCapacity, sources, radii);
        }

        private static void Add(Simulation sim, TileCoord at, float radius, int capacity, List<TileCoord> sources, List<float> radii)
        {
            int used = 0;
            foreach (TowerState t in sim.State.Towers)
                if (t.IsAlive && !t.OnBench && t.PowerCapacity == 0 && t.Powered && t.PoweredBy.Equals(at)) used++;
            if (used >= capacity) return;
            sources.Add(at);
            radii.Add(radius);
        }

        // Where enemies will walk: from every entry of the announced wave, else the first entry.
        private void MarkRoute(Simulation sim)
        {
            SimGrid grid = sim.State.Grid;
            if (onRoute.Length != grid.NodeCount) onRoute = new bool[grid.NodeCount];
            Array.Clear(onRoute, 0, onRoute.Length);
            routeTiles.Clear();
            WavePlan next = sim.State.NextPlan;
            int sources = next != null && next.Groups.Length > 0 ? next.Groups.Length : 1;
            for (int k = 0; k < sources; k++)
            {
                int spawn = next != null && next.Groups.Length > 0 ? next.Groups[k].SpawnIndex : 0;
                MovementClass movement = next != null && next.Groups.Length > 0 ? next.Groups[k].Movement : MovementClass.Ground;
                if (movement == MovementClass.Flying) movement = MovementClass.Ground; // no field: the walkers' way is a fair guess near the core
                Route.Collect(grid, grid.Get(sim.Map.Spawns[spawn]), route, movement);
                for (int i = 0; i < route.Count; i++)
                {
                    if (onRoute[route[i].Index]) continue;
                    onRoute[route[i].Index] = true;
                    routeTiles.Add(new TileCoord(route[i].X, route[i].Y));
                }
            }
        }
    }
}
