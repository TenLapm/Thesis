using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Learning;

namespace Thesis.Tests.Learning
{
    // WORKPLAN WP7: calibrated pairs give ECE ≤ 0.02 over 10⁵ pairs; a prediction of
    // 0.9 against a true rate of 0.5 gives ECE ≈ 0.4.
    public class CalibrationReportTests
    {
        [Test]
        public void PerfectlyCalibratedPairsHaveAnEceNearZero()
        {
            var rng = new Pcg32(8UL, 8UL);
            var report = new CalibrationReport();
            for (int i = 0; i < 100000; i++)
            {
                double p = rng.NextDouble();
                report.Add(p, rng.NextDouble() < p ? 1.0 : 0.0);
            }

            Assert.LessOrEqual(report.ExpectedCalibrationError, 0.02);
        }

        [Test]
        public void AlwaysPredictingNinetyAgainstAFiftyPercentRateHasAnEceNearPointFour()
        {
            var rng = new Pcg32(8UL, 9UL);
            var report = new CalibrationReport();
            for (int i = 0; i < 100000; i++) report.Add(0.9, rng.NextDouble() < 0.5 ? 1.0 : 0.0);

            Assert.AreEqual(0.4, report.ExpectedCalibrationError, 0.01);
        }

        [Test]
        public void TheBrierScoreIsTheMeanSquaredError()
        {
            var report = new CalibrationReport();
            report.Add(0.9, 1.0);   // 0.01
            report.Add(0.2, 1.0);   // 0.64
            report.Add(0.5, 0.0);   // 0.25
            Assert.AreEqual((0.01 + 0.64 + 0.25) / 3.0, report.BrierScore, 1e-12);
        }

        [Test]
        public void APredictionOfExactlyOneFallsInTheLastBin()
        {
            var report = new CalibrationReport();
            report.Add(1.0, 1.0);
            report.Add(0.95, 1.0);
            IReadOnlyList<CalibrationReport.Bin> bins = report.Bins();
            Assert.AreEqual(10, bins.Count);
            Assert.AreEqual(2, bins[9].Count);
        }

        [Test]
        public void AnEmptyReportHasZeroPairs()
        {
            var report = new CalibrationReport();
            Assert.AreEqual(0, report.Count);
            Assert.AreEqual(0.0, report.ExpectedCalibrationError);
            Assert.AreEqual(0.0, report.BrierScore);
        }

        [Test]
        public void MergingTwoReportsEqualsOneReportOfAllThePairs()
        {
            var rng = new Pcg32(2UL, 2UL);
            var all = new CalibrationReport();
            var first = new CalibrationReport();
            var second = new CalibrationReport();
            for (int i = 0; i < 1000; i++)
            {
                double p = rng.NextDouble(), r = rng.NextDouble() < 0.3 ? 1.0 : 0.0;
                all.Add(p, r);
                (i % 3 == 0 ? first : second).Add(p, r);
            }

            first.Merge(second);
            Assert.AreEqual(all.Count, first.Count);
            Assert.AreEqual(all.BrierScore, first.BrierScore, 1e-12);
            Assert.AreEqual(all.ExpectedCalibrationError, first.ExpectedCalibrationError, 1e-12);
        }

        [Test]
        public void APredictionOutsideTheUnitIntervalThrows()
        {
            var report = new CalibrationReport();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => report.Add(1.2, 1.0));
        }
    }
}
