using System;

namespace Thesis.Sim
{
    // Every tunable the simulation reads. The field initialisers are the values the
    // game is actually played with - SampleScene's serialized WaveSpawner /
    // BlockManager / PlayerCore values - NOT the C# field defaults in those scripts,
    // which differ (WaveSpawner.agentsPerWave is 15 in code but 100 in the scene).
    // ARCHITECTURE.md §4.3, last row. WP4's SimConfigAsset maps an inspector asset
    // onto this class.
    //
    // Durations are stored in seconds and converted to ticks in exactly one place,
    // Ticks(), so rounding lives in one spot.
    //
    // [Serializable] (a System attribute, so still engine-free) lets Unity show and
    // save this class directly inside SimConfigAsset: one definition of every
    // tunable, not a second hand-copied list that can drift.
    [Serializable]
    public sealed class SimConfig
    {
        // 50 Hz = Unity's default fixed timestep. x3 game speed means 3x as many
        // ticks per rendered frame, never a bigger tick.
        public float TickSeconds = 0.02f;

        // --- economy (BlockManager, PlayerCore) ---
        public float StartBudget = 60f;          // BlockManager.buildBudget
        public int CoreMaxHp = 10;               // PlayerCore.maxHealth
        public int StipendPerWave = 20;          // WaveSpawner.budgetStipendPerWave

        // --- pacing (WaveSpawner) ---
        public float PrepSeconds = 45f;          // initialPrepDuration
        public float IntermissionSeconds = 10f;  // intermissionDuration

        // --- agent. Rewards live here, not in a WavePlan: they pay the player, and
        // the director must never touch the player's budget (CLAUDE.md I11).
        public float DigRate = 1f;
        public float KillReward = 0.2f;          // was DeathReward: paid when the lifetime clock ran out
        public float WallBreakReward = 1f;       // yes, the player is paid when a wall is breached
        public float TowerBreachReward = 0f;     // a chewed-through tower pays nothing (spec gap S12)

        // --- static escalation (WaveSpawner, SampleScene values) ---
        public int AgentsPerWave = 100;
        public int AgentsPerWaveIncrement = 5;
        public float SpawnDelay = 0.2f;
        public float SpawnDelayDecrement = 0.02f;
        public float MinSpawnDelay = 0.15f;
        public float BaseMoveSpeed = 1.25f;
        public float MoveSpeedIncrementPerWave = 0.0375f;
        public float MaxMoveSpeed = 2.25f;

        // Hit points replace the lifetime that used to grow by 4 s a wave (WP-C1).
        // PLACEHOLDER numbers until the balance check (WP-C5, spec gap S12).
        public float BaseHp = 10f;
        public float HpIncrementPerWave = 2f;

        // --- every wave must end (ARCHITECTURE.md §4.6) ---
        // The lifetime clock used to guarantee that every enemy resolves. Without
        // it, two things do. A slow can never take an enemy below this share of its
        // speed, so a slowed enemy still advances. And if a wave has still not
        // resolved after MaxWaveSeconds, its remaining enemies are removed (no core
        // damage, no reward) and the outcome is marked TimedOut. That backstop
        // should never fire in a healthy game.
        public float MinSlowFactor = 0.25f;
        public float MaxWaveSeconds = 600f;

        // --- per-wave statistics ---
        // An agent "pressured" the core if it leaked or its cheapest BestCost/10
        // (tile units) fell to this radius or below. Placeholder until spec gap S5
        // is closed; it only feeds WaveOutcome.PressureShare, which nothing reads yet.
        public float PressureRadiusTiles = 5f;

        // Allowed |ThreatSpent - budget| when an IThreatPricer is present (I2).
        public float ThreatEpsilon = 1e-3f;

        public int Ticks(float seconds)
        {
            return (int)MathF.Round((float)(seconds / TickSeconds));
        }

        // Every field is a plain number, so a shallow copy is a full copy. A host
        // gives the simulation its own copy, so that editing the source object later
        // (the config asset in Unity's inspector, during play) cannot change a
        // running game.
        public SimConfig Clone() { return (SimConfig)MemberwiseClone(); }

        public void Validate()
        {
            if (!(TickSeconds > 0f)) throw new InvalidOperationException("SimConfig.TickSeconds must be positive, was " + TickSeconds + ".");
            if (CoreMaxHp <= 0) throw new InvalidOperationException("SimConfig.CoreMaxHp must be positive, was " + CoreMaxHp + ".");
            if (PrepSeconds < 0f || IntermissionSeconds < 0f) throw new InvalidOperationException("SimConfig prep/intermission durations must not be negative.");
            if (!(BaseMoveSpeed > 0f)) throw new InvalidOperationException("SimConfig.BaseMoveSpeed must be positive: an enemy that cannot move never resolves.");
            if (!(BaseHp > 0f)) throw new InvalidOperationException("SimConfig.BaseHp must be positive, was " + BaseHp + ".");
            if (HpIncrementPerWave < 0f) throw new InvalidOperationException("SimConfig.HpIncrementPerWave must not be negative, was " + HpIncrementPerWave + ".");
            if (!(MinSlowFactor > 0f) || MinSlowFactor > 1f) throw new InvalidOperationException("SimConfig.MinSlowFactor must be in (0, 1], was " + MinSlowFactor + ".");
            if (!(MaxWaveSeconds > 0f)) throw new InvalidOperationException("SimConfig.MaxWaveSeconds must be positive, was " + MaxWaveSeconds + ".");
        }
    }
}
