using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // The agent pass: every live enemy acts once per tick, in ascending id
    // (ARCHITECTURE.md §4.6 step 4). An enemy on the core tile leaks. Otherwise
    // what it does depends on its movement class (WP-C2):
    //
    //   Ground, Sapper   on a built tile it chews the tile; otherwise it walks one
    //                    step along ITS flow field (ground or sapper).
    //   Flying           one step in a straight line toward the core. It never digs
    //                    and nothing on the board is in its way.
    //
    // There is no lifetime clock any more (WP-C1): an enemy ends by being killed in
    // the tower pass, which runs before this one, or by leaking here.
    //
    // A breach rebuilds the flow fields once, at the end of the tick, not in the
    // middle of this loop: every agent in a tick sees the SAME fields. AgentSystem
    // only reports that a rebuild is needed; the caller does it.
    public static class AgentSystem
    {
        // Ascending Id (i.e. list order, since ids are assigned in spawn order) is
        // part of the contract: it decides which agent's dig call is the one that
        // actually takes a tile's health to zero when several share it, and that
        // agent is the only one credited with the breach.
        //
        // `core` is the grid's node for the core tile: where every enemy is heading,
        // and the one tile on which an enemy leaks.
        public static bool Step(SimGrid grid, SimNode core, IList<AgentState> agents, IList<TowerState> towers, float dt, OccupancyMap occupancy,
                                 float towerBreachReward, ref float buildBudget, ref int coreHp, IList<SimEvent> events)
        {
            bool fieldDirty = false;

            for (int i = 0; i < agents.Count; i++)
            {
                AgentState agent = agents[i];
                if (!agent.IsAlive) continue; // includes agents the tower pass killed this tick

                // A slow wears off by the tick, whatever the agent is doing. Its
                // effect on this tick's step is taken before the countdown, so a
                // slow of N ticks slows exactly N steps.
                float speed = agent.MoveSpeed;
                if (agent.SlowTicks > 0)
                {
                    speed = (float)(speed * agent.SlowFactor);
                    agent.SlowTicks--;
                    if (agent.SlowTicks == 0) agent.SlowFactor = 1f;
                }

                SimNode node = grid.NodeFromPosition(agent.Position);

                // Reached the core? The same test for every class: a walker standing
                // on the core tile, or a flyer over it. (Until WP-C2 this read
                // "node.BestCost == 0", which is the same tile: only the goal of the
                // ground field costs nothing.)
                if (node == core)
                {
                    agent.IsAlive = false;
                    agent.Leaked = true;
                    coreHp -= 1;
                    events?.Add(SimEvent.AgentLeaked(agent.Id));
                    continue;
                }

                int cost = RouteCost.ToCore(agent, node, core, grid.TileSize);
                if (cost < agent.MinCostSeen) agent.MinCostSeen = cost;

                if (agent.Movement == MovementClass.Flying)
                {
                    // Not counted in the occupancy map: that is traffic on the
                    // ground, the thing the player's building shapes. A flyer's line
                    // is the same whatever is built.
                    agent.Position = FlyingMovement.Step(agent.Position, core.Position, (float)(speed * dt));
                    continue;
                }

                occupancy.Increment(node);

                // DIGGING: standing on a built tile (wall or tower) means chewing it
                // instead of walking, whether or not this tick's chew is the one that
                // finishes it off. HasWall is re-checked fresh on every agent, so if
                // an earlier agent this same tick already cleared this tile, later
                // agents fall through to the move branch below instead.
                if (node.HasWall)
                {
                    // Explicit casts round every float intermediate so Mono and CoreCLR
                    // agree bit for bit (ARCHITECTURE.md §9 rule 3).
                    node.WallHealth = (float)(node.WallHealth - (float)(agent.DigRate * dt));
                    if (node.WallHealth <= 0f)
                    {
                        if (node.Occupant == Occupant.Tower)
                        {
                            // Chewing through a tower's tile destroys the tower. It
                            // pays the player nothing by default (spec gap S12).
                            TowerState tower = towers[node.TowerId];
                            tower.IsAlive = false;
                            grid.ClearWall(node);
                            buildBudget += towerBreachReward;
                            events?.Add(SimEvent.TowerDestroyed(tower.Id, node.X, node.Y));
                        }
                        else
                        {
                            grid.ClearWall(node);
                            buildBudget += agent.WallBreakReward;
                            events?.Add(SimEvent.WallBreached(node.X, node.Y));
                        }
                        fieldDirty = true;
                    }
                    continue;
                }

                // Flow-field movement, one tile at a time toward the cheapest
                // neighbour on this agent's own field. MoveTowards clamps at the
                // target, so no matter how fast the agent is or how large dt is, it
                // can never overshoot past a tile in a single step - it always stops
                // at Next and re-samples next tick, so no built tile is ever skipped.
                SimNode next = grid.NextOf(node, agent.Movement);
                if (next != null)
                {
                    agent.Position = Vec2f.MoveTowards(agent.Position, next.Position, (float)(speed * dt));
                }
            }

            return fieldDirty;
        }
    }
}
