using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Core;
using Thesis.Learning;
using Thesis.Learning.Synthetic;

namespace Thesis.Cli
{
    // synth [--seeds 20] [--speed-episodes 1000000] [--estimator binned] [--out dir]
    // G1-synthetic (WORKPLAN WP7): every policy on ThresholdBanditEnv, 300 episodes
    // per seed; the calibration report next to it; the review probe (G = 1 against a
    // flip halfway); and one long run timed, for "10⁶ episodes in seconds".
    // Writes <out>/results.json and <out>/README.md (default Results/<today>_g1-synth).
    internal static class SynthCmd
    {
        private const int Episodes = 300;
        private const int WindowFrom = 250, WindowTo = 300;      // episodes 251–300
        private const int ProbeEpisodes = 600, ProbeFlip = 300;
        private const double PassNumber = 0.85, FailNumber = 0.70;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--seeds", "--speed-episodes", "--estimator", "--out" }, new string[0]);
            args.NoMorePositionals(0);
            int seeds = args.GetInt("--seeds", 20);
            int speedEpisodes = args.GetInt("--speed-episodes", 1000000);
            string estimator = args.Get("--estimator", "binned");
            if (estimator != "binned") throw new ArgumentException("synth: only --estimator binned exists so far");
            if (seeds < 1) throw new ArgumentException("synth: --seeds must be at least 1");
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string outDir = args.Get("--out", Path.Combine("Results", today + "_g1-synth"));

            var g1 = new SyntheticRun.Options { Episodes = Episodes };
            var policies = new[] { SyntheticPolicy.Thompson, SyntheticPolicy.DirectorCorrection, SyntheticPolicy.UniformRandom, SyntheticPolicy.RoundRobin };
            var rows = new List<PolicyRow>();
            var calibration = new CalibrationReport();
            foreach (SyntheticPolicy p in policies)
            {
                var row = new PolicyRow { Policy = p.ToString(), PerSeed = new double[seeds], Curve = new double[Episodes / 25] };
                for (int s = 1; s <= seeds; s++)
                {
                    SyntheticRun.Result r = SyntheticRun.Run(p, g1, (ulong)s);
                    row.PerSeed[s - 1] = SyntheticRun.BestShare(r, WindowFrom, WindowTo);
                    for (int b = 0; b < row.Curve.Length; b++) row.Curve[b] += SyntheticRun.BestShare(r, b * 25, b * 25 + 25) / seeds;
                    if (p == SyntheticPolicy.Thompson) calibration.Merge(r.Calibration);
                }
                row.Mean = Mean(row.PerSeed);
                row.Min = Min(row.PerSeed);
                rows.Add(row);
            }

            double learner = rows[0].Mean;
            bool beats = learner > rows[2].Mean && learner > rows[3].Mean;
            string verdict = learner >= PassNumber && beats ? "PASS" : learner < FailNumber || !beats ? "FAIL" : "BETWEEN (neither pass nor fail number)";

