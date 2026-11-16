using System;
using Thesis.Sim;

namespace Thesis.Director
{
    // The shared threat cost table (I2, spec gap S6, decided by the student on 2026-11-16).
    //
    //   price(enemy) = hit points × speed × the factor of its movement class
    //   price(group) = count × price(enemy)        price(plan) = Σ price(group)
    //   budget(w)    = price(the static baseline's wave w)   (EscalationPlanner.PlanFor)
    //
    // Only relative prices matter: the budget is itself a price, so every strategy spends what
    // the baseline's wave would cost. Ground's factor is 1. The Flying and Sapper factors are
    // MEASURED, not guessed (Results/2026-11-16_threat-factors, `thesis threat`): with the
    // builder player on 20 game maps, flyers get half a wave through on 1/2.35 of the budget
    // walkers need, sappers on 1/1.30. Only this table gets tuned (I2); the balance pass
    // measures again on the final game.
    //
    // Not priced, and refused with an ArgumentException: resistances, raiders and siege enemies.
    // The study's pool sends none of them (S10, S14), so a strategy cannot change the player's
    // income or firepower beyond what the baseline does (I11). PlanValidator turns the refusal
    // into a rejected plan.
    public sealed class ThreatCostTable : IThreatPricer
    {
        // Results/2026-11-16_threat-factors (20 seeds; 40 seeds gave 2.18 and 1.25).
        public const float MeasuredFlyingFactor = 2.35f;
        public const float MeasuredSapperFactor = 1.30f;

        public readonly float FlyingFactor;
        public readonly float SapperFactor;

        private readonly EscalationPlanner baseline;

        public ThreatCostTable(SimConfig config, MapData map, float flyingFactor = MeasuredFlyingFactor, float sapperFactor = MeasuredSapperFactor)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!(flyingFactor > 0f) || float.IsInfinity(flyingFactor)) throw new ArgumentOutOfRangeException(nameof(flyingFactor), flyingFactor, "a class factor must be a positive number");
            if (!(sapperFactor > 0f) || float.IsInfinity(sapperFactor)) throw new ArgumentOutOfRangeException(nameof(sapperFactor), sapperFactor, "a class factor must be a positive number");
            FlyingFactor = flyingFactor;
            SapperFactor = sapperFactor;
            baseline = new EscalationPlanner(config, map); // no pricer: it is what defines the budget
        }

        public float ClassFactor(MovementClass movement)
        {
            switch (movement)
            {
                case MovementClass.Flying: return FlyingFactor;
                case MovementClass.Sapper: return SapperFactor;
                default: return 1f;
            }
        }

        public float EnemyPrice(AgentGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (group.TargetsCollectors) throw new ArgumentException("raiders have no price (S14: not in the study's pool)", nameof(group));
            if (group.Siege) throw new ArgumentException("siege enemies have no price (S14: not in the study's pool)", nameof(group));
            if (group.Resist != null)
                for (int r = 0; r < group.Resist.Length; r++)
                    if (group.Resist[r] != 1f) throw new ArgumentException("resistances have no price (S6): " + (DamageType)r + " is " + group.Resist[r], nameof(group));
            return (float)((float)(group.Hp * group.MoveSpeed) * ClassFactor(group.Movement));
        }

        // Summed in group order, in double, then rounded once: the same on every runtime.
        public float Price(WavePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            double sum = 0.0;
            if (plan.Groups != null)
                for (int g = 0; g < plan.Groups.Length; g++)
                {
                    AgentGroup group = plan.Groups[g];
                    if (group == null || group.Count <= 0) continue;
                    sum += (double)group.Count * EnemyPrice(group);
                }
            return (float)sum;
        }

        public float BudgetForWave(int waveIndex) { return Price(baseline.PlanFor(waveIndex)); }
    }
}
