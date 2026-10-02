using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Core
{
    // Cross-runtime float determinism (found in WP4, see DEVLOG): C# lets the runtime
    // evaluate float expressions at higher precision. CoreCLR rounds every operation
    // to float; Unity's Mono evaluated `a + b / c * d` in double and rounded once, so
    // the two differed by one bit on rare steps. Thesis.* therefore casts every
    // intermediate float result explicitly (ARCHITECTURE.md §9 rule 3).
    //
    // These pin known answers for the exact cases that diverged. They run in BOTH
    // test runners (Unity/Mono and dotnet/CoreCLR), so a missing cast fails the
    // Unity run even though dotnet passes.
    public class FloatDeterminismTests
    {
        private static float F(string hex) { return BitConverter.Int32BitsToSingle(Convert.ToInt32(hex, 16)); }

        private static string Hex(float f) { return BitConverter.SingleToInt32Bits(f).ToString("x8"); }

        [Test]
        public void TheStepThatDivergedAtTick2916GivesTheSameBitsEverywhere()
        {
            // Agent 0 of the WP4 smoke session at tick 2915, stepping toward tile
            // centre (-0.5, -4.5) at speed 1.25. 1.25 * 0.02f is an exact rounding tie.
            var from = new Vec2f(F("c06bd584"), F("c0a392dd"));
            float speed = 1.25f, dt = 0.02f; // variables, not constants: no compile-time folding
            Vec2f to = Vec2f.MoveTowards(from, new Vec2f(-0.5f, -4.5f), (float)(speed * dt));

            Assert.AreEqual("c06a4344", Hex(to.X));
            Assert.AreEqual("c0a36c3c", Hex(to.Y), "Mono without the casts gave c0a36c3d");
        }

        [Test]
        public void TheSameStepThroughAgentSystemGivesTheSameBits()
        {
            // The same step, but with speed * dt formed inside AgentSystem.Step, which
            // is where the game actually computes it.
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));
            // Make (-0.5, -4.5) the next tile from the agent's tile by pointing the
            // agent's tile at it directly.
            SimNode here = grid.NodeFromPosition(new Vec2f(F("c06bd584"), F("c0a392dd")));
            SimNode target = grid.NodeFromPosition(new Vec2f(-0.5f, -4.5f));
            here.BestCost = 100;
            here.NextIndex = target.Index;

            var agent = new AgentState(0, new Vec2f(F("c06bd584"), F("c0a392dd")), 1.25f, 100f, DamageTypes.AllOnes(), 1f, 0f, 0f);
            float budget = 0f;
            int hp = 10;
            AgentSystem.Step(grid, new[] { agent }, new TowerState[0], 0.02f, new OccupancyMap(grid.NodeCount), 0f, ref budget, ref hp, null);

            Assert.AreEqual(Hex(target.Position.X), Hex(-0.5f), "rig: target centre");
            Assert.AreEqual("c06a4344", Hex(agent.Position.X));
            Assert.AreEqual("c0a36c3c", Hex(agent.Position.Y));
        }

        // The same step for a SLOWED agent: the slow multiplies the speed before the
        // speed is multiplied by dt, one more link in the chain that must be rounded
        // step by step (WP-C1).
        [Test]
        public void ASlowedStepGivesTheSameBitsEverywhere()
        {
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));
            SimNode here = grid.NodeFromPosition(new Vec2f(F("c06bd584"), F("c0a392dd")));
            SimNode target = grid.NodeFromPosition(new Vec2f(-0.5f, -4.5f));
            here.BestCost = 100;
            here.NextIndex = target.Index;

            var agent = new AgentState(0, new Vec2f(F("c06bd584"), F("c0a392dd")), 1.3625f, 100f, DamageTypes.AllOnes(), 1f, 0f, 0f);
            agent.SlowTicks = 10;
            agent.SlowFactor = 0.7f;
            float budget = 0f;
            int hp = 10;
            AgentSystem.Step(grid, new[] { agent }, new TowerState[0], 0.02f, new OccupancyMap(grid.NodeCount), 0f, ref budget, ref hp, null);

            Assert.AreEqual(PinnedSlowedX, Hex(agent.Position.X));
            Assert.AreEqual(PinnedSlowedY, Hex(agent.Position.Y));
        }

        // A tower's range check on the very edge: squared distance against squared
        // reach, each product and the sum rounded on its own (WP-C1).
        [Test]
        public void TheRangeCheckGivesTheSameAnswerEverywhere()
        {
            float tx = 1.3f, ty = -2.7f;
            float ax = 7.1f, ay = 1.9f;
            float dx = (float)(ax - tx), dy = (float)(ay - ty);
            float d2 = (float)((float)(dx * dx) + (float)(dy * dy));
            Assert.AreEqual(PinnedDistanceSquared, Hex(d2));

            Assert.IsTrue(Targeting.InRange(new Vec2f(ax, ay), new Vec2f(tx, ty), d2), "exactly on the edge is in range");
            float justUnder = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(d2) - 1);
            Assert.IsFalse(Targeting.InRange(new Vec2f(ax, ay), new Vec2f(tx, ty), justUnder), "one bit less reach and it is out");
        }

        private const string PinnedSlowedX = "c06aa29a";
        private const string PinnedSlowedY = "c0a37564";
        private const string PinnedDistanceSquared = "425b3334";
    }
}
