using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // The bottom rungs of the offline ladder (CLAUDE.md §6: random < static escalation < authored
    // heuristic < full director) and the baselines gate G1 compares the learner against. Both play
    // the study's four strategies at the cost table's budget, so they differ from the director only
    // in how they choose. Neither reads the board to decide, and neither vetoes anything.

    // A strategy drawn uniformly each wave from the strategy stream.
    public sealed class RandomPlanner : IWavePlanner
    {
        public const string Id = "random";

        private readonly IReadOnlyList<IWaveStrategy> pool;
        private readonly ThreatCostTable table;
        private readonly Pcg32 rng;

        public RandomPlanner(SimConfig config, MapData map, ThreatCostTable table, ulong seed)
        {
            this.table = table;
            pool = StrategyPool.Study(new StrategyKit(config, map, table));
            rng = new Pcg32(seed, RngStreams.Strategy);
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context)
        {
            IWaveStrategy s = pool[rng.NextInt(pool.Count)];
            return s.BuildPlan(table.BudgetForWave(context.WaveIndex), context, rng, StrategyKit.EntriesFor(context.WaveIndex));
        }

        public void OnWaveResolved(WaveOutcome outcome) { }
    }

    // The strategies in id order, one a wave: wave 1 the first, wave 5 the first again. By wave
    // number, not by call, so a wave planned twice gets the same strategy.
    public sealed class RoundRobinPlanner : IWavePlanner
    {
        public const string Id = "round-robin";

        private readonly IReadOnlyList<IWaveStrategy> pool;
        private readonly ThreatCostTable table;
        private readonly Pcg32 rng;

        public RoundRobinPlanner(SimConfig config, MapData map, ThreatCostTable table, ulong seed)
        {
            this.table = table;
            pool = StrategyPool.Study(new StrategyKit(config, map, table));
            rng = new Pcg32(seed, RngStreams.Strategy);
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context)
        {
            IWaveStrategy s = pool[(context.WaveIndex - 1) % pool.Count];
            return s.BuildPlan(table.BudgetForWave(context.WaveIndex), context, rng, StrategyKit.EntriesFor(context.WaveIndex));
        }

        public void OnWaveResolved(WaveOutcome outcome) { }
    }
}
