namespace Thesis.Director
{
    // Layer 2 (CLAUDE.md §5): an authored score per strategy from the build profile. It is a whole
    // director on its own (DirectorMode.HeuristicOnly), the ablation condition and a rung of the
    // offline ladder.
    //
    // Each strategy answers one weakness (the student's provisional mapping, 2026-11-16):
    //   breach_thin_wall  ← breach_vulnerability   the route depends on walls holding
    //   swarm_chokepoint  ← chokepoint_reliance    a few towers do the work
    //   split_groups      ← tower_concentration    one cluster cannot cover three sides
    //   flying_bypass     ← maze_length            a long maze is wasted on flyers
    // The feature is mapped to 0..1 over its measured 5 to 95 % range and multiplied by
    // DirectorConfig.HeuristicScale (4), so scores run 0 to 4.
    //
    // This mapping is AUTHORED. CLAUDE.md §8: which layout a counter works against is a result to
    // report, not a rule; the thesis presents this as the hand-written baseline the learner corrects.
    //
    // A feature with no data scores the middle (2), not what its placeholder 0.5 would map to: a
    // raw 0.5 for chokepoint_reliance lies above its 95 % point and would score the maximum.
    public sealed class AuthoredHeuristic
    {
        private readonly DirectorConfig config;

        public AuthoredHeuristic(DirectorConfig config) { this.config = config; }

        public float Score(int strategyId, BuildProfile profile)
        {
            string feature = config.StrategyFeature[strategyId];
            if (!profile.Has(feature)) return (float)(0.5f * config.HeuristicScale);
            return (float)(config.Normalise(feature, profile[feature]) * config.HeuristicScale);
        }
    }
}
