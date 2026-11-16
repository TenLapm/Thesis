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
    // `thesis threat`: measures the threat cost table's class factors (WP9, spec gap S6) with
    // BuilderPolicy on game maps (ThreatCalibration). Writes <out>/README.md (hypothesis, method,
    // table) and <out>/results.json. The hypothesis below was written before the first run.
    public static class ThreatCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--seeds", "--waves", "--iterations", "--config", "--out" }, new string[0]);
            args.NoMorePositionals(0);
            int seeds = args.GetInt("--seeds", 20);
            int iterations = args.GetInt("--iterations", 14);
            string[] waveText = args.Get("--waves", "3,5,7").Split(',');
            var waves = new int[waveText.Length];
            for (int i = 0; i < waves.Length; i++) waves[i] = int.Parse(waveText[i], Inv);
            string outDir = args.Get("--out", Path.Combine("Results", DateTime.Now.ToString("yyyy-MM-dd", Inv) + "_threat-factors"));

            SimConfig config = RunCmd.LoadConfig(args.Get("--config", null));
            ShapeDef[] shapes = ShapeLibraryFile.Load(Path.Combine("Maps", "Shapes.json"));
            TowerDef[] towers = TowerLibraryFile.Load(Path.Combine("Maps", "Towers.json"));

            var clock = Stopwatch.StartNew();
            var boards = new List<ThreatCalibration.Board>();
            var perWave = new Dictionary<int, int>();
            for (ulong s = 1; s <= (ulong)seeds; s++)
                foreach (ThreatCalibration.Board b in ThreatCalibration.Boards(config, shapes, towers, s, waves))
                {
                    boards.Add(b);
                    perWave[b.Wave] = (perWave.ContainsKey(b.Wave) ? perWave[b.Wave] : 0) + 1;
                }
            Console.WriteLine("[threat] " + boards.Count + " boards from " + seeds + " seeds (" + clock.Elapsed.TotalSeconds.ToString("0.0", Inv) + " s)");

            // The table's factors are 1 here: the probes are priced as walkers would be.
            MapData anyMap = boards[0].Snapshot.Map;
            var kit = new StrategyKit(config, anyMap, new ThreatCostTable(config, anyMap, 1f, 1f));
            // Every board keeps its own map; the kit only uses the config and the price, which the
            // map does not change (EscalationPlanner reads the map for nothing but its entries).

            var classes = new[] { ("walker", EnemyArchetypes.Basic()), ("sapper", EnemyArchetypes.Sapper()), ("flyer", EnemyArchetypes.Flyer()) };
            var searches = new Dictionary<string, ThreatCalibration.Search>();
            var atOne = new Dictionary<string, double>();
            foreach (var (name, archetype) in classes)
            {
                atOne[name] = ThreatCalibration.MeanLeak(boards, archetype, 1f, kit);
                searches[name] = ThreatCalibration.Match(boards, archetype, ThreatCalibration.HalfLeak, kit, iterations);
                Console.WriteLine("[threat] " + name + ": half-leak strength " + F(searches[name].Strength) + (searches[name].HitBound ? " (BOUND)" : "")
                                  + ", leak at 1 " + F(atOne[name]) + " (" + clock.Elapsed.TotalSeconds.ToString("0", Inv) + " s)");
            }
            double walker = searches["walker"].Strength;
            double sapperFactor = walker / searches["sapper"].Strength;
            double flyerFactor = walker / searches["flyer"].Strength;
            bool bound = searches["walker"].HitBound || searches["sapper"].HitBound || searches["flyer"].HitBound;

            Directory.CreateDirectory(outDir);
            var json = new Dictionary<string, object>
            {
                ["seeds"] = seeds, ["waves"] = waves, ["iterations"] = iterations, ["boards"] = boards.Count,
                ["halfLeak"] = ThreatCalibration.HalfLeak,
                ["sapperFactor"] = sapperFactor, ["flyerFactor"] = flyerFactor, ["anyBoundHit"] = bound,
                ["leakAtStrengthOne"] = atOne, ["searches"] = searches, ["seconds"] = clock.Elapsed.TotalSeconds,
            };
            File.WriteAllText(Path.Combine(outDir, "results.json"), Json.Serialize(json));
            File.WriteAllText(Path.Combine(outDir, "README.md"), Readme(seeds, waves, iterations, boards.Count, perWave, searches, atOne, sapperFactor, flyerFactor, bound, clock.Elapsed.TotalSeconds));
            Console.WriteLine("[threat] sapper factor " + F(sapperFactor) + ", flyer factor " + F(flyerFactor) + " -> " + outDir);
            return 0;
        }

        private static string Readme(int seeds, int[] waves, int iterations, int boards, Dictionary<int, int> perWave,
                                     Dictionary<string, ThreatCalibration.Search> searches, Dictionary<string, double> atOne,
                                     double sapperFactor, double flyerFactor, bool bound, double seconds)
        {
            var md = new StringBuilder();
            md.Append("# The cost table's class factors, measured (").Append(DateTime.Now.ToString("yyyy-MM-dd", Inv)).Append(")\n\n");
            md.Append("Written by `thesis threat` (WORKPLAN WP9, spec gap S6). Reproduce with:\n\n```\n");
            md.Append("dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- threat --seeds ").Append(seeds)
              .Append(" --waves ").Append(string.Join(",", waves)).Append(" --iterations ").Append(iterations).Append("\n```\n\n");

            md.Append("## Hypothesis (stated before the first run)\n\n");
            md.Append("The student decided on 2026-11-16 that an enemy costs hit points × speed × a factor for its movement class, and that\n");
            md.Append("the flyer and sapper factors are measured, not guessed.\n\n");
            md.Append("- **Flyers are harder to stop than walkers** per unit of hit points × speed: they fly over every wall, take the\n");
            md.Append("  straight line, and only towers that hit flyers can shoot them. Expected: flyer factor above 1.\n");
            md.Append("- **Sappers are about as hard to stop as walkers** on these boards: the scripted player builds walls only under\n");
            md.Append("  its towers, so there is little for a sapper to dig through, and sappers are slower. Expected: sapper factor\n");
            md.Append("  between 0.67 and 1.5.\n");
            md.Append("- **Inconclusive** if any search hits its bound (a strength outside 1/16 to 16): then the factor is not measured.\n\n");

            md.Append("## Method\n\n");
            md.Append("- **Boards.** `BuilderPolicy` plays the game's own rules (`Maps/SimConfig.json`, `Maps/Towers.json`) against the static\n");
            md.Append("  baseline on the map the game generates from each seed (`GameMaps`, 48 × 48). At the build phase of waves ")
              .Append(string.Join(", ", waves)).Append(", after the player has built, the board is copied. Seeds 1 to ").Append(seeds).Append(": ")
              .Append(boards).Append(" boards (");
            var parts = new List<string>();
            foreach (int w in waves) parts.Add("wave " + w + ": " + (perWave.ContainsKey(w) ? perWave[w] : 0));
            md.Append(string.Join(", ", parts)).Append("; a player who has lost gives no later boards).\n");
            md.Append("- **Probe.** On a copy, the planned wave is dropped and one wave of a single type is sent from the baseline's entry,\n");
            md.Append("  priced at the wave's budget × a strength (walkers, sappers and flyers are made from the baseline's walker with\n");
            md.Append("  their `EnemyArchetype`, fitted to the price as the strategies are). The copy's core cannot fall, so every enemy\n");
            md.Append("  that gets through is counted. Leak share = enemies that reached the core ÷ enemies sent.\n");
            md.Append("- **Half-leak strength.** For each type, bisection on log₂ strength in [−4, 4] (").Append(iterations)
              .Append(" steps) for the strength at which the mean leak share over all boards is 0.5.\n");
            md.Append("- **Factor** = the walkers' half-leak strength ÷ the type's. A type that gets half through on half the walkers'\n");
            md.Append("  budget has factor 2.\n\n");

            md.Append("## Result").Append(bound ? ": INCONCLUSIVE (a search hit its bound)" : "").Append("\n\n");
            md.Append("| Type | Mean leak share at strength 1 | Half-leak strength | Leak at that strength | Factor |\n|---|---|---|---|---|\n");
            foreach (string name in new[] { "walker", "sapper", "flyer" })
            {
                ThreatCalibration.Search s = searches[name];
                string factor = name == "walker" ? "1 (reference)" : F(name == "sapper" ? sapperFactor : flyerFactor);
                md.Append("| ").Append(name).Append(" | ").Append(F(atOne[name])).Append(" | ").Append(F(s.Strength)).Append(s.HitBound ? " (bound)" : "")
                  .Append(" | ").Append(F(s.LeakAtStrength)).Append(" | ").Append(factor).Append(" |\n");
            }
            md.Append("\nBisection steps (strength, mean leak share):\n\n");
            foreach (string name in new[] { "walker", "sapper", "flyer" })
            {
                md.Append("- ").Append(name).Append(": ");
                var steps = new List<string>();
                foreach (double[] st in searches[name].Steps) steps.Add("(" + F(st[0]) + ", " + F(st[1]) + ")");
                md.Append(string.Join(" ", steps)).Append('\n');
            }
            md.Append("\nRun time ").Append(seconds.ToString("0", Inv)).Append(" s on this machine.\n\n");

            md.Append("## Deviations\n\n");
            md.Append("- Factors are compared where half a wave gets through, not at the normal budget: a player still alive at these\n");
            md.Append("  waves lets almost nothing through at strength 1, and a share near 0 says little.\n");
            md.Append("- One scripted player, one entry (the baseline's), three wave numbers. The factors hold for boards like these; a\n");
            md.Append("  human who mazes more would give sappers more to dig through.\n");
            return md.ToString();
        }

        private static string F(double v) { return v.ToString("0.000", Inv); }
    }
}
