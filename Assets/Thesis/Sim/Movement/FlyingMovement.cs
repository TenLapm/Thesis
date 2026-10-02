using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // The Flying movement class: no flow field and no terrain. A flyer heads
    // straight for the centre of the core tile at its own speed, over walls, towers
    // and static blockers alike. It never digs, so nothing the player builds changes
    // where it goes; only a tower with CanHitFlying can stop it (Targeting.CanHit).
    //
    // Because its path never depends on the board, a flyer always arrives: the
    // "every wave must end" rules of ARCHITECTURE.md §4.6 hold for it without a
    // route check.
    public static class FlyingMovement
    {
        // One tick's flight. MoveTowards clamps at the target, so a fast flyer stops
        // on the core's centre instead of passing it; the next agent pass then finds
        // it on the core tile and it leaks.
        public static Vec2f Step(Vec2f position, Vec2f core, float maxDistance)
        {
            return Vec2f.MoveTowards(position, core, maxDistance);
        }

        // The straight-line distance to the core in the flow field's units: tiles
        // x 10, a whole number. It is to a flyer what BestCost is to a walker, so the
        // two can be compared ("which enemy is nearest the core?").
        //
        // Explicit casts round every float intermediate, so Mono and .NET get the
        // same integer (ARCHITECTURE.md §9 rule 3). Math.Sqrt is exact on both.
        public static int CostToCore(Vec2f position, Vec2f core, float tileSize)
        {
            float dx = (float)(position.X - core.X);
            float dy = (float)(position.Y - core.Y);
            float distance = (float)Math.Sqrt((float)((float)(dx * dx) + (float)(dy * dy)));
            float tenthsOfATile = (float)((float)(distance / tileSize) * 10f);
            return (int)Math.Round(tenthsOfATile);
        }
    }
}
