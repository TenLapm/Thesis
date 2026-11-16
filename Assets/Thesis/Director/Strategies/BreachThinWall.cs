using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // breach_thin_wall (S10): sappers at the thinnest wall. The whole wave is sappers, from the
    // one entry whose sapper route to the core is cheapest. The sapper field prices the player's
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

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng)
        {
            List<int> candidates = kit.Candidates(context);
            SimGrid grid = context.State.Grid;
            int best = candidates[0];
            int bestCost = int.MaxValue;
            foreach (int e in candidates)
            {
                int cost = grid.Get(kit.Map.Spawns[e]).SapperCost;
                if (cost < bestCost) { bestCost = cost; best = e; }
            }

            AgentGroup sappers = EnemyArchetypes.Sapper().Apply(kit.Basic(context.WaveIndex), 1f);
            sappers.SpawnIndex = best;
            return kit.Fit(Name, context.WaveIndex, new[] { sappers }, new[] { 1f }, threatBudget);
        }
    }
}
