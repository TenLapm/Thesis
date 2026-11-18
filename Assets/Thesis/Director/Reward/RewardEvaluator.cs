using System;
using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Director
{
    public enum RewardMode
    {
        Graduated = 0, // the four parts, weighted (CLAUDE.md §5)
        Binary = 1,    // (restructure >= tau) && survived: the control condition of gate G3
    }

    // The reward for one wave, part by part (all in [0, 1]).
    public sealed class RewardBreakdown
    {
        public float RestructureRaw; // mean change of the built features, normalised, uncapped
        public float Restructure;    // RestructureRaw capped at tau, over tau
        public float Survived;       // 1 if the core still stood after the wave
        public float Pressure;       // share of the wave that got within PressureRadiusTiles (S5: 5 tiles)
        public float Novelty;        // distance from the wave before's composition, at most 1
        public int Binary;
        public float Total;          // what the learner is given: the weighted sum, or Binary in binary mode
    }

    // One wave's reward and the decisions it was credited to.
    public sealed class RewardRecord
    {
        public int Wave;
        public RewardBreakdown Reward;
        public readonly List<CreditEntry> Credited = new List<CreditEntry>();
    }

    public struct CreditEntry
    {
        public int Wave;
        public string Strategy;
        public float Weight;
    }

    // Behaviour change, not victory (CLAUDE.md §5):
    //   r = 0.45 restructure + 0.25 survived + 0.20 pressure + 0.10 novelty
    // restructure: how much the player rebuilt between the decision for the wave and the next
    // one, as the mean absolute change of the built features over their measured ranges, with
    // full credit at tau (0.2, S4). survived keeps a strategy from profiting by ending the run.
    public static class RewardEvaluator
    {
        public static float Restructure(BuildProfile before, BuildProfile after, DirectorConfig cfg)
        {
            double sum = 0.0;
            int n = 0;
            foreach (string f in cfg.RestructureFeatures)
            {
                if (!before.Has(f) || !after.Has(f)) continue;
                sum += Math.Abs((double)cfg.Normalise(f, after[f]) - cfg.Normalise(f, before[f]));
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        public static RewardBreakdown Evaluate(BuildProfile before, BuildProfile after, WaveOutcome outcome, WavePlan plan, WavePlan previousPlan, DirectorConfig cfg)
        {
            var r = new RewardBreakdown();
            r.RestructureRaw = Restructure(before, after, cfg);
            r.Restructure = cfg.Tau > 0f ? Math.Min(1f, (float)(r.RestructureRaw / cfg.Tau)) : 0f;
            r.Survived = outcome.CoreHpAfter > 0 ? 1f : 0f;
            r.Pressure = Clamp01(outcome.PressureShare);
            r.Novelty = plan != null && previousPlan != null ? Math.Min(1f, TooSimilarToPrevious.Distance(plan, previousPlan)) : 0f;
            r.Binary = r.RestructureRaw >= cfg.Tau && r.Survived > 0f ? 1 : 0;
            float graduated = (float)((float)((float)((float)(cfg.RestructureWeight * r.Restructure) + (float)(cfg.SurvivedWeight * r.Survived))
                                              + (float)(cfg.PressureWeight * r.Pressure)) + (float)(cfg.NoveltyWeight * r.Novelty));
            r.Total = cfg.Reward == RewardMode.Binary ? r.Binary : Clamp01(graduated);
            return r;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }
}
