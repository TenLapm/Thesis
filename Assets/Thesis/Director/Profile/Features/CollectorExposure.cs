using Thesis.Sim;

namespace Thesis.Director
{
    // collector_exposure (the student, 2026-11-24, with the raid strategy): how much of the
    // player's income stands unguarded.
    //
    //   the share of standing collectors that no defending tower reaches
    //
    // "Reaches": the collector's tile lies within a tower's range (StrategyKit.Reaches), the same
    // test the strategies use. Which towers: ProfileInput.CountsAsTower. No collector standing: no
    // data (there is nothing to raid, and the raid strategy is vetoed anyway).
    //
    // It is the sixth feature, not one of the proposal's five, and it is measured on nothing yet:
    // its range for the heuristic is the whole of 0 to 1 until the threshold freeze.
    public sealed class CollectorExposure : IBuildFeature
    {
        public const string FeatureName = "collector_exposure";

        public string Name => FeatureName;

        public FeatureValue Compute(ProfileInput input)
        {
            SimGrid grid = input.Grid;
            int collectors = 0, open = 0;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode node = grid.ByIndex(i);
                if (node.Occupant != Occupant.Collector) continue;
                collectors++;
                bool covered = false;
                for (int t = 0; t < input.Towers.Count && !covered; t++)
                {
                    TowerState tower = input.Towers[t];
                    covered = ProfileInput.CountsAsTower(tower) && StrategyKit.Reaches(tower, node.X, node.Y);
                }
                if (!covered) open++;
            }
            if (collectors == 0) return FeatureValue.NoData;
            return FeatureValue.Of((float)((float)open / collectors));
        }
    }
}
