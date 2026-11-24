using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // flying_bypass (S10): flyers over the maze, with bats (2026-11-24: pairs), from the entries
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

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng, int entries)
        {
            SimState s = context.State;
            TileCoord core = context.State.Core;
            List<int> ranked = StrategyKit.RankBy(kit.Candidates(context), e =>
            {
                TileCoord from = kit.Map.Spawns[e];
                int cover = 0;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (StrategyKit.Defends(tower) && tower.CanHitFlying && ReachesLine(tower, from, core)) cover++;
                }
                return cover;
            });
            return kit.Squads(Name, context.WaveIndex, ranked, entries, EnemyArchetypes.Flyer(), EnemyArchetypes.Bats(), StrategyKit.MainShare, threatBudget);
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
