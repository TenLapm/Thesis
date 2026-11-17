using System;
using System.Collections.Generic;
using System.Diagnostics;
using Thesis.Core;
using Thesis.Learning;
using Thesis.Sim;

namespace Thesis.Director
{
    // The director (ARCHITECTURE.md §5.2), as an IWavePlanner. Each wave, as its build phase begins:
    //
    //   profile the board → bin and kernel context → the wave's threat budget →
    //   for every strategy, in id order: its plan, then the vetoes (layer 1, I5), then the authored
    //   score (layer 2), then in Full mode the learner's bounded correction (layer 3, I4) →
    //   play the highest score; ties to the lowest id; everything vetoed → the static baseline.
    //
    // HeuristicOnly is a complete director on its own: the ablation condition and a ladder rung.
    // Full asks an IStrategyEstimator for the correction; the reward and the update are WP11, so
    // here the estimator is only read. A vetoed strategy never reaches the estimator.
    //
    // The director keeps what it chose BY WAVE NUMBER: a wave planned again (the core moved, a wave
    // restarted) overwrites its own entry instead of adding one, so the anti-repeat rules always
    // compare with the wave before.
    public sealed class WaveDirector : IWavePlanner, IDecisionSource
    {
        public const string HeuristicId = "heuristic";
        public const string FullId = "full";
        public const string FallbackAllVetoed = "AllVetoed";

        private readonly DirectorConfig cfg;
        private readonly SimConfig config;
        private readonly MapData map;
        private readonly ThreatCostTable table;
        private readonly DirectorMode mode;
        private readonly IStrategyEstimator estimator;
        private readonly IReadOnlyList<IWaveStrategy> pool;
        private readonly BuildProfiler profiler = BuildProfiler.Default();
        private readonly ContextBucketizer bucketizer;
        private readonly AuthoredHeuristic heuristic;
        private readonly OverBudget overBudget = new OverBudget();
        private readonly RepeatStreak repeatStreak;
        private readonly Uncounterable uncounterable = new Uncounterable();
        private readonly TooSimilarToPrevious tooSimilar;
        private readonly EscalationPlanner fallback;
        private readonly Pcg32 strategyRng;
        private readonly Pcg32 thompsonRng;

        private readonly Dictionary<int, WavePlan> chosenPlans = new Dictionary<int, WavePlan>();
        private readonly Dictionary<int, string> chosenNames = new Dictionary<int, string>();
        private readonly Dictionary<int, DirectorTrace> traces = new Dictionary<int, DirectorTrace>();
        private EmptyBoard empty;
        private TileCoord emptyCore = new TileCoord(int.MinValue, int.MinValue);

        public DirectorTrace LastTrace { get; private set; }

        public WaveDirector(DirectorConfig cfg, SimConfig config, MapData map, ThreatCostTable table, ulong seed,
                            DirectorMode mode = DirectorMode.HeuristicOnly, IStrategyEstimator estimator = null)
        {
            this.cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.table = table ?? throw new ArgumentNullException(nameof(table));
            if (mode == DirectorMode.Full && estimator == null) throw new ArgumentException("the full director needs an estimator", nameof(estimator));
            this.mode = mode;
            this.estimator = estimator;
            pool = StrategyPool.Study(new StrategyKit(config, map, table));
            bucketizer = new ContextBucketizer(cfg);
            heuristic = new AuthoredHeuristic(cfg);
            repeatStreak = new RepeatStreak(cfg.MaxStreak);
            tooSimilar = new TooSimilarToPrevious(cfg.SimilarityThreshold);
            fallback = new EscalationPlanner(config, map, table);
            strategyRng = new Pcg32(seed, RngStreams.Strategy);
            thompsonRng = new Pcg32(seed, RngStreams.Thompson);
        }

        public string Name => mode == DirectorMode.Full ? FullId : HeuristicId;

        public DirectorTrace TraceFor(int wave)
        {
            DirectorTrace t;
            return traces.TryGetValue(wave, out t) ? t : null;
        }

        public WavePlan PlanWave(WaveContext context)
        {
            var clock = Stopwatch.StartNew();
            int wave = context.WaveIndex;
            SimState s = context.State;
            var trace = new DirectorTrace { Wave = wave };

            if (empty == null || !emptyCore.Equals(s.Core)) { empty = EmptyBoard.For(map, config, s.Core); emptyCore = s.Core; }
            BuildProfile profile = profiler.Profile(ProfileInput.From(s, map, empty));
            trace.Profile = profile;
            trace.Bin = bucketizer.Bin(profile);
            DecisionContext dc = bucketizer.Context(profile);
            float budget = table.BudgetForWave(wave);
            trace.ThreatBudget = budget;

            WavePlan previous;
            chosenPlans.TryGetValue(wave - 1, out previous);
            var recent = new List<string>();
            string name;
            if (chosenNames.TryGetValue(wave - 2, out name)) recent.Add(name);
            if (chosenNames.TryGetValue(wave - 1, out name)) recent.Add(name);

            StrategyRow best = null;
            foreach (IWaveStrategy strategy in pool)
            {
                var row = new StrategyRow { Id = strategy.Id, Name = strategy.Name };
                trace.Rows.Add(row);
                row.Plan = strategy.BuildPlan(budget, context, strategyRng);
                row.Veto = overBudget.Check(row.Plan, budget, config)
                        ?? repeatStreak.Check(strategy.Name, recent)
                        ?? uncounterable.Check(strategy.Name, s)
                        ?? tooSimilar.Check(row.Plan, previous);
                if (row.Veto != null) { row.Score = float.NegativeInfinity; continue; } // I5: before the learner

                row.Heuristic = heuristic.Score(strategy.Id, profile);
                float correction = 0f;
                if (mode == DirectorMode.Full)
                {
                    Correction c = estimator.SampleCorrection(dc, strategy.Id, thompsonRng);
                    row.Correction = c;
                    correction = c.Value; // I4: within [-1, +1], 0 when cold
                }
                row.Score = (float)(row.Heuristic.Value + correction);
                if (best == null || row.Score > best.Score) best = row; // strictly greater: ties stay with the lower id
            }

            WavePlan plan;
            if (best == null)
            {
                plan = fallback.PlanFor(wave); // priced by the same table, so it passes the I2 check
                trace.Fallback = FallbackAllVetoed;
                trace.Chosen = plan.StrategyId;
            }
            else
            {
                plan = best.Plan;
                plan.AnnouncementId = AnnouncementTable.Id(best.Name, trace.Bin);
                trace.Chosen = best.Name;
                trace.Announcement = AnnouncementTable.Line(plan.AnnouncementId);
            }

            chosenPlans[wave] = plan.Copy();
            chosenNames[wave] = trace.Chosen;
            trace.ElapsedMicroseconds = clock.ElapsedTicks * 1000000L / Stopwatch.Frequency; // G2; never in a telemetry row
            traces[wave] = trace;
            LastTrace = trace;
            return plan;
        }

        public void OnWaveResolved(WaveOutcome outcome) { } // the reward and the update are WP11
    }
}
