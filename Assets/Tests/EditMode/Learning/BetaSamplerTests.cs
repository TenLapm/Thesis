using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Learning;

namespace Thesis.Tests.Learning
{
    // WORKPLAN WP7: at 200k draws the sample mean and variance are within 1 % of
    // the analytic values, and a shape below 1 throws rather than being clamped.
    public class BetaSamplerTests
    {
        private const int Draws = 200000;

        [TestCase(1.0, 1.0)]
        [TestCase(2.0, 5.0)]
        [TestCase(30.0, 10.0)]
        public void MeanAndVarianceMatchTheAnalyticValues(double a, double b)
        {
            var rng = new Pcg32(20261110UL, 7UL);
            double sum = 0.0, sumSq = 0.0;
            for (int i = 0; i < Draws; i++)
            {
                double x = BetaSampler.Sample(a, b, rng);
                sum += x;
                sumSq += x * x;
            }

            double mean = sum / Draws;
            double variance = sumSq / Draws - mean * mean;
            double expectedMean = a / (a + b);
            double expectedVariance = a * b / ((a + b) * (a + b) * (a + b + 1.0));

            Assert.AreEqual(expectedMean, mean, expectedMean * 0.01, "mean");
            Assert.AreEqual(expectedVariance, variance, expectedVariance * 0.01, "variance");
        }

        [Test]
        public void EveryDrawLiesInTheUnitInterval()
        {
            var rng = new Pcg32(3UL, 1UL);
            for (int i = 0; i < 20000; i++)
            {
                double x = BetaSampler.Sample(1.0, 40.0, rng);
                Assert.That(x >= 0.0 && x <= 1.0, "draw " + i + " was " + x);
            }
        }

        [TestCase(0.99, 2.0)]
        [TestCase(2.0, 0.5)]
        [TestCase(double.NaN, 2.0)]
        public void AShapeBelowOneThrows(double a, double b)
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => BetaSampler.Sample(a, b, rng));
        }

        [Test]
        public void TheSameSeedGivesTheSameDraws()
        {
            var first = new Pcg32(42UL, 5UL);
            var second = new Pcg32(42UL, 5UL);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(BetaSampler.Sample(2.5, 3.5, first), BetaSampler.Sample(2.5, 3.5, second));
        }
    }
}
