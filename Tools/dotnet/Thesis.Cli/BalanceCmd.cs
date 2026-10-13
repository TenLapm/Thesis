using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // `thesis balance`: the balance sanity check of WP-C5. Plays the scripted players
    // over a set of seeds against the static baseline and against the development
    // planner that sends every enemy type, and reports
    //
    //   * how far each player gets,
    //   * what each tower type cost, did and how often it was bought,
    //   * what each kind of wave of the development planner got through.
    //
    // It prints Markdown and writes the same to <out>/balance.md, so a result file can
    // quote it as it is (CLAUDE.md 7). It judges nothing: the hypotheses and what the
    // tables mean for them are written by hand in the result's README.
    public static class BalanceCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class TowerTally
        {
            public int Bought;
            public double Spent;
            public double Damage;
            public long Kills;
            public double BudgetMinutes;   // price x minutes on the board, summed
        }

        private sealed class WaveKindTally
        {
            public int Waves;
            public long Spawned, Killed, Leaked;
        }

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--out", "--map", "--shapes", "--towers", "--config", "--seeds", "--waves", "--policies" }, new string[0]);
            args.NoMorePositionals(0);

            MapData map = MapData.Load(args.Get("--map", Path.Combine("Maps", "SampleScene.map.json")));
            ShapeDef[] shapes = ShapeLibraryFile.Load(args.Get("--shapes", Path.Combine("Maps", "Shapes.json")));
            TowerDef[] towers = TowerLibraryFile.Load(args.Get("--towers", Path.Combine("Maps", "Towers.json")));
            SimConfig config = RunCmd.LoadConfig(args.Get("--config", null));
            int seeds = args.GetInt("--seeds", 10);
            int waves = args.GetInt("--waves", 25);
            string[] policies = args.Get("--policies", "sentry,mixed,restless,idle").Split(',');
            string[] planners = { EscalationPlanner.Id, ClassCyclePlanner.Id };

            var md = new StringBuilder();
            md.Append("Map ").Append(map.Name).Append(' ').Append(map.Width).Append('x').Append(map.Height)
              .Append(", ").Append(towers.Length).Append(" towers, seeds 1 to ").Append(seeds).Append(", up to ").Append(waves).Append(" waves.\n\n");

            md.Append("### The roster\n\n| Tower | Price | Type | Damage | Every | Range | Splash | Slow | Flyers | Damage per second per budget (one target) |\n|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (TowerDef t in towers)
            {
                double dps = t.Damage * 50.0 / t.FireIntervalTicks;
                md.Append("| ").Append(t.Id).Append(" | ").Append(t.Cost).Append(" | ").Append(t.DamageType.ToString().ToLowerInvariant())
                  .Append(" | ").Append(F(t.Damage, "0.##")).Append(" | ").Append(F(t.FireIntervalTicks / 50.0, "0.##")).Append(" s | ").Append(F(t.RangeTiles, "0.#"))
                  .Append(" | ").Append(t.SplashRadiusTiles > 0f ? F(t.SplashRadiusTiles, "0.##") : "-")
                  .Append(" | ").Append(t.SlowTicks > 0 ? "x" + F(t.SlowFactor, "0.##") + " for " + F(t.SlowTicks / 50.0, "0.#") + " s" : "-")
                  .Append(" | ").Append(t.CanHitFlying ? "yes" : "no").Append(" | ").Append(F(dps / Math.Max(1, t.Cost), "0.00")).Append(" |\n");
            }

            foreach (string planner in planners)
            {
                var tally = new Dictionary<string, TowerTally>();
                foreach (TowerDef t in towers) tally[t.Id] = new TowerTally();
                var kinds = new WaveKindTally[ClassCyclePlanner.CycleLength];
                for (int i = 0; i < kinds.Length; i++) kinds[i] = new WaveKindTally();

                md.Append("\n### Against `").Append(planner).Append("`\n\n");
                md.Append("Wave each player lost in, by seed (`").Append(waves).Append("+` = still alive after wave ").Append(waves).Append(", with the core's health).\n\n| Policy |");
                for (int s = 1; s <= seeds; s++) md.Append(' ').Append(s).Append(" |");
                md.Append(" Median |\n|---|");
                for (int s = 1; s <= seeds; s++) md.Append("---|");
                md.Append("---|\n");

                foreach (string policy in policies)
                {
                    var reached = new List<int>();
                    md.Append("| ").Append(policy).Append(" |");
                    for (int seed = 1; seed <= seeds; seed++)
                    {
                        EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
                        {
                            Config = config.Clone(),
                            Map = map,
                            Shapes = shapes,
                            Towers = towers,
                            Seed = (ulong)seed,
                            Planner = planner,
                            Policy = Registry.CreatePolicy(policy.Trim()),
                            MaxWaves = waves,
                        });

                        bool survived = !run.GameOver;
                        // The wave a player lost in is the one after the last it resolved alive.
                        int lostIn = survived ? waves + 1 : run.Outcomes.Count;
                        reached.Add(lostIn);
                        md.Append(' ').Append(survived ? waves + "+ (" + run.CoreHp + ")" : lostIn.ToString(Inv)).Append(" |");

                        foreach (TowerSummary t in run.Towers)
                        {
                            TowerTally row = tally[t.Id];
                            row.Bought++;
                            row.Spent += t.PricePaid;
                            row.Damage += t.DamageDealt;
                            row.Kills += t.Kills;
                            row.BudgetMinutes += t.PricePaid * (t.TicksOnBoard * config.TickSeconds / 60.0);
                        }
                        foreach (WaveOutcome o in run.Outcomes)
                        {
                            WaveKindTally k = kinds[(o.WaveIndex - 1) % ClassCyclePlanner.CycleLength];
                            k.Waves++;
                            k.Spawned += o.Spawned;
                            k.Killed += o.Killed;
                            k.Leaked += o.Leaked;
                        }
                    }
                    reached.Sort();
                    double median = (reached[(reached.Count - 1) / 2] + reached[reached.Count / 2]) / 2.0;
                    md.Append(' ').Append(median > waves ? waves + "+" : F(median, "0.#")).Append(" |\n");
                }

                int allBought = 0;
                foreach (TowerTally t in tally.Values) allBought += t.Bought;
                var values = new List<double>();
                foreach (TowerDef t in towers)
                {
                    TowerTally row = tally[t.Id];
                    if (row.BudgetMinutes > 0) values.Add(row.Damage / row.BudgetMinutes);
                }
                values.Sort();
                double medianValue = values.Count == 0 ? 0 : (values[(values.Count - 1) / 2] + values[values.Count / 2]) / 2.0;

                md.Append("\nTowers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.\n\n");
                md.Append("| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |\n|---|---|---|---|---|---|---|---|\n");
                foreach (TowerDef t in towers)
                {
                    TowerTally row = tally[t.Id];
                    double value = row.BudgetMinutes > 0 ? row.Damage / row.BudgetMinutes : 0;
                    md.Append("| ").Append(t.Id).Append(" | ").Append(row.Bought).Append(" | ").Append(F(allBought > 0 ? 100.0 * row.Bought / allBought : 0, "0.0")).Append(" % | ")
                      .Append(F(row.Spent, "0")).Append(" | ").Append(F(row.Damage, "0")).Append(" | ").Append(row.Kills).Append(" | ")
                      .Append(F(value, "0.0")).Append(" | ").Append(medianValue > 0 ? F(value / medianValue, "0.00") : "-").Append(" |\n");
                }

                if (planner == ClassCyclePlanner.Id)
                {
                    string[] names = { "basic", "sappers", "flyers", "swarm", "basic + sappers + flyers" };
                    md.Append("\nWhat each kind of wave got through, all policies and seeds together.\n\n| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |\n|---|---|---|---|---|---|\n");
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        WaveKindTally k = kinds[i];
                        md.Append("| ").Append(names[i]).Append(" | ").Append(k.Waves).Append(" | ").Append(k.Spawned).Append(" | ").Append(k.Killed).Append(" | ").Append(k.Leaked)
                          .Append(" | ").Append(F(k.Spawned > 0 ? 100.0 * k.Leaked / k.Spawned : 0, "0.0")).Append(" % |\n");
                    }
                }
            }

            string text = md.ToString();
            Console.Write(text);
            string outDir = args.Get("--out", null);
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "balance.md"), text);
                Console.WriteLine("\n[Balance] written to " + Path.Combine(outDir, "balance.md"));
            }
            return 0;
        }

        private static string F(double value, string format) { return value.ToString(format, Inv); }
    }
}
