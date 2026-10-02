using Thesis.Core;

namespace Thesis.Sim
{
    // One tower on the board. Its id is its index in SimState.Towers and is never
    // reused: a destroyed tower stays in the list with IsAlive false, so replays,
    // telemetry and views can refer to a tower by id for the whole run.
    public sealed class TowerState
    {
        public readonly int Id;
        public readonly TowerDef Def;
        public readonly TileCoord Tile;
        public readonly int PlacedTick;

        // World XZ of the tile centre, and the tower's reach squared in world units.
        // Worked out once when the tower is placed, so the per-tick range check is
        // two subtractions, two multiplications and a compare.
        public readonly Vec2f Position;
        public readonly float RangeSquared;
        public readonly float SplashSquared;

        // Ticks until it may fire again. 0 = ready.
        public int Cooldown;

        // False once an enemy has chewed through it (or, from WP-C3, it was sold).
        public bool IsAlive = true;

        // Lifetime totals. DamageDealt is EFFECTIVE damage: hit points actually
        // removed, never the overkill on a dying enemy.
        public float DamageDealt;
        public int Shots;
        public int Kills;

        public TowerState(int id, TowerDef def, TileCoord tile, Vec2f position, float tileSize, int placedTick)
        {
            Id = id;
            Def = def;
            Tile = tile;
            Position = position;
            PlacedTick = placedTick;

            // Explicit casts round every float intermediate (ARCHITECTURE.md §9 rule 3).
            float range = (float)(def.RangeTiles * tileSize);
            RangeSquared = (float)(range * range);
            float splash = (float)(def.SplashRadiusTiles * tileSize);
            SplashSquared = (float)(splash * splash);
        }

        private TowerState(TowerState source)
        {
            Id = source.Id;
            Def = source.Def; // shared: a TowerDef is never changed during a run
            Tile = source.Tile;
            Position = source.Position;
            PlacedTick = source.PlacedTick;
            RangeSquared = source.RangeSquared;
            SplashSquared = source.SplashSquared;
            Cooldown = source.Cooldown;
            IsAlive = source.IsAlive;
            DamageDealt = source.DamageDealt;
            Shots = source.Shots;
            Kills = source.Kills;
        }

        internal TowerState Clone() { return new TowerState(this); }

        public override string ToString()
        {
            return "Tower#" + Id + " " + Def.Id + " @" + Tile + (IsAlive ? "" : " (destroyed)") + " shots=" + Shots + " kills=" + Kills + " dmg=" + DamageDealt;
        }
    }
}
