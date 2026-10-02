using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thesis.Core;

namespace Thesis.Sim
{
    // A whole simulation state as text: AsciiMap's terrain plus a header, the live
    // agents and one data layer. This is what `cli ascii <replay> --tick T` prints
    // (ARCHITECTURE.md §8). Unlike AsciiMap.Render(labels: false), the output is for
    // reading, not for parsing back.
    //
    // `movement` chooses whose flow field the Route and Cost layers show: Ground
    // (the default) or Sapper. Render both to see where sappers leave the route
    // everyone else takes. Flying has no field to draw.
    public static class AsciiState
    {
        private const byte Flying = 1, Walking = 2, Digging = 3;

        public static string Render(Simulation sim, AsciiLayer layer, MovementClass movement = MovementClass.Ground)
        {
            if (movement == MovementClass.Flying)
                throw new ArgumentException("Flying follows no flow field; render Ground or Sapper.", nameof(movement));

            SimState s = sim.State;
            MapData map = sim.Map;
            SimGrid grid = s.Grid;

            var onRoute = new bool[grid.NodeCount];
            if (layer == AsciiLayer.Route)
            {
                var route = new List<SimNode>();
                for (int i = 0; i < map.Spawns.Length; i++)
                {
                    Route.Collect(grid, grid.Get(map.Spawns[i]), route, movement);
                    foreach (SimNode n in route) onRoute[n.Index] = true;
                }
            }

            // What stands on each tile. When several agents share one, the most
            // telling wins: digging over walking over flying.
            var agentOn = new byte[grid.NodeCount];
            IReadOnlyList<AgentState> live = s.LiveAgents;
            for (int i = 0; i < live.Count; i++)
            {
                SimNode n = grid.NodeFromPosition(live[i].Position);
                byte mark = live[i].Movement == MovementClass.Flying ? Flying : n.HasWall ? Digging : Walking;
                if (mark > agentOn[n.Index]) agentOn[n.Index] = mark;
            }

            int maxOccupancy = 0;
            int maxCost = 0;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                if (s.Occupancy[i] > maxOccupancy) maxOccupancy = s.Occupancy[i];
                int cost = grid.ByIndex(i).CostFor(movement);
                if (cost != SimNode.Infinity && cost > maxCost) maxCost = cost;
            }

            var sb = new StringBuilder();
            sb.Append("wave ").Append(s.WaveIndex)
              .Append("  tick ").Append(s.Tick)
              .Append("  phase ").Append(s.Phase)
              .Append("  budget ").Append(s.BuildBudget.ToString("0.0", CultureInfo.InvariantCulture))
              .Append("  core ").Append(s.CoreHp).Append('/').Append(sim.Config.CoreMaxHp)
              .Append("  live ").Append(live.Count)
              .Append("  towers ").Append(LiveTowers(s))
              .Append("  layer=").Append(layer.ToString().ToLowerInvariant());
            // Only said when it is not the default, so a ground render reads as it always did.
            if (movement != MovementClass.Ground) sb.Append("  class=").Append(movement.ToString().ToLowerInvariant());
            sb.Append('\n');

            sb.Append("     ");
            for (int x = 0; x < grid.Width; x++) sb.Append(x % 10 == 0 ? (char)('0' + (x / 10) % 10) : ' ');
            sb.Append('\n');
            sb.Append("     ");
            for (int x = 0; x < grid.Width; x++) sb.Append((char)('0' + x % 10));
            sb.Append('\n');

            for (int y = grid.Height - 1; y >= 0; y--)
            {
                sb.Append(y.ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append("  ");
                for (int x = 0; x < grid.Width; x++)
                {
                    sb.Append(TileChar(grid[x, y], map, s, layer, movement, onRoute, agentOn, maxOccupancy, maxCost));
                }
                sb.Append('\n');
            }

            sb.Append("legend  X static  # wall  T tower  S spawn  C core");
            switch (layer)
            {
                case AsciiLayer.Route:
                    sb.Append("  * ").Append(movement == MovementClass.Ground ? "route" : "sapper route").Append("  + route through a wall or tower  a agent  d digging agent  f flying agent");
                    break;
                case AsciiLayer.Occupancy:
                    sb.Append("  1-9 share of the busiest tile (").Append(maxOccupancy).Append(" agent-ticks)  . unvisited");
                    break;
                case AsciiLayer.Cost:
                    sb.Append("  0-9 cost to core, 9 = ").Append((maxCost / 10.0).ToString("0.#", CultureInfo.InvariantCulture)).Append(" tiles  ? unreachable");
                    break;
                default:
                    sb.Append("  a agent  d digging agent  f flying agent");
                    break;
            }
            sb.Append('\n');
            return sb.ToString();
        }

        private static char TileChar(SimNode n, MapData map, SimState s, AsciiLayer layer, MovementClass movement, bool[] onRoute, byte[] agentOn, int maxOccupancy, int maxCost)
        {
            var t = new TileCoord(n.X, n.Y);
            if (t == map.Core) return 'C';
            for (int i = 0; i < map.Spawns.Length; i++)
                if (t == map.Spawns[i]) return 'S';
            if (!n.IsWalkable) return 'X';

            if (layer == AsciiLayer.Occupancy)
            {
                int count = s.Occupancy[n.Index];
                if (count > 0) return Digit(1 + (int)((long)count * 8 / maxOccupancy), 9);
                return n.HasWall ? Built(n) : '.';
            }

            if (layer == AsciiLayer.Cost)
            {
                if (n.HasWall) return Built(n);
                int cost = n.CostFor(movement);
                if (cost == SimNode.Infinity) return '?';
                return maxCost == 0 ? '0' : Digit((int)((long)cost * 9 / maxCost), 9);
            }

            if (agentOn[n.Index] == Digging) return 'd';
            if (agentOn[n.Index] == Walking) return 'a';
            if (agentOn[n.Index] == Flying) return 'f';
            if (n.HasWall) return onRoute[n.Index] ? AsciiMap.RouteThroughBuilt : Built(n);
            if (onRoute[n.Index]) return '*';
            return '.';
        }

        private static char Built(SimNode n)
        {
            if (n.Occupant == Occupant.Tower) return 'T';
            return n.TerrainCost >= AsciiMap.HeavyWallCost ? 'H' : '#';
        }

        private static int LiveTowers(SimState s)
        {
            int n = 0;
            for (int i = 0; i < s.Towers.Count; i++) if (s.Towers[i].IsAlive) n++;
            return n;
        }

        private static char Digit(int value, int max)
        {
            if (value < 0) value = 0;
            if (value > max) value = max;
            return (char)('0' + value);
        }
    }
}
