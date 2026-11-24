using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // raid_outposts (the student, 2026-11-24: "there must be something that is aiming for the
    // player's collectors"): raiders for the collectors, walkers for the core. The fifth strategy,
    // and the director's own choice: it answers an unguarded collector (collector_exposure).
    //
    // Half the budget buys raiders (EnemyArchetypes.Raider: they follow the raid field to the
    // nearest standing collector, then the next, then the core), half plain walkers, so at least
    // half of the wave goes for the core, as in the development director. Both come from the entry
    // nearest an unguarded collector (any collector when all are guarded); ties go to the lowest
    // entry.
    //
    // I11: this is the one strategy that changes the player's income, which the static version
    // never does. The student chose it knowing that (2026-11-24); it needs the supervisor's
    // agreement before the pilot. Raiders are priced as walkers (ThreatCostTable.RaiderFactor 1),
    // a first guess that has not been measured.
    public sealed class RaidOutposts : IWaveStrategy
    {
        public const string StrategyName = "raid_outposts";
        public const float RaiderShare = 0.5f;

        private readonly StrategyKit kit;

        public RaidOutposts(StrategyKit kit) { this.kit = kit; }

        public int Id => 4;
        public string Name => StrategyName;

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng, int entries)
        {
            SimState s = context.State;
            var targets = new List<TileCoord>();
            var any = new List<TileCoord>();
            SimGrid grid = s.Grid;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode node = grid.ByIndex(i);
                if (node.Occupant != Occupant.Collector) continue;
                var tile = new TileCoord(node.X, node.Y);
                any.Add(tile);
                bool covered = false;
                foreach (TowerState t in s.Towers)
                    if (StrategyKit.Defends(t) && StrategyKit.Reaches(t, node.X, node.Y)) { covered = true; break; }
                if (!covered) targets.Add(tile);
            }
            if (targets.Count == 0) targets = any;

            List<int> ranked = StrategyKit.RankBy(kit.Candidates(context), e =>
            {
                TileCoord sp = kit.Map.Spawns[e];
                int d = int.MaxValue;
                foreach (TileCoord c in targets) d = Math.Min(d, Math.Max(Math.Abs(c.X - sp.X), Math.Abs(c.Y - sp.Y)));
                return d;
            });
            return kit.Squads(Name, context.WaveIndex, ranked, entries, EnemyArchetypes.Raider(), EnemyArchetypes.Basic(), RaiderShare, threatBudget);
        }
    }
}
