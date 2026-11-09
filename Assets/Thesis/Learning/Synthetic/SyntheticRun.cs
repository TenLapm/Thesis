using System;
using Thesis.Core;

namespace Thesis.Learning.Synthetic
{
    public enum SyntheticPolicy
    {
        // Thompson sampling over the posteriors: the arm with the largest drawn θ.
        // This is the learner on its own, which is what G1 asks about.
        Thompson,

        // The director's rule with every authored score 0: the largest Correction.Value,
        // where a cold cell counts exactly 0 (CLAUDE.md §5). Reported as a diagnostic
        // of spec gap S2, because a cold arm can lose to any warm arm with θ > 0.5 and
        // then never be tried.
        DirectorCorrection,

        UniformRandom,
        RoundRobin,
    }

    // Runs one policy against ThresholdBanditEnv. Three named streams per seed, so an
    // extra draw in one (say, a policy that samples more) never shifts another:
    // stream 1 the contexts, stream 2 the pay-outs, stream 3 the policy and learner.
    public static class SyntheticRun
    {
        public sealed class Options
        {
            public int Episodes = 300;
            public double Discount = BinnedPosterior.DefaultDiscount;
            public int FlipAtEpisode = -1;
            public ThresholdBanditEnv.Options Env;   // null: the defaults, with FlipAtEpisode above
        }

        public sealed class Result
        {
            public int[] Chosen;
            public bool[] Best;
            public CalibrationReport Calibration = new CalibrationReport();
        }

        public static Result Run(SyntheticPolicy policy, Options options, ulong seed)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            ThresholdBanditEnv.Options envOptions = options.Env ?? new ThresholdBanditEnv.Options();
            if (options.Env == null) envOptions.FlipAtEpisode = options.FlipAtEpisode;
            var env = new ThresholdBanditEnv(envOptions);
            var posterior = new BinnedPosterior(env.Bins, env.Arms, options.Discount);

            var contexts = new Pcg32(seed, 1UL);
            var payouts = new Pcg32(seed, 2UL);
            var learner = new Pcg32(seed, 3UL);

            var result = new Result { Chosen = new int[options.Episodes], Best = new bool[options.Episodes] };
            for (int e = 0; e < options.Episodes; e++)
            {
                double x = env.NextContext(contexts);
                var ctx = new DecisionContext(env.BinOf(x), (float)x, 0f);
                int arm = Choose(policy, posterior, ctx, env.Arms, e, learner);

                // The prediction is taken before the update it is checked against.
                double predicted = posterior.Mean(ctx.Bin, arm);
                float reward = env.Pull(arm, x, e, payouts);
                result.Calibration.Add(predicted, reward);

                if (policy == SyntheticPolicy.Thompson || policy == SyntheticPolicy.DirectorCorrection)
                    posterior.Update(ctx, arm, reward, 1f, learner);

                result.Chosen[e] = arm;
                result.Best[e] = arm == env.BestArm(x, e);
            }
            return result;
        }

        // Share of best-arm picks in episodes [from, to) (0-based), for one run.
        public static double BestShare(Result r, int from, int to)
        {
            if (from < 0 || to > r.Best.Length || from >= to) throw new ArgumentOutOfRangeException(nameof(from), "window [" + from + ", " + to + ") is not inside the run");
            int hits = 0;
            for (int e = from; e < to; e++) if (r.Best[e]) hits++;
            return (double)hits / (to - from);
        }

        // The G1 number: the mean over seeds 1..seeds of BestShare(from, to).
        public static double MeanBestShare(SyntheticPolicy policy, Options options, int seeds, int from, int to)
        {
            double sum = 0.0;
            for (int s = 1; s <= seeds; s++) sum += BestShare(Run(policy, options, (ulong)s), from, to);
            return sum / seeds;
        }

        private static int Choose(SyntheticPolicy policy, BinnedPosterior posterior, DecisionContext ctx, int arms, int episode, IRandom rng)
        {
            switch (policy)
            {
                case SyntheticPolicy.UniformRandom: return rng.NextInt(arms);
                case SyntheticPolicy.RoundRobin: return episode % arms;
                case SyntheticPolicy.Thompson:
                case SyntheticPolicy.DirectorCorrection:
                {
                    // Every arm is sampled, in ascending id; ties go to the lowest id,
                    // as in WaveDirector.PlanWave (ARCHITECTURE.md §5.2).
                    int best = 0;
                    float bestScore = float.NegativeInfinity;
                    for (int a = 0; a < arms; a++)
                    {
                        Correction c = posterior.SampleCorrection(ctx, a, rng);
                        float score = policy == SyntheticPolicy.Thompson ? c.Theta : c.Value;
                        if (score > bestScore) { bestScore = score; best = a; }
                    }
                    return best;
                }
                default: throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
            }
        }
    }
}
