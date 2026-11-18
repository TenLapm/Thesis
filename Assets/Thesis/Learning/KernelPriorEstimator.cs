using System;
using Thesis.Core;

namespace Thesis.Learning
{
    // The director's estimator (WP11): the 20 Beta cells of BinnedPosterior, with the kernel as an
    // empirical prior for thin cells (CLAUDE.md §5; spec gap S2, the student's provisional answer
    // of 2026-11-18):
    //
    //   thin   a cell with fewer than ThinBelow (4) results. While thin, the kernel's estimate
    //          at this board's point is ADDED on top of the cell's own counts, worth PriorWeight
    //          (2) results: alpha + k * Q, beta + k * (1 - Q). With 4 results the cell stands alone.
    //   cold   a cell with no results AND no kernel data near (the kernel's weight sum below
    //          NearWeight, half an entry's worth): its correction is 0.
    //
    // Why: WP7 measured that with "no results = correction 0" a strategy can stay untried for good
    // (the worst seed fell to 0.54). Borrowing from similar boards gives an untried cell a
    // reason to move. Added on top, not replacing, so a cell's own evidence is never thrown away.
    //
    // The kernel learns from direct credit only (weight 1): a lagged, partial credit goes to the
    // cell, but the kernel stores rewards, not weights, and would count it as a full observation.
    public sealed class KernelPriorEstimator : IStrategyEstimator
    {
        public const string KindName = "binned-kernel";
        public const int DefaultThinBelow = 4;
        public const double DefaultPriorWeight = 2.0;
        public const double DefaultNearWeight = 0.5;

        private BinnedPosterior cells;
        private KernelEstimator kernel;
        private int thinBelow;
        private double priorWeight;
        private double nearWeight;

        public KernelPriorEstimator(int bins = 5, int strategies = 4, double discount = BinnedPosterior.DefaultDiscount,
                                    int thinBelow = DefaultThinBelow, double priorWeight = DefaultPriorWeight, double nearWeight = DefaultNearWeight)
        {
            if (thinBelow < 0) throw new ArgumentOutOfRangeException(nameof(thinBelow), thinBelow, "must be >= 0");
            if (!(priorWeight >= 0.0)) throw new ArgumentOutOfRangeException(nameof(priorWeight), priorWeight, "must be >= 0");
            if (!(nearWeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(nearWeight), nearWeight, "must be > 0");
            cells = new BinnedPosterior(bins, strategies, discount);
            kernel = new KernelEstimator();
            this.thinBelow = thinBelow;
            this.priorWeight = priorWeight;
            this.nearWeight = nearWeight;
        }

        public BinnedPosterior Cells => cells;
        public KernelEstimator Kernel => kernel;
        public int BinCount => cells.BinCount;
        public int StrategyCount => cells.StrategyCount;

        public Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            double a = cells.Alpha(ctx.Bin, strategy), b = cells.Beta(ctx.Bin, strategy);
            int seen = cells.Updates(ctx.Bin, strategy);
            KernelEstimate q = kernel.Query(ctx.KernelX, ctx.KernelY, strategy);
            bool near = q.HasData && q.WeightSum >= nearWeight;
            if (seen < thinBelow && near)
            {
                a += priorWeight * q.Mean;
                b += priorWeight * (1.0 - q.Mean);
            }
            // Theta is drawn for a cold cell too, as in BinnedPosterior: the number of draws never
            // depends on which cells are cold.
            double theta = BetaSampler.Sample(a, b, rng);
            bool cold = seen == 0 && !near;
            return new Correction
            {
                Theta = (float)theta,
                Alpha = (float)a,
                Beta = (float)b,
                Value = cold ? 0f : BinnedPosterior.CorrectionFromTheta(theta),
                Cold = cold,
            };
        }

        public void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng)
        {
            cells.Update(ctx, strategy, reward, creditWeight, rng);
            if (creditWeight == 1f) kernel.Add(ctx.KernelX, ctx.KernelY, strategy, reward);
        }

        public EstimatorState Export()
        {
            EstimatorState s = cells.Export();
            s.Kind = KindName;
            s.Kernel = kernel.Export();
            s.ThinBelow = thinBelow;
            s.PriorWeight = priorWeight;
            s.NearWeight = nearWeight;
            return s;
        }

        // Checks everything before changing anything (the inner Import does the same for the cells).
        public void Import(EstimatorState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Kind != KindName) throw new ArgumentException("estimator kind '" + state.Kind + "', expected '" + KindName + "'");
            if (state.Kernel == null) throw new ArgumentException("a " + KindName + " state needs its kernel");
            var newCells = new BinnedPosterior(state.Bins, state.Strategies, state.Discount);
            var inner = new EstimatorState
            {
                Schema = state.Schema, Kind = BinnedPosterior.KindName, Bins = state.Bins, Strategies = state.Strategies,
                Discount = state.Discount, Alpha = state.Alpha, Beta = state.Beta, Updates = state.Updates,
            };
            newCells.Import(inner);
            var newKernel = new KernelEstimator(state.Kernel.Capacity, state.Kernel.Sigma);
            newKernel.Import(state.Kernel);
            if (state.ThinBelow < 0 || !(state.PriorWeight >= 0.0) || !(state.NearWeight > 0.0))
                throw new ArgumentException("thin-cell settings out of range");
            cells = newCells;
            kernel = newKernel;
            thinBelow = state.ThinBelow;
            priorWeight = state.PriorWeight;
            nearWeight = state.NearWeight;
        }
    }
}
