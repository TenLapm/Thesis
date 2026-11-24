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
    //   profile the board → bin and kernel context →
    //   (Full) reward the wave before, now that it has resolved, and credit it to the decisions
    //   behind it (WP11) → the wave's threat budget →
    //   for every strategy, in id order: its plan, then the vetoes (layer 1, I5), then the authored
    //   score (layer 2), then in Full mode the learner's bounded correction (layer 3, I4) →
    //   play the highest score; ties to the lowest id; everything vetoed → the static baseline.
    //
    // HeuristicOnly is a complete director on its own: the ablation condition and a ladder rung.
    // A vetoed strategy never reaches the estimator.
    //
    // The reward for wave N is computed inside PlanWave(N + 1), before the decision for N + 1, and
    // only once OnWaveResolved has delivered wave N's outcome: never from a wave still running. It
    // is credited once per wave number, to the decisions for N, N - 1 and N - 2 with the weights of
    // DirectorConfig.CreditWeights (1, 0.5, 0.25), as fractional updates (S3).
    //
    // The director keeps what it chose BY WAVE NUMBER: a wave planned again (the core moved, a wave
    // restarted) overwrites its own entry instead of adding one, so the anti-repeat rules always
    // compare with the wave before.
    public sealed class WaveDirector : IWavePlanner, IDecisionSource, IPlannerSetup
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
        private readonly StrategyKit kit;
        private readonly BuildProfiler profiler = BuildProfiler.Default();
        private readonly ContextBucketizer bucketizer;
        private readonly AuthoredHeuristic heuristic;
        private readonly OverBudget overBudget = new OverBudget();
        private readonly RepeatStreak repeatStreak;
        private readonly Uncounterable uncounterable;
        private readonly NoRaidTarget noRaidTarget;
        private readonly TooSimilarToPrevious tooSimilar;
        private readonly EscalationPlanner fallback;
        private readonly Pcg32 strategyRng;
        private readonly Pcg32 thompsonRng;
        private readonly Pcg32 rewardRng;
        private readonly string setup;

        private readonly Dictionary<int, WavePlan> chosenPlans = new Dictionary<int, WavePlan>();
        private readonly Dictionary<int, string> chosenNames = new Dictionary<int, string>();
        private readonly Dictionary<int, DirectorTrace> traces = new Dictionary<int, DirectorTrace>();
        private readonly Dictionary<int, WaveOutcome> outcomes = new Dictionary<int, WaveOutcome>();
        private readonly Dictionary<int, RewardRecord> rewards = new Dictionary<int, RewardRecord>();
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
            kit = new StrategyKit(config, map, table);
            pool = StrategyPool.Study(kit);
            // An estimator made for another pool (four strategies, before the raid) would fail mid-session.
            EstimatorState starting = estimator != null ? estimator.Export() : null;
            if (starting != null && starting.Strategies != pool.Count)
                throw new ArgumentException("the estimator has " + starting.Strategies + " strategies, the pool " + pool.Count, nameof(estimator));
            bucketizer = new ContextBucketizer(cfg);
            heuristic = new AuthoredHeuristic(cfg);
            repeatStreak = new RepeatStreak(cfg.MaxStreak);
            tooSimilar = new TooSimilarToPrevious(cfg.SimilarityThreshold);
            uncounterable = new Uncounterable(cfg.FlyersNeedAntiAirUntilWave);
            noRaidTarget = new NoRaidTarget(cfg.RaidsFromWave);
            fallback = new EscalationPlanner(config, map, table);
            strategyRng = new Pcg32(seed, RngStreams.Strategy);
            thompsonRng = new Pcg32(seed, RngStreams.Thompson);
            rewardRng = new Pcg32(seed, RngStreams.RewardBernoulli);
            // Taken now, before any wave: the state the session starts from (the replay keeps it).
            setup = DirectorSetup.Write(cfg, mode, estimator);
        }

        public string Name => mode == DirectorMode.Full ? FullId : HeuristicId;

        public string PlannerSetup => setup;

        // What the learner knows now (WP14): saved at the end of a session, so another session's
        // director can start from it (DirectorSetup.FromSnapshot; the transfer condition). Null for
        // the heuristic director, which learns nothing.
        public EstimatorState ExportEstimator() { return estimator != null ? estimator.Export() : null; }

        public DirectorTrace TraceFor(int wave)
        {
            DirectorTrace t;
            return traces.TryGetValue(wave, out t) ? t : null;
        }

        public RewardRecord RewardFor(int wave)
        {
            RewardRecord r;
            return rewards.TryGetValue(wave, out r) ? r : null;
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
            trace.Context = dc;

            // Before the decision: the wave before, if it has resolved and not been rewarded yet.
            if (mode == DirectorMode.Full) trace.RewardForPrevious = RewardPrevious(wave - 1, profile);

            float budget = table.BudgetForWave(wave);
            trace.ThreatBudget = budget;
            if (mode == DirectorMode.Full) trace.EstimatorDigest = Digest(estimator.Export());

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
                        ?? uncounterable.Check(strategy.Name, s, wave)
                        ?? noRaidTarget.Check(strategy.Name, s, wave)
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
                plan = Compose(best, trace, budget, context);
                plan.AnnouncementId = AnnouncementTable.Id(best.Name, trace.Bin);
                trace.Chosen = best.Name;
                trace.ChosenId = best.Id;
                trace.Announcement = AnnouncementTable.Line(plan.AnnouncementId);
            }

            chosenPlans[wave] = plan.Copy();
            chosenNames[wave] = trace.Chosen;
            trace.ElapsedMicroseconds = clock.ElapsedTicks * 1000000L / Stopwatch.Frequency; // G2; never in a telemetry row
            traces[wave] = trace;
            LastTrace = trace;
            return plan;
        }

        // The wave as it is sent (the student, 2026-11-24: "more enemy types attacking each weak
        // point", "enemies coming from all over"): the chosen strategy leads with LeadShare of the
        // budget; the rest goes to the other strategies that survived the vetoes and scored above 0,
        // in proportion to their scores (a support below MinSupportShare of the budget is dropped and
        // its part shared out again). The wave comes from StrategyKit.EntriesFor(wave) entries, each
        // strategy taking a part of them in proportion to its share, its own best by its own measure.
        // With no support the lead takes the whole budget and every entry.
        //
        // The learner still learns one decision a wave, the lead (I4 unchanged): the supports follow
        // the authored scores. The whole wave spends the budget (each part is fitted to its own share).
        private WavePlan Compose(StrategyRow lead, DirectorTrace trace, float budget, WaveContext context)
        {
            int entries = Math.Max(1, Math.Min(StrategyKit.EntriesFor(context.WaveIndex), kit.Candidates(context).Count));
            var supports = new List<StrategyRow>();
            foreach (StrategyRow row in trace.Rows)
                if (row != lead && row.Veto == null && row.Score > 0f) supports.Add(row);
            float rest = (float)(1f - cfg.LeadShare);
            for (bool dropped = true; dropped && supports.Count > 0;)
            {
                dropped = false;
                double total = 0.0;
                foreach (StrategyRow r in supports) total += r.Score;
                for (int i = supports.Count - 1; i >= 0; i--)
                    if ((float)(rest * (float)(supports[i].Score / total)) < cfg.MinSupportShare) { supports.RemoveAt(i); dropped = true; }
            }
            trace.Support.Clear();
            if (supports.Count == 0)
            {
                trace.Support.Add(lead.Name + " 1");
                return pool[lead.Id].BuildPlan(budget, context, strategyRng, entries);
            }
            // (split_groups asks for its three sides; inside the entries a part is given it takes what there is.)

            double sum = 0.0;
            foreach (StrategyRow r in supports) sum += r.Score;
            var parts = new List<(StrategyRow row, float share)> { (lead, cfg.LeadShare) };
            foreach (StrategyRow r in supports) parts.Add((r, (float)(rest * (float)(r.Score / sum))));

            // Pass 1: where each part would come from on its own, in its order of preference. The
            // wave's entries are the first `entries` of those, the lead's first, then any others.
            var wanted = new List<int>();
            var prefs = new List<List<int>>();
            var ks = new List<int>();
            foreach (var (row, share) in parts)
            {
                int k = Math.Max(1, (int)(float)((float)(entries * share) + 0.5f));
                ks.Add(k);
                var pref = new List<int>();
                foreach (AgentGroup g in pool[row.Id].BuildPlan((float)(budget * share), context, strategyRng, k).Groups)
                {
                    if (!pref.Contains(g.SpawnIndex)) pref.Add(g.SpawnIndex);
                    if (wanted.Count < entries && !wanted.Contains(g.SpawnIndex)) wanted.Add(g.SpawnIndex);
                }
                prefs.Add(pref);
            }
            foreach (int e in kit.Candidates(context)) if (wanted.Count < entries && !wanted.Contains(e)) wanted.Add(e);

            // The entries shared out (found in play, 2026-11-24: two parts picking the same entry left
            // a wave of 3 entries coming from 2): each part its own, up to its count, the lead first;
            // a part left with none takes an unused one, or shares its first choice; what is still
            // unused goes to the lead. So the wave comes from exactly `entries` entries, as the
            // static version's does.
            var assigned = new List<List<int>>();
            var taken = new HashSet<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                var mine = new List<int>();
                foreach (int e in prefs[i]) if (mine.Count < ks[i] && wanted.Contains(e) && taken.Add(e)) mine.Add(e);
                assigned.Add(mine);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (assigned[i].Count > 0) continue;
                foreach (int e in wanted) if (assigned[i].Count == 0 && taken.Add(e)) assigned[i].Add(e);
                if (assigned[i].Count == 0) assigned[i].Add(prefs[i].Count > 0 && wanted.Contains(prefs[i][0]) ? prefs[i][0] : wanted[0]);
            }
            foreach (int e in wanted) if (taken.Add(e)) assigned[0].Add(e);

            // Pass 2: every part again, inside its own entries.
            var groups = new List<AgentGroup>();
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    var (row, share) = parts[i];
                    kit.Only = assigned[i];
                    WavePlan part = pool[row.Id].BuildPlan((float)(budget * share), context, strategyRng, assigned[i].Count);
                    groups.AddRange(part.Groups);
                    trace.Support.Add(row.Name + " " + share.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            finally
            {
                kit.Only = null;
            }
            var plan = new WavePlan { WaveIndex = context.WaveIndex, StrategyId = lead.Name, Groups = groups.ToArray() };
            plan.ThreatSpent = table.Price(plan);
            return plan;
        }

        // The outcome of a wave, delivered by the simulation once it has resolved. The reward is
        // worked out later, inside the next PlanWave, when the player's answer can be read.
        public void OnWaveResolved(WaveOutcome outcome)
        {
            if (outcome != null) outcomes[outcome.WaveIndex] = outcome;
        }

        private RewardRecord RewardPrevious(int wave, BuildProfile now)
        {
            if (wave < 1 || rewards.ContainsKey(wave)) return null;
            WaveOutcome outcome;
            DirectorTrace decided;
            if (!outcomes.TryGetValue(wave, out outcome) || !traces.TryGetValue(wave, out decided)) return null; // not resolved yet
            WavePlan plan, before;
            chosenPlans.TryGetValue(wave, out plan);
            chosenPlans.TryGetValue(wave - 1, out before);

            var record = new RewardRecord { Wave = wave, Reward = RewardEvaluator.Evaluate(decided.Profile, now, outcome, plan, before, cfg) };
            for (int lag = 0; lag < cfg.CreditWeights.Length; lag++)
            {
                DirectorTrace t;
                if (!traces.TryGetValue(wave - lag, out t) || t.ChosenId < 0) continue; // nothing chosen by the learner's arms
                float w = cfg.CreditWeights[lag];
                estimator.Update(t.Context, t.ChosenId, record.Reward.Total, w, rewardRng);
                record.Credited.Add(new CreditEntry { Wave = t.Wave, Strategy = t.Chosen, Weight = w });
            }
            rewards[wave] = record;
            return record;
        }

        // A short fingerprint of the estimator's state, written into telemetry: two sessions that
        // asked the same estimator have the same digest. FNV-1a over the numbers' BITS, as
        // StateHasher does. Not over the JSON text: the first version hashed Json.Serialize(state),
        // and Mono and .NET write some doubles with different (equally exact) text, so the pinned
        // full-director episode gave two digests for one state.
        private static string Digest(EstimatorState s)
        {
            if (s == null) return null; // an estimator that keeps no state (a test's stand-in)
            var h = new Fnv1a64();
            h.Add(s.Kind ?? "");
            h.Add(s.Bins);
            h.Add(s.Strategies);
            h.Add(s.Discount);
            Add(h, s.Alpha);
            Add(h, s.Beta);
            if (s.Updates != null) foreach (int u in s.Updates) h.Add(u);
            h.Add(s.ThinBelow);
            h.Add(s.PriorWeight);
            h.Add(s.NearWeight);
            KernelState k = s.Kernel;
            if (k != null)
            {
                h.Add(k.Capacity);
                h.Add(k.Sigma);
                h.Add(k.Count);
                h.Add(k.Next);
                for (int i = 0; i < k.Count; i++)
                {
                    h.Add(k.X[i]);
                    h.Add(k.Y[i]);
                    h.Add(k.Strategy[i]);
                    h.Add(k.Reward[i]);
                }
            }
            return ReplayFile.Hex(h.Value);
        }

        private static void Add(Fnv1a64 h, double[] values)
        {
            if (values == null) return;
            foreach (double v in values) h.Add(v);
        }
    }

    // What a director needs, besides its name and the seed, to be rebuilt exactly as it started:
    // its config and its starting estimator (the transfer condition seeds one from another
    // session). Stored in the replay as JSON (IPlannerSetup).
    public sealed class DirectorSetup
    {
        public const int CurrentSchema = 1;
        public int Schema = CurrentSchema;
        public DirectorMode Mode;
        public DirectorConfig Config;
        public EstimatorState Estimator; // null for HeuristicOnly

        public static string Write(DirectorConfig cfg, DirectorMode mode, IStrategyEstimator estimator)
        {
            return Json.Serialize(new DirectorSetup { Mode = mode, Config = cfg, Estimator = estimator != null ? estimator.Export() : null });
        }

        public static DirectorSetup Read(string json)
        {
            DirectorSetup s = Json.Deserialize<DirectorSetup>(json);
            if (s == null || s.Schema != CurrentSchema || s.Config == null) throw new ArgumentException("not a director setup of schema " + CurrentSchema);
            return s;
        }

        // A snapshot file is the estimator's state as JSON (WriteSnapshot). FromSnapshot makes the
        // setup a full director starts from (PlannerFactory.Create's setup), so the replay keeps the
        // snapshot as its starting estimator like any other setup.
        public static string WriteSnapshot(EstimatorState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return Json.SerializeIndented(state);
        }

        public static string FromSnapshot(string snapshot, DirectorConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            EstimatorState state;
            try
            {
                state = string.IsNullOrWhiteSpace(snapshot) ? null : Json.Deserialize<EstimatorState>(snapshot);
            }
            catch (Exception e) when (!(e is ArgumentException))
            {
                throw new ArgumentException("the snapshot is not an estimator's state: " + e.Message, e);
            }
            if (state == null || state.Kind == null || state.Alpha == null || state.Beta == null)
                throw new ArgumentException("the snapshot is not an estimator's state (no kind or no cells).");
            var setup = new DirectorSetup { Mode = DirectorMode.Full, Config = cfg, Estimator = state };
            try
            {
                setup.NewEstimator(); // refuses a state that does not fit its kind
            }
            catch (Exception e) when (!(e is ArgumentException))
            {
                throw new ArgumentException("the snapshot does not load: " + e.Message, e);
            }
            return Json.Serialize(setup);
        }

        // A fresh estimator of the setup's kind, holding its starting state.
        public IStrategyEstimator NewEstimator()
        {
            if (Estimator == null) return null;
            IStrategyEstimator e = Estimator.Kind == KernelPriorEstimator.KindName ? (IStrategyEstimator)new KernelPriorEstimator() : new BinnedPosterior(Estimator.Bins, Estimator.Strategies, Estimator.Discount);
            e.Import(Estimator);
            return e;
        }
    }
}
