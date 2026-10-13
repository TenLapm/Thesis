using System.Collections.Generic;

namespace Thesis.Sim
{
    // Merging by the player's choice (2026-10-14). SimConfig.MergeCount towers of one
    // type and one level become one tower of the next level: the player drags one onto
    // another, the one dropped ON is the one that stays and gains the level, and the
    // rest are used up wherever they are, on the board or on the bench. A tower card
    // dropped on a tower counts as one of the copies.
    //
    // Nothing merges by itself (that was TowerCombine, the third round's rule, which
    // the game no longer switches on). These are the questions "may these merge, and
    // which other copies would go"; the simulation, the scripted player and the Unity
    // preview all ask them here.
    public static class TowerMerge
    {
        // The copies a merge into `target` would use up besides `source` (null when the
        // source is a card): towers of the target's type and level, the benched ones
        // first and then those on the board, oldest first. False when there are not
        // enough of them.
        public static bool FindOthers(SimState s, SimConfig config, TowerState target, TowerState source, List<TowerState> others)
        {
            others?.Clear();
            int need = config.MergeCount - 2;
            if (need <= 0) return true;

            int found = 0;
            for (int pass = 0; pass < 2 && found < need; pass++)
            {
                for (int i = 0; i < s.Towers.Count && found < need; i++)
                {
                    TowerState t = s.Towers[i];
                    if (!t.IsAlive || t == target || t == source || t.Def != target.Def || t.Level != target.Level) continue;
                    if (t.OnBench != (pass == 0)) continue;
                    others?.Add(t);
                    found++;
                }
            }
            return found >= need;
        }

        // Whether dropping the tower `source` on the tower `target` merges them.
        public static bool CanMerge(SimState s, SimConfig config, TowerState source, TowerState target, List<TowerState> others = null)
        {
            if (!config.FeedDuplicates || source == null || target == null || source == target) return false;
            if (!source.IsAlive || !target.IsAlive || source.Def != target.Def || source.Level != target.Level) return false;
            if (target.Level >= TowerState.MaxLevel) return false;
            return FindOthers(s, config, target, source, others);
        }

        // The level `target` would have after a card of this type and level is merged
        // into it, or 0 when the card cannot be. A card above the tower's level simply
        // gives the tower its level. Otherwise the card is one copy of a set: with
        // MergeCount 2 it raises the tower by itself, with more it needs a card of the
        // tower's own level and the other copies.
        public static int LevelAfterCard(SimState s, SimConfig config, TowerDef def, int cardLevel, TowerState target, List<TowerState> others = null)
        {
            others?.Clear();
            if (!config.FeedDuplicates || target == null || !target.IsAlive || target.Def != def) return 0;
            if (cardLevel > target.Level) return cardLevel;
            if (target.Level >= TowerState.MaxLevel) return 0;
            if (config.MergeCount <= 2) return target.Level + 1;
            if (cardLevel != target.Level) return 0;
            return FindOthers(s, config, target, null, others) ? target.Level + 1 : 0;
        }
    }
}
