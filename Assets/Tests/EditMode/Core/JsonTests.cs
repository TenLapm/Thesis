using NUnit.Framework;
using Thesis.Core;

namespace Thesis.Tests.Core
{
    public class JsonTests
    {
        public sealed class Sample
        {
            public int Wave;
            public float Score;
            public double Ratio;
            public string Veto;
            public Pcg32State Rng;
        }

        [Test]
        public void NegativeInfinityRoundTrips()
        {
            // Vetoed strategies are logged with score -Infinity (ARCHITECTURE.md §5.8).
            var s = new Sample { Wave = 7, Score = float.NegativeInfinity, Veto = "Uncounterable" };
            string json = Json.Serialize(s);

            Assert.That(json, Does.Contain("\"-Infinity\""));
            Assert.AreEqual(float.NegativeInfinity, Json.Deserialize<Sample>(json).Score);
        }

        [Test]
        public void FloatsRoundTripBitExactly()
        {
            var s = new Sample { Score = 0.1f + 0.2f, Ratio = 1.0 / 3.0 };
            Sample back = Json.Deserialize<Sample>(Json.Serialize(s));

            Assert.AreEqual(s.Score, back.Score);
            Assert.AreEqual(s.Ratio, back.Ratio);
        }

        [Test]
        public void RngStateRoundTripsSoReplaysCanResume()
        {
            var rng = new Pcg32(5UL, RngStreams.Bag);
            for (int i = 0; i < 10; i++) rng.NextUInt();

            var s = new Sample { Rng = rng.Save() };
            Sample back = Json.Deserialize<Sample>(Json.Serialize(s));

            var resumed = Pcg32.FromState(back.Rng);
            Assert.AreEqual(rng.NextUInt(), resumed.NextUInt());
        }

        [Test]
        public void NullsAreWrittenNotDropped()
        {
            // Escalation-condition rows carry null director fields so every
            // condition parses with one schema.
            Assert.That(Json.Serialize(new Sample()), Does.Contain("\"Veto\":null"));
        }
    }
}
