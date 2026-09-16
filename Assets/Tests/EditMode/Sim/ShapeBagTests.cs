using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class ShapeBagTests
    {
        private static ShapeDef Straight(string name, params (int x, int y)[] tiles)
        {
            var t = new TileCoord[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) t[i] = new TileCoord(tiles[i].x, tiles[i].y);
            return new ShapeDef { Name = name, BuildCost = 4, DigCost = 15, WallHealth = 6f, LocalTiles = t };
        }

        // The seven real shapes SampleScene wires up (BlockManager.shapeLibrary):
        // I, J, L, O, S, T, Z. "7-bag" is a property of that configuration, not a
        // hardcoded assumption inside ShapeBag itself.
        private static ShapeDef[] SevenShapeLibrary()
        {
            return new[]
            {
                Straight("I", (0,0),(1,0),(2,0),(3,0)),
                Straight("J", (0,0),(0,1),(0,2),(1,2)),
                Straight("L", (0,0),(0,1),(0,2),(-1,2)),
                Straight("O", (0,0),(1,0),(0,1),(1,1)),
                Straight("S", (0,0),(1,0),(1,1),(2,1)),
                Straight("T", (0,0),(1,0),(2,0),(1,1)),
                Straight("Z", (0,0),(1,0),(-1,1),(0,1)),
            };
        }

        [Test]
        public void RotateMatchesTheOriginalFormula()
        {
            // Port of BlockShape.Rotate: (x, y) -> (y, -x).
            ShapeDef shape = Straight("I", (0, 0), (1, 0), (2, 0), (3, 0));

            shape.Rotate();
            CollectionAssert.AreEqual(new[] { new TileCoord(0, 0), new TileCoord(0, -1), new TileCoord(0, -2), new TileCoord(0, -3) }, shape.LocalTiles);

            shape.Rotate();
            CollectionAssert.AreEqual(new[] { new TileCoord(0, 0), new TileCoord(-1, 0), new TileCoord(-2, 0), new TileCoord(-3, 0) }, shape.LocalTiles);

            shape.Rotate();
            shape.Rotate();
            CollectionAssert.AreEqual(new[] { new TileCoord(0, 0), new TileCoord(1, 0), new TileCoord(2, 0), new TileCoord(3, 0) }, shape.LocalTiles, "four quarter turns must return to the original");
        }

        [Test]
        public void CloneIsIndependentOfItsMaster()
        {
            ShapeDef master = Straight("I", (0, 0), (1, 0));
            ShapeDef clone = master.Clone();

            clone.Rotate();

            CollectionAssert.AreEqual(new[] { new TileCoord(0, 0), new TileCoord(1, 0) }, master.LocalTiles, "rotating a clone must never mutate the library master");
            Assert.AreNotSame(master.LocalTiles, clone.LocalTiles);
        }

        [Test]
        public void EveryBagOfSevenContainsEachShapeExactlyOnce()
        {
            ShapeDef[] library = SevenShapeLibrary();
            var rng = new Pcg32(2026UL, RngStreams.Bag);
            var bag = new ShapeBag(library, rng);

            // The constructor's own PullNextShape() call is draw #0; each further
            // explicit PullNextShape() is exactly one more raw bag draw (the FIFO
            // preview queue only delays when a draw is OBSERVED, never reorders it).
            var draws = new List<string> { bag.CurrentShape.Name };
            for (int i = 0; i < library.Length * 4 - 1; i++)
            {
                bag.PullNextShape();
                draws.Add(bag.CurrentShape.Name);
            }

            Assert.AreEqual(library.Length * 4, draws.Count);
            for (int bagIndex = 0; bagIndex < 4; bagIndex++)
            {
                var slice = draws.Skip(bagIndex * library.Length).Take(library.Length).OrderBy(n => n).ToArray();
                var expected = library.Select(s => s.Name).OrderBy(n => n).ToArray();
                CollectionAssert.AreEqual(expected, slice, "bag #" + bagIndex + " was not a permutation of the library");
            }
        }

        [Test]
        public void RotatingADrawnShapeNeverAffectsLaterDrawsOfTheSameLibraryEntry()
        {
            // Regression guard for the bug class the original avoided by only ever
            // handing out Instantiate() copies: a clone the player rotated must not
            // leak that rotation into the NEXT time the same library shape is drawn.
            ShapeDef[] library = SevenShapeLibrary();
            var rng = new Pcg32(1UL, RngStreams.Bag);
            var bag = new ShapeBag(library, rng);

            ShapeDef first = bag.CurrentShape;
            var originalTilesOfFirst = (TileCoord[])first.LocalTiles.Clone();
            first.Rotate(); // mutate only this drawn instance

            ShapeDef sameShapeAgain = null;
            for (int i = 0; i < library.Length * 2 && sameShapeAgain == null; i++)
            {
                bag.PullNextShape();
                if (bag.CurrentShape.Name == first.Name && !ReferenceEquals(bag.CurrentShape, first))
                    sameShapeAgain = bag.CurrentShape;
            }

            Assert.IsNotNull(sameShapeAgain, "expected '" + first.Name + "' to be drawn again within two bags");
            CollectionAssert.AreEqual(originalTilesOfFirst, sameShapeAgain.LocalTiles, "a later draw of the same shape must start unrotated");
        }

        [Test]
        public void HoldWorksOncePerPieceAndPreservesRotation()
        {
            var rng = new Pcg32(9UL, RngStreams.Bag);
            var bag = new ShapeBag(SevenShapeLibrary(), rng);

            ShapeDef original = bag.CurrentShape;
            bag.RotateCurrent();
            bag.RotateCurrent();
            Assert.AreEqual(2, bag.CurrentRotationTurns);

            bag.SwapHold(); // empty hold: original (at 2 turns) goes to hold, a fresh piece becomes current
            Assert.AreSame(original, bag.HoldShape);
            Assert.AreEqual(2, bag.HoldRotationTurns);
            Assert.AreEqual(0, bag.CurrentRotationTurns, "a freshly pulled piece always starts unrotated");
            ShapeDef freshCurrent = bag.CurrentShape;

            bag.SwapHold(); // already held this turn - must be a no-op
            Assert.AreSame(freshCurrent, bag.CurrentShape);
            Assert.AreSame(original, bag.HoldShape);

            bag.PullNextShape(); // simulates placing the current piece
            bag.SwapHold(); // hold occupied: swap current <-> hold
            Assert.AreSame(original, bag.CurrentShape);
            Assert.AreEqual(2, bag.CurrentRotationTurns, "the piece must come back out of hold with the rotation it went in with");
        }

        [Test]
        public void RejectsAnEmptyLibrary()
        {
            Assert.Throws<System.ArgumentException>(() => new ShapeBag(new ShapeDef[0], new Pcg32(1UL, RngStreams.Bag)));
        }
    }
}
