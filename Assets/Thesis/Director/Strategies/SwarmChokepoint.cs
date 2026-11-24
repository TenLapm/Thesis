using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // swarm_chokepoint (S10): many weak enemies through the gap the player relies on most, with
    // runners (2026-11-24: pairs), from the entries whose walkers' route passes the most of last
    // wave's damage: for each entry, the sum of WaveDamage of the towers that reach any tile of
    // its route. That is the chokepoint as the student defined it (the towers doing the work,
    // chokepoint_reliance). Before any damage has been dealt, the number of towers reaching the
    // route decides instead. Ties go to the lowest entry.
    public sealed class SwarmChokepoint : IWaveStrategy
    {
        public const string StrategyName = "swarm_chokepoint";

        private readonly StrategyKit kit;
        private readonly List<SimNode> route = new List<SimNode>();

        public SwarmChokepoint(StrategyKit kit) { this.kit = kit; }

        public int Id => 1;
        public string Name => StrategyName;

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng, int entries)
        {
            List<int> candidates = kit.Candidates(context);
            SimState s = context.State;
            var damageOf = new Dictionary<int, double>();
            var coverOf = new Dictionary<int, int>();
            foreach (int e in candidates)
            {
                Route.Collect(s.Grid, s.Grid.Get(kit.Map.Spawns[e]), route);
                double damage = 0.0;
                int cover = 0;
                for (int t = 0; t < s.Towers.Count; t++)
                {
                    TowerState tower = s.Towers[t];
                    if (!StrategyKit.Defends(tower) || !ReachesRoute(tower)) continue;
                    damage += tower.WaveDamage;
                    cover++;
                }
                damageOf[e] = damage;
                coverOf[e] = cover;
            }
            // Most damage first, then most towers; the key is negated because RankBy sorts up.
            List<int> ranked = StrategyKit.RankBy(candidates, e => -(damageOf[e] * 1000.0 + coverOf[e]));
            return kit.Squads(Name, context.WaveIndex, ranked, entries, EnemyArchetypes.Swarm(), EnemyArchetypes.Runner(), StrategyKit.MainShare, threatBudget);
        }

        private bool ReachesRoute(TowerState tower)
        {
            for (int i = 0; i < route.Count; i++) if (StrategyKit.Reaches(tower, route[i].X, route[i].Y)) return true;
            return false;
        }
    }
}
