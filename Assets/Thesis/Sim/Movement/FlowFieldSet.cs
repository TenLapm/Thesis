using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Every flow field the game uses, rebuilt together: the ground field and the
    // sapper field (ARCHITECTURE.md §4.6). The simulation rebuilds only through this
    // class, so the two fields can never be out of step with the board or with each
    // other. Flying enemies follow no field and need nothing here.
    //
    // That is two full floods where there used to be one. Each costs well under a
    // millisecond on this grid, so there is still no caching, no "skip the sapper
    // field while no sapper is alive" and no incremental update: each of those
    // would be state that can go stale, bought with time nobody is short of
    // (CLAUDE.md §8).
    public sealed class FlowFieldSet
    {
        private readonly FlowField field = new FlowField();
        private readonly float sapperDigCostFactor;

        // sapperDigCostFactor is SimConfig.SapperDigCostFactor: the share of a built
        // tile's price that a sapper pays. 1 makes the sapper field equal to the
        // ground field; 0 makes sappers treat walls as open ground when routing.
        public FlowFieldSet(float sapperDigCostFactor)
        {
            if (!(sapperDigCostFactor >= 0f) || sapperDigCostFactor > 1f)
                throw new ArgumentOutOfRangeException(nameof(sapperDigCostFactor), sapperDigCostFactor, "Must be in [0, 1].");
            this.sapperDigCostFactor = sapperDigCostFactor;
        }

        public float SapperDigCostFactor => sapperDigCostFactor;

        // Ground first: it is the one that counts the rebuild (SimGrid.FieldVersion).
        public void Generate(SimGrid grid, TileCoord goal)
        {
            field.Generate(grid, goal);
            field.GenerateSapper(grid, goal, sapperDigCostFactor);
        }
    }
}
