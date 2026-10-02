using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // One enemy. Until WP-C1 it carried a lifetime clock and "stalled" when the
    // clock ran out; now it has hit points and dies when towers have removed them
    // all (CLAUDE.md §2: damage replaces the clock). It still walks the flow field
    // and still chews through anything built in its way.
    //
    // An enemy ends in exactly one of three ways:
    //   Killed   towers took its last hit point       -> the player is paid KillReward
    //   Leaked   it reached the core                  -> the core loses 1 HP
    //   neither  the wave timed out and it was removed (SimConfig.MaxWaveSeconds;
    //            a backstop that should never fire)
    public sealed class AgentState
    {
        public readonly int Id;

        public Vec2f Position;
        public float MoveSpeed;
        public float DigRate;

        public float Hp;
        public readonly float MaxHp;

        // One damage multiplier per DamageType: 1 = normal, 0 = immune, 2 = takes
        // double. The array belongs to the wave plan and is shared by every agent
        // of its group; nothing ever writes to it.
        public readonly float[] Resist;

        // While SlowTicks > 0 the agent moves at MoveSpeed * SlowFactor. Counted
        // down every tick the agent is alive, walking or digging.
        public int SlowTicks;
        public float SlowFactor = 1f;

        public readonly float KillReward;
        public readonly float WallBreakReward;

        public bool IsAlive = true;

        // How it ended (only meaningful once IsAlive is false); see the class comment.
        public bool Leaked;
        public bool Killed;

        // The wave that spawned this agent (1-based). Set by Simulation at spawn.
        public int WaveIndex;

        // Cheapest BestCost this agent has stood on so far this wave (SimNode.Infinity
        // until it takes its first live step). Feeds WaveOutcome.PressureShare.
        public int MinCostSeen = SimNode.Infinity;

        public AgentState(int id, Vec2f position, float moveSpeed, float hp, float[] resist, float digRate, float killReward, float wallBreakReward)
        {
            if (resist == null || resist.Length != DamageTypes.Count)
                throw new ArgumentException("An agent needs one resistance per damage type (" + DamageTypes.Count + ").", nameof(resist));

            Id = id;
            Position = position;
            MoveSpeed = moveSpeed;
            Hp = hp;
            MaxHp = hp;
            Resist = resist;
            DigRate = digRate;
            KillReward = killReward;
            WallBreakReward = wallBreakReward;
        }

        private AgentState(AgentState source)
        {
            Id = source.Id;
            Position = source.Position;
            MoveSpeed = source.MoveSpeed;
            DigRate = source.DigRate;
            Hp = source.Hp;
            MaxHp = source.MaxHp;
            Resist = source.Resist; // shared, read-only
            SlowTicks = source.SlowTicks;
            SlowFactor = source.SlowFactor;
            KillReward = source.KillReward;
            WallBreakReward = source.WallBreakReward;
            IsAlive = source.IsAlive;
            Leaked = source.Leaked;
            Killed = source.Killed;
            WaveIndex = source.WaveIndex;
            MinCostSeen = source.MinCostSeen;
        }

        internal AgentState Clone() { return new AgentState(this); }

        public override string ToString()
        {
            string end = IsAlive ? "alive" : Leaked ? "leaked" : Killed ? "killed" : "removed";
            return "Agent#" + Id + " " + end + " wave=" + WaveIndex + " hp=" + Hp + "/" + MaxHp + " pos=" + Position;
        }
    }
}
