using System;

namespace Thesis.Core
{
    // Logarithm and exponential that give the SAME BITS on every runtime.
    //
    // Why this exists: + - * / and Math.Sqrt are exactly defined by IEEE 754, so the
    // explicit-cast rule (ARCHITECTURE.md §9 rule 3) is enough to make them agree
    // everywhere. Math.Log, Math.Exp, Math.Pow and the trig functions are not: each
    // runtime takes them from a different maths library, and those are allowed to
    // differ in the last bit. Measured 2026-10-02 over a million inputs: Math.Pow
    // already differs between Unity's Mono and .NET 9 on one machine. Math.Log and
    // Math.Exp happened to agree there, which is luck, not a guarantee.
    //
    // A last-bit difference in a Beta sample or an entropy would almost never change
    // which strategy the director picks - and that is the problem: a divergence that
    // shows up once in thousands of waves cannot be found. So Thesis.* never calls
    // those functions. It calls these, which are built from nothing but + - * / on
    // doubles and bit operations, and therefore cannot differ.
    // ForbiddenApiTests fails the build's tests if anyone calls the Math.* versions.
    //
    // The algorithms are the classic fdlibm ones (Sun Microsystems, 1993; the same
    // routines most C libraries and Go's math package descend from), ported line for
    // line. Error is below 1 ulp. Constants are given as bit patterns so that no
    // compiler's decimal parsing is involved (DetMathTests checks each one against
    // the decimal value printed in the fdlibm source).
    //
    // No Pow: raise to a whole power by multiplying, or use Exp(y * Log(x)).
    // No trig: nothing in the design needs it (the normal sampler uses the polar
    // method, which needs only Log and Sqrt).
    public static class DetMath
    {
        private static double D(long bits) { return BitConverter.Int64BitsToDouble(bits); }

        internal static readonly double Ln2Hi = D(0x3FE62E42FEE00000L);   // 6.93147180369123816490e-01
        internal static readonly double Ln2Lo = D(0x3DEA39EF35793C76L);   // 1.90821492927058770002e-10
        internal static readonly double Two54 = D(0x4350000000000000L);   // 2^54
        internal static readonly double Lg1 = D(0x3FE5555555555593L);     // 6.666666666666735130e-01
        internal static readonly double Lg2 = D(0x3FD999999997FA04L);     // 3.999999999940941908e-01
        internal static readonly double Lg3 = D(0x3FD2492494229359L);     // 2.857142874366239149e-01
        internal static readonly double Lg4 = D(0x3FCC71C51D8E78AFL);     // 2.222219843214978396e-01
        internal static readonly double Lg5 = D(0x3FC7466496CB03DEL);     // 1.818357216161805012e-01
        internal static readonly double Lg6 = D(0x3FC39A09D078C69FL);     // 1.531383769920937332e-01
        internal static readonly double Lg7 = D(0x3FC2F112DF3E5244L);     // 1.479819860511658591e-01
        internal static readonly double Third = D(0x3FD5555555555555L);   // 0.33333333333333333

        internal static readonly double OverflowThreshold = D(0x40862E42FEFA39EFL);                 // 7.09782712893383973096e+02
        internal static readonly double UnderflowThreshold = D(unchecked((long)0xC0874910D52D3051UL)); // -7.45133219101941108420e+02
        internal static readonly double InvLn2 = D(0x3FF71547652B82FEL);  // 1.44269504088896338700e+00
        internal static readonly double TwoM1000 = D(0x0170000000000000L); // 2^-1000
        internal static readonly double P1 = D(0x3FC555555555553EL);      //  1.66666666666666019037e-01
        internal static readonly double P2 = D(unchecked((long)0xBF66C16C16BEBD93UL)); // -2.77777777770155933842e-03
        internal static readonly double P3 = D(0x3F11566AAF25DE2CL);      //  6.61375632143793436117e-05
        internal static readonly double P4 = D(unchecked((long)0xBEBBBD41C5D26BF1UL)); // -1.65339022054652515390e-06
        internal static readonly double P5 = D(0x3E66376972BEA4D0L);      //  4.13813679705723846039e-08

