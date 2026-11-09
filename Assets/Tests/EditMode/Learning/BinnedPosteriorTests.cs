using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Learning;

namespace Thesis.Tests.Learning
{
    // WORKPLAN WP7: α, β ≥ 1 after 10⁵ discounted updates; a cell with no updates is
    // Cold with a correction of exactly 0; Value stays in [−1, +1]; Export → Import →
    // same seed gives the same corrections; a pinned fingerprint on both runtimes.
    public class BinnedPosteriorTests
    {
        private static DecisionContext Bin(int bin) { return new DecisionContext(bin, 0f, 0f); }

        [Test]
        public void AlphaAndBetaNeverDropBelowOneAfterManyDiscountedUpdates()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(11UL, 2UL);
            for (int i = 0; i < 100000; i++)
                posterior.Update(Bin(rng.NextInt(5)), rng.NextInt(4), (float)rng.NextDouble(), 1f, rng);

            for (int bin = 0; bin < 5; bin++)
                for (int s = 0; s < 4; s++)
                {
                    Assert.GreaterOrEqual(posterior.Alpha(bin, s), 1.0, "alpha " + bin + "," + s);
                    Assert.GreaterOrEqual(posterior.Beta(bin, s), 1.0, "beta " + bin + "," + s);
                }
        }

        [Test]
        public void ACellWithNoUpdatesIsColdAndCorrectsByExactlyZero()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(1UL, 1UL);
            posterior.Update(Bin(0), 0, 1f, 1f, rng);

            Correction warm = posterior.SampleCorrection(Bin(0), 0, rng);
            Correction otherStrategy = posterior.SampleCorrection(Bin(0), 1, rng);
            Correction otherBin = posterior.SampleCorrection(Bin(1), 0, rng);

