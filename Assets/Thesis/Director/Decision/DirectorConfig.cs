using System;

namespace Thesis.Director
{
    // Every number the director's layers use, in one place (CLAUDE.md §7: chosen by config, not
    // by #if). Provisional() holds the student's choices of 2026-11-16, "for now": they rest on
    // the builder player's boards (`thesis profile`, 20 game maps, 227 readings), not on human
    // data. The bin edges and the normalisation ranges are what the threshold freeze (I6) fixes;
    // after it they are immutable.
    [Serializable]
    public sealed class DirectorConfig
    {
        public const int Schema = 1;

        // ---- the context (S1) ----
        // The feature binned into five, and the four edges between the bins.
        public string BinFeature = ChokepointRelianceName;
        public float[] BinEdges = { 0.06f, 0.10f, 0.14f, 0.22f };
        // A profile whose bin feature has no data goes in this bin, not where its 0.5 would fall.
        public int NoDataBin = 2;
        // The kernel's two coordinates, each normalised by its range below.
        public string KernelXFeature = TowerConcentrationName;
        public string KernelYFeature = MazeLengthName;

        // ---- the heuristic (layer 2) ----
        // Each strategy's feature (by strategy id), and every feature's 5 % and 95 % points from
        // the measurement: a value is mapped to 0..1 over that range, then times HeuristicScale.
        public string[] StrategyFeature = { BreachVulnerabilityName, ChokepointRelianceName, TowerConcentrationName, MazeLengthName };
        public string[] RangeFeature = { MazeLengthName, BreachVulnerabilityName, ChokepointRelianceName, TowerConcentrationName };
        public float[] RangeLow = { 1.056f, 0.000f, 0.017f, 0.763f };
        public float[] RangeHigh = { 1.309f, 0.067f, 0.339f, 0.933f };
        // 0 to 4, so the learner's ±1 can only flip strategies within 2 points of each other.
        public float HeuristicScale = 4f;

        // ---- the constraints (layer 1) ----
        // A plan this close (L2, share per enemy type plus entries / 3) to the last wave's is vetoed.
        public float SimilarityThreshold = 0.1f;
        // A strategy may run this many waves in a row; a backstop to the similarity rule.
        public int MaxStreak = 2;

        // ---- the reward (WP11; S4, S5: the student's provisional answers of 2026-11-18) ----
        public RewardMode Reward = RewardMode.Graduated;
        public float RestructureWeight = 0.45f, SurvivedWeight = 0.25f, PressureWeight = 0.20f, NoveltyWeight = 0.10f;
        // Full credit for restructure at this mean change of the built features (normalised); more
        // counts the same. Also the binary control's threshold.
        public float Tau = 0.2f;
        // What "rebuilding" is measured on: the features that describe what is BUILT. Not
        // chokepoint_reliance or damage_type_mix, which describe how last wave's damage fell and
        // change with the director's own waves even when nothing was rebuilt.
        public string[] RestructureFeatures = { MazeLengthName, BreachVulnerabilityName, TowerConcentrationName };
        // Credit for an observed restructure, spread back over the decisions before it: the wave
        // itself, then the two before, as a fractional update (S3).
        public float[] CreditWeights = { 1f, 0.5f, 0.25f };

        // ---- the learner (S2) ----
        public string EstimatorKind = Thesis.Learning.KernelPriorEstimator.KindName;
        public int ThinBelow = Thesis.Learning.KernelPriorEstimator.DefaultThinBelow;
        public double PriorWeight = Thesis.Learning.KernelPriorEstimator.DefaultPriorWeight;
        public double NearWeight = Thesis.Learning.KernelPriorEstimator.DefaultNearWeight;
        public double Discount = Thesis.Learning.BinnedPosterior.DefaultDiscount;

        // A fresh estimator of the configured kind (CLAUDE.md §7: chosen by config).
        public Thesis.Learning.IStrategyEstimator NewEstimator()
        {
            int bins = BinEdges.Length + 1;
            switch (EstimatorKind)
            {
                case Thesis.Learning.KernelPriorEstimator.KindName:
                    return new Thesis.Learning.KernelPriorEstimator(bins, StrategyFeature.Length, Discount, ThinBelow, PriorWeight, NearWeight);
                case Thesis.Learning.BinnedPosterior.KindName:
                    return new Thesis.Learning.BinnedPosterior(bins, StrategyFeature.Length, Discount);
                default:
                    throw new ArgumentException("unknown estimator kind '" + EstimatorKind + "'");
            }
        }

        public const string MazeLengthName = "maze_length";
        public const string BreachVulnerabilityName = "breach_vulnerability";
        public const string ChokepointRelianceName = "chokepoint_reliance";
        public const string TowerConcentrationName = "tower_concentration";

        public static DirectorConfig Provisional() { return new DirectorConfig(); }

        public int RangeIndex(string feature)
        {
            for (int i = 0; i < RangeFeature.Length; i++) if (RangeFeature[i] == feature) return i;
            throw new ArgumentException("no normalisation range for feature '" + feature + "'", nameof(feature));
        }

        // value → 0..1 over the feature's measured range, clamped.
        public float Normalise(string feature, float value)
        {
            int i = RangeIndex(feature);
            float span = (float)(RangeHigh[i] - RangeLow[i]);
            if (!(span > 0f)) return 0f;
            float t = (float)((float)(value - RangeLow[i]) / span);
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }
    }
}