            var probe = new List<ProbeRow>();
            foreach (double g in new[] { 0.95, 1.0 })
            {
                var o = new SyntheticRun.Options { Episodes = ProbeEpisodes, FlipAtEpisode = ProbeFlip, Discount = g };
                probe.Add(new ProbeRow
                {
                    Discount = g,
                    BeforeFlip = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, o, seeds, 250, 300),
                    First100AfterFlip = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, o, seeds, 300, 400),
                    Last100 = SyntheticRun.MeanBestShare(SyntheticPolicy.Thompson, o, seeds, 500, 600),
                });
            }

            var sw = Stopwatch.StartNew();
            SyntheticRun.Result longRun = SyntheticRun.Run(SyntheticPolicy.Thompson, new SyntheticRun.Options { Episodes = speedEpisodes }, 1UL);
            sw.Stop();
            double speedShare = SyntheticRun.BestShare(longRun, Math.Max(0, speedEpisodes - 10000), speedEpisodes);

            var summary = new Summary
            {
                Command = "dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- synth --seeds " + seeds + " --speed-episodes " + speedEpisodes,
                Env = new ThresholdBanditEnv.Options(),
                Episodes = Episodes,
                Window = "episodes " + (WindowFrom + 1) + "-" + WindowTo,
                Seeds = seeds,
                Discount = BinnedPosterior.DefaultDiscount,
                Policies = rows,
                Verdict = verdict,
                Brier = calibration.BrierScore,
                Ece = calibration.ExpectedCalibrationError,
                CalibrationBins = calibration.Bins(),
                Probe = probe,
                SpeedEpisodes = speedEpisodes,
                SpeedSeconds = sw.Elapsed.TotalSeconds,
                SpeedLast10kBestShare = speedShare,
            };

            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "results.json"), Json.SerializeIndented(summary));
            File.WriteAllText(Path.Combine(outDir, "README.md"), Readme(summary, today));
            Console.WriteLine("G1-synthetic: learner " + F(learner) + " (" + verdict + "), random " + F(rows[2].Mean) + ", round-robin " + F(rows[3].Mean)
                              + "; ECE " + F(summary.Ece) + "; " + speedEpisodes + " episodes in " + summary.SpeedSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s");
            Console.WriteLine("[synth] -> " + Path.GetFullPath(outDir));
            return 0;
        }

        private sealed class PolicyRow
        {
            public string Policy;
            public double Mean;
            public double Min;
            public double[] PerSeed;
            public double[] Curve;   // mean best-arm share per block of 25 episodes
        }

        private sealed class ProbeRow
        {
            public double Discount;
            public double BeforeFlip;
            public double First100AfterFlip;
            public double Last100;
        }

        private sealed class Summary
        {
            public string Command;
            public ThresholdBanditEnv.Options Env;
            public int Episodes;
            public string Window;
            public int Seeds;
            public double Discount;
            public List<PolicyRow> Policies;
            public string Verdict;
            public double Brier;
            public double Ece;
            public IReadOnlyList<CalibrationReport.Bin> CalibrationBins;
            public List<ProbeRow> Probe;
            public int SpeedEpisodes;
            public double SpeedSeconds;
            public double SpeedLast10kBestShare;
        }

        private static string Readme(Summary s, string today)
        {
            var b = new StringBuilder();
            b.AppendLine("# G1-synthetic: does the learner find the best strategy? (" + today + ")");
            b.AppendLine();
            b.AppendLine("Written by `thesis synth` (WORKPLAN WP7). Reproduce with:");
            b.AppendLine();
            b.AppendLine("```");
            b.AppendLine(s.Command);
            b.AppendLine("```");
            b.AppendLine();
            b.AppendLine("## Hypothesis (stated before the table)");
            b.AppendLine();
            b.AppendLine("Gate G1 (`CLAUDE.md` §6): on a problem whose best arm flips at a threshold in the context, `BinnedPosterior` picks the best arm");
            b.AppendLine("at least 85 % of the time within 300 episodes, averaged over " + s.Seeds + " seeds, and beats uniform-random and round-robin.");
            b.AppendLine("Below 70 %, or no separation from the baselines, is the fail number: an implementation defect, or the context carries no information.");
            b.AppendLine();
            b.AppendLine("## The problem (fixed on 2026-11-10, before the first run)");
            b.AppendLine();
            b.AppendLine("No spec gives these numbers; they were chosen once and not tuned against results.");
            b.AppendLine();
            b.AppendLine("- " + s.Env.Arms + " arms, context x ~ U[0, 1), " + s.Env.Bins + " equal bins.");
            b.AppendLine("- x < " + F(s.Env.Threshold) + ": arm " + s.Env.LowBestArm + " pays 1 with p = " + F(s.Env.BestP) + "; x >= " + F(s.Env.Threshold) + ": arm " + s.Env.HighBestArm
                         + " does. Every other arm pays with p = " + F(s.Env.OtherP) + ".");
            b.AppendLine("- The threshold is a bin edge, so the bins can represent it exactly. A threshold inside a bin would cap the best possible share in that bin.");
            b.AppendLine("- Learner: Thompson sampling over the 20 Beta posteriors (largest θ), discount G = " + F(s.Discount) + ".");
            b.AppendLine("- **\"Within 300 episodes\" is read as the best-arm share over " + s.Window + "**, per seed, then averaged. The spec does not say which episodes count.");
            b.AppendLine();
            b.AppendLine("## Result: " + s.Verdict);
            b.AppendLine();
            b.AppendLine("| Policy | Mean best-arm share, " + s.Window + " | Lowest seed |");
            b.AppendLine("|---|---|---|");
            foreach (PolicyRow r in s.Policies) b.AppendLine("| " + r.Policy + " | " + F(r.Mean) + " | " + F(r.Min) + " |");
            b.AppendLine();
            b.AppendLine("`DirectorCorrection` is not part of G1. It is the director's rule with every authored score at 0: the largest `Correction.Value`,");
            b.AppendLine("where a cold cell counts exactly 0. It shows spec gap S2 (WP11): a cold arm loses to any warm arm whose θ is above 0.5 and may never be tried.");
            b.AppendLine();
            b.AppendLine("Learning curve of `Thompson` (mean best-arm share per block of 25 episodes):");
            b.AppendLine();
            b.AppendLine("| Episodes | " + string.Join(" | ", Blocks(s.Policies[0].Curve.Length)) + " |");
            b.AppendLine("|---" + Repeat("|---", s.Policies[0].Curve.Length) + "|");
            b.AppendLine("| share | " + string.Join(" | ", Array.ConvertAll(s.Policies[0].Curve, F)) + " |");
            b.AppendLine();
            b.AppendLine("## Calibration (a diagnostic, not a gate)");
            b.AppendLine();
            b.AppendLine("Pairs of (posterior mean of the chosen arm before the update, the 0/1 reward), all " + s.Episodes + " episodes of every `Thompson` seed.");
            b.AppendLine();
            b.AppendLine("Brier " + F(s.Brier) + ", ECE (10 bins) " + F(s.Ece) + ".");
            b.AppendLine();
            b.AppendLine("| Predicted | Pairs | Mean predicted | Mean realised |");
            b.AppendLine("|---|---|---|---|");
            foreach (CalibrationReport.Bin bin in s.CalibrationBins)
                if (bin.Count > 0) b.AppendLine("| " + F(bin.Lower) + "-" + F(bin.Upper) + " | " + bin.Count + " | " + F(bin.MeanPredicted) + " | " + F(bin.MeanRealised) + " |");
            b.AppendLine();
            b.AppendLine("## Review probe: is the discount doing its job?");
            b.AppendLine();
            b.AppendLine("The two best arms swap at episode " + ProbeFlip + " of " + ProbeEpisodes + ". Expected: without the discount (G = 1) the learner adapts measurably worse.");
            b.AppendLine();
            b.AppendLine("| G | Episodes 251-300 | 301-400 (just after the flip) | 501-600 |");
            b.AppendLine("|---|---|---|---|");
            foreach (ProbeRow p in s.Probe) b.AppendLine("| " + F(p.Discount) + " | " + F(p.BeforeFlip) + " | " + F(p.First100AfterFlip) + " | " + F(p.Last100) + " |");
            b.AppendLine();
            b.AppendLine("## Speed");
            b.AppendLine();
            b.AppendLine(s.SpeedEpisodes + " episodes of one `Thompson` run took " + s.SpeedSeconds.ToString("0.00", CultureInfo.InvariantCulture)
                         + " s on this machine (wall clock, a single run); best-arm share over its last 10,000 episodes " + F(s.SpeedLast10kBestShare) + ".");
            b.AppendLine();
            b.AppendLine("## Deviations");
            b.AppendLine();
            b.AppendLine("- The measurement window (" + s.Window + ") and the problem's numbers were chosen here, because the spec gives neither.");
            return b.ToString();
        }

        private static IEnumerable<string> Blocks(int n)
        {
            for (int i = 0; i < n; i++) yield return (i * 25 + 1) + "-" + (i * 25 + 25);
        }

        private static string Repeat(string s, int n)
        {
            var b = new StringBuilder();
            for (int i = 0; i < n; i++) b.Append(s);
            return b.ToString();
        }

        private static double Mean(double[] a) { double s = 0; foreach (double x in a) s += x; return s / a.Length; }
        private static double Min(double[] a) { double m = double.MaxValue; foreach (double x in a) m = Math.Min(m, x); return m; }
        private static string F(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }
    }
}
