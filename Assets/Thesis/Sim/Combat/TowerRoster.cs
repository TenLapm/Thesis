namespace Thesis.Sim
{
    // The tower types the game ships with.
    //
    // PLACEHOLDER (WP-C1). Three towers, enough to exercise every mechanic the
    // combat core has: a plain single-target tower, a splash tower and a slowing
    // tower, one per damage type. The numbers are not balanced. The real roster is
    // spec gap S12 and arrives in WP-C5 as TowerDef assets exported from Unity,
    // the same way maps and wall shapes are; this class then goes away.
    //
    // ORDER MATTERS: a PlaceTower command names a tower by its index here, so
    // reordering the roster changes what every recorded command means.
    public static class TowerRoster
    {
        public static TowerDef[] Placeholder()
        {
            return new[]
            {
                new TowerDef
                {
                    Id = "archer",
                    Cost = 10,
                    DamageType = DamageType.Physical,
                    Damage = 4f,
                    RangeTiles = 3.5f,
                    FireIntervalTicks = 25,
                    CanHitFlying = true,
                },
                new TowerDef
                {
                    Id = "cannon",
                    Cost = 18,
                    DamageType = DamageType.Fire,
                    Damage = 3f,
                    RangeTiles = 2.5f,
                    FireIntervalTicks = 60,
                    SplashRadiusTiles = 0.75f,
                },
                new TowerDef
                {
                    Id = "frost",
                    Cost = 12,
                    DamageType = DamageType.Frost,
                    Damage = 1f,
                    RangeTiles = 2.5f,
                    FireIntervalTicks = 30,
                    SlowTicks = 75,
                    SlowFactor = 0.5f,
                },
            };
        }
    }
}
