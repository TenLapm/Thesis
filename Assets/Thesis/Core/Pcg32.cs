using System;

namespace Thesis.Core
{
    // PCG-XSH-RR 64/32 (O'Neill, pcg-random.org), a line-for-line port of
    // pcg-c-basic's pcg32_random_r / pcg32_srandom_r / pcg32_boundedrand_r.
    //
    // Why PCG and not xorshift: independent *streams* come for free (the increment
    // selects the stream), and the whole state is two ulongs, so it saves into a
    // replay file or a Clone() trivially. One stream per subsystem (RngStreams)
    // means an extra draw in, say, the shape bag can never shift the Thompson
    // sampler's sequence - which is what keeps a determinism divergence local.
    public sealed class Pcg32 : IRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong state;
        private ulong inc;

        public Pcg32(ulong seed, ulong stream)
        {
            // pcg32_srandom_r: the two warm-up steps are part of the reference
            // algorithm; skipping them changes every output.
            state = 0UL;
            inc = (stream << 1) | 1UL;
            NextUInt();
            state = unchecked(state + seed);
            NextUInt();
        }

        private Pcg32(Pcg32State saved)
        {
            state = saved.State;
            inc = saved.Inc;
        }

        public static Pcg32 FromState(Pcg32State saved)
        {
            if ((saved.Inc & 1UL) == 0UL)
                throw new ArgumentException("Pcg32State.Inc must be odd; this state was not produced by Save().", nameof(saved));
            return new Pcg32(saved);
        }

        public Pcg32State Save()
        {
            return new Pcg32State { State = state, Inc = inc };
        }

        public void Load(Pcg32State saved)
        {
            if ((saved.Inc & 1UL) == 0UL)
                throw new ArgumentException("Pcg32State.Inc must be odd; this state was not produced by Save().", nameof(saved));
            state = saved.State;
            inc = saved.Inc;
        }

        public uint NextUInt()
        {
            ulong old = state;
            state = unchecked(old * Multiplier + inc);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            // (-rot) & 31 as in the C reference; C# masks shift counts to 5 bits anyway,
            // but spelling it out keeps the port readable against the original.
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");
            return (int)BoundedUInt((uint)maxExclusive);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be greater than minInclusive (" + minInclusive + ").");
            uint range = (uint)((long)maxExclusive - minInclusive);
            return (int)((long)minInclusive + BoundedUInt(range));
        }

        public float NextFloat01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        public double NextDouble()
        {
            ulong a = NextUInt() >> 5; // 27 bits
            ulong b = NextUInt() >> 6; // 26 bits
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }

        // pcg32_boundedrand_r: reject the low 2^32 mod bound values so every
        // result is equally likely. Plain modulo would bias small results.
        private uint BoundedUInt(uint bound)
        {
            uint threshold = unchecked((uint)-bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }
    }
}
