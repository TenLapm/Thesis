namespace Thesis.Sim
{
    // One homogeneous batch of agents inside a wave: where they enter, how many,
    // their stats, and how they trickle in. Agent k of the group spawns at
    // waveStartTick + StartDelayTicks + k * SpawnIntervalTicks, matching
    // WaveSpawner's "spawn, then WaitForSeconds(delay)" loop.
    public sealed class AgentGroup
    {
        public int SpawnIndex;          // index into MapData.Spawns
        public int Count;
        public float MoveSpeed;         // world units / second
        public float LifeTime;          // seconds (the stall clock; becomes HP in WP-C1)
        public float DigRate;           // wall health chewed per second
        public int SpawnIntervalTicks;
        public int StartDelayTicks;

        public AgentGroup Copy() { return (AgentGroup)MemberwiseClone(); }

        public override string ToString()
        {
            return Count + " agents @spawn" + SpawnIndex + " speed=" + MoveSpeed + " life=" + LifeTime + " dig=" + DigRate + " every " + SpawnIntervalTicks + "t (+" + StartDelayTicks + "t)";
        }
    }
}
