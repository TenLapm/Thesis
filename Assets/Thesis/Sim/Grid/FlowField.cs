using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of FlowFieldManager.GenerateFlowField: one synchronous weighted flood fill
    // (SPFA - a BFS that re-queues a node whenever a cheaper route is found), run
    // whenever the map changes. On this grid size it costs a fraction of a
    // millisecond per field, which is why the old chunk system, background threads,
    // the isCalculating lock, ValidatePath, sinkhole detection and the global
    // fallback were deleted. Do not reintroduce any of them without a measured
    // reason (CLAUDE.md §8).
    //
    // Since WP-C2 the same flood builds two fields, which differ only in what a
    // built tile costs to enter:
    //   Ground  its TerrainCost               -> SimNode.BestCost / NextIndex
    //   Sapper  TerrainCost x a factor < 1    -> SimNode.SapperCost / SapperNextIndex
    // One implementation for both, so the two cannot drift apart. For Ground it does
    // exactly what it did before the second field existed: the same integers in the
    // same order (FlowFieldSetTests compares it with the WP1 code on random boards).
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

        // The GROUND field, and only it. This is what a caller wants when it asks a
        // routing question about ordinary enemies on a grid of its own: a policy
        // trying a wall on a scratch copy, a map check. The simulation never calls
        // this directly; it rebuilds through FlowFieldSet, which keeps the sapper
        // field in step.
        public void Generate(SimGrid grid, TileCoord goal)
        {
            Fill(grid, goal, MovementClass.Ground, 1f);
            grid.FieldVersion++;
        }

        // The SAPPER field. It does not touch FieldVersion: FlowFieldSet builds both
        // fields, and the two together are one rebuild.
        public void GenerateSapper(SimGrid grid, TileCoord goal, float digCostFactor)
        {
            Fill(grid, goal, MovementClass.Sapper, digCostFactor);
        }

        // What a sapper pays to enter a tile. Open ground costs what it costs anyone.
        // A built tile (a wall or a tower) costs its TerrainCost times the factor,
        // rounded to a whole number and never below open ground. With factor 0.2 a
        // wall of cost 15 reads as "as bad as a 3-tile detour" instead of 15.
        //
        // One multiplication, rounded once, so every runtime gets the same integer
        // (ARCHITECTURE.md §9 rule 3).
        public static int SapperTerrainCost(SimNode node, float digCostFactor)
        {
            if (!node.HasWall) return node.TerrainCost;
            int scaled = (int)Math.Round((float)(node.TerrainCost * digCostFactor));
            return scaled < 1 ? 1 : scaled;
        }

        private void Fill(SimGrid grid, TileCoord goal, MovementClass movement, float digCostFactor)
        {
            bool sapper = movement == MovementClass.Sapper;

            // 1. Reset EVERYTHING up front - cost AND next pointer - so no stale
            // pointer can survive a recalculation. (An old version forgot the pointer
            // and left unreachable pockets pointing wherever they liked.)
            for (int i = 0; i < grid.NodeCount; i++) Write(grid.ByIndex(i), sapper, SimNode.Infinity, -1);

            SimNode target = grid.InBounds(goal) ? grid.Get(goal) : null;
            if (target == null || !target.IsWalkable) return;

            Write(target, sapper, 0, -1);
            open.Clear();
            open.Enqueue(target);

            // 2. Weighted relaxation. Entering a tile costs its TerrainCost, so a wall
            // with digCost 15 reads as "as bad as a 15-tile detour" and the field
            // decides between routing around and digging through on its own.
            while (open.Count > 0)
            {
                SimNode current = open.Dequeue();
                int currentCost = sapper ? current.SapperCost : current.BestCost;
                int count = grid.GetNeighbors(current, neighbors);

                for (int k = 0; k < count; k++)
                {
                    SimNode neighbor = neighbors[k];
                    if (!neighbor.IsWalkable) continue; // static geometry only

                    bool diagonal = neighbor.X != current.X && neighbor.Y != current.Y;
                    int terrain = sapper ? SapperTerrainCost(neighbor, digCostFactor) : neighbor.TerrainCost;
                    int stepCost = (diagonal ? DiagonalCost : CardinalCost) * terrain;
                    int newCost = currentCost + stepCost;

                    // Strict '<': on a tie the neighbour keeps the parent it found
                    // first, which is what makes GetNeighbors' order matter.
                    if (newCost < (sapper ? neighbor.SapperCost : neighbor.BestCost))
                    {
                        Write(neighbor, sapper, newCost, current.Index);
                        open.Enqueue(neighbor);
                    }
                }
            }
        }

        private static void Write(SimNode node, bool sapper, int cost, int nextIndex)
        {
            if (sapper)
            {
                node.SapperCost = cost;
                node.SapperNextIndex = nextIndex;
            }
            else
            {
                node.BestCost = cost;
                node.NextIndex = nextIndex;
            }
        }
    }
}
