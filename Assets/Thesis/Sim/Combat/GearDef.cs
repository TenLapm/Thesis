namespace Thesis.Sim
{
    // One piece of gear: something a tower wears that changes what it does. Gear
    // comes from the shop as a card and is bought ONTO a tower; between waves it can
    // be moved from one tower to another for nothing, so it rewards re-arranging a
    // defence and does not lock a player into the tower that happens to wear it.
    //
    // Pure data, like a TowerDef. How it changes a tower is TowerState.Recompute.
    public sealed class GearDef
    {
        public string Id;
        public string Label;      // one short line for the player
        public int Price = 8;

        // The research tier that lets it be crafted (SimConfig.BuildMenu).
        public int Tier = 1;

        public float DamageMul = 1f;
        public float DamageAdd;          // flat damage a hit (2026-10-27)
        // Flat damage a SECOND (2026-10-28, the student: +3 a hit made the brazier, which hits
        // ten times a second, far too strong): turned into damage a hit by the tower's own
        // rate, so it is worth the same to every tower.
        public float DpsAdd;
        public bool FlatOnly;            // sold only with SimConfig.FlatGear, so older shops stay as they were
        public float IntervalMul = 1f;   // below 1 = fires faster
        public float RangeAdd;           // tiles
        public float SplashAdd;          // tiles; gives a single-target tower a small splash
        public int SlowTicks;            // above 0: every enemy hit is slowed for this long...
        public float SlowFactor = 1f;    // ...to this share of its speed
        public bool GrantsFlying;        // the tower can hit flyers

        // Not worn: dropped on a tower, it takes that tower's gear off into the inventory
        // and is used up (2026-10-14, in place of a free "take gear off" tool).
        public bool Extracts;

        // 2026-10-20. Beacon: a collector within BeaconRange tiles of the tower pays one
        // more ore after a wave both stood through (once per collector, however many
        // beacons). IgnoresResist: the tower's hits ignore an enemy's resistances (a
        // multiplier below 1 counts as 1). Lure: any enemy on foot within LureRadius tiles
        // of the tower (by the way it would walk) goes for it and chews it, and the tower is
        // three times as tough (the lure field, SimNode.LureCost).
        public bool Beacon;
        public bool IgnoresResist;
        public bool Lure;

        public const float BeaconRange = 3f;
        public const int LureRadius = 4;
        public const float LureHealthFactor = 3f;

        // Gear that does something besides numbers: never a plain stat boost, even when
        // it changes a number on the way (anti-armor fires slower).
        public bool HasAbility => Extracts || Beacon || IgnoresResist || Lure || GrantsFlying || SlowTicks > 0;

        // A plain number on the tower, which since 2026-10-14 is the upgrade's job; the
        // shops leave these out unless SimConfig.StatGear.
        public bool IsStatBoost => !HasAbility && (DamageMul != 1f || IntervalMul != 1f || RangeAdd != 0f || SplashAdd != 0f || DamageAdd != 0f || DpsAdd != 0f);

        // Flat damage or flat range and nothing else (2026-10-27, the student: gear is plain
        // numbers again, a tower's character is its upgrade path). Never splash.
        public bool IsFlat => !HasAbility && DamageMul == 1f && IntervalMul == 1f && SplashAdd == 0f && (DamageAdd != 0f || DpsAdd != 0f || RangeAdd != 0f);

        // Whether a shop may offer it under this config. Without one: everything but the extractor.
        public bool OnSale(SimConfig config)
        {
            if (Extracts) return config != null && config.ExtractorGear;
            if (config != null && config.FlatGear) return IsFlat;
            if (FlatOnly) return false;
            if (Beacon || IgnoresResist || Lure) return config != null && config.AbilityGear;
            if (IsStatBoost) return config == null || config.StatGear;
            return true;
        }
    }

    // The game's gear, version 1 (2026-10-12, with the student's "gear and rarity"
    // idea). Five plain pieces, one per thing a tower can be short of. In code, like
    // the enemy types: a number here is a rule, and a replay recorded with other
    // numbers stops matching at the first shot.
    //
    // ORDER MATTERS: a card and a tower name a piece by its index here.
    public static class GearRoster
    {
        public static GearDef[] Default()
        {
            return new[]
            {
                new GearDef { Id = "scope", Label = "+1 range", RangeAdd = 1f },
                new GearDef { Id = "whetstone", Label = "+35% damage", DamageMul = 1.35f },
                new GearDef { Id = "sky-sight", Label = "can hit flyers", GrantsFlying = true, Tier = 2 },
                new GearDef { Id = "powder", Label = "+0.5 splash", SplashAdd = 0.5f },
                new GearDef { Id = "chill", Label = "slows what it hits", SlowTicks = 50, SlowFactor = 0.7f },
                new GearDef { Id = "extractor", Label = "takes a tower's gear off", Extracts = true, Price = 4 },
                new GearDef { Id = "beacon", Label = "+1 ore from collectors within 3 tiles", Beacon = true, Price = 10 },
                new GearDef { Id = "anti-armor", Label = "ignores resistance; 20% slower", IgnoresResist = true, IntervalMul = 1.25f, Price = 8, Tier = 2 },
                new GearDef { Id = "lure", Label = "enemies within 4 go for it; 3x tough", Lure = true, Price = 8, Tier = 2 },
                // 2026-10-27: flat numbers, the only gear with SimConfig.FlatGear.
                new GearDef { Id = "edge", Label = "+2 damage a second", DpsAdd = 2f, Price = 8, FlatOnly = true },
                new GearDef { Id = "keen-edge", Label = "+6 damage a second", DpsAdd = 6f, Price = 20, Tier = 2, FlatOnly = true },
                new GearDef { Id = "far-scope", Label = "+2 range", RangeAdd = 2f, Price = 18, Tier = 2, FlatOnly = true },
            };
        }
    }
}
