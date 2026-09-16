using System;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class OccupancyMapTests
    {
        [Test]
        public void IncrementCountsPerNode()
        {
            var f = TestMaps.Parse("C . .");
            var occ = new OccupancyMap(f.Grid.NodeCount);

            occ.Increment(f.Grid[1, 0]);
            occ.Increment(f.Grid[1, 0]);
            occ.Increment(f.Grid[2, 0]);

            Assert.AreEqual(0, occ[f.Grid[0, 0]]);
            Assert.AreEqual(2, occ[f.Grid[1, 0]]);
            Assert.AreEqual(1, occ[f.Grid[2, 0]]);
            Assert.AreEqual(3, occ.Total());
        }

        [Test]
        public void ResetClearsEverything()
        {
            var f = TestMaps.Parse("C . .");
            var occ = new OccupancyMap(f.Grid.NodeCount);
            occ.Increment(f.Grid[1, 0]);

            occ.Reset();

            Assert.AreEqual(0, occ.Total());
            Assert.AreEqual(0, occ[f.Grid[1, 0]]);
        }

        [Test]
        public void RejectsNegativeSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OccupancyMap(-1));
        }
    }
}
