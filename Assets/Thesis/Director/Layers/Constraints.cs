using System;
using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Director
{
    // Layer 1 (CLAUDE.md §5): hard vetoes, checked before the heuristic and before the learner is
    // ever asked (I5). Each returns its reason, or null to allow. A vetoed strategy scores
    // float.NegativeInfinity. The numbers are DirectorConfig's (the student's provisional choices,
    // 2026-11-16).

    // The plan does not spend the wave's budget (I2). A guard: the strategies fit their plans
    // exactly, so this only fires if one of them is broken.
    public sealed class OverBudget
    {
        public const string Reason = "OverBudget";

        public string Check(WavePlan plan, float budget, SimConfig config)
        {
            float tolerance = (float)(config.ThreatEpsilon * Math.Max(1f, Math.Abs(budget)));
            return Math.Abs(plan.ThreatSpent - budget) > tolerance ? Reason : null;
        }
    }

    // A strategy may run at most MaxStreak waves in a row. A backstop: with the similarity rule
    // below a strategy cannot even repeat once, but the rule stays if that threshold is lowered.
    public sealed class RepeatStreak
    {
        public const string Reason = "RepeatStreak";
        private readonly int maxStreak;

        public RepeatStreak(int maxStreak) { this.maxStreak = maxStreak; }

        // recent: the strategies of the waves before, the latest last.
        public string Check(string strategy, IReadOnlyList<string> recent)
        {
            int streak = 0;
            for (int i = recent.Count - 1; i >= 0 && recent[i] == strategy; i--) streak++;
            return streak >= maxStreak ? Reason : null;
        }
    }

    // A wave the player has nothing to answer with. Today only flyers: flying_bypass is vetoed while
    // the player has no POWERED, standing tower that can hit flyers (the student, 2026-11-16). The
    // build menu always offers one, so the proposal's "and none on offer" never applies; a tower out
    // of power reach does not fire, so it does not count.
    public sealed class Uncounterable
    {
        public const string Reason = "Uncounterable";

        public string Check(string strategy, SimState state)
        {
            if (strategy != FlyingBypass.StrategyName) return null;
            foreach (TowerState t in state.Towers)
                if (t.IsAlive && !t.OnBench && t.PowerCapacity == 0 && t.CanHitFlying && t.Powered) return null;
            return Reason;
        }
    }

    // The anti-collapse rule (CLAUDE.md §5): a plan whose composition lies within a threshold of the
    // previous wave's is vetoed. It replaces a hardcoded "no repeat".
    //
    // Composition: the share of the wave's enemies of each type (EnemyArchetype id), plus the
    // number of entries it uses divided by 3 (the most a wave uses). L2 distance. So swarm and split
    // differ (other types, three entries against one), and the same strategy from another entry
    // does not (provisional, 2026-11-16).
    public sealed class TooSimilarToPrevious
    {
        public const string Reason = "TooSimilarToPrevious";
        private readonly float threshold;

        public TooSimilarToPrevious(float threshold) { this.threshold = threshold; }

        public string Check(WavePlan plan, WavePlan previous)
        {
            if (previous == null) return null;
            return Distance(plan, previous) < threshold ? Reason : null;
        }

        public static float Distance(WavePlan a, WavePlan b)
        {
            var types = new List<string>();
            AddTypes(a, types);
            AddTypes(b, types);
            types.Sort(StringComparer.Ordinal); // a fixed order, so the sum is the same on every runtime
            double sum = 0.0;
            foreach (string t in types)
            {
                double d = Share(a, t) - Share(b, t);
                sum += d * d;
            }
            double e = Entries(a) / 3.0 - Entries(b) / 3.0;
            sum += e * e;
            return (float)Math.Sqrt(sum); // exact under IEEE 754
        }

        private static void AddTypes(WavePlan p, List<string> into)
        {
            if (p.Groups == null) return;
            foreach (AgentGroup g in p.Groups) if (g != null && g.Count > 0 && !into.Contains(g.Archetype)) into.Add(g.Archetype);
        }

        private static double Share(WavePlan p, string type)
        {
            int all = p.TotalAgents();
            if (all == 0) return 0.0;
            int n = 0;
            foreach (AgentGroup g in p.Groups) if (g != null && g.Archetype == type) n += g.Count;
            return (double)n / all;
        }

        private static int Entries(WavePlan p)
        {
            var seen = new HashSet<int>();
            if (p.Groups != null) foreach (AgentGroup g in p.Groups) if (g != null && g.Count > 0) seen.Add(g.SpawnIndex);
            return seen.Count;
        }
    }
}
