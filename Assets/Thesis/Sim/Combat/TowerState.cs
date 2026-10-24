using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // One tower on the board. Its id is its index in SimState.Towers and is never
    // reused: a destroyed tower stays in the list with IsAlive false, so replays,
    // telemetry and views can refer to a tower by id for the whole run.
    //
    // What a tower DOES is no longer its TowerDef alone. A tower has a level (1
    // common, 2 rare, 3 epic) and gear, and the numbers the tower pass uses are
    // worked out from all three by Recompute whenever one of them changes, never per
    // tick. Def, Level and Gear are the state; the "effective" fields below are a
    // cache of them and are not hashed.
    public sealed class TowerState
    {
        public const int MaxLevel = 3;
        public const int MaxGearSlots = 2;
        public const int NoGear = -1;

        public readonly int Id;
        public readonly TowerDef Def;
        // Where it stands. Only a move changes it (Placement.TryMoveTower).
        public TileCoord Tile { get; private set; }
        public readonly int PlacedTick;

        // Everything the player has paid into this tower: its card, its upgrades and
        // the gear bought onto it. A sale refunds a share of this. Gear that is moved
        // takes its price with it.
        public int PricePaid;

        // 1 common, 2 rare, 3 epic.
        public int Level = 1;

        // Indices into the gear roster, NoGear for an empty slot. A common tower has
        // one slot, a rare or epic one has two (GearSlots).
        public readonly int[] Gear = { NoGear, NoGear };

        // World XZ of the tile centre.
        public Vec2f Position { get; private set; }

        // --- effective numbers (Recompute) ---
        public float Damage;
        public int FireIntervalTicks;
        public float RangeTiles;
        public float RangeSquared;      // in world units, squared
        public float SplashSquared;
        public int SlowTicks;
        public float SlowFactor = 1f;
        public bool CanHitFlying;
        public int Targets = 1;         // enemies one shot is fired at
        public bool IgnoresResist;      // gear: anti-armor
        public bool Lure;               // gear: walkers path to it
        public bool Beacon;             // gear: nearby collectors pay more
        public float PowerRadius;       // a power tower: its reach, in tiles
        public int PowerCapacity;       // and how many towers it powers

        // Ticks until it may fire again. 0 = ready.
        public int Cooldown;

        // False once it is off the board: an enemy chewed through it, or the player
        // sold it. Sold says which.
        public bool IsAlive = true;
        public bool Sold;

        // Off the board because it was merged into another tower of its type
        // (TowerCombine). Neither sold nor destroyed.
        public bool Combined;

        // The one upgrade the player chose for it (TowerUpgradeChoices), or None.
        public int Upgrade = TowerUpgradeChoices.None;

        // The second one, with SimConfig.UpgradesPerTower 2. Taken after Upgrade.
        public int Upgrade2 = TowerUpgradeChoices.None;

        public int UpgradeCount => (TowerUpgradeChoices.IsValid(Upgrade) ? 1 : 0) + (TowerUpgradeChoices.IsValid(Upgrade2) ? 1 : 0);

        // 1 common, 2 rare, 3 epic: the card's level and every upgrade since, at most epic.
        // Since 2026-10-14 cards are all common and a tower grows rarer by being upgraded.
        public int Rarity => Math.Min(MaxLevel, Level + UpgradeCount);

        // Which enemy in range it shoots. The type's own order until the player changes it.
        public TargetingMode Mode;

        // Owned but not on the board (TowerBench): it stands on no tile and does not shoot.
        public bool OnBench;

        // Has power and fires (SimConfig.PowerRule; Simulation.RecomputePower). Always true
        // without the rule.
        public bool Powered = true;

        // Lifetime totals. DamageDealt is EFFECTIVE damage: hit points actually
        // removed, never the overkill on a dying enemy.
        public float DamageDealt;
        public int Shots;
        public int Kills;

        private readonly float tileSize;

        public TowerState(int id, TowerDef def, TileCoord tile, Vec2f position, float tileSize, int placedTick, int pricePaid = 0, int level = 1)
        {
            Id = id;
            Def = def;
            Tile = tile;
            Position = position;
            PlacedTick = placedTick;
            PricePaid = pricePaid;
            Level = level;
            Mode = def.Mode;
            this.tileSize = tileSize;
            Recompute(null);
        }

        private TowerState(TowerState source)
        {
            Id = source.Id;
            Def = source.Def; // shared: a TowerDef is never changed during a run
            Tile = source.Tile;
            Position = source.Position;
            PlacedTick = source.PlacedTick;
            PricePaid = source.PricePaid;
            Level = source.Level;
            Gear[0] = source.Gear[0];
            Gear[1] = source.Gear[1];
            tileSize = source.tileSize;
            Damage = source.Damage;
            FireIntervalTicks = source.FireIntervalTicks;
            RangeTiles = source.RangeTiles;
            RangeSquared = source.RangeSquared;
            SplashSquared = source.SplashSquared;
            SlowTicks = source.SlowTicks;
            SlowFactor = source.SlowFactor;
            CanHitFlying = source.CanHitFlying;
            Targets = source.Targets;
            IgnoresResist = source.IgnoresResist;
            Lure = source.Lure;
            Beacon = source.Beacon;
            PowerRadius = source.PowerRadius;
            PowerCapacity = source.PowerCapacity;
            Cooldown = source.Cooldown;
            IsAlive = source.IsAlive;
            Sold = source.Sold;
            Combined = source.Combined;
            OnBench = source.OnBench;
            Powered = source.Powered;
            Upgrade = source.Upgrade;
            Upgrade2 = source.Upgrade2;
            Mode = source.Mode;
            DamageDealt = source.DamageDealt;
            Shots = source.Shots;
            Kills = source.Kills;
        }

        internal TowerState Clone() { return new TowerState(this); }

        internal void MoveTo(TileCoord tile, Vec2f position)
        {
            Tile = tile;
            Position = position;
        }

        // How many gear slots this tower has at its level.
        public int GearSlots => Rarity >= 2 ? MaxGearSlots : 1;

        // The first empty slot the tower may use, or -1.
        public int FreeGearSlot()
        {
            for (int i = 0; i < GearSlots; i++) if (Gear[i] == NoGear) return i;
            return -1;
        }

        // Works the effective numbers out again from Def, Level and Gear. `gear` is
        // the gear roster; null is allowed while the tower has none.
        //
        // Order: the definition, then each level above the first in turn, then each
        // piece of gear in slot order. Every float step is one operation rounded to
        // float at once (ARCHITECTURE.md 9, rule 3), so both runtimes get the same bits.
        public void Recompute(GearDef[] gear)
        {
            float damage = Def.Damage;
            float interval = Def.FireIntervalTicks;
            float range = Def.RangeTiles;
            float splash = Def.SplashRadiusTiles;
            int slowTicks = Def.SlowTicks;
            float slowFactor = Def.SlowFactor;
            bool flying = Def.CanHitFlying;
            bool ignoresResist = false, lure = false, beacon = false;
            int targets = Def.Targets;
            float powerRadius = Def.PowerRadius;
            int powerCapacity = Def.PowerCapacity;

            // A level or an upgrade: the numbers, then what it adds besides them.
            void Apply(TowerLevelMod mod)
            {
                damage = (float)(damage * mod.DamageMul);
                interval = (float)(interval * mod.IntervalMul);
                range = (float)(range + mod.RangeAdd);
                splash = (float)(splash + mod.SplashAdd);
                slowTicks = (int)(float)(slowTicks * mod.SlowTicksMul);
                targets += mod.ExtraTargets;
                if (mod.GrantsFlying) flying = true;
                if (mod.IgnoresResist) ignoresResist = true;
                slowTicks += mod.SlowTicksAdd;
                if (mod.SlowFactorTo < slowFactor) slowFactor = mod.SlowFactorTo;
                powerRadius = (float)(powerRadius + mod.PowerRadiusAdd);
                powerCapacity += mod.PowerCapacityAdd;
            }

            for (int level = 2; level <= Level; level++) Apply(Def.LevelMod(level));

            // The upgrades the player chose (this tower type's own), after the levels and before the gear.
            TowerLevelMod[] choices = TowerUpgradeChoices.For(Def);
            for (int u = 0; u < 2; u++)
            {
                int choice = u == 0 ? Upgrade : Upgrade2;
                if (choice >= 0 && choice < choices.Length) Apply(choices[choice]);
            }

            for (int slot = 0; slot < MaxGearSlots; slot++)
            {
                if (Gear[slot] == NoGear) continue;
                if (gear == null) throw new InvalidOperationException("[Sim] Tower #" + Id + " has gear but no gear roster was given.");
                GearDef g = gear[Gear[slot]];
                damage = (float)(damage * g.DamageMul);
                interval = (float)(interval * g.IntervalMul);
                range = (float)(range + g.RangeAdd);
                splash = (float)(splash + g.SplashAdd);
                if (g.SlowTicks > slowTicks) slowTicks = g.SlowTicks;
                if (g.SlowTicks > 0 && g.SlowFactor < slowFactor) slowFactor = g.SlowFactor;
                if (g.GrantsFlying) flying = true;
                if (g.IgnoresResist) ignoresResist = true;
                if (g.Lure) lure = true;
                if (g.Beacon) beacon = true;
            }

            Damage = damage;
            FireIntervalTicks = Math.Max(1, (int)interval);
            RangeTiles = range;
            float reach = (float)(range * tileSize);
            RangeSquared = (float)(reach * reach);
            float blast = (float)(splash * tileSize);
            SplashSquared = (float)(blast * blast);
            SlowTicks = slowTicks;
            SlowFactor = slowFactor;
            CanHitFlying = flying;
            IgnoresResist = ignoresResist;
            Lure = lure;
            Beacon = beacon;
            PowerRadius = powerRadius;
            PowerCapacity = powerCapacity;
            Targets = Math.Max(1, targets);
        }

        public override string ToString()
        {
            return "Tower#" + Id + " " + Def.Id + " L" + Level + " @" + Tile + (IsAlive ? "" : Sold ? " (sold)" : Combined ? " (combined)" : " (destroyed)") + " shots=" + Shots + " kills=" + Kills + " dmg=" + DamageDealt;
        }
    }
}
