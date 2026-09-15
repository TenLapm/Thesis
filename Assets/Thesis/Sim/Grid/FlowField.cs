using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of FlowFieldManager.GenerateFlowField: one synchronous weighted flood fill
    // (SPFA - a BFS that re-queues a node whenever a cheaper route is found), run
    // whenever the map changes. On this grid size it costs microseconds, which is why
    // the old chunk system, background threads, the isCalculating lock, ValidatePath,
    // sinkhole detection and the global fallback were deleted. Do not reintroduce any
    // of them without a measured reason (CLAUDE.md §8).
    //
    // An instance only holds reusable buffers; all field data lives on the SimNodes.
    public sealed class FlowField
    {
        // Integer step costs, x10 so diagonals can cost ~sqrt(2) without floats.
        // Anything derived from BestCost must divide by 10 to get tile units.
        public const int CardinalCost = 10;
        public const int DiagonalCost = 14;

        private readonly Queue<SimNode> open = new Queue<SimNode>();
        private readonly SimNode[] neighbors = new SimNode[8];

        public void Generate(SimGrid grid, TileCoord goal)
        {
            // 1. Reset EVERYTHING up front - cost AND next pointer - so no stale
            // pointer can survive a recalculation. (An old version forgot the pointer
            // and left unreachable pockets pointing wherever they liked.)
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode n = grid.ByIndex(i);
                n.BestCost = SimNode.Infinity;
                n.NextIndex = -1;
            }

            SimNode target = grid.InBounds(goal) ? grid.Get(goal) : null;
            if (target == null || !target.IsWalkable)
            {
                grid.FieldVersion++;
                return;
            }

            target.BestCost = 0;
            open.Clear();
            open.Enqueue(target);

            // 2. Weighted relaxation. Entering a tile costs its TerrainCost, so a wall
            // with digCost 15 reads as "as bad as a 15-tile detour" and the field
            // decides between routing around and digging through on its own.
            while (open.Count > 0)
            {
                SimNode current = open.Dequeue();
                int count = grid.GetNeighbors(current, neighbors);

                for (int k = 0; k < count; k++)
                {
                    SimNode neighbor = neighbors[k];
                    if (!neighbor.IsWalkable) continue; // static geometry only

                    bool diagonal = neighbor.X != current.X && neighbor.Y != current.Y;
                    int stepCost = (diagonal ? DiagonalCost : CardinalCost) * neighbor.TerrainCost;
                    int newCost = current.BestCost + stepCost;

                    // Strict '<': on a tie the neighbour keeps the parent it found
                    // first, which is what makes GetNeighbors' order matter.
                    if (newCost < neighbor.BestCost)
                    {
                        neighbor.BestCost = newCost;
                        neighbor.NextIndex = current.Index;
                        open.Enqueue(neighbor);
                    }
                }
            }

            grid.FieldVersion++;
        }
    }
}
