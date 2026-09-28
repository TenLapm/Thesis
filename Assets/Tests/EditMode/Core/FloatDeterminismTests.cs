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

            var agent = new AgentState(0, new Vec2f(F("c06bd584"), F("c0a392dd")), 1.25f, 100f, 1f, 0f, 0f);
            float budget = 0f;
            int hp = 10;
            AgentSystem.Step(grid, new[] { agent }, 0.02f, new OccupancyMap(grid.NodeCount), ref budget, ref hp, null);

            Assert.AreEqual(Hex(target.Position.X), Hex(-0.5f), "rig: target centre");
            Assert.AreEqual("c06a4344", Hex(agent.Position.X));
            Assert.AreEqual("c0a36c3c", Hex(agent.Position.Y));
        }

        [Test]
        public void EscalationBaseLifeTimeBits()
        {
            var planner = new EscalationPlanner(new SimConfig(), TestSims.SampleSceneMap());
            Assert.AreEqual("429ad0c2", Hex(planner.BaseLifeTime));
        }
    }
}
