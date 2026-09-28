using System;

namespace Thesis.Sim
{
    // The static baseline condition: WaveSpawner.SpawnTrickleRoutine's escalation,
    // ported number-for-number. The game as it exists today IS this planner.
    public sealed class EscalationPlanner : IWavePlanner
    {
        public const string Id = "escalation";

        private readonly SimConfig config;
        private readonly IThreatPricer pricer;

        // WaveSpawner.Start overwrites the serialized baseLifeTime (60) with this:
        // the fastest possible unobstructed spawn-to-core travel time plus a margin,
        // so a totally open board always leaks and the maze has to buy at least
        // LifeTimeSafetyMargin seconds to stall anything.
        public readonly float BaseLifeTime;

        public EscalationPlanner(SimConfig config, MapData map, IThreatPricer pricer = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.SpawnWorlds.Length == 0) throw new InvalidOperationException("EscalationPlanner needs a spawn; map '" + map.Name + "' has none.");

            this.config = config;
            this.pricer = pricer;
            // Explicit casts round every float intermediate (ARCHITECTURE.md §9 rule 3).
            BaseLifeTime = (float)((float)(Distance3(map.SpawnWorlds[0], map.CoreWorld) / config.BaseMoveSpeed) + config.LifeTimeSafetyMargin);
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context) { return PlanFor(context.WaveIndex); }

        public void OnWaveResolved(WaveOutcome outcome) { } // static: never reacts

        // Same expressions, same float types, same order as SpawnTrickleRoutine.
        public WavePlan PlanFor(int waveNumber)
        {
            if (waveNumber < 1) throw new ArgumentOutOfRangeException(nameof(waveNumber), waveNumber, "Waves are 1-based.");

            int waveIndex = waveNumber - 1;
            int count = config.AgentsPerWave + waveIndex * config.AgentsPerWaveIncrement;
            float delay = Math.Max(config.MinSpawnDelay, (float)(config.SpawnDelay - (float)(waveIndex * config.SpawnDelayDecrement)));
            float life = Math.Min(config.MaxLifeTime, (float)(BaseLifeTime + (float)(waveIndex * config.LifeTimeIncrementPerWave)));
            float speed = Math.Min(config.MaxMoveSpeed, (float)(config.BaseMoveSpeed + (float)(waveIndex * config.MoveSpeedIncrementPerWave)));

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
                        MoveSpeed = speed,
                        LifeTime = life,
                        DigRate = config.DigRate,
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

        // Vector3.Distance, in the same float arithmetic Unity uses.
        private static float Distance3(WorldPoint a, WorldPoint b)
        {
            float dx = (float)(a.X - b.X);
            float dy = (float)(a.Y - b.Y);
            float dz = (float)(a.Z - b.Z);
            return (float)Math.Sqrt((float)((float)((float)(dx * dx) + (float)(dy * dy)) + (float)(dz * dz)));
        }
    }
}
