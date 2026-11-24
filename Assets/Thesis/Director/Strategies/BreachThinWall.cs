using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // breach_thin_wall (S10): sappers at the thinnest wall, with brutes behind them (2026-11-24:
    // pairs), from the entries whose sapper route to the core is cheapest. The sapper field prices the player's
    // tiles at SapperDigCostFactor and rocks at full price, so the cheapest sapper route is the
    // one with the least of the player's wall in the way: the thinnest wall. Ties go to the
    // lowest entry, so the choice depends on the board alone.
    public sealed class BreachThinWall : IWaveStrategy
    {
        public const string StrategyName = "breach_thin_wall";

        private readonly StrategyKit kit;

        public BreachThinWall(StrategyKit kit) { this.kit = kit; }

        public int Id => 0;
        public string Name => StrategyName;

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng, int entries)
        {
            SimGrid grid = context.State.Grid;
            List<int> ranked = StrategyKit.RankBy(kit.Candidates(context), e => grid.Get(kit.Map.Spawns[e]).SapperCost);
            return kit.Squads(Name, context.WaveIndex, ranked, entries, EnemyArchetypes.Sapper(), EnemyArchetypes.Brute(), StrategyKit.MainShare, threatBudget);
        }
    }
}
