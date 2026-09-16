namespace Thesis.Sim
{
    // Everything a view (Unity, telemetry, the CLI) needs to react to without
    // polling. ARCHITECTURE.md §4.5: "views rely on these events and never poll
    // for changes." One closed enum plus three untyped payload slots keeps this a
    // single small type instead of eight separate event classes; each factory
    // method below documents what its own Kind puts in which slot.
    //
    // Only WallPlaced, WallBreached, AgentStalled and AgentLeaked are raised as of
    // WP2. The rest exist now so WP3+ never needs to touch this file again.
    public enum SimEventKind
    {
        WallPlaced,
        WallBreached,
        AgentSpawned,
        AgentStalled,
        AgentLeaked,
        WaveStarted,
        WaveResolved,
        GameOver,
    }

    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly int IntA;
        public readonly int IntB;
        public readonly float FloatA;

        private SimEvent(SimEventKind kind, int intA, int intB, float floatA)
        {
            Kind = kind;
            IntA = intA;
            IntB = intB;
            FloatA = floatA;
        }

        // IntA = tile X, IntB = tile Y.
        public static SimEvent WallPlaced(int x, int y) { return new SimEvent(SimEventKind.WallPlaced, x, y, 0f); }

        // IntA = tile X, IntB = tile Y.
        public static SimEvent WallBreached(int x, int y) { return new SimEvent(SimEventKind.WallBreached, x, y, 0f); }

        // IntA = agent id.
        public static SimEvent AgentSpawned(int agentId) { return new SimEvent(SimEventKind.AgentSpawned, agentId, 0, 0f); }

        // IntA = agent id, FloatA = deathReward credited.
        public static SimEvent AgentStalled(int agentId, float deathReward) { return new SimEvent(SimEventKind.AgentStalled, agentId, 0, deathReward); }

        // IntA = agent id.
        public static SimEvent AgentLeaked(int agentId) { return new SimEvent(SimEventKind.AgentLeaked, agentId, 0, 0f); }

        // IntA = wave index (1-based, matches WaveSpawner.currentWave).
        public static SimEvent WaveStarted(int waveIndex) { return new SimEvent(SimEventKind.WaveStarted, waveIndex, 0, 0f); }

        // IntA = wave index.
        public static SimEvent WaveResolved(int waveIndex) { return new SimEvent(SimEventKind.WaveResolved, waveIndex, 0, 0f); }

        public static SimEvent GameOver() { return new SimEvent(SimEventKind.GameOver, 0, 0, 0f); }

        public override string ToString()
        {
            return Kind + "(" + IntA + "," + IntB + "," + FloatA + ")";
        }
    }
}
