using NUnit.Framework;
using Thesis.Core;
using Thesis.Learning;
using Thesis.Learning.Synthetic;

namespace Thesis.Tests.Learning
{
    // WORKPLAN WP7, G1-synthetic: in ThresholdBanditEnv the BinnedPosterior reaches
    // ≥ 85 % best-arm selection within 300 episodes, averaged over 20 seeds, and
    // beats uniform-random and round-robin. Plus the review probe: without the
    // discount (G = 1) the learner adapts measurably worse after the best arm flips.
    public class ThresholdBanditTests
    {
        [Test]
        public void TheBestArmFlipsAtTheThreshold()
        {
            var env = new ThresholdBanditEnv(new ThresholdBanditEnv.Options());
            Assert.AreEqual(0, env.BestArm(0.59, 0));
            Assert.AreEqual(1, env.BestArm(0.60, 0));
            Assert.AreEqual(0.8, env.RewardProbability(0, 0.1, 0));
            Assert.AreEqual(0.4, env.RewardProbability(1, 0.1, 0));
            Assert.AreEqual(0.8, env.RewardProbability(1, 0.9, 0));
            Assert.AreEqual(0.4, env.RewardProbability(3, 0.9, 0));
        }

        [Test]
        public void AfterTheFlipEpisodeTheTwoBestArmsSwap()
        {
            var env = new ThresholdBanditEnv(new ThresholdBanditEnv.Options { FlipAtEpisode = 300 });
            Assert.AreEqual(0, env.BestArm(0.1, 299));
            Assert.AreEqual(1, env.BestArm(0.1, 300));
            Assert.AreEqual(0, env.BestArm(0.9, 300));
        }

        [Test]
        public void TheBinsSplitTheUnitIntervalEvenly()
        {
            var env = new ThresholdBanditEnv(new ThresholdBanditEnv.Options());
            Assert.AreEqual(0, env.BinOf(0.0));
            Assert.AreEqual(2, env.BinOf(0.5));
            Assert.AreEqual(3, env.BinOf(0.6));
            Assert.AreEqual(4, env.BinOf(0.9999));
        }

        [Test]
        public void TheSameSeedGivesTheSameRun()
        {
            var options = new SyntheticRun.Options { Episodes = 300 };
            SyntheticRun.Result a = SyntheticRun.Run(SyntheticPolicy.Thompson, options, 12UL);
            SyntheticRun.Result b = SyntheticRun.Run(SyntheticPolicy.Thompson, options, 12UL);
            CollectionAssert.AreEqual(a.Chosen, b.Chosen);
        }

        [Test]
        public void G1SyntheticTheLearnerPicksTheBestArmAtLeast85PercentWithin300Episodes()
        {
            var options = new SyntheticRun.Options { Episodes = 300 };
            double learner = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, options, 20, 250, 300);
            double random = SyntheticRun.MeanBestShare(SyntheticPolicy.UniformRandom, options, 20, 250, 300);
            double roundRobin = SyntheticRun.MeanBestShare(SyntheticPolicy.RoundRobin, options, 20, 250, 300);
            TestContext.WriteLine("learner " + learner + ", random " + random + ", round-robin " + roundRobin);

            Assert.GreaterOrEqual(learner, 0.85, "G1 pass number");
            Assert.Greater(learner, random, "beats uniform-random");
            Assert.Greater(learner, roundRobin, "beats round-robin");
        }

        // The review probe: G = 1 keeps every old result at full weight, so after
        // the flip the stale evidence for the old best arm outvotes the new.
        [Test]
        public void WithoutTheDiscountTheLearnerAdaptsWorseAfterTheBestArmFlips()
        {
            var discounted = new SyntheticRun.Options { Episodes = 600, FlipAtEpisode = 300, Discount = 0.95 };
            var undiscounted = new SyntheticRun.Options { Episodes = 600, FlipAtEpisode = 300, Discount = 1.0 };
            double with = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, discounted, 20, 300, 400);
            double without = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, undiscounted, 20, 300, 400);
            TestContext.WriteLine("after the flip: G = 0.95 " + with + ", G = 1 " + without);

            Assert.Greater(with, without + 0.05);
        }
    }
}
