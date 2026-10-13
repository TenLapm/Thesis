using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // DEVELOPMENT ONLY, like ClassCyclePlanner: not a study condition (2026-10-13).
    //
    // It sends the development cycle of enemy types and, on a map with many entry
    // points (MapGenerator), chooses WHERE each wave comes in: one entry in the first
    // waves, two from wave 4, three from wave 8. The choice alternates between two
    // rules, so both can be felt in play before the director exists:
    //
    //   odd waves   the weak side: the entries whose routes pass the fewest tower
    //               ranges on the board as it stands
    //   even waves  somewhere else: a spread of entries picked by the wave's number
    //
    // An entry needs a route of at least SimConfig.MinEntryRouteTiles to the core, so
    // that no wave starts next to it, and two entries of a wave are at least
    // MinEntrySpacingTiles apart. With waves planned ahead (SimConfig.PlanAhead) the
    // board it reads is the one the last wave ended on; the player then answers.
    //
    // These two rules are placeholders for the director's strategies (WP9), which
    // must also keep a wave's strength the same whichever entries it uses.
    public sealed class RaidPlanner : IWavePlanner
    {
        public const string Id = "raid";

        private readonly ClassCyclePlanner cycle;
        private readonly List<SimNode> route = new List<SimNode>();

        public RaidPlanner(SimConfig config, MapData map)
        {
            cycle = new ClassCyclePlanner(config, map);
        }

        public string Name => Id;

        public void OnWaveResolved(WaveOutcome outcome) { }

        public static int EntriesFor(int wave) { return wave < 4 ? 1 : wave < 8 ? 2 : 3; }

        public WavePlan PlanWave(WaveContext context)
        {
            WavePlan plan = cycle.PlanFor(context.WaveIndex);
            int[] entries = ChooseEntries(context);

            var groups = new List<AgentGroup>();
            for (int g = 0; g < plan.Groups.Length; g++)
            {
                AgentGroup group = plan.Groups[g];
                for (int e = 0; e < entries.Length; e++)
                {
                    int count = group.Count / entries.Length + (e < group.Count % entries.Length ? 1 : 0);
                    if (count <= 0) continue;
                    AgentGroup part = group.Copy();
                    part.SpawnIndex = entries[e];
                    part.Count = count;
                    groups.Add(part);
                }
            }
            return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = Id, Groups = groups.ToArray(), ThreatSpent = 0f };
        }

        private int[] ChooseEntries(WaveContext context)
        {
            MapData map = context.Map;
            SimGrid grid = context.State.Grid;
            SimConfig config = context.Config;
            int wave = context.WaveIndex;
            if (map.Spawns.Length <= 1) return new[] { 0 };

            // Candidates: far enough from the core by the way an enemy would walk.
            var candidates = new List<int>();
            int minCost = (int)(float)(config.MinEntryRouteTiles * 10f);
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                int cost = grid.Get(map.Spawns[i]).BestCost;
                if (cost != SimNode.Infinity && cost >= minCost) candidates.Add(i);
            }
            if (candidates.Count == 0) for (int i = 0; i < map.Spawns.Length; i++) candidates.Add(i);

            // An order that differs from wave to wave but depends on nothing else.
            var h = new Fnv1a64();
            h.Add("raid");
            h.Add(wave);
            int start = (int)(h.Value % (ulong)candidates.Count);
            var order = new List<int>(candidates.Count);
            for (int k = 0; k < candidates.Count; k++) order.Add(candidates[(start + k * 7) % candidates.Count]);
            if (candidates.Count % 7 == 0) { order.Clear(); for (int k = 0; k < candidates.Count; k++) order.Add(candidates[(start + k) % candidates.Count]); }

            if (wave % 2 == 1)
            {
                // The weak side first. A stable sort by cover keeps the wave's order among equals.
                var cover = new Dictionary<int, int>();
                for (int k = 0; k < order.Count; k++) cover[order[k]] = Cover(context, order[k]);
                var sorted = new List<int>(order.Count);
                for (int k = 0; k < order.Count; k++)
                {
                    int at = sorted.Count;
                    while (at > 0 && cover[sorted[at - 1]] > cover[order[k]]) at--;
                    sorted.Insert(at, order[k]);
                }
                order = sorted;
            }

            int want = Math.Min(EntriesFor(wave), order.Count);
            var chosen = new List<int>(want);
            for (int k = 0; k < order.Count && chosen.Count < want; k++)
                if (FarFromAll(map, order[k], chosen, config.MinEntrySpacingTiles)) chosen.Add(order[k]);
            for (int k = 0; k < order.Count && chosen.Count < want; k++)
                if (!chosen.Contains(order[k])) chosen.Add(order[k]); // the spacing could not be kept
            return chosen.ToArray();
        }

        private static bool FarFromAll(MapData map, int entry, List<int> chosen, int spacing)
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                int dx = Math.Abs(map.Spawns[entry].X - map.Spawns[chosen[i]].X);
                int dy = Math.Abs(map.Spawns[entry].Y - map.Spawns[chosen[i]].Y);
                if (Math.Max(dx, dy) < spacing) return false;
            }
            return true;
        }

        // How well the walkers' route from this entry is covered: for each tile of the
        // route, the number of towers on the board that reach it.
        private int Cover(WaveContext context, int entry)
        {
            SimState s = context.State;
            Route.Collect(s.Grid, s.Grid.Get(context.Map.Spawns[entry]), route);
            int cover = 0;
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!tower.IsAlive || tower.OnBench) continue;
                float reach = (float)(tower.RangeTiles * tower.RangeTiles);
                for (int i = 0; i < route.Count; i++)
                {
                    int dx = route[i].X - tower.Tile.X;
                    int dy = route[i].Y - tower.Tile.Y;
                    if ((float)(dx * dx + dy * dy) <= reach) cover++;
                }
            }
            return cover;
        }
    }
}
