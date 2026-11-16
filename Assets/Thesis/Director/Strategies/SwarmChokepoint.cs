using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // swarm_chokepoint (S10): many weak enemies through the gap the player relies on most. The
    // whole wave is a swarm, from the one entry whose walkers' route passes the most of last
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

        public WavePlan BuildPlan(float threatBudget, WaveContext context, IRandom rng)
        {
            List<int> candidates = kit.Candidates(context);
            SimState s = context.State;
            int best = candidates[0];
            double bestDamage = -1.0;
            int bestCover = -1;
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
                if (damage > bestDamage || (damage == bestDamage && cover > bestCover))
                {
                    bestDamage = damage;
                    bestCover = cover;
                    best = e;
                }
            }

            AgentGroup swarm = EnemyArchetypes.Swarm().Apply(kit.Basic(context.WaveIndex), 1f);
            swarm.SpawnIndex = best;
            return kit.Fit(Name, context.WaveIndex, new[] { swarm }, new[] { 1f }, threatBudget);
        }

        private bool ReachesRoute(TowerState tower)
        {
            for (int i = 0; i < route.Count; i++) if (StrategyKit.Reaches(tower, route[i].X, route[i].Y)) return true;
            return false;
        }
    }
}
