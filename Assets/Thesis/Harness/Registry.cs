using System;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Names -> instances, for everything a command line or a replay file refers to
    // by name. One place, so `run --planner X`, a replay's "Planner": "X" and (later)
    // the ladder all mean the same thing by X.
    public static class Registry
    {
        // class-cycle is a development planner, not a study condition: it sends
        // sappers and flyers until the director's strategies exist (WP9).
        public static readonly string[] PlannerNames = PlannerFactory.Names;

        public static readonly string[] PolicyNames = { IdlePolicy.Id, GreedyDetourPolicy.Id, SentryPolicy.Id, SentryPolicy.UpgraderId, SequencePolicy.MixedId, SequencePolicy.RestlessId, RaidPolicy.Id, SentryPolicy.TurtleId, BuilderPolicy.Id };

        // One factory for every planner (Thesis.Director.PlannerFactory, WP10), shared with the
        // Unity host. The seed is the run's rngSeed: the director and the ladder planners draw
        // their own streams from it.
        public static IWavePlanner CreatePlanner(string name, SimConfig config, MapData map, ulong seed = 1, string setup = null)
        {
            return PlannerFactory.Create(name, config, map, seed, null, setup);
        }

        public static IPlayerPolicy CreatePolicy(string name)
        {
            switch (name)
            {
                case IdlePolicy.Id: return new IdlePolicy();
                case GreedyDetourPolicy.Id: return new GreedyDetourPolicy();
                case SentryPolicy.Id: return new SentryPolicy();
                case SentryPolicy.UpgraderId: return new SentryPolicy { UpgradeWithWhatIsLeft = true };
                case SequencePolicy.MixedId: return SequencePolicy.Mixed();
                case SequencePolicy.RestlessId: return SequencePolicy.Restless();
                case RaidPolicy.Id: return new RaidPolicy();
                case SentryPolicy.TurtleId: return new SentryPolicy { RouteTail = 7 };
                case BuilderPolicy.Id: return new BuilderPolicy();
                default: throw new ArgumentException("Unknown policy '" + name + "'. Known: " + string.Join(", ", PolicyNames) + ".");
            }
        }
    }
}
