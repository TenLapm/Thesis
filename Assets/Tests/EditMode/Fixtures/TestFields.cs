using Thesis.Sim;

namespace Thesis.Tests
{
    // Flow fields for tests that need both of them (ground and sapper), built the
    // way the simulation builds them: through FlowFieldSet.
    public static class TestFields
    {
        // The game's own share: what a sapper pays of a built tile's price.
        public static float DefaultFactor => new SimConfig().SapperDigCostFactor;

        public static FlowFieldSet Set() { return new FlowFieldSet(DefaultFactor); }

        public static AsciiFixture Built(string text) { return Built(text, DefaultFactor); }

        public static AsciiFixture Built(string text, float sapperDigCostFactor)
        {
            AsciiFixture f = TestMaps.Parse(text);
            new FlowFieldSet(sapperDigCostFactor).Generate(f.Grid, f.Map.Core);
            return f;
        }
    }
}
