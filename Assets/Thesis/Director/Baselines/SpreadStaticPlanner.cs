using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // The study's static version since 2026-11-24 (the student: both versions should spread their
    // enemies the same way). The escalation's wave (EscalationPlanner.PlanFor: the same walkers, the
    // same count, hit points and speed), shared out over StrategyKit.EntriesFor(wave) entries, the
    // same number the director's waves use. Which entries: the candidates in an order drawn from
    // the seed, kept apart by MinEntrySpacingTiles where the map allows, as split_groups does. It
    // never reads the player's board beyond which entries the walkers can reach.
    //
    // The count is split as evenly as whole enemies allow (the first entries take the remainder),
    // so the wave's price is the escalation's to the last enemy: the threat budget is unchanged (I2).
    public sealed class SpreadStaticPlanner : IWavePlanner
    {
        public const string Id = "static";

        private readonly EscalationPlanner escalation;
        private readonly StrategyKit kit;
        private readonly Pcg32 rng;

        public SpreadStaticPlanner(SimConfig config, MapData map, ThreatCostTable table, ulong seed)
        {
            escalation = new EscalationPlanner(config, map, table);
            kit = new StrategyKit(config, map, table);
            rng = new Pcg32(seed, RngStreams.Strategy);
        }

        public string Name => Id;

        public WavePlan PlanWave(WaveContext context)
        {
            WavePlan plan = escalation.PlanFor(context.WaveIndex);
            AgentGroup walkers = plan.Groups[0];
            List<int> order = kit.Candidates(context);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }
            int want = Math.Max(1, Math.Min(Math.Min(StrategyKit.EntriesFor(context.WaveIndex), order.Count), walkers.Count));
            var chosen = new List<int>(want);
            foreach (int e in order) if (chosen.Count < want && FarFromAll(e, chosen)) chosen.Add(e);
            foreach (int e in order) if (chosen.Count < want && !chosen.Contains(e)) chosen.Add(e);

            var groups = new AgentGroup[chosen.Count];
            for (int g = 0; g < chosen.Count; g++)
            {
                groups[g] = walkers.Copy();
                groups[g].SpawnIndex = chosen[g];
                groups[g].Count = walkers.Count / chosen.Count + (g < walkers.Count % chosen.Count ? 1 : 0);
            }
            plan.Groups = groups;
            plan.StrategyId = Id;
            plan.ThreatSpent = kit.Table.Price(plan);
            return plan;
        }

        public void OnWaveResolved(WaveOutcome outcome) { } // static: never reacts

        private bool FarFromAll(int entry, List<int> chosen)
        {
            TileCoord a = kit.Map.Spawns[entry];
            foreach (int c in chosen)
            {
                TileCoord b = kit.Map.Spawns[c];
                if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) < kit.Config.MinEntrySpacingTiles) return false;
            }
            return true;
        }
    }
}
