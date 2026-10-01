using System;
using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    public class DetMathTests
    {
        private static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }

        // Units in the last place between two finite doubles of the same sign.
        private static long UlpsApart(double a, double b) { return Math.Abs(Bits(a) - Bits(b)); }

        // Every constant is written in DetMath as a bit pattern. The decimal values
        // here are the ones printed in the fdlibm source (e_log.c, e_exp.c). The two
        // were typed independently, so a slip in either one fails this test.
        [Test]
        public void TheConstantsAreTheFdlibmOnes()
        {
            Assert.AreEqual(Bits(6.93147180369123816490e-01), Bits(DetMath.Ln2Hi), "ln2_hi");
            Assert.AreEqual(Bits(1.90821492927058770002e-10), Bits(DetMath.Ln2Lo), "ln2_lo");
            Assert.AreEqual(Bits(1.80143985094819840000e+16), Bits(DetMath.Two54), "two54");
            Assert.AreEqual(Bits(6.666666666666735130e-01), Bits(DetMath.Lg1), "Lg1");
            Assert.AreEqual(Bits(3.999999999940941908e-01), Bits(DetMath.Lg2), "Lg2");
            Assert.AreEqual(Bits(2.857142874366239149e-01), Bits(DetMath.Lg3), "Lg3");
            Assert.AreEqual(Bits(2.222219843214978396e-01), Bits(DetMath.Lg4), "Lg4");
            Assert.AreEqual(Bits(1.818357216161805012e-01), Bits(DetMath.Lg5), "Lg5");
            Assert.AreEqual(Bits(1.531383769920937332e-01), Bits(DetMath.Lg6), "Lg6");
            Assert.AreEqual(Bits(1.479819860511658591e-01), Bits(DetMath.Lg7), "Lg7");
            Assert.AreEqual(Bits(0.33333333333333333), Bits(DetMath.Third), "one third");

            Assert.AreEqual(Bits(7.09782712893383973096e+02), Bits(DetMath.OverflowThreshold), "o_threshold");
            Assert.AreEqual(Bits(-7.45133219101941108420e+02), Bits(DetMath.UnderflowThreshold), "u_threshold");
            Assert.AreEqual(Bits(1.44269504088896338700e+00), Bits(DetMath.InvLn2), "invln2");
            Assert.AreEqual(Bits(9.33263618503218878990e-302), Bits(DetMath.TwoM1000), "twom1000");
            Assert.AreEqual(Bits(1.66666666666666019037e-01), Bits(DetMath.P1), "P1");
            Assert.AreEqual(Bits(-2.77777777770155933842e-03), Bits(DetMath.P2), "P2");
            Assert.AreEqual(Bits(6.61375632143793436117e-05), Bits(DetMath.P3), "P3");
            Assert.AreEqual(Bits(-1.65339022054652515390e-06), Bits(DetMath.P4), "P4");
            Assert.AreEqual(Bits(4.13813679705723846039e-08), Bits(DetMath.P5), "P5");
        }

        [Test]
        public void LogMatchesTheRuntimeLogToTheLastBitOrTwo()
        {
            var rng = new Pcg32(2026, 1);
            long worst = 0;
            for (int i = 0; i < 200000; i++)
            {
                // Spread over many magnitudes, with a third of the samples hugging 1,
                // where log is smallest and a sloppy implementation loses the most.
                double x;
                int kind = i % 3;
                if (kind == 0) x = rng.NextDouble() * 1000.0 + 1e-300;
                else if (kind == 1) x = 1.0 + (rng.NextDouble() - 0.5) * 1e-3;
                else x = Math.Pow(10.0, rng.NextDouble() * 600.0 - 300.0);

                long apart = UlpsApart(DetMath.Log(x), Math.Log(x));
                if (apart > worst) worst = apart;
                if (apart > 2) Assert.Fail("Log(" + x.ToString("R") + ") is " + apart + " ulps from Math.Log");
            }
            TestContext.WriteLine("worst difference from Math.Log: " + worst + " ulp");
        }

        [Test]
        public void ExpMatchesTheRuntimeExpToTheLastBitOrTwo()
        {
            var rng = new Pcg32(2026, 2);
            long worst = 0;
            for (int i = 0; i < 200000; i++)
            {
                double x;
                int kind = i % 3;
                if (kind == 0) x = rng.NextDouble() * 1400.0 - 700.0;
                else if (kind == 1) x = (rng.NextDouble() - 0.5) * 2.0;
                else x = (rng.NextDouble() - 0.5) * 1e-6;

                long apart = UlpsApart(DetMath.Exp(x), Math.Exp(x));
                if (apart > worst) worst = apart;
                if (apart > 2) Assert.Fail("Exp(" + x.ToString("R") + ") is " + apart + " ulps from Math.Exp");
            }
            TestContext.WriteLine("worst difference from Math.Exp: " + worst + " ulp");
        }

        [Test]
        public void LogHandlesTheEdges()
        {
            Assert.AreEqual(0.0, DetMath.Log(1.0));
            Assert.IsTrue(double.IsNegativeInfinity(DetMath.Log(0.0)));
            Assert.IsTrue(double.IsNegativeInfinity(DetMath.Log(-0.0)));
            Assert.IsTrue(double.IsNaN(DetMath.Log(-1.0)));
            Assert.IsTrue(double.IsNaN(DetMath.Log(double.NaN)));
            Assert.IsTrue(double.IsPositiveInfinity(DetMath.Log(double.PositiveInfinity)));

            // Subnormal input: the smallest positive double is 2^-1074.
            Assert.AreEqual(-1074.0 * Math.Log(2.0), DetMath.Log(double.Epsilon), 1e-10);
            Assert.AreEqual(Math.Log(double.MaxValue), DetMath.Log(double.MaxValue), 1e-10);
            // Exact powers of two: k * ln2, with f == 0.
            Assert.AreEqual(Math.Log(1024.0), DetMath.Log(1024.0), 1e-13);
            Assert.AreEqual(Math.Log(0.125), DetMath.Log(0.125), 1e-13);
        }

        [Test]
        public void ExpHandlesTheEdges()
        {
            Assert.AreEqual(1.0, DetMath.Exp(0.0));
            Assert.AreEqual(1.0, DetMath.Exp(-0.0));
            Assert.IsTrue(double.IsPositiveInfinity(DetMath.Exp(710.0)));
            Assert.AreEqual(0.0, DetMath.Exp(-746.0));
            Assert.IsTrue(double.IsPositiveInfinity(DetMath.Exp(double.PositiveInfinity)));
            Assert.AreEqual(0.0, DetMath.Exp(double.NegativeInfinity));
            Assert.IsTrue(double.IsNaN(DetMath.Exp(double.NaN)));

            Assert.AreEqual(1.0 + 1e-10, DetMath.Exp(1e-10), "tiny arguments are 1 + x");
            Assert.Greater(DetMath.Exp(-745.0), 0.0, "still a (subnormal) positive number just above the underflow threshold");
            Assert.Less(DetMath.Exp(709.0), double.PositiveInfinity);
            Assert.AreEqual(Math.E, DetMath.Exp(1.0), 1e-15);
        }

        [Test]
        public void ExpUndoesLog()
        {
            var rng = new Pcg32(7, 3);
            for (int i = 0; i < 20000; i++)
            {
                double x = rng.NextDouble() * 1e6 + 1e-6;
                double back = DetMath.Exp(DetMath.Log(x));
                Assert.AreEqual(x, back, x * 1e-14, "x = " + x.ToString("R"));
            }
        }

        // THE POINT OF THE CLASS. These two numbers were produced under .NET; the same
        // test runs in Unity's test runner under Mono. If the hashes match there, a
        // million logs and a million exponentials came out bit-identical on both
        // runtimes. If this fails on a new machine, runtime or build target, that
        // platform cannot reproduce the learner's arithmetic: find out why before
        // trusting any replay of a director session from it.
        [Test]
        public void AMillionResultsHaveTheSameBitsOnEveryRuntime()
        {
            var rng = new Pcg32(12345UL, 99UL);
            var log = new Fnv1a64();
            var exp = new Fnv1a64();
            for (int i = 0; i < 1000000; i++)
            {
                double u = rng.NextDouble();
                log.Add(DetMath.Log(u * 20.0 + 1e-9));
                exp.Add(DetMath.Exp(u * 40.0 - 20.0));
            }

            TestContext.WriteLine("log " + log.Value.ToString("x16") + " exp " + exp.Value.ToString("x16"));
            Assert.AreEqual(PinnedLog, log.Value, "DetMath.Log");
            Assert.AreEqual(PinnedExp, exp.Value, "DetMath.Exp");
        }

        private const ulong PinnedLog = 0xecae23c8f3ec7547UL;
        private const ulong PinnedExp = 0x83dd88a47b9a8422UL;
    }
}
