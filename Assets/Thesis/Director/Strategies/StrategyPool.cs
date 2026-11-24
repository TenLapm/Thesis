using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // The study's strategies (S10, the student, 2026-11-16): the proposal's four, in a fixed
    // order that is also their arm in the learner, and since 2026-11-24 the raid (the student), so
    // 5 bins × 5 strategies.
    public static class StrategyPool
    {
        public static IReadOnlyList<IWaveStrategy> Study(StrategyKit kit)
        {
            return new IWaveStrategy[] { new BreachThinWall(kit), new SwarmChokepoint(kit), new SplitGroups(kit), new FlyingBypass(kit), new RaidOutposts(kit) };
        }
    }

    // One strategy every wave, at the table's budget. Not a study condition: for tests, and for
    // measuring the cost table's class factors with the scripted players (WP9).
    public sealed class FixedStrategyPlanner : IWavePlanner
    {
        public readonly IWaveStrategy Strategy;
        private readonly ThreatCostTable table;
        private readonly Pcg32 rng;

        public FixedStrategyPlanner(IWaveStrategy strategy, ThreatCostTable table, ulong seed)
        {
            Strategy = strategy;
            this.table = table;
            rng = new Pcg32(seed, RngStreams.Strategy);
        }

        public string Name => "fixed-" + Strategy.Name;

        public WavePlan PlanWave(WaveContext context) { return Strategy.BuildPlan(table.BudgetForWave(context.WaveIndex), context, rng); }

        public void OnWaveResolved(WaveOutcome outcome) { }
    }
}
