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

        public float DamageMul = 1f;
        public float IntervalMul = 1f;   // below 1 = fires faster
        public float RangeAdd;           // tiles
        public float SplashAdd;          // tiles; gives a single-target tower a small splash
        public int SlowTicks;            // above 0: every enemy hit is slowed for this long...
        public float SlowFactor = 1f;    // ...to this share of its speed
        public bool GrantsFlying;        // the tower can hit flyers
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
                new GearDef { Id = "sky-sight", Label = "can hit flyers", GrantsFlying = true },
                new GearDef { Id = "powder", Label = "+0.5 splash", SplashAdd = 0.5f },
                new GearDef { Id = "chill", Label = "slows what it hits", SlowTicks = 50, SlowFactor = 0.7f },
            };
        }
    }
}
