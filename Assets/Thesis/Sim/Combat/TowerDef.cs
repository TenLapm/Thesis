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
    // What one level above the first adds to a tower (2 = rare, 3 = epic). Each level
    // is applied on top of the one below it (TowerState.Recompute). Label is the one
    // line the player reads: a level should change what a tower DOES, not only a number.
    public sealed class TowerLevelMod
    {
        public string Label;
        public float DamageMul = 1f;
        public float IntervalMul = 1f;   // below 1 = fires faster
        public float RangeAdd;           // tiles
        public float SplashAdd;          // tiles
        public float SlowTicksMul = 1f;
        public int ExtraTargets;         // more enemies shot at per shot

        // 2026-10-24: what an upgrade can do besides numbers. Id names the choice in the
        // build log ("archer+volley"); the rest change what the tower is.
        public string Id;
        public bool GrantsFlying;        // hits flyers from now on
        public bool IgnoresResist;       // a resistance (or an immunity) counts as none
        public int SlowTicksAdd;         // every enemy hit is slowed this many ticks more...
        public float SlowFactorTo = 1f;  // ...to this share of its speed, if lower than before
        public float PowerRadiusAdd;     // a power tower reaches this many tiles further
        public int PowerCapacityAdd;     // and powers this many towers more

        // 2026-10-27: paths. Every upgrade of a tower with paths belongs to one (Path 1..3)
        // and is its first or second step (Step 1, 2): the first upgrade chooses a path, the
        // second can only be that path's second step (TowerUpgradeChoices.Allowed). Path 0:
        // no paths, any two different choices, as before.
        public int Path;
        public int Step;
        public string PathName;          // the path's name, shown on its first step

        // What a path can make a tower do besides numbers (2026-10-27).
        public float DamageAdd;          // flat damage, after DamageMul
        public bool Lure;                // enemies on foot within GearDef.LureRadius go for it; 3x tough
        public bool Beacon;              // collectors within GearDef.BeaconRange pay one more ore
        public int CritEvery;            // every this-many-th shot...
        public float CritMul = 1f;       // ...does this many times the damage
        public float ExecuteBelow;       // a hit that leaves an enemy under this share of its hit points kills it
        public float FlyingDamageMul = 1f; // damage against flyers
        public float BurnDps;            // a hit sets the enemy burning: fire damage a second...
        public int BurnTicks;            // ...for this many ticks
        public float ExposeMul = 1f;     // a hit makes the enemy take this many times the damage...
        public int ExposeTicks;          // ...from every tower, for this many ticks
    }

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

        // How many enemies one shot is fired at (a chain tower: three). Each gets the
        // full hit; with fewer enemies in range the spare ones are not fired.
        public int Targets = 1;

        // The tower as terrain.
        public int DigCost = 20;
        public float TowerHealth = 8f;

        // Relative chance of being offered in the shop (WP-C3).
        public float ShopWeight = 1f;

        // The research tier that unlocks it in the build menu (SimConfig.BuildMenu): 1, 2 or 3.
        public int Tier = 1;

        // The upgrades this tower may be given (2026-10-24: each tower its own, so an upgrade
        // is a choice about what the tower becomes). Empty: the four of TowerUpgradeChoices.
        public TowerLevelMod[] Upgrades;

        // A power tower (2026-10-24, SimConfig.PowerRule): it powers up to PowerCapacity
        // towers within PowerRadius tiles, as the core and a collector do. It has power itself.
        public float PowerRadius;
        public int PowerCapacity;

        // What level 2 (rare) and level 3 (epic) add, in that order. A tower type that
        // gives none still has three levels: each is then half as much damage again.
        public TowerLevelMod[] Levels;

        private static readonly TowerLevelMod PlainLevel = new TowerLevelMod { Label = "+50% damage", DamageMul = 1.5f };

        // The modifier of `level` (2 or 3).
        public TowerLevelMod LevelMod(int level)
        {
            int i = level - 2;
            return Levels != null && i >= 0 && i < Levels.Length && Levels[i] != null ? Levels[i] : PlainLevel;
        }

        public void Validate()
        {
            string who = "TowerDef '" + Id + "': ";
            if (string.IsNullOrEmpty(Id)) throw new InvalidOperationException("A TowerDef needs an Id.");
            if (Cost < 0) throw new InvalidOperationException(who + "Cost " + Cost + " is negative.");
            if (!(Damage >= 0f) || float.IsInfinity(Damage)) throw new InvalidOperationException(who + "Damage " + Damage + " must be a number >= 0.");
            if (!(RangeTiles >= 0f) || float.IsInfinity(RangeTiles)) throw new InvalidOperationException(who + "RangeTiles " + RangeTiles + " must be a number >= 0.");
            if (FireIntervalTicks < 1) throw new InvalidOperationException(who + "FireIntervalTicks " + FireIntervalTicks + " must be at least 1.");
            if (!(SplashRadiusTiles >= 0f) || float.IsInfinity(SplashRadiusTiles)) throw new InvalidOperationException(who + "SplashRadiusTiles " + SplashRadiusTiles + " must be a number >= 0.");
            if (Targets < 1 || Targets > 8) throw new InvalidOperationException(who + "Targets " + Targets + " must be between 1 and 8.");
            if (SlowTicks < 0) throw new InvalidOperationException(who + "SlowTicks " + SlowTicks + " is negative.");
            if (!(SlowFactor > 0f) || SlowFactor > 1f) throw new InvalidOperationException(who + "SlowFactor " + SlowFactor + " must be in (0, 1]; 1 means no slow.");
            if (DigCost < 2) throw new InvalidOperationException(who + "DigCost " + DigCost + " must be at least 2 (a tower tile must cost more than open ground).");
            if (!(TowerHealth > 0f)) throw new InvalidOperationException(who + "TowerHealth " + TowerHealth + " must be positive.");
            if (PowerCapacity < 0 || !(PowerRadius >= 0f)) throw new InvalidOperationException(who + "PowerCapacity and PowerRadius must not be negative.");
            if (!(ShopWeight >= 0f)) throw new InvalidOperationException(who + "ShopWeight " + ShopWeight + " must be >= 0.");
        }

        public override string ToString() { return Id + " (" + DamageType + " " + Damage + " every " + FireIntervalTicks + "t, range " + RangeTiles + ", cost " + Cost + ")"; }
    }
}
