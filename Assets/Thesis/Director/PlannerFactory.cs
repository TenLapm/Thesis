using System;
using Thesis.Sim;

namespace Thesis.Director
{
    // Every planner, built from its name (the 2026-10-02 review: one factory). The Unity host
    // (DirectorHost) and the headless harness (Harness.Registry) both call this; neither keeps a
    // list of its own, so a name means the same planner everywhere, replays included.
    //
    // The planners that use the cost table get the measured one (ThreatCostTable's defaults), and
    // the director gets DirectorConfig.Provisional() unless a config is passed.
    public static class PlannerFactory
    {
        public static readonly string[] Names =
        {
            EscalationPlanner.Id, ClassCyclePlanner.Id, RaidPlanner.Id, RoughDirector.Id,
            WaveDirector.HeuristicId, WaveDirector.FullId, RandomPlanner.Id, RoundRobinPlanner.Id,
        };

        // setup: a replay's PlannerSetup (DirectorSetup JSON). With it the director is rebuilt with the
        // config and the starting estimator it was recorded with; without, the provisional config and
        // a fresh estimator.
        public static IWavePlanner Create(string name, SimConfig config, MapData map, ulong seed, DirectorConfig director = null, string setup = null)
        {
            DirectorSetup recorded = setup != null && (name == WaveDirector.HeuristicId || name == WaveDirector.FullId) ? DirectorSetup.Read(setup) : null;
            if (recorded != null) director = recorded.Config;
            switch (name)
            {
                case EscalationPlanner.Id: return new EscalationPlanner(config, map);
                case ClassCyclePlanner.Id: return new ClassCyclePlanner(config, map);
                case RaidPlanner.Id: return new RaidPlanner(config, map);
                case RoughDirector.Id: return new RoughDirector(config, map);
                case WaveDirector.HeuristicId:
                    return new WaveDirector(director ?? DirectorConfig.Provisional(), config, map, new ThreatCostTable(config, map), seed, DirectorMode.HeuristicOnly);
                case WaveDirector.FullId:
                {
                    DirectorConfig cfg = director ?? DirectorConfig.Provisional();
                    Thesis.Learning.IStrategyEstimator estimator = recorded != null ? recorded.NewEstimator() : cfg.NewEstimator();
                    return new WaveDirector(cfg, config, map, new ThreatCostTable(config, map), seed, DirectorMode.Full, estimator);
                }
                case RandomPlanner.Id: return new RandomPlanner(config, map, new ThreatCostTable(config, map), seed);
                case RoundRobinPlanner.Id: return new RoundRobinPlanner(config, map, new ThreatCostTable(config, map), seed);
                default: throw new ArgumentException("Unknown planner '" + name + "'. Known: " + string.Join(", ", Names) + ".");
            }
        }
    }
}
