using Thesis.Learning;

namespace Thesis.Director
{
    // Profile → the learner's context (spec gap S1, the student's provisional choice of
    // 2026-11-16): the bin is chokepoint_reliance cut at four edges (its 20/40/60/80 % points over
    // the builder player's boards); the kernel's two coordinates are tower_concentration and
    // maze_length, each normalised over its measured 5 to 95 % range. The threshold freeze (I6)
    // fixes them; after it they are immutable.
    public sealed class ContextBucketizer
    {
        private readonly DirectorConfig config;

        public ContextBucketizer(DirectorConfig config) { this.config = config; }

        public int BinCount => config.BinEdges.Length + 1;

        // The number of edges at or below the value. No data: DirectorConfig.NoDataBin (the
        // middle), not where the placeholder 0.5 would fall (above the top edge).
        public int Bin(BuildProfile profile)
        {
            if (!profile.Has(config.BinFeature)) return config.NoDataBin;
            float v = profile[config.BinFeature];
            int bin = 0;
            for (int i = 0; i < config.BinEdges.Length; i++) if (v >= config.BinEdges[i]) bin = i + 1;
            return bin;
        }

        public DecisionContext Context(BuildProfile profile)
        {
            return new DecisionContext(Bin(profile), Coordinate(profile, config.KernelXFeature), Coordinate(profile, config.KernelYFeature));
        }

        private float Coordinate(BuildProfile profile, string feature)
        {
            return profile.Has(feature) ? config.Normalise(feature, profile[feature]) : 0.5f;
        }
    }
}
