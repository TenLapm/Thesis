using System;
using Thesis.Core;

namespace Thesis.Learning.Synthetic
{
    // The known-answer problem for G1 and G3 (ARCHITECTURE.md §5.6): the best arm
    // flips at a threshold in a one-dimensional context. This is the shape that
    // made a linear contextual bandit fail (CLAUDE.md §8), so it is the shape the
    // binned learner has to get right.
    //
    // Each episode: a context x ~ U[0, 1), binned into equal bins; the learner picks
    // an arm; the arm pays 1 with probability p(arm, x), else 0.
    //   x <  Threshold: LowBestArm  pays with BestP, every other arm with OtherP
    //   x >= Threshold: HighBestArm pays with BestP, every other arm with OtherP
    // From FlipAtEpisode on (the review probe) the two best arms swap.
    //
    // The numbers are not given by any spec; they were fixed on 2026-11-10, before
    // the first run, and are written into every result: 4 arms, 5 bins, threshold
    // 0.6 (a bin edge, so the bins can represent it exactly), BestP 0.8, OtherP 0.4.
    public sealed class ThresholdBanditEnv
    {
        public sealed class Options
        {
            public int Arms = 4;
            public int Bins = 5;
            public double Threshold = 0.6;
            public double BestP = 0.8;
            public double OtherP = 0.4;
            public int LowBestArm = 0;
            public int HighBestArm = 1;
            public int FlipAtEpisode = -1;   // < 0: never
        }

        private readonly Options o;

        public ThresholdBanditEnv(Options options)
        {
            o = options ?? throw new ArgumentNullException(nameof(options));
            if (o.Arms < 2) throw new ArgumentOutOfRangeException(nameof(options), "needs at least two arms");
            if (o.Bins < 1) throw new ArgumentOutOfRangeException(nameof(options), "needs at least one bin");
            if (o.LowBestArm < 0 || o.LowBestArm >= o.Arms || o.HighBestArm < 0 || o.HighBestArm >= o.Arms)
                throw new ArgumentOutOfRangeException(nameof(options), "the best arms must be arms of the problem");
            if (!(o.BestP >= 0.0 && o.BestP <= 1.0 && o.OtherP >= 0.0 && o.OtherP <= 1.0))
                throw new ArgumentOutOfRangeException(nameof(options), "pay-out probabilities must be in [0, 1]");
        }

        public int Arms => o.Arms;
        public int Bins => o.Bins;

        public double NextContext(IRandom rng) { return rng.NextDouble(); }

        public int BinOf(double x)
        {
            int bin = (int)(x * o.Bins);
            if (bin < 0) bin = 0;
            if (bin >= o.Bins) bin = o.Bins - 1;
            return bin;
        }

        public int BestArm(double x, int episode)
        {
            bool flipped = o.FlipAtEpisode >= 0 && episode >= o.FlipAtEpisode;
            bool low = x < o.Threshold;
            if (flipped) low = !low;
            return low ? o.LowBestArm : o.HighBestArm;
        }

        public double RewardProbability(int arm, double x, int episode)
        {
            return arm == BestArm(x, episode) ? o.BestP : o.OtherP;
        }

        public float Pull(int arm, double x, int episode, IRandom rng)
        {
            if (arm < 0 || arm >= o.Arms) throw new ArgumentOutOfRangeException(nameof(arm), arm, "not an arm of this problem");
            return rng.NextDouble() < RewardProbability(arm, x, episode) ? 1f : 0f;
        }
    }
}
