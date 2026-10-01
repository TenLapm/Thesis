using System;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Names -> instances, for everything a command line or a replay file refers to
    // by name. One place, so `run --planner X`, a replay's "Planner": "X" and (later)
    // the ladder all mean the same thing by X.
    public static class Registry
    {
        public static readonly string[] PlannerNames = { EscalationPlanner.Id };

        public static readonly string[] PolicyNames = { IdlePolicy.Id, GreedyDetourPolicy.Id };

        // The director's planners register here as they are built (WP7 baselines,
        // WP10 heuristic, WP11 full director).
        public static IWavePlanner CreatePlanner(string name, SimConfig config, MapData map)
        {
            switch (name)
            {
                case EscalationPlanner.Id: return new EscalationPlanner(config, map);
                default: throw new ArgumentException("Unknown planner '" + name + "'. Known: " + string.Join(", ", PlannerNames) + ".");
            }
        }

        public static IPlayerPolicy CreatePolicy(string name)
        {
            switch (name)
            {
                case IdlePolicy.Id: return new IdlePolicy();
                case GreedyDetourPolicy.Id: return new GreedyDetourPolicy();
                default: throw new ArgumentException("Unknown policy '" + name + "'. Known: " + string.Join(", ", PolicyNames) + ".");
            }
        }
    }
}