            Assert.IsFalse(warm.Cold);
            Assert.IsTrue(otherStrategy.Cold);
            Assert.IsTrue(otherBin.Cold);
            Assert.AreEqual(0f, otherStrategy.Value);
            Assert.AreEqual(0f, otherBin.Value);
        }

        [TestCase(-3.0)]
        [TestCase(0.0)]
        [TestCase(0.3)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        [TestCase(4.0)]
        public void TheCorrectionStaysWithinPlusOrMinusOneForAnyTheta(double theta)
        {
            float value = BinnedPosterior.CorrectionFromTheta(theta);
            Assert.That(value >= -1f && value <= 1f, "theta " + theta + " gave " + value);
        }

        [Test]
        public void SampledCorrectionsStayWithinPlusOrMinusOne()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(9UL, 3UL);
            for (int i = 0; i < 20000; i++)
            {
                int bin = rng.NextInt(5), s = rng.NextInt(4);
                posterior.Update(Bin(bin), s, rng.NextDouble() < 0.9 ? 1f : 0f, 1f, rng);
                Correction c = posterior.SampleCorrection(Bin(bin), s, rng);
                Assert.That(c.Value >= -1f && c.Value <= 1f, "value " + c.Value);
            }
        }

        [Test]
        public void TheUpdateDecaysTowardBetaOneOneThenAddsTheDraw()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(1UL, 1UL);

            // A reward of exactly 1 or 0 makes the Bernoulli draw certain.
            posterior.Update(Bin(2), 3, 1f, 1f, rng);
            Assert.AreEqual(2.0, posterior.Alpha(2, 3));
            Assert.AreEqual(1.0, posterior.Beta(2, 3));

            posterior.Update(Bin(2), 3, 0f, 1f, rng);
            Assert.AreEqual(1.0 + 0.95 * 1.0, posterior.Alpha(2, 3), 1e-12);
            Assert.AreEqual(2.0, posterior.Beta(2, 3), 1e-12);
        }

        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        public void ARewardOutsideTheUnitIntervalThrows(float reward)
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            Assert.Throws<ArgumentOutOfRangeException>(() => posterior.Update(Bin(0), 0, reward, 1f, new Pcg32(1UL, 1UL)));
        }

        // Spec gap S3 (how the 0.5^lag credit weight combines with the Bernoulli
        // update) is WP11's to close. Until then only full credit is accepted, so no
        // caller can pick an answer by accident.
        [Test]
        public void PartialCreditIsRefusedUntilSpecGapS3IsClosed()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            Assert.Throws<NotSupportedException>(() => posterior.Update(Bin(0), 0, 1f, 0.5f, new Pcg32(1UL, 1UL)));
        }

        [Test]
        public void ABinOrStrategyOutOfRangeThrows()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => posterior.SampleCorrection(Bin(5), 0, rng));
            Assert.Throws<ArgumentOutOfRangeException>(() => posterior.SampleCorrection(Bin(0), 4, rng));
            Assert.Throws<ArgumentOutOfRangeException>(() => posterior.Update(Bin(-1), 0, 1f, 1f, rng));
        }

        [Test]
        public void ExportThenImportGivesTheSameCorrectionsForTheSameSeed()
        {
            var original = new BinnedPosterior(5, 4, 0.95);
            var train = new Pcg32(5UL, 5UL);
            for (int i = 0; i < 500; i++)
                original.Update(Bin(train.NextInt(5)), train.NextInt(4), (float)train.NextDouble(), 1f, train);

            string json = Json.Serialize(original.Export());
            var restored = new BinnedPosterior(5, 4, 0.95);
            restored.Import(Json.Deserialize<EstimatorState>(json));

            var a = new Pcg32(77UL, 1UL);
            var b = new Pcg32(77UL, 1UL);
            for (int i = 0; i < 2000; i++)
            {
                int bin = i % 5, s = (i / 5) % 4;
                Correction x = original.SampleCorrection(Bin(bin), s, a);
                Correction y = restored.SampleCorrection(Bin(bin), s, b);
                Assert.AreEqual(x.Theta, y.Theta, "theta at " + i);
                Assert.AreEqual(x.Value, y.Value, "value at " + i);
                Assert.AreEqual(x.Cold, y.Cold, "cold at " + i);
            }
        }

        [Test]
        public void ImportRefusesAStateWithAShapeBelowOne()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            EstimatorState state = posterior.Export();
            state.Alpha[3] = 0.5;
            Assert.Throws<ArgumentException>(() => posterior.Import(state));
        }

        [Test]
        public void ImportRefusesAStateOfAnotherShape()
        {
            EstimatorState state = new BinnedPosterior(3, 4, 0.95).Export();
            Assert.Throws<ArgumentException>(() => new BinnedPosterior(5, 4, 0.95).Import(state));
        }

        // The same idea as DetMathTests.AMillionResultsHaveTheSameBitsOnEveryRuntime:
        // Unity (Mono) and dotnet must produce the same bits, or a session cannot be
        // replayed headless. If this fails on one runtime only, find the operation
        // that is not reproducible; do not re-pin.
        [Test]
        public void AFixedSequenceOfCallsHasTheSameBitsOnEveryRuntime()
        {
            var posterior = new BinnedPosterior(5, 4, 0.95);
            var rng = new Pcg32(20261110UL, 17UL);
            var hash = new Fnv1a64();
            for (int i = 0; i < 100000; i++)
            {
                var ctx = Bin(rng.NextInt(5));
                int s = rng.NextInt(4);
                Correction c = posterior.SampleCorrection(ctx, s, rng);
                hash.Add(c.Theta);
                hash.Add(c.Value);
                hash.Add(c.Cold);
                posterior.Update(ctx, s, (float)rng.NextDouble(), 1f, rng);
                hash.Add(posterior.Alpha(ctx.Bin, s));
                hash.Add(posterior.Beta(ctx.Bin, s));
            }

            TestContext.WriteLine("fingerprint " + hash.Value.ToString("x16"));
            Assert.AreEqual(PinnedFingerprint, hash.Value);
        }

        // Pinned under .NET 9 on 2026-11-10; Unity's Mono must give the same.
        private const ulong PinnedFingerprint = 0x64a19c09794b6873UL;
    }
}
