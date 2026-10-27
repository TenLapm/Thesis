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
            new TowerLevelMod { Id = "power", Label = "Power: +50% damage", DamageMul = 1.5f },
            new TowerLevelMod { Id = "rapid", Label = "Rapid: fires 40% faster", IntervalMul = 0.7f },
            new TowerLevelMod { Id = "reach", Label = "Reach: +1.5 range", RangeAdd = 1.5f },
            new TowerLevelMod { Id = "volley", Label = "Volley: hits one more enemy", ExtraTargets = 1 },
        };

        public static readonly string[] Ids = { "power", "rapid", "reach", "volley" };

        // Whether a tower has a choice in this place (counting a tower's upgrades).
        public static bool IsValid(int choice) { return choice >= 0; }

        // The choices of this tower type: its own (TowerDef.Upgrades), or the four above.
        public static TowerLevelMod[] For(TowerDef def) { return def != null && def.Upgrades != null && def.Upgrades.Length > 0 ? def.Upgrades : All; }

        public static bool IsValidFor(TowerDef def, int choice) { return choice >= 0 && choice < For(def).Length; }

        // Whether the tower's upgrades come in paths (TowerLevelMod.Path).
        public static bool HasPaths(TowerDef def)
        {
            foreach (TowerLevelMod mod in For(def)) if (mod.Path > 0) return true;
            return false;
        }

        // Whether this tower may take this choice next (2026-10-27). With paths: a first step
        // first, then the second step of the same path. Without: any choice, the same one twice too.
        public static bool Allowed(TowerState tower, int choice)
        {
            if (tower == null || !IsValidFor(tower.Def, choice)) return false;
            TowerLevelMod[] list = For(tower.Def);
            if (!HasPaths(tower.Def)) return true;
            if (tower.UpgradeCount == 0) return list[choice].Step == 1;
            if (tower.UpgradeCount == 1) return list[choice].Step == 2 && IsValidFor(tower.Def, tower.Upgrade) && list[choice].Path == list[tower.Upgrade].Path;
            return false;
        }

        // The choices the tower may take next, in list order.
        public static int[] Options(TowerState tower)
        {
            var options = new System.Collections.Generic.List<int>();
            if (tower != null) for (int i = 0; i < For(tower.Def).Length; i++) if (Allowed(tower, i)) options.Add(i);
            return options.ToArray();
        }

        // The name of a choice in the build log.
        public static string IdOf(TowerDef def, int choice)
        {
            TowerLevelMod mod = For(def)[choice];
            if (!string.IsNullOrEmpty(mod.Id)) return mod.Id;
            string label = mod.Label ?? "upgrade";
            int colon = label.IndexOf(':');
            return (colon > 0 ? label.Substring(0, colon) : label).Trim().ToLowerInvariant().Replace(' ', '-');
        }
    }
}
