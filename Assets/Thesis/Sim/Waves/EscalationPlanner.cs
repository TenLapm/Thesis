using System;

namespace Thesis.Sim
{
    // The static baseline condition: the game's own escalation. Every wave brings
    // more enemies, a little faster, closer together, and (since WP-C1) with more
    // hit points. Count, speed and spacing are WaveSpawner.SpawnTrickleRoutine's
    // numbers, ported verbatim in WP3. The hit points replace what used to be a
    // lifetime that grew each wave (CLAUDE.md §3): the principle is the same, the
    // quantity that escalates is the one the player now fights.
    //
    // The HP numbers are placeholders until the balance check (WP-C5, spec gap S12).
    public sealed class EscalationPlanner : IWavePlanner
    {
        public const string Id = "escalation";
        public const string Archetype = "basic";

        private readonly SimConfig config;
        private readonly IThreatPricer pricer;

        public EscalationPlanner(SimConfig config, MapData map, IThreatPricer pricer = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.SpawnWorlds.Length == 0) throw new InvalidOperationException("EscalationPlanner needs a spawn; map '" + map.Name + "' has none.");

            this.config = config;
            this.pricer = pricer;
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context) { return PlanFor(context.WaveIndex); }

        public void OnWaveResolved(WaveOutcome outcome) { } // static: never reacts

        // Explicit casts round every float intermediate (ARCHITECTURE.md §9 rule 3).
        public WavePlan PlanFor(int waveNumber)
        {
            if (waveNumber < 1) throw new ArgumentOutOfRangeException(nameof(waveNumber), waveNumber, "Waves are 1-based.");

            int waveIndex = waveNumber - 1;
            int count = config.AgentsPerWave + waveIndex * config.AgentsPerWaveIncrement;
            float delay = Math.Max(config.MinSpawnDelay, (float)(config.SpawnDelay - (float)(waveIndex * config.SpawnDelayDecrement)));
            float speed = Math.Min(config.MaxMoveSpeed, (float)(config.BaseMoveSpeed + (float)(waveIndex * config.MoveSpeedIncrementPerWave)));
            float hp = (float)(config.BaseHp + (float)(waveIndex * config.HpIncrementPerWave));

            var plan = new WavePlan
            {
                WaveIndex = waveNumber,
                StrategyId = Id,
                Groups = new[]
                {
                    new AgentGroup
                    {
                        SpawnIndex = 0,
                        Count = count,
                        Archetype = Archetype,
                        MoveSpeed = speed,
                        Hp = hp,
                        DigRate = config.DigRate,
                        Resist = DamageTypes.AllOnes(),
                        // WaitForSeconds(delay) was quantised to frames; here it is
                        // quantised to the nearest tick (0.15 s -> 8 ticks = 0.16 s).
                        SpawnIntervalTicks = config.Ticks(delay),
                        StartDelayTicks = 0,
                    },
                },
            };
            plan.ThreatSpent = pricer != null ? pricer.Price(plan) : 0f;
            return plan;
        }
    }
}
