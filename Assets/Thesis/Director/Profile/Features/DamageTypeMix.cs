using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // damage_type_mix (ARCHITECTURE.md §5.3): was last wave's damage one type or a mix?
    //
    //   H(damage share per DamageType) ÷ ln(DamageTypes.Count)
    //
    // 0 means all damage of one type, 1 an equal share of every type. Over the three types
    // the game has (physical, fire, frost): the student kept them on 2026-11-10. Frost is a
    // damage type (the shard tower deals it), though no enemy sent today resists it.
    //
    // Damage actually dealt (WaveOutcome.DamageByType: hit points removed, burns included),
    // not what the towers could deal. No wave played, or nothing hit: no data.
    public sealed class DamageTypeMix : IBuildFeature
    {
        public const string FeatureName = "damage_type_mix";

        public string Name => FeatureName;

        public FeatureValue Compute(ProfileInput input)
        {
            float[] dealt = input.LastDamageByType;
            if (dealt == null) return FeatureValue.NoData;

            double total = 0.0;
            for (int k = 0; k < dealt.Length; k++) total += dealt[k];
            if (!(total > 0.0)) return FeatureValue.NoData;

            double h = 0.0;
            for (int k = 0; k < dealt.Length; k++)
            {
                if (!(dealt[k] > 0f)) continue;
                double share = dealt[k] / total;
                h -= share * DetMath.Log(share);
            }
            double v = h / DetMath.Log(DamageTypes.Count);
            return FeatureValue.Of((float)(v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v));
        }
    }
}
