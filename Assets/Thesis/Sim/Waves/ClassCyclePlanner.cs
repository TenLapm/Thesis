using System;

namespace Thesis.Sim
{
    // DEVELOPMENT ONLY: not a study condition and not a rung of the ladder (WP-C2).
    //
    // The director's strategies (WP9) are what will send sappers and flyers. Until
    // they exist nothing would, and the two new movement classes could not be seen
    // in Unity, recorded in a replay, or compared between Mono and .NET. This planner
    // fills that gap. It takes each wave's escalation plan and turns its enemies
    // into another class, in a fixed four-wave cycle:
    //
    //   waves 1, 5, 9 ...    ground    the escalation wave, unchanged
    //   waves 2, 6, 10 ...   sappers   half as many; tougher, slower, chew 4x as fast
    //   waves 3, 7, 11 ...   flyers    half as many; weaker and faster
    //   waves 4, 8, 12 ...   all three: half ground, a quarter sappers, a quarter flyers
    //
    // The sapper and flyer numbers are PLACEHOLDERS, like the tower roster. The real
    // enemy archetypes are spec gap S12 and arrive with the balance check (WP-C5).
    //
    // It is not budget-neutral: nothing prices a sapper or a flyer yet (the cost
    // table is WP9), so with an IThreatPricer present its plans would be refused.
    // Use it without one.
    public sealed class ClassCyclePlanner : IWavePlanner
    {
        public const string Id = "class-cycle";

        public const string SapperArchetype = "sapper";
        public const string FlyerArchetype = "flyer";

        // Placeholder archetypes, as multiples of that wave's escalation enemy.
        public const float SapperHpFactor = 1.5f;
        public const float SapperSpeedFactor = 0.8f;
        public const float SapperDigRateFactor = 4f;
        public const float FlyerHpFactor = 0.5f;
        public const float FlyerSpeedFactor = 1.2f;

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
            int half = Math.Max(1, basic.Count / 2);
            int quarter = Math.Max(1, basic.Count / 4);

            AgentGroup[] groups;
            switch ((waveNumber - 1) % 4)
            {
                case 0: groups = new[] { basic }; break;
                case 1: groups = new[] { Sapper(basic, half) }; break;
                case 2: groups = new[] { Flyer(basic, half) }; break;
                default: groups = new[] { WithCount(basic, half), Sapper(basic, quarter), Flyer(basic, quarter) }; break;
            }

            var plan = new WavePlan { WaveIndex = waveNumber, StrategyId = Id, Groups = groups };
            plan.ThreatSpent = pricer != null ? pricer.Price(plan) : 0f;
            return plan;
        }

        private static AgentGroup WithCount(AgentGroup basic, int count)
        {
            AgentGroup g = basic.Copy();
            g.Count = count;
            return g;
        }

        // Each stat is one multiplication stored straight to a field, so it needs no
        // intermediate cast (ARCHITECTURE.md §9 rule 3); the casts are there anyway,
        // so that nobody has to work that out again.
        private static AgentGroup Sapper(AgentGroup basic, int count)
        {
            AgentGroup g = WithCount(basic, count);
            g.Archetype = SapperArchetype;
            g.Movement = MovementClass.Sapper;
            g.Hp = (float)(basic.Hp * SapperHpFactor);
            g.MoveSpeed = (float)(basic.MoveSpeed * SapperSpeedFactor);
            g.DigRate = (float)(basic.DigRate * SapperDigRateFactor);
            return g;
        }

        private static AgentGroup Flyer(AgentGroup basic, int count)
        {
            AgentGroup g = WithCount(basic, count);
            g.Archetype = FlyerArchetype;
            g.Movement = MovementClass.Flying;
            g.Hp = (float)(basic.Hp * FlyerHpFactor);
            g.MoveSpeed = (float)(basic.MoveSpeed * FlyerSpeedFactor);
            g.DigRate = 0f; // a flyer never digs
            return g;
        }
    }
}
