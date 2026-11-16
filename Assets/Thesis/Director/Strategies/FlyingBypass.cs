using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // flying_bypass (S10): flyers over the maze. The whole wave is flyers, from the one entry
    // whose straight line to the core passes the fewest towers that can hit flyers (a tower
    // counts when its range reaches the line). Ties go to the lowest entry.
    //
    // Whether the player can hit flyers at all is not this class's business: the director's
    // constraint layer vetoes the strategy then (CLAUDE.md §2, WP10).
    public sealed class FlyingBypass : IWaveStrategy
    {
        public const string StrategyName = "flying_bypass";

        private readonly StrategyKit kit;

        public FlyingBypass(StrategyKit kit) { this.kit = kit; }

        public int Id => 3;
        public string Name => StrategyName;

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng)
        {
            List<int> candidates = kit.Candidates(context);
            SimState s = context.State;
            TileCoord core = context.State.Core;
            int best = candidates[0];
            int bestCover = int.MaxValue;
            foreach (int e in candidates)
            {
                TileCoord from = kit.Map.Spawns[e];
                int cover = 0;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (StrategyKit.Defends(tower) && tower.CanHitFlying && ReachesLine(tower, from, core)) cover++;
                }
                if (cover < bestCover) { bestCover = cover; best = e; }
            }

            AgentGroup flyers = EnemyArchetypes.Flyer().Apply(kit.Basic(context.WaveIndex), 1f);
            flyers.SpawnIndex = best;
            return kit.Fit(Name, context.WaveIndex, new[] { flyers }, new[] { 1f }, threatBudget);
        }

        // Squared distance from the tower to the segment from → to, in tiles, against its range.
        private static bool ReachesLine(TowerState tower, TileCoord from, TileCoord to)
        {
            float ax = from.X, ay = from.Y;
            float dx = (float)(to.X - from.X), dy = (float)(to.Y - from.Y);
            float px = (float)(tower.Tile.X - ax), py = (float)(tower.Tile.Y - ay);
            float length = (float)((float)(dx * dx) + (float)(dy * dy));
            float u = length > 0f ? (float)((float)((float)(px * dx) + (float)(py * dy)) / length) : 0f;
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            float ex = (float)(px - (float)(u * dx)), ey = (float)(py - (float)(u * dy));
            float d2 = (float)((float)(ex * ex) + (float)(ey * ey));
            return d2 <= (float)(tower.RangeTiles * tower.RangeTiles);
        }
    }
}
