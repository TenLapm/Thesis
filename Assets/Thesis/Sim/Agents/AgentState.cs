using Thesis.Core;

namespace Thesis.Sim
{
    // Port of FlowAgent's data (movement, digging, the lifetime clock), minus
    // everything Unity-only (the GameObject, the life-bar Image, groundHeight -
    // the view interpolates Position onto the floor itself).
    //
    // WP-C1 will replace LifeTime/DeathReward with Hp/MaxHp/Resist and drop the
    // clock (CLAUDE.md §2); until then this is a faithful port of the current game.
    public sealed class AgentState
    {
        public readonly int Id;

        public Vec2f Position;
        public float MoveSpeed;
        public float DigRate;

        public float LifeTime;
        public readonly float DeathReward;
        public readonly float WallBreakReward;

        public bool IsAlive = true;

        // How it died: true = reached the core, false = stalled (only meaningful
        // once IsAlive is false). WaveOutcome's pressure count reads it.
        public bool Leaked;

        // The wave that spawned this agent (1-based). Set by Simulation at spawn.
        public int WaveIndex;

        // Cheapest BestCost this agent has stood on so far this wave (SimNode.Infinity
        // until it takes its first live step). Not consumed until the reward's
        // `pressure` term (WP11); tracked from the first tick so no wave is missing
        // data once that lands.
        public int MinCostSeen = SimNode.Infinity;

        public AgentState(int id, Vec2f position, float moveSpeed, float lifeTime, float digRate, float deathReward, float wallBreakReward)
        {
            Id = id;
            Position = position;
            MoveSpeed = moveSpeed;
            LifeTime = lifeTime;
            DigRate = digRate;
            DeathReward = deathReward;
            WallBreakReward = wallBreakReward;
        }

        internal AgentState Clone()
        {
            return new AgentState(Id, Position, MoveSpeed, LifeTime, DigRate, DeathReward, WallBreakReward)
            {
                IsAlive = IsAlive,
                Leaked = Leaked,
                WaveIndex = WaveIndex,
                MinCostSeen = MinCostSeen,
            };
        }

        public override string ToString()
        {
            return "Agent#" + Id + (IsAlive ? " alive" : (Leaked ? " leaked" : " stalled")) + " wave=" + WaveIndex + " life=" + LifeTime + " pos=" + Position;
        }
    }
}
