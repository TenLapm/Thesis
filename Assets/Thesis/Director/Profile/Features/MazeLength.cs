using Thesis.Sim;

namespace Thesis.Director
{
    // maze_length (ARCHITECTURE.md §5.3): how much longer the walkers' way to the core is
    // than on the empty board. 1 means the player's building has not lengthened it.
    //
    // The mean, over EVERY candidate entry of the map, of (ground cost now ÷ ground cost on
    // the empty board). Not only this wave's entries: RaidPlanner picks one to three a wave,
    // and the number would move while the player built nothing (the student, 2026-11-10).
    //
    // It is a cost, not a walked distance: a wall the enemies dig through counts as the
    // detour it is worth to them (the field's cost model, CLAUDE.md §1). Ground field only:
    // flyers ignore the route, and sappers and raiders come only with some strategies.
    public sealed class MazeLength : IBuildFeature
    {
        public const string FeatureName = "maze_length";

        public string Name => FeatureName;

        public FeatureValue Compute(ProfileInput input)
        {
            float sum = 0f;
            int counted = 0;
            for (int i = 0; i < input.Spawns.Length; i++)
            {
                int now = input.Grid.Get(input.Spawns[i]).BestCost;
                int empty = input.Empty.Cost(i);
                // An entry the core cannot be reached from, or one on the core, says nothing.
                if (now == SimNode.Infinity || empty == SimNode.Infinity || empty <= 0) continue;
                sum = (float)(sum + (float)(ProfileInput.Tiles(now) / ProfileInput.Tiles(empty)));
                counted++;
            }
            return counted == 0 ? FeatureValue.NoData : FeatureValue.Of((float)(sum / counted));
        }
    }
}
