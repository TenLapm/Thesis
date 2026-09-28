namespace Thesis.Sim
{
    // The one seam every study condition plugs into (ARCHITECTURE.md §4.4). The
    // static baseline (EscalationPlanner), the ladder baselines and both director
    // modes are all implementations of this interface, run by the same Simulation.
    // That is what makes the conditions comparable: only the planner differs.
    public interface IWavePlanner
    {
        // Written to telemetry as the study condition.
        string Name { get; }

        // Called once at the start of each wave. Must only READ context.State;
        // Simulation throws InvalidOperationException if the state hash changes.
        WavePlan PlanWave(WaveContext context);

        // Called once per wave, after its last agent resolves or the core dies.
        // Receives a copy; must not reach back into the state either.
        void OnWaveResolved(WaveOutcome outcome);
    }
}
