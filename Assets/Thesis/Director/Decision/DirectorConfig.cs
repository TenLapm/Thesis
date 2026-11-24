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
        // Widened 2026-11-24 (the student, after an 11-wave play-test in which this feature sat in the
        // top bins): its 20/40/60/80 % points over three players weighted alike, `builder`, `reactive`
        // and the student's session (Results/2026-11-24_director-ranges). Was 0.06 / 0.10 / 0.14 / 0.22.
        public float[] BinEdges = { 0.125f, 0.224f, 0.352f, 0.484f };
        // A profile whose bin feature has no data goes in this bin, not where its 0.5 would fall.
        public int NoDataBin = 2;
        // The kernel's two coordinates, each normalised by its range below.
        public string KernelXFeature = TowerConcentrationName;
        public string KernelYFeature = MazeLengthName;

        // ---- the heuristic (layer 2) ----
        // Each strategy's feature (by strategy id), and every feature's 5 % and 95 % points from
        // the measurement: a value is mapped to 0..1 over that range, then times HeuristicScale.
        // The raid (id 4, 2026-11-24) reads collector_exposure, a share already in 0..1, unmeasured.
        public string[] StrategyFeature = { BreachVulnerabilityName, ChokepointRelianceName, TowerConcentrationName, MazeLengthName, CollectorExposureName };
        public string[] RangeFeature = { MazeLengthName, BreachVulnerabilityName, ChokepointRelianceName, TowerConcentrationName, CollectorExposureName };
        // Widened 2026-11-24 (the student: "do as you suggest"): the lowest 5 % point and the highest
        // 95 % point over `builder`, `reactive` (on the ladder, against all four rungs) and the student's
        // play-test, plus a tenth of the span at the top so that no board sits fixed at the maximum
        // (tower_concentration capped at 1, its largest possible value). Was 1.056-1.309, 0-0.067,
        // 0.017-0.339, 0.763-0.933: the builder's boards against the static waves only, which the
        // student's own board overran on three of the four features (Results/2026-11-24_director-ranges).
        public float[] RangeLow = { 1.048f, 0.000f, 0.033f, 0.740f, 0f };
        public float[] RangeHigh = { 1.385f, 0.052f, 0.888f, 1.000f, 1f };
        // 0 to 4, so the learner's ±1 can only flip strategies within 2 points of each other.
        public float HeuristicScale = 4f;

        // ---- the constraints (layer 1) ----
        // A plan this close (L2, share per enemy type plus entries / 3) to the last wave's is vetoed.
        // 0 = off: the student, 2026-11-24 (rule B4), "it's ok if the enemy type is similar". Was 0.1,
        // which made the director alternate two strategies; MaxStreak below still limits repeats.
        public float SimilarityThreshold = 0f;

        // Flyers are banned while the player has no powered anti-air tower, up to this wave; from the
        // next one every strategy may come (the student, 2026-11-24, rule B3). Before, always banned
        // (CLAUDE.md §2's rule, which the student relaxed).
        public int FlyersNeedAntiAirUntilWave = 5;

        // A wave is the chosen strategy leading with this share of the budget, and the others that
        // scored above 0 sharing the rest by score, each with at least MinSupportShare (the student,
        // 2026-11-24). LeadShare 1 is the old one-strategy wave.
        public float LeadShare = 0.5f;
        public float MinSupportShare = 0.1f;

        // The raid comes from this wave on, and only while a collector stands (NoRaidTarget).
        public int RaidsFromWave = 4;
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
        public const string CollectorExposureName = "collector_exposure";

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
