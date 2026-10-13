using System;

namespace Thesis.Sim
{
    // DEVELOPMENT ONLY: not a study condition and not a rung of the ladder (WP-C2).
    //
    // The director's strategies (WP9) are what will send sappers, flyers and swarms.
    // Until they exist nothing would, and those enemies could not be seen in Unity,
    // recorded in a replay, or compared between Mono and .NET. This planner fills
    // that gap. It takes each wave's escalation plan and turns its enemies into
    // another type (EnemyArchetypes), in a fixed five-wave cycle:
    //
    //   waves 1, 6, 11 ...   basic     the escalation wave, unchanged
    //   waves 2, 7, 12 ...   sappers
    //   waves 3, 8, 13 ...   flyers
    //   waves 4, 9, 14 ...   a swarm
    //   waves 5, 10, 15 ...  half basic, half the wave's sappers, half its flyers
    //
    // It is not budget-neutral: nothing prices an enemy type yet (the cost table is
    // WP9), so with an IThreatPricer present its plans would be refused. Use it
    // without one.
    public sealed class ClassCyclePlanner : IWavePlanner
    {
        public const string Id = "class-cycle";

        public const int CycleLength = 5;

        private readonly EscalationPlanner escalation;
        private readonly IThreatPricer pricer;

        public ClassCyclePlanner(SimConfig config, MapData map, IThreatPricer pricer = null)
        {
            escalation = new EscalationPlanner(config, map);
            this.pricer = pricer;
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context) { return PlanFor(context.WaveIndex); }

        public void OnWaveResolved(WaveOutcome outcome) { } // a fixed cycle: never reacts

        public WavePlan PlanFor(int waveNumber)
        {
            AgentGroup basic = escalation.PlanFor(waveNumber).Groups[0];

            AgentGroup[] groups;
            switch ((waveNumber - 1) % CycleLength)
            {
                case 0: groups = new[] { basic }; break;
                case 1: groups = new[] { EnemyArchetypes.Sapper().Apply(basic, 1f) }; break;
                case 2: groups = new[] { EnemyArchetypes.Flyer().Apply(basic, 1f) }; break;
                case 3: groups = new[] { EnemyArchetypes.Swarm().Apply(basic, 1f) }; break;
                default:
                    groups = new[] { EnemyArchetypes.Basic().Apply(basic, 0.5f), EnemyArchetypes.Sapper().Apply(basic, 0.5f), EnemyArchetypes.Flyer().Apply(basic, 0.5f) };
                    break;
            }

            var plan = new WavePlan { WaveIndex = waveNumber, StrategyId = Id, Groups = groups };
            plan.ThreatSpent = pricer != null ? pricer.Price(plan) : 0f;
            return plan;
        }
    }
}
