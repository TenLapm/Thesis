using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of FlowAgent.Update's per-agent rules, run over every live agent once
    // per tick. This is ARCHITECTURE.md §4.2 step 3 exactly; step 4 (rebuilding the
    // field once, at the end of the tick, if any tile broke) is the caller's job -
    // AgentSystem only reports whether that's needed.
    //
    // Deliberate change from the original (documented in ARCHITECTURE §4.2): a
    // breach used to call GenerateFlowField() immediately, so later agents in the
    // same Unity frame could already see the new field. Here every agent in a tick
    // sees the SAME field, and the rebuild happens once afterwards.
    public static class AgentSystem
    {
        // Ascending Id (i.e. list order, since ids are assigned in spawn order) is
        // part of the contract: it decides which agent's dig call is the one that
        // actually crosses a wall's health to zero when several share a tile, and
        // that agent is the only one credited with the breach.
        public static bool Step(SimGrid grid, IList<AgentState> agents, float dt, OccupancyMap occupancy,
                                 ref float buildBudget, ref int coreHp, IList<SimEvent> events)
        {
            bool fieldDirty = false;

            for (int i = 0; i < agents.Count; i++)
            {
                AgentState agent = agents[i];
                if (!agent.IsAlive) continue;

                // THE CLOCK IS TICKING - and it keeps ticking while digging, which is
                // the whole point: chewing a wall wastes the agent's time too.
                agent.LifeTime -= dt;
                if (agent.LifeTime <= 0f)
                {
                    agent.IsAlive = false;
                    buildBudget += agent.DeathReward;
                    events?.Add(SimEvent.AgentStalled(agent.Id, agent.DeathReward));
                    continue;
                }

                SimNode node = grid.NodeFromPosition(agent.Position);

                // Reached the core?
                if (node.BestCost == 0)
                {
                    agent.IsAlive = false;
                    coreHp -= 1;
                    events?.Add(SimEvent.AgentLeaked(agent.Id));
                    continue;
                }

                if (node.BestCost < agent.MinCostSeen) agent.MinCostSeen = node.BestCost;
                occupancy.Increment(node);

                // DIGGING: standing on a wall tile means chewing it instead of
                // walking, whether or not this tick's chew is the one that finishes
                // it off. HasWall is re-checked fresh on every agent, so if an
                // earlier agent this same tick already cleared this tile, later
                // agents fall through to the move branch below instead.
                if (node.HasWall)
                {
                    node.WallHealth -= agent.DigRate * dt;
                    if (node.WallHealth <= 0f)
                    {
                        grid.ClearWall(node);
                        buildBudget += agent.WallBreakReward;
                        fieldDirty = true;
                        events?.Add(SimEvent.WallBreached(node.X, node.Y));
                    }
                    continue;
                }

                // Flow-field movement, one tile at a time toward the cheapest
                // neighbour. MoveTowards clamps at the target, so no matter how
                // fast the agent is or how large dt is, it can never overshoot past
                // a tile in a single step - it always stops at Next and re-samples
                // next tick, so no wall tile is ever skipped.
                SimNode next = grid.NextOf(node);
                if (next != null)
                {
                    agent.Position = Vec2f.MoveTowards(agent.Position, next.Position, agent.MoveSpeed * dt);
                }
            }

            return fieldDirty;
        }
    }
}
