using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    public class Fnv1a64Tests
    {
        [Test]
        public void EmptyHashIsTheOffsetBasis()
        {
            var h = new Fnv1a64();
            Assert.AreEqual(0xcbf29ce484222325UL, h.Value);
        }

        [Test]
        public void SingleByteMatchesPublishedVector()
        {
            // FNV-1a 64 of "a" from the reference test vectors.
            var h = new Fnv1a64();
            h.AddByte((byte)'a');
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, h.Value);
        }

        [Test]
        public void MultiByteValuesAreLittleEndian()
        {
            var viaInt = new Fnv1a64();
            viaInt.Add(0x04030201);

            var viaBytes = new Fnv1a64();
            viaBytes.AddByte(1);
            viaBytes.AddByte(2);
            viaBytes.AddByte(3);
            viaBytes.AddByte(4);

            Assert.AreEqual(viaBytes.Value, viaInt.Value);
        }

        [Test]
        public void FloatsHashByBitPattern()
        {
            var positiveZero = new Fnv1a64();
            positiveZero.Add(0f);
            var negativeZero = new Fnv1a64();
            negativeZero.Add(-0f);

            // A state hash must see every bit, including the sign of zero.
            Assert.AreNotEqual(positiveZero.Value, negativeZero.Value);
        }

        [Test]
        public void StringsAreLengthPrefixed()
        {
            var ab_c = new Fnv1a64();
            ab_c.Add("ab");
            ab_c.Add("c");

            var a_bc = new Fnv1a64();
            a_bc.Add("a");
            a_bc.Add("bc");

            Assert.AreNotEqual(ab_c.Value, a_bc.Value);
        }

        [Test]
        public void OrderMatters()
        {
            var a = new Fnv1a64();
            a.Add(1);
            a.Add(2);

            var b = new Fnv1a64();
            b.Add(2);
            b.Add(1);

            Assert.AreNotEqual(a.Value, b.Value);
        }
    }
}
