using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Range checks and target choice for towers.
    public static class Targeting
    {
        // True when two world positions are no further apart than the radius whose
        // SQUARE is given. Squared distances avoid a square root per check.
        // Explicit casts round every float intermediate, so Mono and .NET agree to
        // the bit on which side of the edge an enemy is (ARCHITECTURE.md §9 rule 3).
        public static bool InRange(Vec2f a, Vec2f b, float radiusSquared)
        {
            float dx = (float)(a.X - b.X);
            float dy = (float)(a.Y - b.Y);
            float distanceSquared = (float)((float)(dx * dx) + (float)(dy * dy));
            return distanceSquared <= radiusSquared;
        }

        // The index in `live` of the enemy this tower shoots, or -1 for none.
        // `live` must be in ascending id order (SimState.Live is).
        public static int Pick(TargetingMode mode, TowerState tower, SimGrid grid, IList<AgentState> live)
        {
            switch (mode)
            {
                case TargetingMode.First: return PickFirst(tower, grid, live);
                default: throw new InvalidOperationException("[Sim] Unknown targeting mode " + mode + ".");
            }
        }

        // The enemy with the lowest route cost to the core, i.e. the one that will
        // leak soonest. Ties go to the lowest id: with a strict '<' the first one
        // found keeps the shot, and the list is in id order.
        private static int PickFirst(TowerState tower, SimGrid grid, IList<AgentState> live)
        {
            int best = -1;
            int bestCost = int.MaxValue;
            for (int i = 0; i < live.Count; i++)
            {
                AgentState a = live[i];
                if (!a.IsAlive) continue; // killed earlier this tick, not yet removed
                if (!InRange(a.Position, tower.Position, tower.RangeSquared)) continue;

                int cost = grid.NodeFromPosition(a.Position).BestCost;
                if (best < 0 || cost < bestCost)
                {
                    best = i;
                    bestCost = cost;
                }
            }
            return best;
        }
    }
}
