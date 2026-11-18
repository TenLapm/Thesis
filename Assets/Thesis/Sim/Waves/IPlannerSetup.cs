namespace Thesis.Sim
{
    // A planner whose behaviour depends on more than its name and the seed (the full director: its
    // config and the estimator state it starts from). The replay stores this text, taken before the
    // first tick, so the planner can be rebuilt exactly as it began (`replay --rerun-director`,
    // WP11). The simulation never reads it.
    public interface IPlannerSetup
    {
        string PlannerSetup { get; }
    }
}
