using System;

namespace Thesis.Sim
{
    // The data of one tower type: what it costs, how it shoots, and what it is as
    // terrain. Pure data, shared by every tower of that type and never changed
    // during a run (a TowerState holds the per-tower part).
    //
    // A tower stands on the grid exactly like a wall tile: expensive, diggable
    // terrain (CLAUDE.md §2). DigCost and TowerHealth are that tile's TerrainCost
    // and WallHealth, so enemies route around a tower or chew through it by the
    // same cost model as walls, and chewing through destroys the tower.
    public sealed class TowerDef
    {
        public string Id;
        public int Cost = 10;

        public DamageType DamageType = DamageType.Physical;
        public float Damage = 1f;

        // Reach in TILES, measured from the centre of the tower's tile to the
        // enemy's position. 3 means "three tile-widths away".
        public float RangeTiles = 3f;

        // Ticks between shots (50 ticks = 1 second). A tower with nothing in range
        // keeps its shot and fires on the first tick a target appears.
        public int FireIntervalTicks = 25;
        public TargetingMode Mode = TargetingMode.First;

        // > 0: the shot also hits every other enemy within this many tiles of the target.
        public float SplashRadiusTiles;

        // SlowTicks > 0: every enemy the shot hits moves at SlowFactor times its
        // speed for that many ticks. 1 = no slow.
        public int SlowTicks;
        public float SlowFactor = 1f;

        // Read by the Flying movement class (WP-C2); nothing flies yet.
        public bool CanHitFlying;

        // The tower as terrain.
        public int DigCost = 20;
        public float TowerHealth = 8f;

        // Relative chance of being offered in the shop (WP-C3).
        public float ShopWeight = 1f;

        public void Validate()
        {
            string who = "TowerDef '" + Id + "': ";
            if (string.IsNullOrEmpty(Id)) throw new InvalidOperationException("A TowerDef needs an Id.");
            if (Cost < 0) throw new InvalidOperationException(who + "Cost " + Cost + " is negative.");
            if (!(Damage >= 0f) || float.IsInfinity(Damage)) throw new InvalidOperationException(who + "Damage " + Damage + " must be a number >= 0.");
            if (!(RangeTiles >= 0f) || float.IsInfinity(RangeTiles)) throw new InvalidOperationException(who + "RangeTiles " + RangeTiles + " must be a number >= 0.");
            if (FireIntervalTicks < 1) throw new InvalidOperationException(who + "FireIntervalTicks " + FireIntervalTicks + " must be at least 1.");
            if (!(SplashRadiusTiles >= 0f) || float.IsInfinity(SplashRadiusTiles)) throw new InvalidOperationException(who + "SplashRadiusTiles " + SplashRadiusTiles + " must be a number >= 0.");
            if (SlowTicks < 0) throw new InvalidOperationException(who + "SlowTicks " + SlowTicks + " is negative.");
            if (!(SlowFactor > 0f) || SlowFactor > 1f) throw new InvalidOperationException(who + "SlowFactor " + SlowFactor + " must be in (0, 1]; 1 means no slow.");
            if (DigCost < 2) throw new InvalidOperationException(who + "DigCost " + DigCost + " must be at least 2 (a tower tile must cost more than open ground).");
            if (!(TowerHealth > 0f)) throw new InvalidOperationException(who + "TowerHealth " + TowerHealth + " must be positive.");
            if (!(ShopWeight >= 0f)) throw new InvalidOperationException(who + "ShopWeight " + ShopWeight + " must be >= 0.");
        }

        public override string ToString() { return Id + " (" + DamageType + " " + Damage + " every " + FireIntervalTicks + "t, range " + RangeTiles + ", cost " + Cost + ")"; }
    }
}
