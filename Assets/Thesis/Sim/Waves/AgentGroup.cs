namespace Thesis.Sim
{
    // One homogeneous batch of enemies inside a wave: where they enter, how many,
    // their stats, and how they trickle in. Agent k of the group spawns at
    // waveStartTick + StartDelayTicks + k * SpawnIntervalTicks.
    public sealed class AgentGroup
    {
        public int SpawnIndex;          // index into MapData.Spawns
        public int Count;

        // The enemy type these are, e.g. "basic". A label for telemetry and views;
        // the numbers below are what the simulation uses.
        public string Archetype = "basic";

        // How they get to the core: on the ground field, on the sapper field, or in
        // a straight line through the air (WP-C2). Ground unless a planner says otherwise.
        public MovementClass Movement = MovementClass.Ground;

        public float MoveSpeed;         // world units / second; must be > 0
        public float Hp;                // hit points; must be > 0
        public float DigRate;           // structure health chewed per second; unused by Flying

        // One damage multiplier per DamageType (1 = normal, 0 = immune).
        // Null means "1 for every type"; Copy() always fills it in.
        public float[] Resist;

        public int SpawnIntervalTicks;
        public int StartDelayTicks;

        // A deep copy with Resist always present, so the simulation's own copy of a
        // plan never has to deal with null and never shares an array with the planner.
        public AgentGroup Copy()
        {
            var c = (AgentGroup)MemberwiseClone();
            c.Resist = Resist == null ? DamageTypes.AllOnes() : (float[])Resist.Clone();
            return c;
        }

        public override string ToString()
        {
            return Count + " " + Archetype + " (" + Movement + ") @spawn" + SpawnIndex + " speed=" + MoveSpeed + " hp=" + Hp + " dig=" + DigRate + " every " + SpawnIntervalTicks + "t (+" + StartDelayTicks + "t)";
        }
    }
}
