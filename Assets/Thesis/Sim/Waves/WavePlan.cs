namespace Thesis.Sim
{
    // What a planner decides for one wave. The Simulation validates it and keeps
    // its own Copy(), so a planner can never change a wave after handing it over.
    public sealed class WavePlan
    {
        public int WaveIndex;          // 1-based, matches WaveSpawner.currentWave
        public string StrategyId;      // "escalation", later "breach_thin_wall", ...
        public AgentGroup[] Groups;
        public float ThreatSpent;      // priced by the shared cost table (I2); 0 when none is in use
        public string AnnouncementId;  // null for the static baseline

        public WavePlan Copy()
        {
            var c = (WavePlan)MemberwiseClone();
            if (Groups != null)
            {
                c.Groups = new AgentGroup[Groups.Length];
                for (int i = 0; i < Groups.Length; i++) c.Groups[i] = Groups[i]?.Copy();
            }
            return c;
        }

        public int TotalAgents()
        {
            int n = 0;
            if (Groups != null) foreach (AgentGroup g in Groups) if (g != null) n += g.Count;
            return n;
        }
    }
}
