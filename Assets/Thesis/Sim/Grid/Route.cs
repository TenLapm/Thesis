using System.Collections.Generic;

namespace Thesis.Sim
{
    // Walking the NextIndex chain from a start tile. Shared by the path preview,
    // the benchmark parity checks and (later) the build-profile features.
    public static class Route
    {
        // Port of ScenarioBenchmark's path metric: hops along NextIndex until a node
        // with BestCost 0 (the goal). Kept exactly, including its edge behaviour: an
        // unreachable start counts 1 hop (it steps to "null" once). The guard stops a
        // corrupted field with a cycle from hanging the caller.
        public static int CountHops(SimGrid grid, SimNode start)
        {
            int hops = 0;
            SimNode walk = start;
            int guard = grid.Width * grid.Height + 5;
            while (walk != null && walk.BestCost != 0 && guard-- > 0)
            {
                walk = grid.NextOf(walk);
                hops++;
            }
            return hops;
        }

        // Every node from start to the goal inclusive, or just the nodes reachable
        // before the chain ends. Clears `into` first.
        public static void Collect(SimGrid grid, SimNode start, List<SimNode> into)
        {
            into.Clear();
            SimNode walk = start;
            int guard = grid.Width * grid.Height + 5;
            while (walk != null && guard-- > 0)
            {
                into.Add(walk);
                if (walk.BestCost == 0) break;
                walk = grid.NextOf(walk);
            }
        }

        // Cost to the goal in tile units, or -1 when unreachable. The /10 undoes
        // FlowField's x10 integer costs.
        public static double CostInTiles(SimNode start)
        {
            return start.BestCost == SimNode.Infinity ? -1.0 : start.BestCost / 10.0;
        }
    }
}
