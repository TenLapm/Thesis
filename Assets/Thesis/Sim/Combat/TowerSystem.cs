using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // The tower pass: ARCHITECTURE.md §4.6 step 3, run once per tick BEFORE the
    // agents move. Every hit resolves here, inside the tick it is fired in
    // (CLAUDE.md I12). A projectile in flight exists only in the Unity view.
    //
    // Order is part of the rules: towers act in ascending id, and a splash hits its
    // victims in ascending id. An enemy killed by one tower is dead for every later
    // tower in the same tick, so a kill is paid for exactly once.
    //
    // Who a tower may hit is Targeting.CanHit: a tower without CanHitFlying never
    // touches a flyer, as a target or with its splash (WP-C2).
    //
    // Damage everywhere means EFFECTIVE damage: the hit points actually removed.
    // A 6-damage shot at an enemy with 2 HP deals 2. Otherwise the damage map and
    // the per-type totals would reward overkill, and the director's features read both.
    public static class TowerSystem
    {
        // damageByType may be null when no wave is open (nothing is alive to hit then).
        // `core` is the grid's node for the core tile: target choice ranks enemies
        // by how far they still have to go to it.
        public static void Step(SimGrid grid, SimNode core, IList<TowerState> towers, IList<AgentState> live, DamageMap damage,
                                float[] damageByType, float minSlowFactor, ref float buildBudget, IList<SimEvent> events)
        {
            for (int t = 0; t < towers.Count; t++)
            {
                TowerState tower = towers[t];
                if (!tower.IsAlive) continue;

                if (tower.Cooldown > 0) tower.Cooldown--;
                if (tower.Cooldown > 0) continue;

                // Nothing in range: keep the shot and fire the tick a target appears.
                int index = Targeting.Pick(tower.Def.Mode, tower, grid, core, live);
                if (index < 0) continue;

                AgentState target = live[index];
                Vec2f centre = target.Position; // a splash is centred where the target stood
                Hit(tower, target, grid, damage, damageByType, minSlowFactor, ref buildBudget, events);

                if (tower.SplashSquared > 0f)
                {
                    for (int i = 0; i < live.Count; i++)
                    {
                        AgentState other = live[i];
                        if (i == index || !other.IsAlive) continue;
                        if (!Targeting.CanHit(tower.Def, other)) continue;
                        if (!Targeting.InRange(other.Position, centre, tower.SplashSquared)) continue;
                        Hit(tower, other, grid, damage, damageByType, minSlowFactor, ref buildBudget, events);
                    }
                }

                tower.Cooldown = tower.Def.FireIntervalTicks;
                tower.Shots++;
                events?.Add(SimEvent.TowerFired(tower.Id, target.Id));
            }
        }

        private static void Hit(TowerState tower, AgentState agent, SimGrid grid, DamageMap damage, float[] damageByType,
                                float minSlowFactor, ref float buildBudget, IList<SimEvent> events)
        {
            TowerDef def = tower.Def;
            int type = (int)def.DamageType;
            float resist = agent.Resist[type];

            // Resist 0 is immunity: no damage and no slow either.
            if (!(resist > 0f)) return;

            float dealt = (float)(def.Damage * resist);
            if (dealt > agent.Hp) dealt = agent.Hp; // overkill is not damage
            if (dealt > 0f)
            {
                agent.Hp = (float)(agent.Hp - dealt);
                damage.Add(grid.NodeFromPosition(agent.Position), dealt);
                if (damageByType != null) damageByType[type] = (float)(damageByType[type] + dealt);
                tower.DamageDealt = (float)(tower.DamageDealt + dealt);
            }

            // Slow keeps the LONGEST remaining time and the STRONGEST (smallest)
            // factor, so two slowing towers never weaken each other. The floor is what
            // guarantees a slowed enemy still advances and its wave still ends.
            if (def.SlowTicks > 0)
            {
                if (def.SlowTicks > agent.SlowTicks) agent.SlowTicks = def.SlowTicks;
                float factor = Math.Max(def.SlowFactor, minSlowFactor);
                if (factor < agent.SlowFactor) agent.SlowFactor = factor;
            }

            if (agent.Hp <= 0f)
            {
                agent.IsAlive = false;
                agent.Killed = true;
                buildBudget += agent.KillReward;
                tower.Kills++;
                events?.Add(SimEvent.AgentKilled(agent.Id, agent.KillReward));
            }
        }
    }
}
