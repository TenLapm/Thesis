using System;
using System.Collections.Generic;

namespace Thesis.Sim
{
    // One plan a planner handed the simulation, as the replay stores it (schema 16, WP10).
    public sealed class RecordedPlan
    {
        public int Wave;
        public WavePlan Plan;
    }

    // Plays a recording's plans back, in the order the planner made them (WP10). The simulation
    // asks its planner at exactly the same moments on a replay as in the session, so the n-th call
    // gets the n-th recorded plan; a wave planned twice (the core moved, a wave restarted) simply has
    // two entries. This is what lets a session replay without the planner's code: a director that
    // has changed since, or one removed from PlannerFactory, no longer matters to the simulation.
    public sealed class RecordedPlanner : IWavePlanner
    {
        private readonly List<RecordedPlan> plans;
        private int next;

        public RecordedPlanner(string name, List<RecordedPlan> plans)
        {
            Name = name ?? "recorded";
            this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        }

        public string Name { get; }

        public int Used => next;

        public WavePlan PlanWave(WaveContext context)
        {
            if (next >= plans.Count)
                throw new RecordedPlansExhaustedException("the simulation asked for a plan for wave " + context.WaveIndex + ", but the recording holds only "
                                                          + plans.Count + " recorded plan(s): it ran differently from the session.");
            RecordedPlan p = plans[next];
            if (p.Wave != context.WaveIndex)
                throw new RecordedPlansExhaustedException("recorded plan " + next + " is for wave " + p.Wave + ", but the simulation asked for wave "
                                                          + context.WaveIndex + ": it ran differently from the session.");
            next++;
            return p.Plan.Copy();
        }

        public void OnWaveResolved(WaveOutcome outcome) { }
    }

    // The recording and the run have parted: a replay reports this rather than crashing.
    public sealed class RecordedPlansExhaustedException : InvalidOperationException
    {
        public RecordedPlansExhaustedException(string message) : base(message) { }
    }
}
