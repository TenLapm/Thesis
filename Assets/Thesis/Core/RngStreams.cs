namespace Thesis.Core
{
    // Stream ids for Pcg32. Each subsystem draws only from its own stream, all seeded
    // from the same run seed. Never reuse or renumber an id: recorded replays
    // depend on these exact values.
    public static class RngStreams
    {
        public const ulong Bag = 1;              // wall shape 7-bag
        public const ulong Spawn = 2;            // spawn jitter / ordering, if ever needed
        public const ulong Strategy = 3;         // IWaveStrategy.BuildPlan
        public const ulong Thompson = 4;         // Beta samples in the learned layer
        public const ulong RewardBernoulli = 5;  // Bernoulli(r) in the posterior update
        public const ulong Policy = 6;           // scripted player policies (harness only)

        // The shop does NOT keep a running stream. Each roll seeds a fresh Pcg32 from
        // Fnv1a64(rngSeed, "shop", waveIndex, rerollCount) on this stream id, so offers
        // never depend on what happened earlier in the run (CLAUDE.md I10).
        public const ulong Shop = 7;
    }
}
