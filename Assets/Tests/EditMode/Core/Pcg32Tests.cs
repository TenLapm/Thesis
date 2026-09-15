using System;
using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    public class Pcg32Tests
    {
        // Known-answer values from pcg-c-basic's pcg32-demo, which seeds with
        // pcg32_srandom_r(&rng, 42u, 54u) and prints these as the first six outputs.
        // If this fails, check the reference source before touching the generator:
        // every recorded replay depends on it.
        [Test]
        public void MatchesPcgCBasicReferenceOutput()
        {
            var rng = new Pcg32(42UL, 54UL);
            uint[] expected = { 0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu };

            for (int i = 0; i < expected.Length; i++)
            {
                uint actual = rng.NextUInt();
                Assert.AreEqual(expected[i], actual, "output #" + i + " was 0x" + actual.ToString("x8"));
            }
        }

        [Test]
        public void SaveThenLoadReproducesTheSameSequence()
        {
            var rng = new Pcg32(123UL, RngStreams.Bag);
            for (int i = 0; i < 37; i++) rng.NextUInt();

            Pcg32State saved = rng.Save();
            var first = new uint[100];
            for (int i = 0; i < first.Length; i++) first[i] = rng.NextUInt();

            rng.Load(saved);
            for (int i = 0; i < first.Length; i++)
                Assert.AreEqual(first[i], rng.NextUInt(), "draw #" + i + " after Load");

            var restored = Pcg32.FromState(saved);
            for (int i = 0; i < first.Length; i++)
                Assert.AreEqual(first[i], restored.NextUInt(), "draw #" + i + " from FromState");
        }

        [Test]
        public void DifferentStreamsOnTheSameSeedDiffer()
        {
            var a = new Pcg32(999UL, RngStreams.Bag);
            var b = new Pcg32(999UL, RngStreams.Thompson);

            int equal = 0;
            for (int i = 0; i < 1000; i++)
                if (a.NextUInt() == b.NextUInt()) equal++;

            Assert.Less(equal, 3, "two streams should be effectively independent");
        }

        [Test]
        public void SameSeedAndStreamIsDeterministic()
        {
            var a = new Pcg32(2026UL, RngStreams.Strategy);
            var b = new Pcg32(2026UL, RngStreams.Strategy);
            for (int i = 0; i < 10000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void FromStateRejectsEvenIncrement()
        {
            Assert.Throws<ArgumentException>(() => Pcg32.FromState(new Pcg32State { State = 1, Inc = 2 }));
        }

        [Test]
        public void NextIntStaysInRangeAndHitsEveryValue()
        {
            var rng = new Pcg32(7UL, RngStreams.Policy);
            var seen = new int[7];
            for (int i = 0; i < 70000; i++)
            {
                int v = rng.NextInt(7);
                Assert.That(v, Is.InRange(0, 6));
                seen[v]++;
            }
            // Expected 10000 each; 5% tolerance is > 10 standard deviations, so this
            // only fails on real bias, never on noise.
            for (int v = 0; v < seen.Length; v++)
                Assert.That(seen[v], Is.InRange(9500, 10500), "value " + v);
        }

        [Test]
        public void NextIntWithMinimumStaysInRange()
        {
            var rng = new Pcg32(8UL, RngStreams.Policy);
            for (int i = 0; i < 10000; i++)
                Assert.That(rng.NextInt(-3, 4), Is.InRange(-3, 3));

            Assert.AreEqual(int.MinValue, new Pcg32(1UL, 1UL).NextInt(int.MinValue, int.MinValue + 1));
        }

        [Test]
        public void NextIntRejectsEmptyRanges()
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        }

        [Test]
        public void UnitIntervalDrawsAreHalfOpenAndCentred()
        {
            var rng = new Pcg32(11UL, RngStreams.Thompson);
            double sumF = 0, sumD = 0;
            const int n = 200000;
            for (int i = 0; i < n; i++)
            {
                float f = rng.NextFloat01();
                double d = rng.NextDouble();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(d, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
                sumF += f;
                sumD += d;
            }
            Assert.AreEqual(0.5, sumF / n, 0.005);
            Assert.AreEqual(0.5, sumD / n, 0.005);
        }
    }
}
