using System;

namespace Thesis.Sim
{
    // How far an enemy still has to go, in the flow field's units (tiles x 10),
    // measured the way THAT enemy travels:
    //
    //   Ground   the ground field's cost of the tile it is on
    //   Sapper   the sapper field's cost of the tile it is on
    //   Flying   the straight line to the core
    //
    // One number per enemy means the classes can be compared. It is what "nearest
    // the core" means for a tower choosing a target (Targeting.First) and for the
    // pressure statistic (AgentState.MinCostSeen). Using the ground field for a
    // flyer would be wrong in exactly the case that matters: a flyer above a thick
    // maze is close to the core, while the ground under it is a long walk away.
    public static class RouteCost
    {
        // `under` is the tile the agent is on (SimGrid.NodeFromPosition); the caller
        // usually has it already.
        public static int ToCore(AgentState agent, SimNode under, SimNode core, float tileSize)
        {
            switch (agent.Movement)
            {
                case MovementClass.Ground: return under.BestCost;
                case MovementClass.Sapper: return under.SapperCost;
                case MovementClass.Flying: return FlyingMovement.CostToCore(agent.Position, core.Position, tileSize);
                default: throw new InvalidOperationException("[Sim] Unknown movement class " + agent.Movement + ".");
            }
        }
    }
}
