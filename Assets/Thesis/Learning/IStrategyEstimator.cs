using Thesis.Core;

namespace Thesis.Learning
{
    // Chosen by a config value (DirectorConfig.EstimatorKind, WP11), never by #if
    // (CLAUDE.md §7).
    public interface IStrategyEstimator
    {
        int BinCount { get; }
        int StrategyCount { get; }
        Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng);
        void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng);
        EstimatorState Export();
        void Import(EstimatorState state);
    }
}
