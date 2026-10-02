namespace Thesis.Sim
{
    // Everything a view (Unity, telemetry, the CLI) needs to react to without
    // polling. ARCHITECTURE.md §4.5: "views rely on these events and never poll
    // for changes." One closed enum plus a few untyped payload slots keeps this a
    // single small type instead of a class per event; each factory method below
    // documents what its own Kind puts in which slot.
    //
    // Events are not part of the state and are not hashed, so this enum may be
    // extended freely. AgentKilled took the place of AgentStalled when hit points
    // replaced the lifetime clock (WP-C1).
    public enum SimEventKind
    {
        WallPlaced,
        WallBreached,
        AgentSpawned,
        AgentKilled,
        AgentLeaked,
        WaveStarted,
        WaveResolved,
        GameOver,
        TowerPlaced,
        TowerDestroyed,
        TowerFired,
        AgentRemoved,
        WaveTimedOut,
    }

    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly int IntA;
        public readonly int IntB;
        public readonly int IntC;
        public readonly float FloatA;

        private SimEvent(SimEventKind kind, int intA, int intB, int intC, float floatA)
        {
            Kind = kind;
            IntA = intA;
            IntB = intB;
            IntC = intC;
            FloatA = floatA;
        }

        // IntA = tile X, IntB = tile Y.
        public static SimEvent WallPlaced(int x, int y) { return new SimEvent(SimEventKind.WallPlaced, x, y, 0, 0f); }

        // IntA = tile X, IntB = tile Y.
        public static SimEvent WallBreached(int x, int y) { return new SimEvent(SimEventKind.WallBreached, x, y, 0, 0f); }

        // IntA = agent id.
        public static SimEvent AgentSpawned(int agentId) { return new SimEvent(SimEventKind.AgentSpawned, agentId, 0, 0, 0f); }

        // IntA = agent id, FloatA = kill reward credited.
        public static SimEvent AgentKilled(int agentId, float killReward) { return new SimEvent(SimEventKind.AgentKilled, agentId, 0, 0, killReward); }

        // IntA = agent id.
        public static SimEvent AgentLeaked(int agentId) { return new SimEvent(SimEventKind.AgentLeaked, agentId, 0, 0, 0f); }

        // IntA = wave index (1-based, matches WaveSpawner.currentWave).
        public static SimEvent WaveStarted(int waveIndex) { return new SimEvent(SimEventKind.WaveStarted, waveIndex, 0, 0, 0f); }

        // IntA = wave index.
        public static SimEvent WaveResolved(int waveIndex) { return new SimEvent(SimEventKind.WaveResolved, waveIndex, 0, 0, 0f); }

        public static SimEvent GameOver() { return new SimEvent(SimEventKind.GameOver, 0, 0, 0, 0f); }

        // IntA = tower id, IntB = tile X, IntC = tile Y.
        public static SimEvent TowerPlaced(int towerId, int x, int y) { return new SimEvent(SimEventKind.TowerPlaced, towerId, x, y, 0f); }

        // An enemy chewed through the tower's tile. IntA = tower id, IntB = tile X, IntC = tile Y.
        public static SimEvent TowerDestroyed(int towerId, int x, int y) { return new SimEvent(SimEventKind.TowerDestroyed, towerId, x, y, 0f); }

        // One shot; the hit has already been resolved. IntA = tower id, IntB = target agent id.
        public static SimEvent TowerFired(int towerId, int targetAgentId) { return new SimEvent(SimEventKind.TowerFired, towerId, targetAgentId, 0, 0f); }

        // The MaxWaveSeconds backstop took this agent off the board. IntA = agent id.
        public static SimEvent AgentRemoved(int agentId) { return new SimEvent(SimEventKind.AgentRemoved, agentId, 0, 0, 0f); }

        // The MaxWaveSeconds backstop fired. IntA = wave index.
        public static SimEvent WaveTimedOut(int waveIndex) { return new SimEvent(SimEventKind.WaveTimedOut, waveIndex, 0, 0, 0f); }

        public override string ToString()
        {
            return Kind + "(" + IntA + "," + IntB + "," + IntC + "," + FloatA + ")";
        }
    }
}
