using System;
using Thesis.Core;

namespace Thesis.Learning
{
    // Beta(a, b) as X / (X + Y) with X ~ Gamma(a), Y ~ Gamma(b), each Gamma drawn by
    // Marsaglia & Tsang (2000), "A simple method for generating gamma variables".
    //
    // Why no branch for shapes below 1: the update in BinnedPosterior decays toward
    // Beta(1, 1), so a and b can never drop below 1. A shape below 1 here therefore
    // means a bug in the update, and it throws instead of being clamped, so the bug
    // shows up where it happened (ARCHITECTURE.md §5.6).
    //
    // Maths (§9 rule 3): only DetMath.Log and Math.Sqrt, both bit-identical on every
    // runtime. The normal draws use the polar method for the same reason: Box-Muller
    // would need sin and cos. Everything is double; a caller that wants a float
    // rounds once, at the end.
    public static class BetaSampler
    {
        public static double Sample(double a, double b, IRandom rng)
        {
            CheckShape(a, nameof(a));
            CheckShape(b, nameof(b));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            double x = Gamma(a, rng);
            double y = Gamma(b, rng);
            return x / (x + y);
        }

        // Gamma(shape, 1) for shape >= 1.
        internal static double Gamma(double shape, IRandom rng)
        {
            double d = shape - 1.0 / 3.0;
            double c = 1.0 / Math.Sqrt(9.0 * d);
            while (true)
            {
                double z = StandardNormal(rng);
                double t = 1.0 + c * z;
                if (t <= 0.0) continue;
                double v = t * t * t;

                // 1 - NextDouble() is in (0, 1], so its log is finite.
                double u = 1.0 - rng.NextDouble();
                double z2 = z * z;

                // The cheap squeeze accepts about 98 % of draws without a log.
                if (u < 1.0 - 0.0331 * z2 * z2) return d * v;
                if (DetMath.Log(u) < 0.5 * z2 + d * (1.0 - v + DetMath.Log(v))) return d * v;
            }
        }

        // The polar (Marsaglia) method. It makes two normals per accepted pair; the
        // second is thrown away on purpose, because keeping it would be hidden state
        // that Export/Import would also have to carry.
        internal static double StandardNormal(IRandom rng)
        {
            while (true)
            {
                double u = 2.0 * rng.NextDouble() - 1.0;
                double v = 2.0 * rng.NextDouble() - 1.0;
                double s = u * u + v * v;
                if (s >= 1.0 || s == 0.0) continue;
                return u * Math.Sqrt(-2.0 * DetMath.Log(s) / s);
            }
        }

        private static void CheckShape(double shape, string name)
        {
            // !(shape >= 1) also catches NaN.
            if (!(shape >= 1.0) || double.IsInfinity(shape))
                throw new ArgumentOutOfRangeException(name, shape, "A Beta shape below 1 can only come from a bug in the posterior update.");
        }
    }
}
