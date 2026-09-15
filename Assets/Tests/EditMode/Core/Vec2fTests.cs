using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    public class Vec2fTests
    {
        [Test]
        public void MoveTowardsReturnsTargetExactlyWhenStepCoversTheDistance()
        {
            var current = new Vec2f(0.1f, 0.3f);
            var target = new Vec2f(2.7f, -1.9f);

            // 1000x the distance: must land exactly on the target, never beyond it.
            Vec2f result = Vec2f.MoveTowards(current, target, Vec2f.Distance(current, target) * 1000f);

            Assert.AreEqual(target.X, result.X);
            Assert.AreEqual(target.Y, result.Y);
        }

        [Test]
        public void MoveTowardsNeverOvershootsAcrossManySteps()
        {
            var target = new Vec2f(10f, 4f);
            var pos = new Vec2f(0f, 0f);
            float previousDistance = Vec2f.Distance(pos, target);

            for (int i = 0; i < 1000 && pos != target; i++)
            {
                pos = Vec2f.MoveTowards(pos, target, 0.37f);
                float d = Vec2f.Distance(pos, target);
                Assert.LessOrEqual(d, previousDistance, "step " + i + " moved away from the target");
                previousDistance = d;
            }

            Assert.AreEqual(target, pos);
        }

        [Test]
        public void MoveTowardsMovesByExactlyTheStepWhenShort()
        {
            var current = new Vec2f(0f, 0f);
            var target = new Vec2f(3f, 4f); // distance 5
            Vec2f result = Vec2f.MoveTowards(current, target, 1f);

            Assert.AreEqual(0.6f, result.X, 1e-6f);
            Assert.AreEqual(0.8f, result.Y, 1e-6f);
        }

        [Test]
        public void MoveTowardsAtTargetStaysPut()
        {
            var p = new Vec2f(1f, 1f);
            Assert.AreEqual(p, Vec2f.MoveTowards(p, p, 5f));
        }

        [Test]
        public void TileCoordEquality()
        {
            Assert.AreEqual(new TileCoord(3, 4), new TileCoord(3, 4));
            Assert.AreNotEqual(new TileCoord(3, 4), new TileCoord(4, 3));
            Assert.IsTrue(new TileCoord(1, 2) == new TileCoord(1, 2));
        }
    }
}
