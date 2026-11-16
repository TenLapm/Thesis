using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Measures the threat cost table's class factors (spec gap S6, the student, 2026-11-16: the
    // flyer and sapper factors are measured, not guessed).
    //
    // Boards: BuilderPolicy against the static baseline on game maps, each copied at the build
    // phase of chosen waves, after the player has built. On a copy the planned wave is dropped
    // and one test wave is sent instead: a single type (walker, sapper or flyer) from the
    // baseline's entry, priced at the wave's budget times a strength. The copy's core cannot
    // fall, so every enemy that gets through is counted.
    //
    // For each class, the strength at which half its wave gets through, averaged over all boards
    // (bisection; HalfLeak). A class factor is the walkers' half-leak strength over the class's:
    // a class that needs only half the walkers' budget to get half through is twice as hard to
    // stop per unit of hit points × speed, so its factor is 2 and a strategy gets fewer of them
    // for the same budget.
    //
    // Why half, not "the walkers' leak at strength 1": a player who is still alive at waves 3
    // to 7 lets almost nothing through at the normal budget (a first run found 0 on a board), and
    // matching a share near 0 says little. Half is where the leak moves most with strength.
    public static class ThreatCalibration
    {
        public const double HalfLeak = 0.5;

        public sealed class Board
        {
            public ulong Seed;
            public int Wave;
            public Simulation Snapshot; // never ticked; every probe clones it
        }

        public struct Probe
        {
            public double LeakShare;    // enemies that reached the core ÷ enemies sent
            public double DamageShare;  // hit points the defence removed ÷ hit points sent
        }

        // The boards of one seed: the game with BuilderPolicy, copied at the build phase of each
        // wave listed, if the player gets that far.
        public static List<Board> Boards(SimConfig config, ShapeDef[] shapes, TowerDef[] towers, ulong seed, int[] waves)
        {
            MapData map = GameMaps.Generate(seed);
            var sim = new Simulation(config, map, shapes, towers, seed, new EscalationPlanner(config, map));
            var policy = new BuilderPolicy();
            var rng = new Pcg32(seed, RngStreams.Policy);
            Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };
            var wanted = new HashSet<int>(waves);
            int last = 0;
            foreach (int w in waves) last = Math.Max(last, w);
            var boards = new List<Board>();

            bool built = false;
            for (int guard = 0; guard < 2000000 && !sim.State.IsGameOver; guard++)
            {
                SimState s = sim.State;
                bool buildPhase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (buildPhase && !built && (!config.PlanAhead || s.NextPlan != null))
                {
                    policy.OnIntermission(sim, send, rng);
                    s = sim.State;
                    if (config.PlanAhead && s.NextPlan == null) continue; // the core moved: the wave is planned again
                    built = true;
                    int coming = s.WaveIndex + 1;
                    if (wanted.Contains(coming)) boards.Add(new Board { Seed = seed, Wave = coming, Snapshot = sim.Clone(new EscalationPlanner(config, map)) });
                    if (coming >= last) break;
                    for (int g = 0; g < 64 && s.GearOverflow.Count > 0; g++) send(SimCommand.SellOverflow(0));
                    send(SimCommand.StartWaveNow());
                }
                else if (!buildPhase) built = false;
                sim.Tick();
            }
            return boards;
        }

        // One test wave on a copy of the board.
        public static Probe Run(Board board, EnemyArchetype archetype, float strength, StrategyKit kit, int maxTicks = 200000)
        {
            var planner = new ProbePlanner(archetype, strength, kit);
            Simulation sim = board.Snapshot.Clone(planner);
            SimState s = sim.State;
            s.NextPlan = null;                 // the baseline's plan is dropped; the probe plans on the next tick
            s.CoreHp = int.MaxValue / 4;       // the core cannot fall: every leak is counted
            sim.Enqueue(SimCommand.StartWaveNow());
            sim.FlushInput();
            for (int t = 0; t < maxTicks; t++)
            {
                sim.Tick();
                WaveOutcome o = sim.State.LastOutcome;
                if (o != null && o.WaveIndex == board.Wave)
                {
                    double hpSent = planner.HpSent;
                    double dealt = 0.0;
                    for (int k = 0; k < o.DamageByType.Length; k++) dealt += o.DamageByType[k];
                    return new Probe
                    {
                        LeakShare = o.Spawned == 0 ? 0.0 : (double)o.Leaked / o.Spawned,
                        DamageShare = hpSent > 0.0 ? Math.Min(1.0, dealt / hpSent) : 0.0,
                    };
                }
            }
            throw new InvalidOperationException("[ThreatCalibration] the probe wave on seed " + board.Seed + ", wave " + board.Wave + " did not end within " + maxTicks + " ticks.");
        }

        public static double MeanLeak(List<Board> boards, EnemyArchetype archetype, float strength, StrategyKit kit)
        {
            double sum = 0.0;
            foreach (Board b in boards) sum += Run(b, archetype, strength, kit).LeakShare;
            return boards.Count == 0 ? 0.0 : sum / boards.Count;
        }

        public sealed class Search
        {
            public double Target;
            public double Strength;      // where the class's mean leak share is the target
            public double LeakAtStrength;
            public bool HitBound;        // the answer lies outside [1/16, 16]: not measured
            public List<double[]> Steps = new List<double[]>(); // (strength, mean leak)
        }

        // Bisection on log2(strength) in [-4, 4]; the leak share grows with strength.
        public static Search Match(List<Board> boards, EnemyArchetype archetype, double target, StrategyKit kit, int iterations = 14)
        {
            var r = new Search { Target = target };
            double lo = -4.0, hi = 4.0;
            double leakLo = MeanLeak(boards, archetype, (float)Pow2(lo), kit);
            double leakHi = MeanLeak(boards, archetype, (float)Pow2(hi), kit);
            r.Steps.Add(new[] { Pow2(lo), leakLo });
            r.Steps.Add(new[] { Pow2(hi), leakHi });
            if (leakLo > target || leakHi < target)
            {
                r.HitBound = true;
                r.Strength = leakLo > target ? Pow2(lo) : Pow2(hi);
                r.LeakAtStrength = leakLo > target ? leakLo : leakHi;
                return r;
            }
            double leakMid = 0.0, mid = 0.0;
            for (int i = 0; i < iterations; i++)
            {
                mid = (lo + hi) / 2.0;
                leakMid = MeanLeak(boards, archetype, (float)Pow2(mid), kit);
                r.Steps.Add(new[] { Pow2(mid), leakMid });
                if (leakMid < target) lo = mid; else hi = mid;
            }
            r.Strength = Pow2((lo + hi) / 2.0);
            r.LeakAtStrength = leakMid;
            return r;
        }

        // 2^x through DetMath.Exp, never Math.Pow (ARCHITECTURE.md §9 rule 3).
        private static double Pow2(double x) { return DetMath.Exp(x * 0.69314718055994530942); }

        // One type, from the baseline's entry, at the wave's budget times a strength.
        private sealed class ProbePlanner : IWavePlanner
        {
            private readonly EnemyArchetype archetype;
            private readonly float strength;
            private readonly StrategyKit kit;
            public double HpSent;

            public ProbePlanner(EnemyArchetype archetype, float strength, StrategyKit kit)
            {
                this.archetype = archetype;
                this.strength = strength;
                this.kit = kit;
            }

            public string Name => "probe-" + archetype.Id;

            public WavePlan PlanWave(WaveContext context)
            {
                int w = context.WaveIndex;
                AgentGroup g = archetype.Apply(kit.Basic(w), 1f);
                g.SpawnIndex = 0;
                WavePlan plan = kit.Fit(Name, w, new[] { g }, new[] { 1f }, (float)(kit.Table.BudgetForWave(w) * strength));
                HpSent = 0.0;
                foreach (AgentGroup group in plan.Groups) HpSent += (double)group.Count * group.Hp;
                return plan;
            }

            public void OnWaveResolved(WaveOutcome outcome) { }
        }
    }
}
