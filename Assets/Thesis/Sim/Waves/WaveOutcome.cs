namespace Thesis.Sim
{
    // How one wave went. Opened when the wave begins, counted up during it, and
    // closed when its last agent resolves (D4) or the core dies. The planner
    // receives a Copy() in OnWaveResolved; the director's reward reads these fields.
    public sealed class WaveOutcome
    {
        public int WaveIndex;
        public string StrategyId;

        public int Spawned;
        public int Stalled;
        public int Leaked;
        public int WallsBreached;

        public int CoreHpBefore;
        public int CoreHpAfter;

        // BudgetAfter is taken at resolution, BEFORE the intermission stipend, so it
        // measures the wave itself (stall and breach rewards minus what was spent).
        public float BudgetBefore;
        public float BudgetAfter;

        public int TickStarted;
        public int TickResolved = -1;

        // Agents that leaked, or whose cheapest BestCost/10 reached
        // SimConfig.PressureRadiusTiles. Share = PressureCount / Spawned.
        public int PressureCount;
        public float PressureShare;

        // True when the wave was closed by the core dying rather than by its last
        // agent resolving - the "survived" reward term reads this.
        public bool CoreDestroyed;

        // Agents of this wave have ids [FirstAgentId, FirstAgentId + Spawned).
        public int FirstAgentId;

        public bool IsClosed => TickResolved >= 0;

        public WaveOutcome Copy() { return (WaveOutcome)MemberwiseClone(); }

        public override string ToString()
        {
            return "wave " + WaveIndex + " [" + StrategyId + "] spawned=" + Spawned + " stalled=" + Stalled + " leaked=" + Leaked
                   + " breaches=" + WallsBreached + " core " + CoreHpBefore + "->" + CoreHpAfter
                   + (CoreDestroyed ? " (DESTROYED)" : "") + " pressure=" + PressureShare.ToString("0.###");
        }
    }
}
