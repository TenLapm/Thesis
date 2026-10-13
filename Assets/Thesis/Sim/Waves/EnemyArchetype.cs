using System;

namespace Thesis.Sim
{
    // One enemy type (WP-C5, spec gap S12): a movement class and a fixed set of
    // numbers, each a multiple of the plain walker of the same wave. A planner makes a
    // group of a type by applying it to that wave's escalation enemy, so every type
    // grows with the wave number exactly as the baseline does.
    //
    // CountFactor is what keeps the types roughly comparable before there is a price
    // list: a type that is tougher or more dangerous comes in smaller numbers. The
    // shared threat cost table (WP9, I2) replaces these rough factors with prices.
    public sealed class EnemyArchetype
    {
        public string Id;
        public MovementClass Movement = MovementClass.Ground;
        public float CountFactor = 1f;
        public float HpFactor = 1f;
        public float SpeedFactor = 1f;
        public float DigRateFactor = 1f;
        public float SpawnIntervalFactor = 1f;

        // One damage multiplier per DamageType; null means 1 for every type.
        public float[] Resist;

        // A group of this type sized as `share` of the wave (1 = the whole wave).
        // Every stat is one product rounded to float at once (ARCHITECTURE.md 9, rule 3).
        public AgentGroup Apply(AgentGroup basic, float share)
        {
            AgentGroup g = basic.Copy();
            g.Archetype = Id;
            g.Movement = Movement;
            float count = (float)((float)(basic.Count * share) * CountFactor);
            g.Count = Math.Max(1, (int)count);
            g.Hp = (float)(basic.Hp * HpFactor);
            g.MoveSpeed = (float)(basic.MoveSpeed * SpeedFactor);
            g.DigRate = Movement == MovementClass.Flying ? 0f : (float)(basic.DigRate * DigRateFactor); // a flyer never digs
            g.SpawnIntervalTicks = Math.Max(1, (int)(float)(basic.SpawnIntervalTicks * SpawnIntervalFactor));
            if (Resist != null) g.Resist = (float[])Resist.Clone();
            return g;
        }
    }

    // The game's enemy types, version 1. The numbers passed the sanity check of
    // Results/2026-10-12_balance-v1 and are not balanced beyond that; the balance
    // pass of the second semester tunes them. No type uses resistances yet: which
    // waves do is the proposal's 6.3 (spec gap S10, WP9).
    public static class EnemyArchetypes
    {
        public const string BasicId = "basic";
        public const string SwarmId = "swarm";
        public const string SapperId = "sapper";
        public const string FlyerId = "flyer";

        // The plain walker: the escalation enemy itself.
        public static EnemyArchetype Basic() { return new EnemyArchetype { Id = BasicId }; }

        // Many, weak, fast and close together: what a single kill zone cannot keep up with.
        public static EnemyArchetype Swarm()
        {
            return new EnemyArchetype { Id = SwarmId, CountFactor = 2f, HpFactor = 0.4f, SpeedFactor = 1.3f, SpawnIntervalFactor = 0.5f };
        }

        // Fewer, tougher, slower, and through a wall four times as fast.
        public static EnemyArchetype Sapper()
        {
            return new EnemyArchetype { Id = SapperId, Movement = MovementClass.Sapper, CountFactor = 0.5f, HpFactor = 1.5f, SpeedFactor = 0.8f, DigRateFactor = 4f };
        }

        // Fewer, weaker, faster, and over everything.
        public static EnemyArchetype Flyer()
        {
            return new EnemyArchetype { Id = FlyerId, Movement = MovementClass.Flying, CountFactor = 0.5f, HpFactor = 0.5f, SpeedFactor = 1.2f, DigRateFactor = 0f };
        }

        public static EnemyArchetype[] All() { return new[] { Basic(), Swarm(), Sapper(), Flyer() }; }
    }
}
