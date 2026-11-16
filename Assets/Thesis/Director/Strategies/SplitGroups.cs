using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // split_groups (S10): enemies from several sides at once. Plain walkers, shared equally
    // over the most entries the game uses in one wave, three (RaidPlanner.EntriesFor), or fewer
    // if the map has fewer; every group starts at once. The student chose "always the most",
    // because the development waves already use one to three entries and "several sides"
    // must stand out from them.
    //
    // Which entries: the candidates in an order drawn from the strategy's stream, keeping two
    // entries of the wave at least MinEntrySpacingTiles apart where the map allows it.
    public sealed class SplitGroups : IWaveStrategy
    {
        public const string StrategyName = "split_groups";
        public const int MostEntries = 3;

        private readonly StrategyKit kit;

        public SplitGroups(StrategyKit kit) { this.kit = kit; }

        public int Id => 2;
        public string Name => StrategyName;

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng)
        {
            List<int> order = kit.Candidates(context);
            for (int i = order.Count - 1; i > 0; i--) // Fisher-Yates, from the strategy's own stream
            {
                int j = rng.NextInt(i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }

            int want = Math.Min(MostEntries, order.Count);
            var chosen = new List<int>(want);
            foreach (int e in order) if (chosen.Count < want && FarFromAll(e, chosen)) chosen.Add(e);
            foreach (int e in order) if (chosen.Count < want && !chosen.Contains(e)) chosen.Add(e); // spacing could not be kept

            AgentGroup basic = EnemyArchetypes.Basic().Apply(kit.Basic(context.WaveIndex), 1f);
            var shapes = new AgentGroup[chosen.Count];
            var shares = new float[chosen.Count];
            for (int i = 0; i < chosen.Count; i++)
            {
                shapes[i] = basic.Copy();
                shapes[i].SpawnIndex = chosen[i];
                shapes[i].StartDelayTicks = 0;
                shares[i] = (float)(1f / chosen.Count);
            }
            return kit.Fit(Name, context.WaveIndex, shapes, shares, threatBudget);
        }

        private bool FarFromAll(int entry, List<int> chosen)
        {
            TileCoord a = kit.Map.Spawns[entry];
            for (int i = 0; i < chosen.Count; i++)
            {
                TileCoord b = kit.Map.Spawns[chosen[i]];
                if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) < kit.Config.MinEntrySpacingTiles) return false;
            }
            return true;
        }
    }
}
