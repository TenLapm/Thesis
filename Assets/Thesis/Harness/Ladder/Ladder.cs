using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // The offline ladder (CLAUDE.md §6, WORKPLAN WP12): before any participant, every rung plays
    // every scripted player on the same seeds, and the rungs should come out
    //     random < static escalation < authored heuristic < full director
    // in how much the player rebuilds. If the full director does not beat the heuristic, that must
    // show here, in semester 1, not in the study.
    //
    // What is measured per episode (each on the game's 48 x 48 board, GameMaps, seed = map seed =
    // rng seed):
    //   entropy      the primary metric (PlacementEntropy, walls and towers, 6 x 6), over the first W
    //                waves, where W is the fewest waves any rung lasted for that player and seed, so
    //                that a longer run is not credited for having had more waves to build in
    //   restructure  the mean change of the built features from one build phase to the next (what the
    //                full director is rewarded for, RewardEvaluator.Restructure), over the same window
    //   waves        waves survived; reported, not ranked (CLAUDE.md §3: not that the AI wins)
    //
    // Episodes are independent and Thesis.* keeps no static mutable state, so they run one per core;
    // each result goes to its own slot, so the file is the same on 1 core and on 16. Wall-clock times
    // (the director's, for G2) are kept apart from the results and never written into the file that
    // must be identical.
    public static class Ladder
    {
        public static readonly string[] Rungs = { RandomPlanner.Id, EscalationPlanner.Id, WaveDirector.HeuristicId, WaveDirector.FullId };
        public static readonly string[] Policies = { BuilderPolicy.Id, ReactivePolicy.Id };

        public sealed class Spec
        {
            public SimConfig Config;
            public ShapeDef[] Shapes;
            public TowerDef[] Towers;
            public string[] Rungs = Ladder.Rungs;
            public string[] Policies = Ladder.Policies;
            public ulong[] Seeds;
            public int MaxWaves = 50;
            public int Threads = Environment.ProcessorCount;
        }

        public sealed class Episode
        {
            public string Rung;
            public string Policy;
            public ulong Seed;
            public int Waves;                 // resolved with the core standing
            public bool CoreDestroyed;
            public int Placements;
            public string FinalHash;
            public List<WaveRecord> Rows;     // telemetry
            public MapData Map;
            public readonly SortedDictionary<int, BuildProfile> Profiles = new SortedDictionary<int, BuildProfile>(); // after the player built, by wave about to come
            public readonly List<long> DirectorMicros = new List<long>(); // wall clock: G2 only, never in the compared file
            public int Window;                // set after all episodes ran
            public double Entropy;
            public double Restructure;
        }

        public static List<Episode> Run(Spec spec)
        {
            if (spec == null || spec.Seeds == null || spec.Seeds.Length == 0) throw new ArgumentException("the ladder needs seeds");
            var jobs = new List<Episode>();
            foreach (string policy in spec.Policies)
                foreach (ulong seed in spec.Seeds)
                    foreach (string rung in spec.Rungs)
                        jobs.Add(new Episode { Rung = rung, Policy = policy, Seed = seed });

            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, spec.Threads) };
            Parallel.For(0, jobs.Count, options, i => Play(spec, jobs[i]));
            Measure(spec, jobs);
            return jobs;
        }

        private static void Play(Spec spec, Episode e)
        {
            MapData map = GameMaps.Generate(e.Seed);
            IWavePlanner planner = Registry.CreatePlanner(e.Rung, spec.Config, map, e.Seed);
            var policy = new ProfilingPolicy(Registry.CreatePolicy(e.Policy), spec.Config, e.Profiles);
            EpisodeResult r = EpisodeRunner.Run(new EpisodeOptions
            {
                Config = spec.Config, Map = map, Shapes = spec.Shapes, Towers = spec.Towers, Seed = e.Seed,
                Planner = e.Rung, PlannerInstance = planner, Policy = policy, MaxWaves = spec.MaxWaves,
                Session = "ladder-" + e.Rung + "-" + e.Policy + "-" + e.Seed,
            });
            e.Map = map;
            e.Rows = r.Telemetry;
            e.CoreDestroyed = r.GameOver && r.CoreHp <= 0;
            e.Waves = e.CoreDestroyed ? r.WavesResolved - 1 : r.WavesResolved;
            e.Placements = r.Placements;
            e.FinalHash = r.Replay.FinalHash;
            if (planner is WaveDirector d)
                for (int w = 1; w <= r.WavesResolved + 1; w++)
                {
                    DirectorTrace t = d.TraceFor(w);
                    if (t != null) e.DirectorMicros.Add(t.ElapsedMicroseconds);
                }
        }

        // The common window, then entropy and restructure inside it.
        private static void Measure(Spec spec, List<Episode> jobs)
        {
            var window = new Dictionary<string, int>();
            foreach (Episode e in jobs)
            {
                string key = e.Policy + "/" + e.Seed;
                int w = Math.Max(1, PlacementEntropy.CompletedWaves(e.Rows));
                window[key] = window.TryGetValue(key, out int old) ? Math.Min(old, w) : w;
            }
            DirectorConfig cfg = DirectorConfig.Provisional();
            foreach (Episode e in jobs)
            {
                e.Window = window[e.Policy + "/" + e.Seed];
                e.Entropy = PlacementEntropy.Compute(e.Rows, e.Map, e.Window, PlacementEntropy.DefaultPartition, PlacementScope.WallsAndTowers, spec.Config.TowersOnWalls);
                double sum = 0.0;
                int n = 0;
                BuildProfile before = null;
                foreach (var kv in e.Profiles)
                {
                    if (kv.Key > e.Window + 1) break;
                    if (before != null) { sum += RewardEvaluator.Restructure(before, kv.Value, cfg); n++; }
                    before = kv.Value;
                }
                e.Restructure = n == 0 ? 0.0 : sum / n;
            }
        }

        // ------------------------------------------------------------------ the summary

        public sealed class RungSummary
        {
            public string Rung;
            public string Policy;
            public int Episodes;
            public double Entropy, EntropySd;
            public double Restructure;
            public double Waves;
        }

        public static List<RungSummary> Summarise(List<Episode> episodes, Spec spec)
        {
            var list = new List<RungSummary>();
            foreach (string policy in spec.Policies)
                foreach (string rung in spec.Rungs)
                {
                    var s = new RungSummary { Rung = rung, Policy = policy };
                    var ent = new List<double>();
                    double re = 0.0, wv = 0.0;
                    foreach (Episode e in episodes)
                    {
                        if (e.Rung != rung || e.Policy != policy) continue;
                        ent.Add(e.Entropy);
                        re += e.Restructure;
                        wv += e.Waves;
                    }
                    s.Episodes = ent.Count;
                    if (ent.Count > 0)
                    {
                        double mean = 0.0;
                        foreach (double x in ent) mean += x;
                        mean /= ent.Count;
                        double v = 0.0;
                        foreach (double x in ent) v += (x - mean) * (x - mean);
                        s.Entropy = mean;
                        s.EntropySd = ent.Count > 1 ? Math.Sqrt(v / (ent.Count - 1)) : 0.0;
                        s.Restructure = re / ent.Count;
                        s.Waves = wv / ent.Count;
                    }
                    list.Add(s);
                }
            return list;
        }

        // Does the ladder hold for this player: each rung's mean entropy above the one below it?
        public static bool Holds(List<RungSummary> summary, string policy, string[] rungs, out string where)
        {
            where = null;
            double previous = double.NegativeInfinity;
            string previousRung = null;
            foreach (string rung in rungs)
            {
                RungSummary s = summary.Find(x => x.Policy == policy && x.Rung == rung);
                if (s == null) continue;
                if (!(s.Entropy > previous))
                {
                    where = previousRung + " >= " + rung;
                    return false;
                }
                previous = s.Entropy;
                previousRung = rung;
            }
            return true;
        }

        // The per-episode lines that must come out the same on any number of cores.
        public static string Fingerprint(List<Episode> episodes)
        {
            var b = new System.Text.StringBuilder();
            foreach (Episode e in episodes)
                b.Append(e.Policy).Append(' ').Append(e.Rung).Append(' ').Append(e.Seed.ToString(CultureInfo.InvariantCulture)).Append(' ')
                 .Append(e.Waves).Append(' ').Append(e.Window).Append(' ').Append(Json.FormatDouble(e.Entropy)).Append(' ')
                 .Append(Json.FormatDouble(e.Restructure)).Append(' ').Append(e.FinalHash).Append('\n');
            return b.ToString();
        }

        // Wraps a player: after it built, the board's build profile, keyed by the wave about to come.
        private sealed class ProfilingPolicy : IPlayerPolicy
        {
            private readonly IPlayerPolicy inner;
            private readonly SimConfig config;
            private readonly SortedDictionary<int, BuildProfile> into;
            private readonly BuildProfiler profiler = BuildProfiler.Default();
            private EmptyBoard empty;
            private TileCoord emptyCore = new TileCoord(int.MinValue, int.MinValue);

            public ProfilingPolicy(IPlayerPolicy inner, SimConfig config, SortedDictionary<int, BuildProfile> into)
            {
                this.inner = inner;
                this.config = config;
                this.into = into;
            }

            public string Name => inner.Name;

            public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
            {
                inner.OnIntermission(sim, send, rng);
                SimState s = sim.State;
                if (s.IsGameOver) return;
                if (empty == null || !emptyCore.Equals(s.Core)) { empty = EmptyBoard.For(sim.Map, config, s.Core); emptyCore = s.Core; }
                into[s.WaveIndex + 1] = profiler.Profile(ProfileInput.From(s, sim.Map, empty));
            }
        }
    }
}
