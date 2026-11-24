using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // `thesis ladder` (WP12): every rung against every scripted player on the game's board, written
    // to <out>/README.md (hypothesis first, then the tables, then the verdict) and <out>/episodes.csv.
    // The hypothesis and its pass and fail numbers were written before the first run (CLAUDE.md §6).
    public static class LadderCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public const string Hypothesis =
@"Ranked by how much the player rebuilds, the rungs come out **random < static escalation < authored heuristic < full director** (CLAUDE.md §6).

- **Measure.** The primary metric, the placement entropy of walls and towers on the 6 x 6 partition (`PlacementEntropy`), over a common window: for each player and seed, the fewest waves any rung lasted, so a rung is not credited for a longer game. Beside it, the mean change of the built features from one build phase to the next (`restructure`, what the full director is rewarded for). Waves survived are reported and not ranked (CLAUDE.md §3).
- **Pass.** For the reacting player (`reactive`, the one that answers waves), every rung's mean entropy is above the one below it.
- **Fail, and what follows.** The full director at or below the heuristic: the learner does not add to the authored rules against a player that reacts. Reported as found; before the pilot the reward and the learner's settings (S2 to S4) are revisited, not the measure. `builder` never rebuilds, so for it the rungs are expected to be close: it is the control that shows the metric is not moved by the waves alone.";

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--seeds", "--first-seed", "--waves", "--threads", "--config", "--out", "--date" }, new string[0]);
            args.NoMorePositionals(0);
            int seeds = args.GetInt("--seeds", 20);
            ulong first = args.GetULong("--first-seed", 201);
            int waves = args.GetInt("--waves", 50);
            int threads = args.GetInt("--threads", Environment.ProcessorCount);
            string date = args.Get("--date", DateTime.Now.ToString("yyyy-MM-dd", Inv));
            string outDir = args.Get("--out", Path.Combine("Results", date + "_ladder"));

            var seedList = new ulong[seeds];
            for (int i = 0; i < seeds; i++) seedList[i] = first + (ulong)i;
            var spec = new Ladder.Spec
            {
                Config = RunCmd.LoadConfig(args.Get("--config", null)),
                Shapes = ShapeLibraryFile.Load(Path.Combine("Maps", "Shapes.json")),
                Towers = TowerLibraryFile.Load(Path.Combine("Maps", "Towers.json")),
                Seeds = seedList, MaxWaves = waves, Threads = threads,
            };

            var clock = Stopwatch.StartNew();
            List<Ladder.Episode> episodes = Ladder.Run(spec);
            double seconds = clock.Elapsed.TotalSeconds;
            Console.WriteLine("[ladder] " + episodes.Count + " episodes on " + threads + " threads in " + seconds.ToString("0.0", Inv) + " s");
            List<Ladder.RungSummary> summary = Ladder.Summarise(episodes, spec);

            var r = new ResultWriter
            {
                Title = "The offline ladder (WP12)",
                Date = date,
                Command = "dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- ladder --seeds " + seeds + " --first-seed " + first + " --waves " + waves,
                Hypothesis = Hypothesis,
            };
            r.Text("Method", "Each episode is one game on the game's 48 x 48 board (`GameMaps`), with the game's numbers (`Maps/SimConfig.json`); map seed and rng seed are the episode's seed, "
                   + seedList[0] + " to " + seedList[seeds - 1] + " (none of them a study seed). The game has no last wave; an episode stops at " + waves + " waves or when the core falls. "
                   + "Players: `builder` (never rebuilds) and `reactive` (answers the last wave: anti-air after flyers, a longer maze after walkers, fewer walls after sappers, power on the entry side after a split wave). "
                   + "Rungs: `random` (a study strategy at random), `escalation` (the static version), `heuristic` (layers 1 and 2), `full` (with the learner, starting cold). "
                   + "The whole set ran in " + seconds.ToString("0", Inv) + " s on " + threads + " threads; the episodes file is the same on any number of threads (`LadderTests`).");

            var rows = new List<string[]>();
            foreach (Ladder.RungSummary s in summary)
                rows.Add(new[] { s.Policy, s.Rung, s.Episodes.ToString(Inv), F(s.Entropy) + " ± " + F(s.EntropySd), F(s.Restructure), s.Waves.ToString("0.0", Inv) });
            r.Table("Result", new[] { "player", "rung", "episodes", "entropy (mean ± sd)", "restructure", "waves survived" }, rows);

            var verdict = new StringBuilder();
            foreach (string policy in spec.Policies)
            {
                bool holds = Ladder.Holds(summary, policy, spec.Rungs, out string where);
                verdict.Append("- `").Append(policy).Append("`: ").Append(holds ? "the ladder **holds**." : "the ladder **does not hold** (" + where + ").").Append('\n');
            }
            bool pass = Ladder.Holds(summary, ReactivePolicy.Id, spec.Rungs, out _);
            verdict.Append("\n**Verdict: ").Append(pass ? "PASS" : "FAIL").Append("** (the pass rule is the reacting player's ladder).\n");
            r.Text("Verdict", verdict.ToString());

            var timing = new List<long>();
            foreach (Ladder.Episode e in episodes) timing.AddRange(e.DirectorMicros);
            timing.Sort();
            if (timing.Count > 0)
                r.Text("The director's time per wave (wall clock, for gate G2)",
                       "Over " + timing.Count + " decisions of the two directors: median " + Ms(timing[timing.Count / 2]) + ", 99th percentile " + Ms(timing[Math.Min(timing.Count - 1, timing.Count * 99 / 100)])
                       + ", most " + Ms(timing[timing.Count - 1]) + ". Measured with " + threads + " episodes running at once, so these are upper bounds; G2 itself is measured on one thread (`thesis gate g2`).");

            r.Text("Limits", "- A scripted player acts only at the start of a build phase; people also build in the middle of a wave.\n"
                             + "- The reacting player remembers one wave and has four answers. It is a stand-in to show the directors can be told apart, not a model of a participant.\n"
                             + "- Entropy is compared over a common window, which is short when one rung ends a game early.");
            Directory.CreateDirectory(outDir);
            r.Write(Path.Combine(outDir, "README.md"));

            var csv = new StringBuilder("policy,rung,seed,waves,coreDestroyed,window,entropy,restructure,placements,finalHash\n");
            foreach (Ladder.Episode e in episodes)
                csv.Append(e.Policy).Append(',').Append(e.Rung).Append(',').Append(e.Seed.ToString(Inv)).Append(',').Append(e.Waves).Append(',')
                   .Append(e.CoreDestroyed ? "true" : "false").Append(',').Append(e.Window).Append(',').Append(Json.FormatDouble(e.Entropy)).Append(',')
                   .Append(Json.FormatDouble(e.Restructure)).Append(',').Append(e.Placements).Append(',').Append(e.FinalHash).Append('\n');
            File.WriteAllText(Path.Combine(outDir, "episodes.csv"), csv.ToString());
            Console.WriteLine("[ladder] " + (pass ? "PASS" : "FAIL") + " -> " + Path.GetFullPath(outDir));
            return 0;
        }

        private static string F(double v) { return v.ToString("0.000", Inv); }

        private static string Ms(long micros) { return (micros / 1000.0).ToString("0.00", Inv) + " ms"; }
    }
}
