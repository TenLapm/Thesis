namespace Thesis.Sim
{
    // The one upgrade a tower can be given (2026-10-14, after Emberward): the player pays
    // once and CHOOSES what the tower gains. It took the place of merging: a level is no
    // longer something towers are fed into, and a tower is improved by a decision about
    // that tower.
    //
    // Fixed data, the same for every tower and every run, in this order: a tower holds
    // the index of its choice, and a replay names it by that index. Append, never reorder.
    public static class TowerUpgradeChoices
    {
        public const int None = -1;

        public static readonly TowerLevelMod[] All =
        {
            new TowerLevelMod { Label = "Power: +50% damage", DamageMul = 1.5f },
            new TowerLevelMod { Label = "Rapid: fires 40% faster", IntervalMul = 0.7f },
            new TowerLevelMod { Label = "Reach: +1.5 range", RangeAdd = 1.5f },
            new TowerLevelMod { Label = "Volley: hits one more enemy", ExtraTargets = 1 },
        };

        public static readonly string[] Ids = { "power", "rapid", "reach", "volley" };

        public static bool IsValid(int choice) { return choice >= 0 && choice < All.Length; }
    }
}