        // Natural logarithm. Log(0) = -infinity, Log(negative) = NaN, as Math.Log.
        public static double Log(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int hx = (int)(bits >> 32);
            uint lx = (uint)bits;

            int k = 0;
            if (hx < 0x00100000)
            {
                // x < 2^-1022: zero, negative, or subnormal.
                if ((hx & 0x7fffffff) == 0 && lx == 0) return double.NegativeInfinity; // log(+-0)
                if (hx < 0) return double.NaN;
                k -= 54;
                x *= Two54; // scale a subnormal up into the normal range
                bits = BitConverter.DoubleToInt64Bits(x);
                hx = (int)(bits >> 32);
            }
            if (hx >= 0x7ff00000) return x + x; // +infinity or NaN

            k += (hx >> 20) - 1023;
            hx &= 0x000fffff;
            int i = (hx + 0x95f64) & 0x100000;
            // Normalise x into [sqrt(2)/2, sqrt(2)) by rewriting its exponent.
            long high = (uint)(hx | (i ^ 0x3ff00000));
            x = BitConverter.Int64BitsToDouble((high << 32) | ((long)BitConverter.DoubleToInt64Bits(x) & 0xFFFFFFFFL));
            k += i >> 20;
            double f = x - 1.0;

            if ((0x000fffff & (2 + hx)) < 3)
            {
                // |f| < 2^-20: a short series is already exact enough.
                if (f == 0.0)
                {
                    if (k == 0) return 0.0;
                    double dk0 = k;
                    return dk0 * Ln2Hi + dk0 * Ln2Lo;
                }
                double r0 = f * f * (0.5 - Third * f);
                if (k == 0) return f - r0;
                double dk1 = k;
                return dk1 * Ln2Hi - ((r0 - dk1 * Ln2Lo) - f);
            }

            double s = f / (2.0 + f);
            double dk = k;
            double z = s * s;
            i = hx - 0x6147a;
            double w = z * z;
            int j = 0x6b851 - hx;
            double t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
            double t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
            i |= j;
            double r = t2 + t1;
            if (i > 0)
            {
                double hfsq = 0.5 * f * f;
                if (k == 0) return f - (hfsq - s * (hfsq + r));
                return dk * Ln2Hi - ((hfsq - (s * (hfsq + r) + dk * Ln2Lo)) - f);
            }
            if (k == 0) return f - s * (f - r);
            return dk * Ln2Hi - ((s * (f - r) - dk * Ln2Lo) - f);
        }

        // e^x. Overflows to +infinity above ~709.78 and underflows to 0 below ~-745.13.
        public static double Exp(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            uint hx = (uint)(bits >> 32);
            int xsb = (int)(hx >> 31) & 1; // sign bit of x
            hx &= 0x7fffffff;              // high word of |x|

            if (hx >= 0x40862E42)
            {
                // |x| >= 709.78...: infinity, NaN, overflow or underflow.
                if (hx >= 0x7ff00000)
                {
                    if (((hx & 0xfffff) | (uint)bits) != 0) return x + x; // NaN
                    return xsb == 0 ? x : 0.0;                            // e^+inf = inf, e^-inf = 0
                }
                if (x > OverflowThreshold) return double.PositiveInfinity;
                if (x < UnderflowThreshold) return 0.0;
            }

            // Argument reduction: x = k*ln2 + r with |r| <= 0.5*ln2, r kept as hi - lo.
            double hi = 0.0, lo = 0.0;
            int k = 0;
            if (hx > 0x3fd62e42)
            {
                // |x| > 0.5 ln2
                if (hx < 0x3FF0A2B2)
                {
                    // and |x| < 1.5 ln2
                    hi = xsb == 0 ? x - Ln2Hi : x + Ln2Hi;
                    lo = xsb == 0 ? Ln2Lo : -Ln2Lo;
                    k = 1 - xsb - xsb;
                }
                else
                {
                    k = (int)(InvLn2 * x + (xsb == 0 ? 0.5 : -0.5));
                    double t0 = k;
                    hi = x - t0 * Ln2Hi; // t0 * Ln2Hi is exact here
                    lo = t0 * Ln2Lo;
                }
                x = hi - lo;
            }
            else if (hx < 0x3e300000)
            {
                // |x| < 2^-28: e^x is 1 + x to the last bit.
                return 1.0 + x;
            }

            // x is now in the primary range.
            double t = x * x;
            double c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            if (k == 0) return 1.0 - ((x * c) / (c - 2.0) - x);

            double y = 1.0 - ((lo - (x * c) / (2.0 - c)) - hi);
            if (k >= -1021)
            {
                return AddToExponent(y, k);
            }
            return AddToExponent(y, k + 1000) * TwoM1000;
        }

        // y * 2^k by adding k to y's exponent field; the caller guarantees it stays normal.
        private static double AddToExponent(double y, int k)
        {
            long bits = BitConverter.DoubleToInt64Bits(y);
            return BitConverter.Int64BitsToDouble(bits + ((long)k << 52));
        }
    }
}
