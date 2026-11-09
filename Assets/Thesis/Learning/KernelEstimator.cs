using System;
using Thesis.Core;

namespace Thesis.Learning
{
    // A Nadaraya–Watson (1964) estimate over a ring buffer of the last 128
    // (point, strategy, reward) entries: the Gaussian-weighted average reward of a
    // strategy near a point in the kernel's 2-D subspace, σ = 0.15 (CLAUDE.md §5).
    //
    //     Q̂(x, s) = Σ w_i r_i / Σ w_i,   w_i = exp(−|x − x_i|² / 2σ²),  over entries with strategy s
    //
    // It is the empirical prior for thin cells of BinnedPosterior
    // (α₀ = 1 + k·Q̂, β₀ = 1 + k·(1 − Q̂)). How the two combine, and k, are spec gap
    // S2 (WP11), so this class only stores and answers; it chooses nothing.
    public sealed class KernelEstimator
    {
        public const int DefaultCapacity = 128;
        public const double DefaultSigma = 0.15;

        private readonly int capacity;
        private readonly double sigma;
        private float[] xs;
        private float[] ys;
        private int[] strategyOf;
        private float[] rewards;
        private int count;
        private int next;

        public KernelEstimator(int capacity = DefaultCapacity, double sigma = DefaultSigma)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "needs room for one entry");
            CheckSigma(sigma);
            this.capacity = capacity;
            this.sigma = sigma;
            xs = new float[capacity];
            ys = new float[capacity];
            strategyOf = new int[capacity];
            rewards = new float[capacity];
        }

        public int Capacity => capacity;
        public double Sigma => sigma;
        public int Count => count;

        // Once full, each new entry overwrites the oldest.
        public void Add(float x, float y, int strategy, float reward)
        {
            if (!(reward >= 0f && reward <= 1f)) throw new ArgumentOutOfRangeException(nameof(reward), reward, "a reward must be in [0, 1]");
            if (strategy < 0) throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "a strategy id is not negative");
            xs[next] = x;
            ys[next] = y;
            strategyOf[next] = strategy;
            rewards[next] = reward;
            next = (next + 1) % capacity;
            if (count < capacity) count++;
        }

        public KernelEstimate Query(float x, float y, int strategy)
        {
            return Query(x, y, strategy, sigma);
        }

        // With its own σ, for the tests and for a σ sweep. Entries are visited in
        // buffer order, so the sum is the same on every runtime.
        public KernelEstimate Query(float x, float y, int strategy, double querySigma)
        {
            CheckSigma(querySigma);
            double twoSigmaSq = 2.0 * querySigma * querySigma;
            double weightSum = 0.0, weighted = 0.0;
            int entries = 0;
            for (int i = 0; i < count; i++)
            {
                if (strategyOf[i] != strategy) continue;
                double dx = (double)x - xs[i];
                double dy = (double)y - ys[i];
                double w = DetMath.Exp(-(dx * dx + dy * dy) / twoSigmaSq);
                weightSum += w;
                weighted += w * rewards[i];
                entries++;
            }

            // Far from every entry all weights underflow to 0: no estimate either.
            bool has = weightSum > 0.0;
            return new KernelEstimate
            {
                HasData = has,
                Mean = has ? weighted / weightSum : 0.0,
                WeightSum = weightSum,
                Entries = entries,
            };
        }

        public KernelState Export()
        {
            return new KernelState
            {
                Capacity = capacity,
                Sigma = sigma,
                Count = count,
                Next = next,
                X = (float[])xs.Clone(),
                Y = (float[])ys.Clone(),
                Strategy = (int[])strategyOf.Clone(),
                Reward = (float[])rewards.Clone(),
            };
        }

        public void Import(KernelState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Schema != KernelState.CurrentSchema) throw new ArgumentException("kernel state schema " + state.Schema + ", expected " + KernelState.CurrentSchema);
            if (state.Capacity != capacity || state.Sigma != sigma)
                throw new ArgumentException("kernel state has capacity " + state.Capacity + " and σ " + state.Sigma + "; this kernel " + capacity + " and " + sigma);
            if (state.Count < 0 || state.Count > capacity || state.Next < 0 || state.Next >= capacity)
                throw new ArgumentException("kernel state has count " + state.Count + " and next " + state.Next + " for capacity " + capacity);
            if (state.X == null || state.Y == null || state.Strategy == null || state.Reward == null
                || state.X.Length != capacity || state.Y.Length != capacity || state.Strategy.Length != capacity || state.Reward.Length != capacity)
                throw new ArgumentException("kernel state must hold " + capacity + " entries in each array");

            xs = (float[])state.X.Clone();
            ys = (float[])state.Y.Clone();
            strategyOf = (int[])state.Strategy.Clone();
            rewards = (float[])state.Reward.Clone();
            count = state.Count;
            next = state.Next;
        }

        private static void CheckSigma(double s)
        {
            if (!(s > 0.0) || double.IsInfinity(s)) throw new ArgumentOutOfRangeException("sigma", s, "σ must be a positive number");
        }
    }

    public struct KernelEstimate
    {
        public bool HasData;
        public double Mean;
        public double WeightSum;    // a smooth "how much evidence is near": the k of S2 may scale with it
        public int Entries;         // entries of this strategy in the buffer, near or not
    }

    public sealed class KernelState
    {
        public const int CurrentSchema = 1;

        public int Schema = CurrentSchema;
        public int Capacity;
        public double Sigma;
        public int Count;
        public int Next;
        public float[] X;
        public float[] Y;
        public int[] Strategy;
        public float[] Reward;
    }
}
