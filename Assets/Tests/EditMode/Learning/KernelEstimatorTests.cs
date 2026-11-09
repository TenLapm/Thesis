using NUnit.Framework;
using Thesis.Core;
using Thesis.Learning;

namespace Thesis.Tests.Learning
{
    // WORKPLAN WP7: a query at a stored point with a small σ returns that point's
    // reward, and the 129th entry overwrites the oldest.
    public class KernelEstimatorTests
    {
        [Test]
        public void AQueryAtAStoredPointWithASmallSigmaReturnsThatPointsReward()
        {
            var kernel = new KernelEstimator(128, 0.15);
            kernel.Add(0.2f, 0.3f, 1, 0.9f);
            kernel.Add(0.25f, 0.3f, 1, 0.1f);
            kernel.Add(0.8f, 0.8f, 1, 0.4f);

            KernelEstimate q = kernel.Query(0.2f, 0.3f, 1, 0.001);
            Assert.AreEqual((double)0.9f, q.Mean, 1e-9, "rewards are stored as float");
        }

        [Test]
        public void TheQueryIsAGaussianWeightedAverage()
        {
            var kernel = new KernelEstimator(128, 0.15);
            kernel.Add(0f, 0f, 0, 1f);
            kernel.Add(0.15f, 0f, 0, 0f);

            // At the first point: weights 1 and exp(-0.5), so the mean is 1 / (1 + e^-0.5).
            KernelEstimate q = kernel.Query(0f, 0f, 0);
            double w = DetMath.Exp(-0.5 * (0.15f * (double)0.15f) / (0.15 * 0.15));
            Assert.AreEqual(1.0 / (1.0 + w), q.Mean, 1e-12);
            Assert.AreEqual(1.0 + w, q.WeightSum, 1e-12);
        }

        [Test]
        public void OnlyEntriesOfTheAskedStrategyCount()
        {
            var kernel = new KernelEstimator(128, 0.15);
            kernel.Add(0.5f, 0.5f, 0, 1f);
            kernel.Add(0.5f, 0.5f, 2, 0f);

            Assert.AreEqual(1.0, kernel.Query(0.5f, 0.5f, 0).Mean, 1e-12);
            Assert.AreEqual(0.0, kernel.Query(0.5f, 0.5f, 2).Mean, 1e-12);
        }

        [Test]
        public void AStrategyWithNoEntriesHasNoEstimate()
        {
            var kernel = new KernelEstimator(128, 0.15);
            kernel.Add(0.5f, 0.5f, 0, 1f);

            KernelEstimate q = kernel.Query(0.5f, 0.5f, 3);
            Assert.IsFalse(q.HasData);
            Assert.AreEqual(0.0, q.WeightSum);
        }

        [Test]
        public void The129thEntryOverwritesTheOldest()
        {
            var kernel = new KernelEstimator(128, 0.15);
            kernel.Add(0.1f, 0.1f, 0, 1f);                     // the oldest, alone near (0.1, 0.1)
            for (int i = 1; i < 128; i++) kernel.Add(0.9f, 0.9f, 0, 0f);
            Assert.AreEqual(128, kernel.Count);
            Assert.AreEqual(1.0, kernel.Query(0.1f, 0.1f, 0, 0.001).Mean, 1e-9);

            kernel.Add(0.9f, 0.9f, 0, 0f);                     // the 129th
            Assert.AreEqual(128, kernel.Count);
            Assert.AreEqual(0.0, kernel.Query(0.1f, 0.1f, 0, 0.15).Mean, 1e-12, "the oldest entry is gone");
        }

        [Test]
        public void ExportThenImportGivesTheSameAnswers()
        {
            var original = new KernelEstimator(128, 0.15);
            var rng = new Pcg32(4UL, 4UL);
            for (int i = 0; i < 300; i++)
                original.Add((float)rng.NextDouble(), (float)rng.NextDouble(), rng.NextInt(4), (float)rng.NextDouble());

            var restored = new KernelEstimator(128, 0.15);
            restored.Import(Json.Deserialize<KernelState>(Json.Serialize(original.Export())));

            for (int i = 0; i < 50; i++)
            {
                float x = i / 50f, y = 1f - i / 50f;
                int s = i % 4;
                Assert.AreEqual(original.Query(x, y, s).Mean, restored.Query(x, y, s).Mean, "query " + i);
            }

            // The write position travels too: the next Add overwrites the same entry in both.
            original.Add(0.5f, 0.5f, 1, 1f);
            restored.Add(0.5f, 0.5f, 1, 1f);
            Assert.AreEqual(original.Query(0.5f, 0.5f, 1).Mean, restored.Query(0.5f, 0.5f, 1).Mean);
        }

        [Test]
        public void ARewardOutsideTheUnitIntervalThrows()
        {
            var kernel = new KernelEstimator(128, 0.15);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => kernel.Add(0f, 0f, 0, 1.5f));
        }
    }
}
