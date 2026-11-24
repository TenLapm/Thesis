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
    // Not priced, and refused with an ArgumentException: resistances and siege enemies (S10, S14).
    // PlanValidator turns the refusal into a rejected plan. Raiders are priced since 2026-11-24 (the
    // raid strategy, the student's choice): as walkers of their hit points and speed, RaiderFactor 1,
    // not measured. A raid changes the player's income, which the static version does not (I11).
    public sealed class ThreatCostTable : IThreatPricer
    {
        // Measured again after the balance pass's hit-point curve (2026-11-24,
        // Results/2026-11-24_threat-factors-balance, 40 seeds; 20 gave 2.54 and 1.08). Before it,
        // Results/2026-11-16_threat-factors: 2.35 and 1.30.
        public const float MeasuredFlyingFactor = 2.48f;
        public const float MeasuredSapperFactor = 1.10f;
        // Raiders (TargetsCollectors), on top of their class's factor. A first guess, not measured.
        public const float RaiderFactor = 1f;

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
            if (group.Siege) throw new ArgumentException("siege enemies have no price (S14: not in the study's pool)", nameof(group));
            if (group.Resist != null)
                for (int r = 0; r < group.Resist.Length; r++)
                    if (group.Resist[r] != 1f) throw new ArgumentException("resistances have no price (S6): " + (DamageType)r + " is " + group.Resist[r], nameof(group));
            float price = (float)((float)(group.Hp * group.MoveSpeed) * ClassFactor(group.Movement));
            return group.TargetsCollectors ? (float)(price * RaiderFactor) : price;
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
