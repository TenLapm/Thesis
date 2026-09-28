namespace Thesis.Sim
{
    // Everything a planner may look at when deciding a wave. State is the live
    // SimState, handed over for reading only: Simulation hashes it before and after
    // every planner call and throws if anything changed (ARCHITECTURE.md §4.4).
    public sealed class WaveContext
    {
        public readonly int WaveIndex;
        public readonly SimState State;
        public readonly MapData Map;
        public readonly SimConfig Config;

        public WaveContext(int waveIndex, SimState state, MapData map, SimConfig config)
        {
            WaveIndex = waveIndex;
            State = state;
            Map = map;
            Config = config;
        }
    }
}
