using System;
using Thesis.Core;

namespace Thesis.Learning
{
    // One Beta posterior per (bin, strategy) cell: 5 × 4 = 20 in the study
    // (CLAUDE.md §5). Binning and not a linear model, because the best strategy
    // flips at a threshold in one context feature, which a linear model cannot
    // represent (CLAUDE.md §8). Do not "simplify" it back.
    //
    // Selection (Thompson sampling as a correction, I4):
    //     theta ~ Beta(alpha, beta);   Value = Cold ? 0 : 2 (theta - 0.5)
    // Update (the discounted Bernoulli update, CLAUDE.md §5):
    //     rTilde ~ Bernoulli(r)
    //     alpha = 1 + G (alpha - 1) + rTilde
    //     beta  = 1 + G (beta  - 1) + (1 - rTilde)
    // Decaying toward Beta(1, 1) rather than toward 0 is what keeps alpha, beta >= 1,
    // so BetaSampler never needs its below-1 branch. Only the cell that is updated
    // decays; the others keep what they had.
    //
    // "Cold" here means the cell has never been updated. Whether a thin cell (a few
    // updates) should also count as cold, and how the kernel prior fills it, is
    // spec gap S2, closed in WP11.
    public sealed class BinnedPosterior : IStrategyEstimator
    {
        public const string KindName = "binned-posterior";
        public const double DefaultDiscount = 0.95;

        private readonly int bins;
        private readonly int strategies;
        private readonly double discount;
        private double[] alpha;
        private double[] beta;
        private int[] updates;

        public BinnedPosterior(int bins = 5, int strategies = 4, double discount = DefaultDiscount)
        {
            if (bins < 1) throw new ArgumentOutOfRangeException(nameof(bins), bins, "needs at least one bin");
            if (strategies < 1) throw new ArgumentOutOfRangeException(nameof(strategies), strategies, "needs at least one strategy");
            if (!(discount > 0.0 && discount <= 1.0)) throw new ArgumentOutOfRangeException(nameof(discount), discount, "the discount G must be in (0, 1]");

            this.bins = bins;
            this.strategies = strategies;
            this.discount = discount;
            alpha = Filled(bins * strategies, 1.0);
            beta = Filled(bins * strategies, 1.0);
            updates = new int[bins * strategies];
        }

        public int BinCount => bins;
        public int StrategyCount => strategies;
        public double Discount => discount;

        public double Alpha(int bin, int strategy) { return alpha[Cell(bin, strategy)]; }
        public double Beta(int bin, int strategy) { return beta[Cell(bin, strategy)]; }
        public int Updates(int bin, int strategy) { return updates[Cell(bin, strategy)]; }

        // The posterior mean α / (α + β): the "predicted p" of CalibrationReport.
        public double Mean(int bin, int strategy)
        {
            int cell = Cell(bin, strategy);
            return alpha[cell] / (alpha[cell] + beta[cell]);
        }

        // Theta is drawn even for a cold cell, so the number of draws taken from rng
        // never depends on which cells happen to be cold. The selection rule can then
        // change (S2) without shifting every later draw of the stream.
        public Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            int cell = Cell(ctx.Bin, strategy);
            double theta = BetaSampler.Sample(alpha[cell], beta[cell], rng);
            bool cold = updates[cell] == 0;
            return new Correction
            {
                Theta = (float)theta,
                Alpha = (float)alpha[cell],
                Beta = (float)beta[cell],
                Value = cold ? 0f : CorrectionFromTheta(theta),
                Cold = cold,
            };
        }

        // 2 (theta - 0.5), clamped so the bound of I4 holds even for a theta that is
        // not a probability.
        public static float CorrectionFromTheta(double theta)
        {
            double value = 2.0 * (theta - 0.5);
            if (value > 1.0) value = 1.0;
            if (value < -1.0) value = -1.0;
            return (float)value;
        }

        public void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            int cell = Cell(ctx.Bin, strategy);
            if (!(reward >= 0f && reward <= 1f))
                throw new ArgumentOutOfRangeException(nameof(reward), reward, "a reward must be in [0, 1]");

            // Spec gap S3: scale r before the draw, or a fractional update? WP11
            // decides. Until then only full credit is accepted, so nothing picks an
            // answer by accident.
            if (creditWeight != 1f)
                throw new NotSupportedException("Partial credit (weight " + creditWeight + ") waits for spec gap S3 (ARCHITECTURE.md §10, WP11).");

            // Bernoulli(r) keeps the Beta update conjugate for a reward anywhere in
            // [0, 1]. The draw is taken even for r = 0 or 1, so the stream advances
            // the same way whatever the reward.
            int rTilde = rng.NextDouble() < reward ? 1 : 0;
            alpha[cell] = 1.0 + discount * (alpha[cell] - 1.0) + rTilde;
            beta[cell] = 1.0 + discount * (beta[cell] - 1.0) + (1 - rTilde);
            if (updates[cell] < int.MaxValue) updates[cell]++;
        }

        public EstimatorState Export()
        {
            return new EstimatorState
            {
                Kind = KindName,
                Bins = bins,
                Strategies = strategies,
                Discount = discount,
                Alpha = (double[])alpha.Clone(),
                Beta = (double[])beta.Clone(),
                Updates = (int[])updates.Clone(),
            };
        }

        // Checks everything before changing anything, so a refused state leaves the
        // estimator as it was.
        public void Import(EstimatorState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Schema != EstimatorState.CurrentSchema) throw new ArgumentException("estimator state schema " + state.Schema + ", expected " + EstimatorState.CurrentSchema);
            if (state.Kind != KindName) throw new ArgumentException("estimator state is a " + state.Kind + ", not a " + KindName);
            if (state.Bins != bins || state.Strategies != strategies)
                throw new ArgumentException("estimator state is " + state.Bins + " × " + state.Strategies + ", this posterior is " + bins + " × " + strategies);
            if (state.Discount != discount) throw new ArgumentException("estimator state has discount " + state.Discount + ", this posterior " + discount);

            int n = bins * strategies;
            if (state.Alpha == null || state.Beta == null || state.Updates == null
                || state.Alpha.Length != n || state.Beta.Length != n || state.Updates.Length != n)
                throw new ArgumentException("estimator state must hold " + n + " cells in each array");
            for (int i = 0; i < n; i++)
            {
                if (!(state.Alpha[i] >= 1.0) || !(state.Beta[i] >= 1.0) || double.IsInfinity(state.Alpha[i]) || double.IsInfinity(state.Beta[i]))
                    throw new ArgumentException("cell " + i + " has alpha " + state.Alpha[i] + ", beta " + state.Beta[i] + "; both must be at least 1");
                if (state.Updates[i] < 0) throw new ArgumentException("cell " + i + " has a negative update count");
            }

            alpha = (double[])state.Alpha.Clone();
            beta = (double[])state.Beta.Clone();
            updates = (int[])state.Updates.Clone();
        }

        private int Cell(int bin, int strategy)
        {
            if (bin < 0 || bin >= bins) throw new ArgumentOutOfRangeException(nameof(bin), bin, "bin must be in [0, " + bins + ")");
            if (strategy < 0 || strategy >= strategies) throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "strategy must be in [0, " + strategies + ")");
            return bin * strategies + strategy;
        }

        private static double[] Filled(int n, double value)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = value;
            return a;
        }
    }
}
