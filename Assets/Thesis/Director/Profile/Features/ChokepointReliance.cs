using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // chokepoint_reliance (ARCHITECTURE.md §5.3): did last wave's damage come from a few towers
    // or from all of them?
    //
    //   1 − H(each tower's share of last wave's damage) ÷ ln(number of towers)
    //
    // 1 means one tower did all the damage; 0 means every tower did an equal share. The
    // student decided on 2026-11-10 that a chokepoint is about the towers, not the route
    // tiles: the first definition (entropy of DamageMap over route tiles) was replaced.
    // It differs from tower_concentration: close towers can share the work, spread ones can
    // leave it to one.
    //
    // Which towers: ProfileInput.CountsAsTower, read as the build phase begins, so a tower
    // that dealt nothing still counts (it is part of the spread). Their TowerState.WaveDamage
    // is last wave's. A tower destroyed during that wave is gone from the set, and so is its
    // damage: the profile describes the base that is left.
    //
    // No wave played, no towers, or nothing hit: no data. One tower that hit: 1.
    public sealed class ChokepointReliance : IBuildFeature
    {
        public const string FeatureName = "chokepoint_reliance";

        public string Name => FeatureName;

        public FeatureValue Compute(ProfileInput input)
        {
            if (input.LastDamageByType == null) return FeatureValue.NoData;

            int n = 0;
            double total = 0.0;
            for (int t = 0; t < input.Towers.Count; t++)
            {
                TowerState tower = input.Towers[t];
                if (!ProfileInput.CountsAsTower(tower)) continue;
                n++;
                total += tower.WaveDamage;
            }
            if (n == 0 || !(total > 0.0)) return FeatureValue.NoData;
            if (n == 1) return FeatureValue.Of(1f);

            double h = 0.0;
            for (int t = 0; t < input.Towers.Count; t++)
            {
                TowerState tower = input.Towers[t];
                if (!ProfileInput.CountsAsTower(tower) || !(tower.WaveDamage > 0f)) continue;
                double share = tower.WaveDamage / total;
                h -= share * DetMath.Log(share);
            }
            double v = 1.0 - h / DetMath.Log(n);
            return FeatureValue.Of((float)(v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v));
        }
    }
}
