using System;
using System.Collections.Generic;

namespace Thesis.Learning
{
    // Does the learner's belief match what happened? Pairs of (predicted p, realised
    // reward), where p is a posterior mean α / (α + β), give two numbers:
    //   Brier score (Brier 1950): the mean of (p − r)²
    //   ECE, 10 bins (Guo et al. 2017): Σ_b (n_b / N) · |mean p_b − mean r_b|
    // Bins are [0, 0.1), [0.1, 0.2), …, [0.9, 1.0]; a p of exactly 1 goes in the last.
    //
    // A diagnostic reported beside G1, never a pass or fail: the gates were fixed
    // before implementation (CLAUDE.md §6). Borrowed from the Jev review (§8).
    public sealed class CalibrationReport
    {
        public const int BinCount = 10;

        public struct Bin
        {
            public double Lower;
            public double Upper;
            public int Count;
            public double MeanPredicted;
            public double MeanRealised;
        }

        private readonly int[] counts = new int[BinCount];
        private readonly double[] sumPredicted = new double[BinCount];
        private readonly double[] sumRealised = new double[BinCount];
        private double sumSquaredError;
        private int total;

        public int Count => total;

        public void Add(double predicted, double realised)
        {
            if (!(predicted >= 0.0 && predicted <= 1.0)) throw new ArgumentOutOfRangeException(nameof(predicted), predicted, "a prediction must be in [0, 1]");
            if (!(realised >= 0.0 && realised <= 1.0)) throw new ArgumentOutOfRangeException(nameof(realised), realised, "a realised reward must be in [0, 1]");

            int bin = (int)(predicted * BinCount);
            if (bin >= BinCount) bin = BinCount - 1;
            counts[bin]++;
            sumPredicted[bin] += predicted;
            sumRealised[bin] += realised;
            double e = predicted - realised;
            sumSquaredError += e * e;
            total++;
        }

        // Pools another report's pairs into this one. Exact, because both numbers are
        // made from sums: merging gives what adding every pair here would have given.
        public void Merge(CalibrationReport other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            for (int b = 0; b < BinCount; b++)
            {
                counts[b] += other.counts[b];
                sumPredicted[b] += other.sumPredicted[b];
                sumRealised[b] += other.sumRealised[b];
            }
            sumSquaredError += other.sumSquaredError;
            total += other.total;
        }

        public double BrierScore => total == 0 ? 0.0 : sumSquaredError / total;

        public double ExpectedCalibrationError
        {
            get
            {
                if (total == 0) return 0.0;
                double ece = 0.0;
                for (int b = 0; b < BinCount; b++)
                {
                    if (counts[b] == 0) continue;
                    double gap = sumPredicted[b] / counts[b] - sumRealised[b] / counts[b];
                    ece += (double)counts[b] / total * Math.Abs(gap);
                }
                return ece;
            }
        }

        public IReadOnlyList<Bin> Bins()
        {
            var bins = new Bin[BinCount];
            for (int b = 0; b < BinCount; b++)
            {
                bins[b] = new Bin
                {
                    Lower = (double)b / BinCount,
                    Upper = (double)(b + 1) / BinCount,
                    Count = counts[b],
                    MeanPredicted = counts[b] == 0 ? 0.0 : sumPredicted[b] / counts[b],
                    MeanRealised = counts[b] == 0 ? 0.0 : sumRealised[b] / counts[b],
                };
            }
            return bins;
        }
    }
}
