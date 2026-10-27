using Thesis.Sim;
using UnityEngine;

// The inspector home of one tower type (WP-C5). The numbers are the simulation's
// TowerDef, field for field; this asset only holds them where they can be edited.
// SimHost.towerRoster lists the assets the shop sells from, IN ORDER (a shop offer
// names a tower by its place in that list), and Thesis/Export Towers writes the same
// list to Maps/Towers.json for headless runs.
//
// After changing a number here: export the towers, record the pinned replays again,
// and run the balance check (`thesis balance`).
[CreateAssetMenu(fileName = "NewTower", menuName = "TowerDefense/Tower")]
public class TowerDefAsset : ScriptableObject
{
    [Tooltip("The name the simulation, the replays and the telemetry know this tower by. Lower case, never changed once sessions are recorded.")]
    public string id;
    public int cost = 10;

    [Header("Attack")]
    public DamageType damageType = DamageType.Physical;
    public float damage = 1f;
    [Tooltip("Reach in tiles, from the centre of the tower's tile.")]
    public float rangeTiles = 3f;
    [Tooltip("Ticks between shots; 50 ticks are one second.")]
    public int fireIntervalTicks = 25;
    [Tooltip("Above 0: the shot also hits every enemy within this many tiles of its target.")]
    public float splashRadiusTiles;
    [Tooltip("Above 0: every enemy hit moves at slowFactor times its speed for this many ticks.")]
    public int slowTicks;
    [Range(0.05f, 1f)] public float slowFactor = 1f;
    public bool canHitFlying;
    [Tooltip("How many enemies one shot is fired at.")]
    [Range(1, 8)] public int targets = 1;

    [Header("As terrain")]
    [Tooltip("What stepping onto this tower's tile counts as, in tiles of walking.")]
    public int digCost = 20;
    [Tooltip("Seconds one enemy needs to chew through it.")]
    public float towerHealth = 8f;

    [Header("Levels: what rare (first entry) and epic (second) add")]
    public Level[] levels = new Level[0];

    [System.Serializable]
    public class Level
    {
        [Tooltip("One short line for the player: what this level makes the tower do.")]
        public string label;
        public float damageMul = 1f;
        public float intervalMul = 1f;
        public float rangeAdd;
        public float splashAdd;
        public float slowTicksMul = 1f;
        public int extraTargets;
        [Tooltip("For an upgrade: its name in the build log (lower case).")]
        public string id;
        public bool grantsFlying;
        public bool ignoresResist;
        public int slowTicksAdd;
        [Range(0.05f, 1f)] public float slowFactorTo = 1f;
        public float powerRadiusAdd;
        public int powerCapacityAdd;

        [Header("Paths (2026-10-27): path 1-3, step 1 or 2; 0 = no path")]
        public int path;
        public int step;
        public string pathName;
        public float damageAdd;
        public bool lure;
        public bool beacon;
        public int critEvery;
        public float critMul = 1f;
        [Range(0f, 1f)] public float executeBelow;
        public float flyingDamageMul = 1f;
        public float burnDps;
        public int burnTicks;
        public float exposeMul = 1f;
        public int exposeTicks;

        public TowerLevelMod ToMod()
        {
            return new TowerLevelMod
            {
                Label = label, DamageMul = damageMul, IntervalMul = intervalMul, RangeAdd = rangeAdd, SplashAdd = splashAdd, SlowTicksMul = slowTicksMul,
                ExtraTargets = extraTargets, Id = id, GrantsFlying = grantsFlying, IgnoresResist = ignoresResist, SlowTicksAdd = slowTicksAdd,
                SlowFactorTo = slowFactorTo, PowerRadiusAdd = powerRadiusAdd, PowerCapacityAdd = powerCapacityAdd,
                Path = path, Step = step, PathName = pathName, DamageAdd = damageAdd, Lure = lure, Beacon = beacon, CritEvery = critEvery, CritMul = critMul,
                ExecuteBelow = executeBelow, FlyingDamageMul = flyingDamageMul, BurnDps = burnDps, BurnTicks = burnTicks, ExposeMul = exposeMul, ExposeTicks = exposeTicks,
            };
        }
    }

    [Header("Upgrades the player chooses from (2026-10-24: each tower its own)")]
    public Level[] upgrades = new Level[0];

    [Header("Power tower (SimConfig.PowerRule)")]
    [Tooltip("Above 0: it powers this many towers within powerRadius tiles, and has power itself.")]
    public int powerCapacity;
    public float powerRadius;

    [Header("Shop")]
    [Tooltip("Relative chance of being offered.")]
    public float shopWeight = 1f;

    [Tooltip("The research tier that unlocks it in the build menu (SimConfig.BuildMenu): 1, 2 or 3.")]
    public int tier = 1;

    public TowerDef ToDef()
    {
        var mods = new TowerLevelMod[levels != null ? levels.Length : 0];
        for (int i = 0; i < mods.Length; i++) mods[i] = levels[i].ToMod();
        var ups = new TowerLevelMod[upgrades != null ? upgrades.Length : 0];
        for (int i = 0; i < ups.Length; i++) ups[i] = upgrades[i].ToMod();

        return new TowerDef
        {
            Levels = mods.Length > 0 ? mods : null,
            Upgrades = ups.Length > 0 ? ups : null,
            PowerCapacity = powerCapacity,
            PowerRadius = powerRadius,
            Id = string.IsNullOrEmpty(id) ? name.ToLowerInvariant() : id,
            Cost = cost,
            DamageType = damageType,
            Damage = damage,
            RangeTiles = rangeTiles,
            FireIntervalTicks = fireIntervalTicks,
            SplashRadiusTiles = splashRadiusTiles,
            SlowTicks = slowTicks,
            SlowFactor = slowFactor,
            CanHitFlying = canHitFlying,
            Targets = Mathf.Max(1, targets),
            DigCost = digCost,
            TowerHealth = towerHealth,
            ShopWeight = shopWeight,
            Tier = tier,
        };
    }
}
