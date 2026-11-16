using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // One kind of wave (ARCHITECTURE.md §5.4): a threat budget and the board → a plan that
    // spends exactly that budget (I2). Id is the strategy's arm in the learner (0 to 3, the
    // order of StrategyPool.Study). A strategy only READS the context; the simulation checks.
    public interface IWaveStrategy
    {
        int Id { get; }
        string Name { get; }
        WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng);
    }
}
