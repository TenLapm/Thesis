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
        public bool TargetsCollectors;
        public bool Siege;

        // One damage multiplier per DamageType; null means 1 for every type.
        public float[] Resist;

        // A group of this type sized as `share` of the wave (1 = the whole wave).
        // Every stat is one product rounded to float at once (ARCHITECTURE.md 9, rule 3).
        public AgentGroup Apply(AgentGroup basic, float share)
        {
            AgentGroup g = basic.Copy();
            g.Archetype = Id;
            g.Movement = Movement;
            g.TargetsCollectors = TargetsCollectors;
            g.Siege = Siege;
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

        // Fewer, weaker, and over everything. Weaker and no faster than a walker since
        // 2026-10-14: they skip the whole maze and only some towers reach them, which
        // at half the hit points and 1.2 times the speed was too much (the student's play).
        public static EnemyArchetype Flyer()
        {
            return new EnemyArchetype { Id = FlyerId, Movement = MovementClass.Flying, CountFactor = 0.5f, HpFactor = 0.35f, SpeedFactor = 1f, DigRateFactor = 0f };
        }

        public static EnemyArchetype[] All() { return new[] { Basic(), Swarm(), Sapper(), Flyer() }; }

        // --- more of them (2026-10-14). Few KINDS of behaviour, many variants: each of
        // these is a walker or a flyer with other numbers, or one that shrugs off a
        // damage type, which is what makes a mix of tower types worth having. First
        // guesses, like the four above. ---
        public const string BruteId = "brute";
        public const string RunnerId = "runner";
        public const string ArmoredId = "armored";
        public const string EmberId = "ember";
        public const string FrostbornId = "frostborn";
        public const string BatsId = "bats";

        // A few, each four times as tough, slow.
        public static EnemyArchetype Brute() { return new EnemyArchetype { Id = BruteId, CountFactor = 0.25f, HpFactor = 4f, SpeedFactor = 0.7f, SpawnIntervalFactor = 3f }; }

        // Fast and frail: through a short kill zone before it has fired twice.
        public static EnemyArchetype Runner() { return new EnemyArchetype { Id = RunnerId, CountFactor = 0.8f, HpFactor = 0.6f, SpeedFactor = 1.7f }; }

        // Takes 35% of physical damage: arrows and bolts glance off.
        public static EnemyArchetype Armored() { return new EnemyArchetype { Id = ArmoredId, CountFactor = 0.7f, HpFactor = 1.2f, SpeedFactor = 0.9f, Resist = Resists(0.35f, 1f, 1f) }; }

        // Takes 25% of fire damage.
        public static EnemyArchetype Ember() { return new EnemyArchetype { Id = EmberId, CountFactor = 0.8f, HpFactor = 1f, SpeedFactor = 1.1f, Resist = Resists(1f, 0.25f, 1f) }; }

        // Immune to frost: no frost damage and no slow from it.
        public static EnemyArchetype Frostborn() { return new EnemyArchetype { Id = FrostbornId, CountFactor = 0.8f, HpFactor = 1.1f, SpeedFactor = 1f, Resist = Resists(1f, 1f, 0f) }; }

        // Many small flyers close together.
        public static EnemyArchetype Bats() { return new EnemyArchetype { Id = BatsId, Movement = MovementClass.Flying, CountFactor = 1f, HpFactor = 0.15f, SpeedFactor = 1.2f, SpawnIntervalFactor = 0.5f, DigRateFactor = 0f }; }

        public static EnemyArchetype[] Everything() { return new[] { Basic(), Swarm(), Sapper(), Flyer(), Brute(), Runner(), Armored(), Ember(), Bats(), Raider() }; } // frostborn out since 2026-10-24: too strong

        // 2026-10-14: goes for the player's collectors, not the core, while any stand.
        public const string RaiderId = "raider";
        public static EnemyArchetype Raider() { return new EnemyArchetype { Id = RaiderId, CountFactor = 0.5f, HpFactor = 1f, SpeedFactor = 1.2f, DigRateFactor = 1.5f, TargetsCollectors = true }; }

        // 2026-10-26: slow and tough; stops outside most towers' reach and knocks them out
        // (SimConfig.SiegeRangeTiles). Not in Everything(): only the rough director sends it,
        // a few at a time, and it says how many itself.
        public const string SiegeId = "siege";
        public static EnemyArchetype Siege() { return new EnemyArchetype { Id = SiegeId, CountFactor = 1f, HpFactor = 6f, SpeedFactor = 0.6f, SpawnIntervalFactor = 4f, Siege = true }; }

        // One multiplier per DamageType, in the enum's order: physical, fire, frost.
        private static float[] Resists(float physical, float fire, float frost)
        {
            float[] r = DamageTypes.AllOnes();
            r[(int)DamageType.Physical] = physical;
            r[(int)DamageType.Fire] = fire;
            r[(int)DamageType.Frost] = frost;
            return r;
        }
    }
}
