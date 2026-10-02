namespace Thesis.Sim
{
    // How one wave went. Opened when the wave begins, counted up during it, and
    // closed when its last agent resolves (D4) or the core dies. The planner
    // receives a Copy() in OnWaveResolved; the director's reward reads these fields.
    public sealed class WaveOutcome
    {
        public int WaveIndex;
        public string StrategyId;

        // Spawned = Killed + Leaked + Removed once the wave is closed normally.
        public int Spawned;
        public int Killed;
        public int Leaked;

        // Agents taken off the board by the MaxWaveSeconds backstop: no core damage,
        // no reward. 0 in any healthy wave; TimedOut says the backstop fired.
        public int Removed;
        public bool TimedOut;

        public int WallsBreached;
        public int TowersDestroyed;

        // Effective damage dealt to this wave's agents, per DamageType.
        public float[] DamageByType = new float[DamageTypes.Count];

        public int CoreHpBefore;
        public int CoreHpAfter;

        // BudgetAfter is taken at resolution, BEFORE the intermission stipend, so it
        // measures the wave itself (kill and breach rewards minus what was spent).
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

        public float TotalDamage()
        {
            float sum = 0f;
            for (int i = 0; i < DamageByType.Length; i++) sum = (float)(sum + DamageByType[i]);
            return sum;
        }

        // Deep: the damage array is copied, so a planner holding an old outcome
        // never sees a later wave's numbers.
        public WaveOutcome Copy()
        {
            var c = (WaveOutcome)MemberwiseClone();
            c.DamageByType = (float[])DamageByType.Clone();
            return c;
        }

        public override string ToString()
        {
            return "wave " + WaveIndex + " [" + StrategyId + "] spawned=" + Spawned + " killed=" + Killed + " leaked=" + Leaked
                   + " breaches=" + WallsBreached + (TowersDestroyed > 0 ? " towersLost=" + TowersDestroyed : "")
                   + " core " + CoreHpBefore + "->" + CoreHpAfter
                   + (CoreDestroyed ? " (DESTROYED)" : "") + (TimedOut ? " (TIMED OUT, " + Removed + " removed)" : "")
                   + " pressure=" + PressureShare.ToString("0.###");
        }
    }
}
