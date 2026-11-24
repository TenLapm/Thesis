using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // One kind of wave (ARCHITECTURE.md §5.4): a threat budget and the board → a plan that
    // spends exactly that budget (I2). Id is the strategy's arm in the learner (the order of
    // StrategyPool.Study). A strategy only READS the context; the simulation checks.
    //
    // Since 2026-11-24 (the student: "instead of a whole wave dedicated to a single enemy type")
    // a strategy sends pairs: its main type with a helper that fits it, from its `entries` best
    // entries by its own measure, and the director builds a wave from several strategies
    // (WaveDirector: the lead and its supports).
    public interface IWaveStrategy
    {
        int Id { get; }
        string Name { get; }
        WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng, int entries);
    }

    public static class WaveStrategies
    {
        // One entry: the strategy's best.
        public static WavePlan BuildPlan(this IWaveStrategy s, float threatBudget, WaveContext context, IRandom rng)
        {
            return s.BuildPlan(threatBudget, context, rng, 1);
        }
    }
}
