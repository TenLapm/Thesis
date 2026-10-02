using Thesis.Core;

namespace Thesis.Sim
{
    // One 64-bit fingerprint of a SimState. Two runs that should be identical are
    // compared tick by tick with this; the first tick where the hashes differ is
    // where a determinism bug starts (ARCHITECTURE.md §8).
    //
    // Rule: if a field can change what happens next, it goes in. Floats go in by
    // bit pattern, so even a last-bit rounding difference is caught.
    //
    // One deliberate omission: dead agents. AgentSystem never touches an agent once
    // it is dead, so its fields are frozen; every value it ever had was hashed on
    // the ticks it was alive, and its death shows up in the budget/core HP and in
    // Agents.Count. Hashing the whole ever-growing list every tick would make the
    // 20,000-tick determinism test quadratic for no extra detection.
    public static class StateHasher
    {
        public static ulong Compute(SimState s)
        {
            var h = new Fnv1a64();

            h.Add(s.Tick);
            h.Add((int)s.Phase);
            h.Add(s.PhaseTicksRemaining);
            h.Add(s.WaveIndex);
            h.Add(s.BuildBudget);
            h.Add(s.CoreHp);

            AddGrid(ref h, s.Grid);

            h.Add(s.Agents.Count);
            h.Add(s.Live.Count);
            for (int i = 0; i < s.Live.Count; i++) AddAgent(ref h, s.Live[i]);

            s.Bag.AddToHash(ref h);
            AddRng(ref h, s.BagRng.Save());
            AddRng(ref h, s.SpawnRng.Save());

            h.Add(s.Towers.Count);
            for (int i = 0; i < s.Towers.Count; i++) AddTower(ref h, s.Towers[i]);

            s.Occupancy.AddToHash(ref h);
            s.Damage.AddToHash(ref h);

            h.Add(s.PlacementLog.Count);
            for (int i = 0; i < s.PlacementLog.Count; i++) AddPlacement(ref h, s.PlacementLog[i]);

            AddPlan(ref h, s.CurrentPlan);
            AddOutcome(ref h, s.CurrentOutcome);
            AddOutcome(ref h, s.LastOutcome);

            h.Add(s.Schedule.Length);
            h.Add(s.ScheduleCursor);
            for (int i = s.ScheduleCursor; i < s.Schedule.Length; i++)
            {
                h.Add(s.Schedule[i].Tick);
                h.Add(s.Schedule[i].Group);
                h.Add(s.Schedule[i].Slot);
            }

            h.Add(s.StartWaveRequested);
            h.Add(s.Pending.Count);
            for (int i = 0; i < s.Pending.Count; i++)
            {
                h.Add((int)s.Pending[i].Kind);
                h.Add(s.Pending[i].X);
                h.Add(s.Pending[i].Y);
                h.Add(s.Pending[i].A);
            }

            return h.Value;
        }

        private static void AddGrid(ref Fnv1a64 h, SimGrid g)
        {
            h.Add(g.FieldVersion);
            for (int i = 0; i < g.NodeCount; i++)
            {
                SimNode n = g.ByIndex(i);
                h.Add(n.TerrainCost);
                h.Add(n.WallHealth);
                h.Add(n.MaxWallHealth);
                h.Add((int)n.Occupant);
                h.Add(n.TowerId);
                h.Add(n.BestCost);
                h.Add(n.NextIndex);
            }
        }

        private static void AddAgent(ref Fnv1a64 h, AgentState a)
        {
            h.Add(a.Id);
            h.Add(a.Position.X);
            h.Add(a.Position.Y);
            h.Add(a.MoveSpeed);
            h.Add(a.DigRate);
            h.Add(a.Hp);
            h.Add(a.MaxHp);
            for (int i = 0; i < a.Resist.Length; i++) h.Add(a.Resist[i]);
            h.Add(a.SlowTicks);
            h.Add(a.SlowFactor);
            h.Add(a.KillReward);
            h.Add(a.WallBreakReward);
            h.Add(a.IsAlive);
            h.Add(a.Leaked);
            h.Add(a.Killed);
            h.Add(a.WaveIndex);
            h.Add(a.MinCostSeen);
        }

        // Every tower, destroyed ones included: there are few, and a destroyed
        // tower's id and tile still matter to anything that refers to it.
        private static void AddTower(ref Fnv1a64 h, TowerState t)
        {
            h.Add(t.Id);
            h.Add(t.Def.Id);
            h.Add(t.Tile.X);
            h.Add(t.Tile.Y);
            h.Add(t.PlacedTick);
            h.Add(t.Cooldown);
            h.Add(t.IsAlive);
            h.Add(t.DamageDealt);
            h.Add(t.Shots);
            h.Add(t.Kills);
        }

        private static void AddRng(ref Fnv1a64 h, Pcg32State r)
        {
            h.Add(r.State);
            h.Add(r.Inc);
        }

        private static void AddPlacement(ref Fnv1a64 h, PlacementRecord p)
        {
            h.Add((int)p.Kind);
            h.Add(p.TowerId);
            h.Add(p.Tick);
            h.Add(p.ShapeName);
            h.Add(p.RotationTurns);
            h.Add(p.OriginX);
            h.Add(p.OriginY);
            h.Add(p.Tiles.Length);
        }

        private static void AddPlan(ref Fnv1a64 h, WavePlan p)
        {
            if (p == null)
            {
                h.Add(-1);
                return;
            }
            h.Add(p.WaveIndex);
            h.Add(p.StrategyId);
            h.Add(p.ThreatSpent);
            h.Add(p.AnnouncementId);
            h.Add(p.Groups.Length);
            for (int i = 0; i < p.Groups.Length; i++)
            {
                AgentGroup g = p.Groups[i];
                h.Add(g.SpawnIndex);
                h.Add(g.Count);
                h.Add(g.Archetype);
                h.Add(g.MoveSpeed);
                h.Add(g.Hp);
                h.Add(g.DigRate);
                for (int r = 0; r < g.Resist.Length; r++) h.Add(g.Resist[r]);
                h.Add(g.SpawnIntervalTicks);
                h.Add(g.StartDelayTicks);
            }
        }

        private static void AddOutcome(ref Fnv1a64 h, WaveOutcome o)
        {
            if (o == null)
            {
                h.Add(-1);
                return;
            }
            h.Add(o.WaveIndex);
            h.Add(o.StrategyId);
            h.Add(o.Spawned);
            h.Add(o.Killed);
            h.Add(o.Leaked);
            h.Add(o.Removed);
            h.Add(o.TimedOut);
            h.Add(o.WallsBreached);
            h.Add(o.TowersDestroyed);
            for (int i = 0; i < o.DamageByType.Length; i++) h.Add(o.DamageByType[i]);
            h.Add(o.CoreHpBefore);
            h.Add(o.CoreHpAfter);
            h.Add(o.BudgetBefore);
            h.Add(o.BudgetAfter);
            h.Add(o.TickStarted);
            h.Add(o.TickResolved);
            h.Add(o.PressureCount);
            h.Add(o.PressureShare);
            h.Add(o.CoreDestroyed);
            h.Add(o.FirstAgentId);
        }
    }
}
