using System.Collections.Generic;
using Thesis.Learning;
using Thesis.Sim;

namespace Thesis.Director
{
    public enum DirectorMode
    {
        HeuristicOnly = 0, // layers 1 and 2: the ablation condition (WP10)
        Full = 1,          // plus the learner's bounded correction (layer 3; its update is WP11)
    }

    // One strategy's line in a decision (ARCHITECTURE.md §5.2): its plan, its veto or its scores.
    public sealed class StrategyRow
    {
        public int Id;
        public string Name;
        public WavePlan Plan;
        public string Veto;          // null when allowed
        public float? Heuristic;     // null when vetoed
        public Correction? Correction; // null when vetoed or in HeuristicOnly
        public float Score;          // NegativeInfinity when vetoed
    }

    // Everything one decision used and produced, so "why did it pick X in wave 7?" is answered by
    // reading it. Telemetry writes it into the wave's row (TelemetryRecorder). The timing is for
    // gate G2 and is never written to a row: a row holds nothing the simulation does not determine
    // (CLAUDE.md §7).
    public sealed class DirectorTrace
    {
        public int Wave;
        public BuildProfile Profile;
        public int Bin;
        public float ThreatBudget;
        public readonly List<StrategyRow> Rows = new List<StrategyRow>();
        public string Chosen;
        public string Fallback;      // WaveDirector.FallbackAllVetoed, or null
        public string Announcement;  // the banner's line, or null
        public long ElapsedMicroseconds;
    }

    // A planner that can say how it decided a wave. TelemetryRecorder asks it.
    public interface IDecisionSource
    {
        DirectorTrace TraceFor(int wave);
    }
}
