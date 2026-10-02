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
            AgentSystem.Step(grid, grid[0, 0], new[] { agent }, new TowerState[0], 0.02f, new OccupancyMap(grid.NodeCount), 0f, ref budget, ref hp, null);

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
            AgentSystem.Step(grid, grid[0, 0], new[] { agent }, new TowerState[0], 0.02f, new OccupancyMap(grid.NodeCount), 0f, ref budget, ref hp, null);

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

        // A flyer's step (WP-C2): straight at the core, with the slow in the chain, and
        // the straight-line distance it reports. The direction is a diagonal with no
        // round numbers in it, so both divisions inside MoveTowards have to round.
        [Test]
        public void AFlyersStepGivesTheSameBitsEverywhere()
        {
            var grid = new SimGrid(TestMaps.Open(38, 38, 75f, 75f, 1f));
            SimNode core = grid[34, 31];
            var flyer = new AgentState(0, new Vec2f(F("c06bd584"), F("c0a392dd")), 1.59f, 100f, DamageTypes.AllOnes(), 0f, 0f, 0f, MovementClass.Flying);
            flyer.SlowTicks = 10;
            flyer.SlowFactor = 0.7f;
            float budget = 0f;
            int hp = 10;
            AgentSystem.Step(grid, core, new[] { flyer }, new TowerState[0], 0.02f, new OccupancyMap(grid.NodeCount), 0f, ref budget, ref hp, null);

            Assert.AreEqual(PinnedFlyerX, Hex(flyer.Position.X));
            Assert.AreEqual(PinnedFlyerY, Hex(flyer.Position.Y));
            Assert.AreEqual(PinnedFlyerCost, flyer.MinCostSeen, "the distance before the step, in tenths of a tile");
        }

        // FlyingMovement.CostToCore: a square root, a division and a rounding to a
        // whole number. The first three are worked by hand; the rest pin awkward inputs.
        [Test]
        public void AFlyersDistanceToTheCoreIsTheSameWholeNumberEverywhere()
        {
            Assert.AreEqual(0, FlyingMovement.CostToCore(new Vec2f(3f, 4f), new Vec2f(3f, 4f), 2f));
            Assert.AreEqual(25, FlyingMovement.CostToCore(new Vec2f(0f, 0f), new Vec2f(3f, 4f), 2f), "5 world units = 2.5 tiles");
            Assert.AreEqual(5, FlyingMovement.CostToCore(new Vec2f(0f, 0f), new Vec2f(1f, 0f), 2f), "half a tile");

            float ax = 1.3f, ay = -2.7f, bx = 34.1f, by = 33.9f, tile = 2f; // variables, not constants: no compile-time folding
            Assert.AreEqual(PinnedCostFar, FlyingMovement.CostToCore(new Vec2f(ax, ay), new Vec2f(bx, by), tile));
            Assert.AreEqual(PinnedCostNear, FlyingMovement.CostToCore(new Vec2f(F("c06bd584"), F("c0a392dd")), new Vec2f(-0.5f, -4.5f), tile));
            float odd = 1.9736842f; // SampleScene's 75 / 38 would be this tile size
            Assert.AreEqual(PinnedCostOddTile, FlyingMovement.CostToCore(new Vec2f(ax, ay), new Vec2f(bx, by), odd));
        }

        // The sapper's price for a built tile: one float product, rounded to a whole
        // number (half goes to the even neighbour on every runtime).
        [Test]
        public void TheSapperDiscountRoundsTheSameWayEverywhere()
        {
            var f = TestMaps.Parse("C . . . . . . S");
            int[] terrain = { 15, 20, 25, 35, 200, 7 };
            float[] factor = { 0.2f, 0.2f, 0.1f, 0.1f, 0.2f, 0.5f };
            int[] expected = { 3, 4, 2, 4, 40, 4 };   // 2.5 -> 2 and 3.5 -> 4: to the even neighbour
            for (int i = 0; i < terrain.Length; i++)
            {
                SimNode node = f.Grid[1 + i, 0];
                f.Grid.SetWall(node, terrain[i], 6f);
                Assert.AreEqual(expected[i], FlowField.SapperTerrainCost(node, factor[i]), terrain[i] + " x " + factor[i]);
            }
        }

        private const string PinnedSlowedX = "c06aa29a";
        private const string PinnedSlowedY = "c0a37564";
        private const string PinnedDistanceSquared = "425b3334";
        private const string PinnedFlyerX = "c06ac25e";
        private const string PinnedFlyerY = "c0a31b2c";
        private const int PinnedFlyerCost = 233;
        private const int PinnedCostFar = 246;
        private const int PinnedCostNear = 16;
        private const int PinnedCostOddTile = 249;
    }
}
