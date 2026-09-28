namespace Thesis.Sim
{
    // The wave loop as an explicit state machine (WaveSpawner kept it implicit in
    // coroutines and two bools). ARCHITECTURE.md §4.2 step 2.
    //
    //   Prep ──(timer or StartWaveNow)──► Spawning ──(last agent spawned)──► Resolving
    //     ▲                                                                      │
    //     └────── Intermission ◄──(every agent of the wave stalled or leaked)────┘
    //
    // GameOver is terminal: Tick() becomes a no-op.
    public enum SimPhase
    {
        Prep,
        Spawning,
        Resolving,
        Intermission,
        GameOver,
    }
}
