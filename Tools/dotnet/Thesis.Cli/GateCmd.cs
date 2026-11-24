using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // `thesis gate g1|g2|g3|all` (WP13). Each gate writes Results/<date>_gate-<name>/README.md with its
    // pass and fail numbers (CLAUDE.md §6) stated before the result, and the decision the result implies.
    public static class GateCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--date", "--config" }, new string[0]);
            string which = args.Positional(0, "the gate (g1, g2, g3 or all)");
            args.NoMorePositionals(1);
            string date = args.Get("--date", DateTime.Now.ToString("yyyy-MM-dd", Inv));
            bool all = which == "all";
            if (!all && which != "g1" && which != "g2" && which != "g3") throw new ArgumentException("gate: g1, g2, g3 or all, not '" + which + "'");
            if (all || which == "g1") G1(date);
            if (all || which == "g2") G2(date, RunCmd.LoadConfig(args.Get("--config", null)));
            if (all || which == "g3") G3(date);
            return 0;
        }

        private static string Out(string date, string gate) { return Path.Combine("Results", date + "_gate-" + gate, "README.md"); }

        private static void G1(string date)
        {
            Gates.G1Result r = Gates.G1(20);
            var w = new ResultWriter
            {
                Title = "Gate G1: the learner works", Date = date,
                Command = "dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g1",
                Hypothesis = "On the synthetic threshold problem (`ThresholdBanditEnv`: 4 strategies, 5 bins, the best strategy flips at a context threshold of 0.6), Thompson sampling over `BinnedPosterior` picks the best strategy in **at least 85 %** of episodes 251 to 300, averaged over 20 seeds, **and** more often than uniform random and round-robin. "
                             + "**Fail:** below 70 %, or no better than either baseline: an implementation defect, or the context carries no information; revisit the features. Between the two numbers: neither; reported.",
            };
            w.Table("Result", new[] { "policy", "best-strategy share, episodes 251-300" }, new[]
            {
                new[] { "Thompson (the learner)", F(r.Learner) },
                new[] { "uniform random", F(r.Random) },
                new[] { "round-robin", F(r.RoundRobin) },
            });
            w.Text("Verdict", "**" + r.Verdict + "** (" + r.Seeds + " seeds). The same number as `Results/2026-11-10_g1-synth` (0.851), a narrow pass. "
                              + "G1 is run on the synthetic problem because against a real board the best strategy is not known (the student, 2026-11-24); it shows the learner can learn a threshold, not that it learns from people. CLAUDE.md §6: G1 passing does not license claiming G5.");
            w.Write(Out(date, "g1"));
            Console.WriteLine("[gate] G1 " + r.Verdict + ": learner " + F(r.Learner) + ", random " + F(r.Random) + ", round-robin " + F(r.RoundRobin));
        }

        private static void G2(string date, SimConfig config)
        {
            var seeds = new ulong[20];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = 201 + (ulong)i;
            Gates.G2Result r = Gates.G2(config, ShapeLibraryFile.Load(Path.Combine("Maps", "Shapes.json")), TowerLibraryFile.Load(Path.Combine("Maps", "Towers.json")), seeds, 50);
            var w = new ResultWriter
            {
                Title = "Gate G2: the director costs nothing visible", Date = date,
                Command = "dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g2",
                Hypothesis = "The full director's work at a wave boundary (profile, vetoes, scores, the learner, the reward of the wave before; timed inside `WaveDirector`) takes **at most 16 ms** at every boundary: the full director against the reacting player on the game's 48 x 48 board, seeds 201 to 220, up to 50 waves, one episode at a time. "
                             + "**Fail:** any boundary above 33 ms (a visible frame spike): make the director's work incremental. Between: reported.",
            };
            w.Table("Result", new[] { "decisions", "median", "99th percentile", "most" }, new[] { new[] { r.Decisions.ToString(Inv), Ms(r.MedianMs), Ms(r.P99Ms), Ms(r.MaxMs) } });
            w.Text("Verdict", "**" + r.Verdict + "**.\n\n**Limit.** Measured headless under .NET on the development machine. The study build runs on Mono (D5), whose code is slower; the same director in the Unity editor has not been timed yet. The F9 overlay shows each decision's time while playing.");
            w.Write(Out(date, "g2"));
            Console.WriteLine("[gate] G2 " + r.Verdict + ": " + r.Decisions + " decisions, median " + Ms(r.MedianMs) + ", p99 " + Ms(r.P99Ms) + ", most " + Ms(r.MaxMs));
        }

        private static void G3(string date)
        {
            Gates.G3Result r = Gates.G3();
            var w = new ResultWriter
            {
                Title = "Gate G3: the graduated reward beats the binary one", Date = date,
                Command = "dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g3",
                Hypothesis = "With the graduated reward (0.45 restructure + 0.25 survived + 0.20 pressure + 0.10 novelty), the learner reaches G1's accuracy (85 % best-strategy picks over 50 episodes in a row) **in fewer episodes** than with the binary control (`restructure >= tau && survived`), comparing the medians over 20 seeds.\n\n"
                             + "**The problem** (fixed before the run): the threshold problem of G1 decides which strategy is best; a pull's reward is built like the director's: the player's rebuild is drawn evenly between 0 and twice its mean, " + Gates.G3BestRebuild.ToString(Inv) + " for the best strategy and " + Gates.G3OtherRebuild.ToString(Inv)
                             + " for the others (the ladder's reacting player rebuilt 0.11 a wave against the directors); survival (" + Gates.G3Survive.ToString(Inv) + "), pressure and novelty are the same noise for every strategy; tau = 0.2. Runs stop at " + Gates.G3Episodes + " episodes.\n\n"
                             + "**Fail** (no difference, or the binary control is faster): ship the binary reward, and report it (CLAUDE.md §6).",
            };
            var rows = new List<string[]>();
            for (int s = 0; s < r.Graduated.Length; s++) rows.Add(new[] { (s + 1).ToString(Inv), Episodes(r.Graduated[s]), Episodes(r.Binary[s]) });
            w.Table("Result, per seed (episodes to reach the accuracy)", new[] { "seed", "graduated", "binary" }, rows);
            w.Text("Summary", "Median: graduated " + r.GraduatedMedian.ToString("0.#", Inv) + ", binary " + r.BinaryMedian.ToString("0.#", Inv) + ". Reached within " + Gates.G3Episodes + ": graduated " + r.GraduatedReached + " of 20, binary " + r.BinaryReached + " of 20.\n\n**Verdict: " + r.Verdict + "**."
                                  + (r.Verdict == "PASS" ? "" : " By the rule fixed beforehand, the director ships with the binary reward. **Not switched yet:** `DirectorConfig.Provisional()` still uses the graduated one; the switch changes the study and is the student's and the supervisor's to make, with this result in hand."));
            w.Text("Limits", "The result is about this problem. How far a rebuild depends on the strategy, and how noisy the other terms are, decide how much the extra information in the graduated reward helps; both are guesses until the pilot's telemetry measures them.");
            w.Write(Out(date, "g3"));
            Console.WriteLine("[gate] G3 " + r.Verdict + ": median graduated " + r.GraduatedMedian + ", binary " + r.BinaryMedian + " (reached " + r.GraduatedReached + " / " + r.BinaryReached + ")");
        }

        private static string Episodes(int n) { return n >= Gates.G3Episodes ? "not reached" : n.ToString(Inv); }

        private static string F(double v) { return v.ToString("0.000", Inv); }

        private static string Ms(double ms) { return ms.ToString("0.00", Inv) + " ms"; }
    }
}
