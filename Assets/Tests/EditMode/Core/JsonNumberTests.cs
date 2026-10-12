using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    // The number text of telemetry lines (WP-C3). A telemetry file is compared byte
    // for byte with the one made again from its replay, on the other runtime too,
    // so the text of a number may not depend on who wrote it. Every expected string
    // here is checked by both test runners, which is the cross-runtime check.
    public class JsonNumberTests
    {
        [TestCase(57.8f, "57.8")]
        [TestCase(0.27f, "0.27")]
        [TestCase(812f, "812")]
        [TestCase(-1.125f, "-1.125")]
        [TestCase(0.5f, "0.5")]
        [TestCase(1f, "1")]
        [TestCase(100f, "100")]
        [TestCase(0.2f, "0.2")]
        [TestCase(1.0000001f, "1")]
        [TestCase(1234.5678f, "1234.568")]
        [TestCase(0.0001234567f, "0.0001235")]
        public void AFloatIsWrittenAsAShortPlainDecimal(float value, string expected)
        {
            Assert.AreEqual(expected, Json.FormatFloat(value));
        }

        [Test]
        public void TheFloatThatTheTwoRuntimesPrintDifferentlyHasOneText()
        {
            // Found in WP-C3: the library prints this float as "0.20000458" under .NET
            // and as "0.200004578" under Unity's Mono.
            Assert.AreEqual("0.2000046", Json.FormatFloat(0.20000458f));
        }

        [Test]
        public void ArithmeticResultsHaveOneTextToo()
        {
            float third = (float)(1.0 / 3.0);
            float sum = 0.1f + 0.2f;
            float refund = (float)((float)((float)(15f * 0.5f) * 3f) / 4f);   // a wall sale: three of four tiles standing
            float kills = 0f;
            for (int i = 0; i < 37; i++) kills = (float)(kills + 0.2f);       // 37 kill rewards added one by one

            Assert.AreEqual("0.3333333", Json.FormatFloat(third));
            Assert.AreEqual("0.3", Json.FormatFloat(sum));
            Assert.AreEqual("5.625", Json.FormatFloat(refund));
            Assert.AreEqual("7.399997", Json.FormatFloat(kills), "the float sum, with its error, not a tidy 7.4");
        }

        [Test]
        public void ADoubleIsWrittenToTwelveDigits()
        {
            Assert.AreEqual("0.333333333333", Json.FormatDouble(1.0 / 3.0));
            Assert.AreEqual("0.666666666667", Json.FormatDouble(2.0 / 3.0));
            Assert.AreEqual("1234.56789012", Json.FormatDouble(1234.567890123456));
            Assert.AreEqual("0.1", Json.FormatDouble(0.1));
            Assert.AreEqual("0.3", Json.FormatDouble(0.1 + 0.2));
        }

        [Test]
        public void ZeroHasOneSpelling()
        {
            Assert.AreEqual("0", Json.FormatFloat(0f));
            Assert.AreEqual("0", Json.FormatFloat(-0f));
            Assert.AreEqual("0", Json.FormatFloat(1e-9f), "too small to show");
            Assert.AreEqual("0", Json.FormatFloat(-1e-9f), "and never '-0'");
        }

        [Test]
        public void AHalfRoundsAwayFromZero()
        {
            Assert.AreEqual("3", Json.FormatNumber(2.5, 1));
            Assert.AreEqual("-3", Json.FormatNumber(-2.5, 1));
            Assert.AreEqual("0.13", Json.FormatNumber(0.125, 2));
            Assert.AreEqual("-0.13", Json.FormatNumber(-0.125, 2));
        }

        [Test]
        public void RoundingCanCarryIntoTheWholePart()
        {
            Assert.AreEqual("10", Json.FormatNumber(9.99999996, 7));
            Assert.AreEqual("1", Json.FormatNumber(0.99999999, 7));
            Assert.AreEqual("100", Json.FormatNumber(99.9999999, 7));
        }

        [Test]
        public void TheWholePartIsNeverCutShort()
        {
            Assert.AreEqual("12345678", Json.FormatFloat(12345678f));
            Assert.AreEqual("16777216", Json.FormatFloat(16777216f));
            Assert.AreEqual("1234568", Json.FormatNumber(1234567.9, 7));
        }

        [Test]
        public void ThereIsNeverAnExponent()
        {
            foreach (float f in new[] { 1e-7f, 1e-5f, 123456.7f, 1e7f, 1e10f, 3.4e15f })
            {
                string text = Json.FormatFloat(f);
                Assert.That(text, Does.Not.Contain("E").And.Not.Contain("e"), text);
            }
        }

        [Test]
        public void WhatHasNoJsonNumberIsNamed()
        {
            Assert.AreEqual("NaN", Json.FormatFloat(float.NaN));
            Assert.AreEqual("Infinity", Json.FormatFloat(float.PositiveInfinity));
            Assert.AreEqual("-Infinity", Json.FormatFloat(float.NegativeInfinity));
            Assert.IsFalse(Json.IsFinite(double.NaN));
            Assert.IsFalse(Json.IsFinite(double.NegativeInfinity));
            Assert.IsTrue(Json.IsFinite(0.0));
        }

        [Test]
        public void WhatCannotBeWrittenIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.FormatDouble(1e19));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.FormatDouble(-1e19));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.FormatNumber(1.0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.FormatNumber(1.0, 13));
        }

        [Test]
        public void ReadingTheTextBackGivesTheNumberToWithinItsLastDigit()
        {
            // Telemetry text is for reading, not the bit-exact record (the replay is).
            var rng = new Pcg32(11UL, RngStreams.Policy);
            for (int i = 0; i < 2000; i++)
            {
                float f = (float)(rng.NextDouble() * 2000.0 - 1000.0);
                double back = double.Parse(Json.FormatFloat(f), System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual(f, back, 1e-3 * 0.5, "float " + f.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                Assert.AreEqual(f, back, Math.Abs(f) * 1e-6 + 1e-7);
            }
        }

        // ---- a whole line ----

        public sealed class Sample
        {
            public int Wave;
            public float Score;
            public double Ratio;
            public float? Theta;
            public ulong Seed;
            public string Veto;
        }

        [Test]
        public void ALineIsWrittenWithThoseNumbers()
        {
            var s = new Sample { Wave = 3, Score = 0.20000458f, Ratio = 1.0 / 3.0, Theta = 57.8f, Seed = 18446744073709551615UL };
            Assert.AreEqual("{\"Wave\":3,\"Score\":0.2000046,\"Ratio\":0.333333333333,\"Theta\":57.8,\"Seed\":18446744073709551615,\"Veto\":null}", Json.SerializeLine(s));
        }

        [Test]
        public void ALineKeepsWholeNumbersAndNullsAsTheyAre()
        {
            string line = Json.SerializeLine(new Sample { Score = 812f });
            Assert.That(line, Does.Contain("\"Score\":812,"), "no '.0'");
            Assert.That(line, Does.Contain("\"Theta\":null"));
        }

        [Test]
        public void AVetoedScoreIsWrittenAsAStringAndReadBack()
        {
            string line = Json.SerializeLine(new Sample { Score = float.NegativeInfinity, Theta = float.NaN });
            Assert.That(line, Does.Contain("\"Score\":\"-Infinity\""));
            Assert.That(line, Does.Contain("\"Theta\":\"NaN\""));

            Sample back = Json.DeserializeLine<Sample>(line);
            Assert.AreEqual(float.NegativeInfinity, back.Score);
            Assert.IsTrue(float.IsNaN(back.Theta.Value));
        }

        [Test]
        public void ALineReadsNumbersInAnyNotation()
        {
            Sample back = Json.DeserializeLine<Sample>("{\"Wave\":2,\"Score\":1e-3,\"Ratio\":5,\"Theta\":null,\"Veto\":\"x\"}");
            Assert.AreEqual(2, back.Wave);
            Assert.AreEqual(0.001f, back.Score);
            Assert.AreEqual(5.0, back.Ratio);
            Assert.IsNull(back.Theta);
            Assert.AreEqual("x", back.Veto);
        }

        [Test]
        public void ALineRefusesWhatIsNotANumber()
        {
            Assert.Throws<JsonSerializationException>(() => Json.DeserializeLine<Sample>("{\"Score\":\"lots\"}"));
            Assert.Throws<JsonSerializationException>(() => Json.DeserializeLine<Sample>("{\"Score\":null}"), "a plain float cannot be null");
            Assert.Throws<JsonSerializationException>(() => Json.DeserializeLine<Sample>("{\"Score\":[1]}"));
        }

        [Test]
        public void TheOrdinarySettingsStillKeepEveryBit()
        {
            // Replays and snapshots use Json.Serialize and need the exact float back.
            var s = new Sample { Score = 0.20000458f, Ratio = 1.0 / 3.0 };
            Sample back = Json.Deserialize<Sample>(Json.Serialize(s));
            Assert.AreEqual(s.Score, back.Score);
            Assert.AreEqual(s.Ratio, back.Ratio);
        }

        // ---- NamedValues ----

        public sealed class Holder
        {
            public NamedValues Damage;
            public NamedValues Nothing;
        }

        [Test]
        public void NamedValuesAreWrittenAsAnObjectInTheirOwnOrder()
        {
            var h = new Holder { Damage = new NamedValues().Add("physical", 812).Add("fire", 240.5f).Add("frost", 0.20000458f).Add("arcane", 0) };
            const string expected = "{\"Damage\":{\"physical\":812,\"fire\":240.5,\"frost\":0.2000046,\"arcane\":0},\"Nothing\":null}";

            Assert.AreEqual(expected, Json.SerializeLine(h));
            Assert.AreEqual(expected, Json.Serialize(h), "the same text under the ordinary settings");
        }

        [Test]
        public void NamedValuesReadBackInOrder()
        {
            Holder back = Json.DeserializeLine<Holder>("{\"Damage\":{\"zeta\":1,\"alpha\":2.5,\"mid\":\"-Infinity\"},\"Nothing\":null}");

            Assert.AreEqual(3, back.Damage.Count);
            Assert.AreEqual("zeta", back.Damage.NameAt(0));
            Assert.AreEqual("alpha", back.Damage.NameAt(1));
            Assert.AreEqual(2.5, back.Damage.ValueAt(1));
            Assert.AreEqual(double.NegativeInfinity, back.Damage["mid"]);
            Assert.IsNull(back.Nothing);
        }

        [Test]
        public void NamedValuesAnswerByName()
        {
            NamedValues v = new NamedValues().Add("ground", 30).Add("sapper", 12).Add("flying", 0);

            Assert.AreEqual(12.0, v["sapper"]);
            Assert.AreEqual(42.0, v.Sum());
            Assert.IsTrue(v.Has("flying"));
            Assert.IsFalse(v.Has("swimming"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => { double unused = v["swimming"]; });
        }

        [Test]
        public void ANameCanBeUsedOnce()
        {
            NamedValues v = new NamedValues().Add("ground", 1);
            Assert.Throws<ArgumentException>(() => v.Add("ground", 2));
            Assert.Throws<ArgumentNullException>(() => v.Add(null, 2));
            Assert.Throws<JsonSerializationException>(() => Json.DeserializeLine<Holder>("{\"Damage\":{\"a\":\"many\"}}"));
            Assert.Throws<JsonSerializationException>(() => Json.DeserializeLine<Holder>("{\"Damage\":[1,2]}"));
        }
    }
}
