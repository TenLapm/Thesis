using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;
using Thesis.Learning;
using Thesis.Learning.Synthetic;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Gates G1 to G3 (CLAUDE.md §6, WORKPLAN WP13). Each has its pass and fail number fixed here,
    // before it is run, and the action a failure implies; `thesis gate` writes one Results folder
    // per gate with the numbers stated first.
    //
    // G1 and G3 run on the synthetic threshold problem (ThresholdBanditEnv), where the best strategy
    // for each context is known; against a real board nobody knows it (the student, 2026-11-24).
    // G2 times the real director on the real board.
    public static class Gates
    {
        // ------------------------------------------------------------------ G1: the learner works

        public const double G1Pass = 0.85, G1Fail = 0.70;
        public const int G1Episodes = 300, G1WindowFrom = 250;

        public sealed class G1Result
        {
            public double Learner, Random, RoundRobin;
            public int Seeds;
            public string Verdict;   // PASS, FAIL or BETWEEN
        }

        public static G1Result G1(int seeds)
        {
            var o = new SyntheticRun.Options { Episodes = G1Episodes };
            var r = new G1Result
            {
                Seeds = seeds,
                Learner = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, o, seeds, G1WindowFrom, G1Episodes),
                Random = SyntheticRun.MeanBestShare(SyntheticPolicy.UniformRandom, o, seeds, G1WindowFrom, G1Episodes),
                RoundRobin = SyntheticRun.MeanBestShare(SyntheticPolicy.RoundRobin, o, seeds, G1WindowFrom, G1Episodes),
            };
            bool beats = r.Learner > r.Random && r.Learner > r.RoundRobin;
            r.Verdict = r.Learner >= G1Pass && beats ? "PASS" : r.Learner < G1Fail || !beats ? "FAIL" : "BETWEEN";
            return r;
        }

        // ------------------------------------------------------------------ G2: costs nothing visible

        public const double G2PassMs = 16.0, G2FailMs = 33.0;

        public sealed class G2Result
        {
            public int Decisions;
            public double MedianMs, P99Ms, MaxMs;
            public string Verdict;
        }

        // The full director against the reacting player on the game's board, one episode at a time
        // (nothing else running), every decision timed by the director itself (DirectorTrace).
        // The first episode is played once untimed, so the timing is not the JIT's.
        public static G2Result G2(SimConfig config, ShapeDef[] shapes, TowerDef[] towers, ulong[] seeds, int waves)
        {
            var micros = new List<long>();
            for (int k = -1; k < seeds.Length; k++)
            {
                ulong seed = seeds[Math.Max(0, k)];
                MapData map = GameMaps.Generate(seed);
                var d = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, config, map, seed);
                EpisodeResult r = EpisodeRunner.Run(new EpisodeOptions
                {
                    Config = config, Map = map, Shapes = shapes, Towers = towers, Seed = seed,
                    Planner = WaveDirector.FullId, PlannerInstance = d, Policy = new ReactivePolicy(), MaxWaves = waves,
                });
                if (k < 0) continue; // the warm-up
                for (int w = 1; w <= r.WavesResolved + 1; w++)
                {
                    DirectorTrace t = d.TraceFor(w);
                    if (t != null) micros.Add(t.ElapsedMicroseconds);
                }
            }
            micros.Sort();
            var g = new G2Result { Decisions = micros.Count };
            if (micros.Count == 0) { g.Verdict = "FAIL"; return g; }
            g.MedianMs = micros[micros.Count / 2] / 1000.0;
            g.P99Ms = micros[Math.Min(micros.Count - 1, micros.Count * 99 / 100)] / 1000.0;
            g.MaxMs = micros[micros.Count - 1] / 1000.0;
            g.Verdict = g.MaxMs <= G2PassMs ? "PASS" : g.MaxMs > G2FailMs ? "FAIL" : "BETWEEN";
            return g;
        }

        // ------------------------------------------------------------------ G3: graduated beats binary

        // A reward built like the director's (RewardEvaluator): only the rebuild depends on the
        // strategy; survival, pressure and novelty are the same noise for every strategy. Numbers
        // fixed before the run, from the ladder (2026-11-24: the reacting player's mean rebuild per
        // wave against the two directors was 0.11): the best strategy for the context makes the
        // player rebuild 0.14 on average, every other one 0.08, each drawn evenly from 0 to twice that.
        public const double G3BestRebuild = 0.14, G3OtherRebuild = 0.08, G3Survive = 0.85;
        public const int G3Episodes = 3000, G3Window = 50, G3Seeds = 20;

        public sealed class G3Result
        {
            public int[] Graduated, Binary;    // episodes to reach G1's accuracy, per seed (G3Episodes when never)
            public double GraduatedMedian, BinaryMedian;
            public int GraduatedReached, BinaryReached;
            public string Verdict;             // PASS, or FAIL (no difference or binary better: ship binary)
        }

        public static float G3Reward(bool binary, int arm, double x, int episode, IRandom rng, ThresholdBanditEnv env, DirectorConfig cfg)
        {
            double mean = arm == env.BestArm(x, episode) ? G3BestRebuild : G3OtherRebuild;
            double raw = rng.NextDouble() * 2.0 * mean;
            double survived = rng.NextDouble() < G3Survive ? 1.0 : 0.0;
            double pressure = rng.NextDouble();
            double novelty = rng.NextDouble();
            if (binary) return raw >= cfg.Tau && survived > 0.0 ? 1f : 0f;
            double restructure = Math.Min(1.0, raw / cfg.Tau);
            return (float)(cfg.RestructureWeight * restructure + cfg.SurvivedWeight * survived + cfg.PressureWeight * pressure + cfg.NoveltyWeight * novelty);
        }

        public static int EpisodesToAccuracy(SyntheticRun.Result r)
        {
            int hits = 0;
            for (int e = 0; e < r.Best.Length; e++)
            {
                if (r.Best[e]) hits++;
                if (e >= G3Window && r.Best[e - G3Window]) hits--;
                if (e + 1 >= G3Window && hits >= G1Pass * G3Window) return e + 1;
            }
            return r.Best.Length;
        }

        public static G3Result G3(int seeds = G3Seeds, int episodes = G3Episodes)
        {
            DirectorConfig cfg = DirectorConfig.Provisional();
            var env = new ThresholdBanditEnv(new ThresholdBanditEnv.Options());
            var g = new G3Result { Graduated = new int[seeds], Binary = new int[seeds] };
            for (int k = 0; k < 2; k++)
            {
                bool binary = k == 1;
                var o = new SyntheticRun.Options { Episodes = episodes, Pull = (arm, x, e, rng) => G3Reward(binary, arm, x, e, rng, env, cfg) };
                for (int s = 1; s <= seeds; s++)
                {
                    int n = EpisodesToAccuracy(SyntheticRun.Run(SyntheticPolicy.Thompson, o, (ulong)s));
                    (binary ? g.Binary : g.Graduated)[s - 1] = n;
                    if (n < episodes) { if (binary) g.BinaryReached++; else g.GraduatedReached++; }
                }
            }
            g.GraduatedMedian = Median(g.Graduated);
            g.BinaryMedian = Median(g.Binary);
            g.Verdict = g.GraduatedMedian < g.BinaryMedian ? "PASS" : "FAIL";
            return g;
        }

        private static double Median(int[] values)
        {
            var v = (int[])values.Clone();
            Array.Sort(v);
            return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2.0;
        }
    }
}
