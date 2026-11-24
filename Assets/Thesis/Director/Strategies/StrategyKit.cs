using System;
using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Director
{
    // What every strategy shares: the baseline's enemy of a wave, the entries a wave may use,
    // and the fit that makes a plan spend exactly its budget.
    public sealed class StrategyKit
    {
        public readonly SimConfig Config;
        public readonly MapData Map;
        public readonly ThreatCostTable Table;

        private readonly EscalationPlanner baseline;

        public StrategyKit(SimConfig config, MapData map, ThreatCostTable table)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Table = table ?? throw new ArgumentNullException(nameof(table));
            baseline = new EscalationPlanner(config, map);
        }

        // The static baseline's walker of a wave. Every strategy makes its enemies from it with
        // an EnemyArchetype, so they grow with the wave number as the baseline does.
        public AgentGroup Basic(int wave) { return baseline.PlanFor(wave).Groups[0]; }

        // The entries a wave may use: those the walkers reach the core from, at least
        // MinEntryRouteTiles away by their route (as RaidPlanner), so no wave starts next to the
        // core. If none is that far (a small test map), every reachable entry; then every entry.
        // When set, the entries a wave may use are only these (WaveDirector.Compose: every part of a
        // wave shares the wave's EntriesFor entries, so it comes from as many as the static version's).
        public List<int> Only;

        public List<int> Candidates(WaveContext context)
        {
            if (Only != null && Only.Count > 0) return new List<int>(Only);
            SimGrid grid = context.State.Grid;
            var far = new List<int>();
            var reachable = new List<int>();
            int minCost = (int)(float)(Config.MinEntryRouteTiles * 10f);
            for (int i = 0; i < Map.Spawns.Length; i++)
            {
                int cost = grid.Get(Map.Spawns[i]).BestCost;
                if (cost == SimNode.Infinity) continue;
                reachable.Add(i);
                if (cost >= minCost) far.Add(i);
            }
            if (far.Count > 0) return far;
            if (reachable.Count > 0) return reachable;
            var all = new List<int>(Map.Spawns.Length);
            for (int i = 0; i < Map.Spawns.Length; i++) all.Add(i);
            return all;
        }

        // Turns shaped groups into a plan that spends the budget.
        //
        // Each group gets its share of the budget. Its COUNT is chosen first, the nearest whole
        // number of its type's enemies that share buys, so a swarm stays many weak enemies and a
        // flyer stays a light one. Then every group's hit points are scaled by one common factor
        // that absorbs the remainder (it is within half an enemy of 1). Hit points, not count,
        // because counts are whole and a budget is not.
        public WavePlan Fit(string strategyId, int wave, AgentGroup[] shapes, float[] shares, float budget)
        {
            if (shapes == null || shares == null || shapes.Length != shares.Length || shapes.Length == 0)
                throw new ArgumentException("one share per group, at least one group");
            if (!(budget > 0f)) throw new ArgumentOutOfRangeException(nameof(budget), budget, "a budget must be positive");

            var groups = new AgentGroup[shapes.Length];
            for (int g = 0; g < shapes.Length; g++)
            {
                AgentGroup group = shapes[g].Copy();
                float unit = Table.EnemyPrice(group);
                float want = (float)((float)(budget * shares[g]) / unit);
                group.Count = Math.Max(1, (int)(float)(want + 0.5f));
                groups[g] = group;
            }

            var plan = new WavePlan { WaveIndex = wave, StrategyId = strategyId, AnnouncementId = strategyId, Groups = groups };
            float k = (float)(budget / Table.Price(plan));
            for (int g = 0; g < groups.Length; g++) groups[g].Hp = (float)(groups[g].Hp * k);
            plan.ThreatSpent = Table.Price(plan);
            return plan;
        }

        // How many entries a wave comes from (the student, 2026-11-24: "enemies coming from all
        // over"): 2 at wave 1, one more every 4 waves, at most 5. The static version uses the same
        // number (SpreadStaticPlanner), so the spread is not a difference between the versions.
        public const int FirstEntries = 2, MostEntries = 5, MoreEntriesEvery = 4;

        public static int EntriesFor(int wave)
        {
            return Math.Min(MostEntries, FirstEntries + Math.Max(0, wave - 1) / MoreEntriesEvery);
        }

        // Pairs at the first `entries` of `ranked`: at each, the main type with mainShare of that
        // entry's part of the budget and the helper with the rest (all start together).
        public WavePlan Squads(string strategy, int wave, IList<int> ranked, int entries, EnemyArchetype main, EnemyArchetype helper, float mainShare, float budget)
        {
            int k = Math.Max(1, Math.Min(entries, ranked.Count));
            AgentGroup basic = Basic(wave);
            var shapes = new AgentGroup[2 * k];
            var shares = new float[2 * k];
            for (int i = 0; i < k; i++)
            {
                AgentGroup m = main.Apply(basic, 1f);
                m.SpawnIndex = ranked[i];
                m.StartDelayTicks = 0;
                AgentGroup h = helper.Apply(basic, 1f);
                h.SpawnIndex = ranked[i];
                h.StartDelayTicks = 0;
                shapes[2 * i] = m;
                shapes[2 * i + 1] = h;
                shares[2 * i] = (float)(mainShare / k);
                shares[2 * i + 1] = (float)((float)(1f - mainShare) / k);
            }
            return Fit(strategy, wave, shapes, shares, budget);
        }

        // The candidates sorted by a key, smallest first; ties keep the lower entry.
        public static List<int> RankBy(List<int> candidates, Func<int, double> key)
        {
            var keys = new Dictionary<int, double>();
            foreach (int e in candidates) keys[e] = key(e);
            var ranked = new List<int>(candidates);
            ranked.Sort((a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));
            return ranked;
        }

        // The main type's share of each pair.
        public const float MainShare = 0.7f;

        // A tower that takes part in the defence (as the build profile counts them).
        public static bool Defends(TowerState t) { return ProfileInput.CountsAsTower(t); }

        public static bool Reaches(TowerState tower, int x, int y)
        {
            int dx = x - tower.Tile.X, dy = y - tower.Tile.Y;
            return (float)(dx * dx + dy * dy) <= (float)(tower.RangeTiles * tower.RangeTiles);
        }
    }
}
