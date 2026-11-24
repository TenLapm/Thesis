using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // How far a real player's boards are from being pressed (the balance pass, 2026-11-24). The
    // scripted players are far weaker than a person, so a tester's own session is the yardstick:
    //
    //   1. The session runs once under its own config, and every command is labelled with the
    //      build phase it was given in (the number of waves played before it). Commands given
    //      during a wave (a retry, a speed change) are dropped.
    //   2. It runs again under the config being tuned. At the start of each build phase the
    //      commands of that phase are applied, in order, and the waves the player met are sent
    //      again (the last plan of each wave number in the recording). The core cannot fall.
    //      Re-timed this way, a build never lands in the middle of a wave when the new numbers
    //      make waves shorter or longer.
    //   3. At each wave asked for, the board as the player left it is copied, and a probe of
    //      plain walkers (ThreatCalibration) finds the strength, as a multiple of the static
    //      wave's, at which a given share of it gets through.
    //
    // A strength of 3 at wave 10 means: this player's wave-10 board lets 10 % through only when the
    // walkers are three times as strong as the game sends. Limits: the player built against the
    // waves of the original rules; their later choices would differ under new ones.
    public static class BoardCurve
    {
        public sealed class Point
        {
            public int Wave;
            public double StrengthAtTarget;
            public bool HitBound;
            public int Towers;
            public float Ore;
        }

        public static List<ThreatCalibration.Board> Boards(ReplayFile file, SimConfig config, int[] waves)
        {
            return Boards(file, config, waves, out _);
        }

        // What each tower type did over the re-run session: copies, damage dealt, ore paid (tower,
        // upgrades, gear), damage per ore. By type id, in roster order.
        public static List<(string id, int copies, double damage, double paid)> TowerValue(ReplayFile file, SimConfig config, int lastWave)
        {
            Boards(file, config, new[] { lastWave }, out Simulation sim);
            var rows = new List<(string, int, double, double)>();
            foreach (TowerDef def in sim.TowerLibrary)
            {
                int n = 0; double dmg = 0.0, paid = 0.0;
                foreach (TowerState t in sim.State.Towers)
                {
                    if (t.Def.Id != def.Id || t.PowerCapacity > 0) continue;
                    n++;
                    dmg += t.DamageDealt;
                    paid += t.PricePaid;
                }
                if (n > 0) rows.Add((def.Id, n, dmg, paid));
            }
            return rows;
        }

        private static List<ThreatCalibration.Board> Boards(ReplayFile file, SimConfig config, int[] waves, out Simulation played)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            Dictionary<int, List<SimCommand>> byPhase = Label(file);

            var plans = new Dictionary<int, WavePlan>();
            if (file.Plans != null) foreach (RecordedPlan p in file.Plans) plans[p.Wave] = p.Plan;
            var sim = new Simulation(config, file.MapData, file.Shapes, file.Towers, file.RngSeed, new ByWave(file.Planner, plans));
            sim.State.CoreHp = int.MaxValue / 4;
            Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };

            var wanted = new HashSet<int>(waves);
            int last = 0;
            foreach (int w in waves) last = Math.Max(last, w);
            var boards = new List<ThreatCalibration.Board>();
            int appliedFor = -1;
            for (int guard = 0; guard < 4000000; guard++)
            {
                SimState s = sim.State;
                bool build = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (build && s.NextPlan != null && appliedFor != s.WaveIndex)
                {
                    appliedFor = s.WaveIndex;
                    if (byPhase.TryGetValue(s.WaveIndex, out List<SimCommand> commands))
                        foreach (SimCommand c in commands)
                        {
                            if (c.Kind == SimCommandKind.StartWaveNow) continue;
                            send(c);
                        }
                    s = sim.State;
                    s.CoreHp = int.MaxValue / 4;
                    int coming = s.WaveIndex + 1;
                    if (wanted.Contains(coming)) boards.Add(new ThreatCalibration.Board { Seed = file.RngSeed, Wave = coming, Snapshot = sim.Clone(new ByWave(file.Planner, plans)) });
                    if (coming >= last || !plans.ContainsKey(coming + 1)) break;
                    for (int g = 0; g < 64 && s.GearOverflow.Count > 0; g++) send(SimCommand.SellOverflow(0));
                    send(SimCommand.StartWaveNow());
                }
                sim.Tick();
            }
            played = sim;
            return boards;
        }

        // The commands of each build phase, keyed by the waves played before it.
        private static Dictionary<int, List<SimCommand>> Label(ReplayFile file)
        {
            var sim = new Simulation(file.Config, file.MapData, file.Shapes, file.Towers, file.RngSeed, new RecordedPlanner(file.Planner, file.Plans));
            var byPhase = new Dictionary<int, List<SimCommand>>();
            int cursor = 0;
            List<ReplayCommand> commands = file.Commands;
            while (sim.State.Tick < file.FinalTick)
            {
                int tick = sim.State.Tick;
                while (cursor < commands.Count && commands[cursor].Tick <= tick)
                {
                    ReplayCommand rc = commands[cursor++];
                    if (rc.Tick != tick) continue;
                    SimCommand c = rc.ToCommand();
                    SimState s = sim.State;
                    bool build = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                    if (build && c.Kind != SimCommandKind.RetryWave)
                    {
                        if (!byPhase.TryGetValue(s.WaveIndex, out List<SimCommand> list)) byPhase[s.WaveIndex] = list = new List<SimCommand>();
                        list.Add(c);
                    }
                    sim.Enqueue(c);
                }
                sim.Tick();
            }
            return byPhase;
        }

        public static List<Point> Measure(List<ThreatCalibration.Board> boards, SimConfig config, double target, int iterations = 12)
        {
            var points = new List<Point>();
            foreach (ThreatCalibration.Board b in boards)
            {
                var kit = new StrategyKit(config, b.Snapshot.Map, new ThreatCostTable(config, b.Snapshot.Map, 1f, 1f));
                ThreatCalibration.Search s = ThreatCalibration.Match(new List<ThreatCalibration.Board> { b }, EnemyArchetypes.Basic(), target, kit, iterations);
                int towers = 0;
                foreach (TowerState t in b.Snapshot.State.Towers) if (t.IsAlive && !t.OnBench && t.PowerCapacity == 0) towers++;
                points.Add(new Point { Wave = b.Wave, StrengthAtTarget = s.Strength, HitBound = s.HitBound, Towers = towers, Ore = b.Snapshot.State.BuildBudget });
            }
            return points;
        }

        // The wave the player met, every group's hit points times `strength`: the share that got
        // through. The core cannot fall.
        public static double MetLeak(ThreatCalibration.Board board, float strength, int maxTicks = 300000)
        {
            var planner = new Scaled(board.Snapshot.State.NextPlan, strength);
            Simulation sim = board.Snapshot.Clone(planner);
            SimState s = sim.State;
            s.NextPlan = null;
            s.CoreHp = int.MaxValue / 4;
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.FlushInput();
            for (int t = 0; t < maxTicks; t++)
            {
                sim.Tick();
                WaveOutcome o = sim.State.LastOutcome;
                if (o != null && o.WaveIndex == board.Wave) return o.Spawned == 0 ? 0.0 : (double)o.Leaked / o.Spawned;
            }
            throw new InvalidOperationException("[BoardCurve] wave " + board.Wave + " did not end within " + maxTicks + " ticks.");
        }

        // The strength (1/16 to 16, by halving) at which `target` of the met wave gets through; NaN
        // when it lies outside.
        public static double MetStrength(ThreatCalibration.Board board, double target, int iterations = 12)
        {
            double lo = 1.0 / 16, hi = 16.0;
            if (MetLeak(board, (float)hi) < target || MetLeak(board, (float)lo) > target) return double.NaN;
            for (int i = 0; i < iterations; i++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (MetLeak(board, (float)mid) < target) lo = mid; else hi = mid;
            }
            return Math.Sqrt(lo * hi);
        }

        private sealed class Scaled : IWavePlanner
        {
            private readonly WavePlan plan;
            private readonly float strength;
            public Scaled(WavePlan plan, float strength) { this.plan = plan; this.strength = strength; }
            public string Name => "scaled";
            public WavePlan PlanWave(WaveContext context)
            {
                WavePlan p = plan.Copy();
                foreach (AgentGroup g in p.Groups) g.Hp = (float)(g.Hp * strength);
                return p;
            }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        // The recorded waves by number (a retry or a moved core planned some waves twice; the last
        // plan of a number is the one the player met).
        private sealed class ByWave : IWavePlanner
        {
            private readonly string name;
            private readonly Dictionary<int, WavePlan> plans;

            public ByWave(string name, Dictionary<int, WavePlan> plans) { this.name = name; this.plans = plans; }

            public string Name => name;

            public WavePlan PlanWave(WaveContext context)
            {
                if (!plans.TryGetValue(context.WaveIndex, out WavePlan p)) throw new InvalidOperationException("[BoardCurve] the recording has no wave " + context.WaveIndex + ".");
                return p.Copy();
            }

            public void OnWaveResolved(WaveOutcome outcome) { }
        }
    }
}
