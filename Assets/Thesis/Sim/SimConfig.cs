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

        // --- agent (FlowAgent). Not serialized on Assets/Scripts/Prefabs/Agent.prefab,
        // so the C# defaults are what the game uses. Rewards live here, not in a
        // WavePlan: they pay the player, and the director must never touch the
        // player's budget (CLAUDE.md I11).
        public float DigRate = 1f;
        public float DeathReward = 0.2f;
        public float WallBreakReward = 1f;

        // --- static escalation (WaveSpawner, SampleScene values) ---
        public int AgentsPerWave = 100;
        public int AgentsPerWaveIncrement = 5;
        public float SpawnDelay = 0.2f;
        public float SpawnDelayDecrement = 0.02f;
        public float MinSpawnDelay = 0.15f;
        public float LifeTimeIncrementPerWave = 4f;
        public float MaxLifeTime = 200f;
        public float BaseMoveSpeed = 1.25f;
        public float MoveSpeedIncrementPerWave = 0.0375f;
        public float MaxMoveSpeed = 2.25f;
        public float LifeTimeSafetyMargin = 5f;

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
            if (BaseMoveSpeed <= 0f) throw new InvalidOperationException("SimConfig.BaseMoveSpeed must be positive (it divides the spawn-core distance).");
        }
    }
}
