using System;

namespace Thesis.Sim
{
    // The rules a WavePlan must satisfy before its wave may start. Simulation
    // enforces them on every plan; SafePlanner runs the same check first, so that a
    // bad plan from a director becomes a fallback instead of an exception in the
    // middle of a tick. One implementation, so the two can never disagree.
    public static class PlanValidator
    {
        // Null for a valid plan, otherwise one sentence saying what is wrong.
        // pricer may be null: then no cost table is in use and I2 is not checked.
        public static string Check(WavePlan plan, int wave, MapData map, SimConfig config, IThreatPricer pricer)
        {
            if (plan == null) return "PlanWave returned null.";
            if (plan.WaveIndex != wave) return "plan is for wave " + plan.WaveIndex + ".";
            if (plan.Groups == null) return "Groups is null.";

            for (int i = 0; i < plan.Groups.Length; i++)
            {
                AgentGroup g = plan.Groups[i];
                string at = "group " + i + " ";
                if (g == null) return at + "is null.";
                if (g.Count < 0) return at + "has Count " + g.Count + ".";
                if (g.SpawnIndex < 0 || g.SpawnIndex >= map.Spawns.Length)
                    return at + "uses spawn " + g.SpawnIndex + " but map '" + map.Name + "' has " + map.Spawns.Length + ".";
                if (g.SpawnIntervalTicks < 0 || g.StartDelayTicks < 0)
                    return at + "has a negative interval (" + g.SpawnIntervalTicks + ") or start delay (" + g.StartDelayTicks + ").";
                if (g.Count > 0 && !(g.LifeTime > 0f)) return at + "has LifeTime " + g.LifeTime + "; agents would stall on spawn.";
                if (!(g.MoveSpeed >= 0f) || !(g.DigRate >= 0f))
                    return at + "has MoveSpeed " + g.MoveSpeed + " / DigRate " + g.DigRate + "; both must be >= 0 and not NaN.";
            }

            if (pricer != null)
            {
                float budget = pricer.BudgetForWave(wave);
                float price = pricer.Price(plan);
                if (Math.Abs(plan.ThreatSpent - budget) > config.ThreatEpsilon)
                    return "spends " + plan.ThreatSpent + " but the wave's threat budget is " + budget + " (I2).";
                if (Math.Abs(price - plan.ThreatSpent) > config.ThreatEpsilon)
                    return "claims ThreatSpent " + plan.ThreatSpent + " but the cost table prices it at " + price + ".";
            }

            return null;
        }
    }
}
