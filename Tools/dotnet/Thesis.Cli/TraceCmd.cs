using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thesis.Core;
using Thesis.Director;

namespace Thesis.Cli
{
    // trace <telemetry.jsonl> [--wave N]
    // Reads a telemetry file as text a person can follow.
    //   (no --wave)   one line per row: the whole session at a glance
    //   --wave N      everything row N holds: the plan, the outcome, what the player
    //                 built, bought and sold, and (once a director writes rows) how
    //                 each strategy was scored
    internal static class TraceCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--wave" }, new string[0]);
            string path = args.Positional(0, "the telemetry file");
            args.NoMorePositionals(1);

            List<WaveRecord> rows = TelemetryWriter.Load(path);
            if (rows.Count == 0) throw new ArgumentException("trace: " + path + " has no rows");

            if (!args.Has("--wave"))
            {
                WaveRecord first = rows[0];
                Console.WriteLine("session " + first.Session + "  condition " + first.Condition + "  player " + first.Player + "  map " + first.Map + "  seed " + first.RngSeed + "  build " + first.Build);
                foreach (WaveRecord row in rows) Console.WriteLine(Summary(row));
                return 0;
            }

            int wave = args.GetInt("--wave", 0);
            WaveRecord found = null;
            foreach (WaveRecord row in rows) if (row.Wave == wave) found = row;
            if (found == null) throw new ArgumentException("trace: no row for wave " + wave + " (the file has waves 0.." + rows[rows.Count - 1].Wave + ")");

            Console.Write(Detail(found));
            return 0;
        }

        private static string N(float value) { return Json.FormatFloat(value); }

        private static string Summary(WaveRecord r)
        {
            var sb = new StringBuilder();
            sb.Append("wave ").Append(r.Wave.ToString(CultureInfo.InvariantCulture).PadLeft(2));
            sb.Append("  ticks ").Append(r.TickStart).Append("-").Append(r.TickEnd);

            if (r.Outcome != null)
            {
                sb.Append("  ").Append(r.Plan != null ? r.Plan.Strategy : "?")
                  .Append("  spawned ").Append(r.Outcome.Spawned).Append(" killed ").Append(r.Outcome.Killed).Append(" leaked ").Append(r.Outcome.Leaked)
                  .Append("  core ").Append(r.Outcome.CoreHpBefore).Append("->").Append(r.Outcome.CoreHpAfter)
                  .Append(r.Outcome.CoreDestroyed ? " (destroyed)" : "");
            }
            else
            {
                sb.Append(r.Wave == 0 ? "  (before the first wave)" : "  (not resolved)");
            }

            int walls = 0, towers = 0, sold = 0;
            foreach (WaveRecord.PlacementRow p in r.Placements)
            {
                if (p.Kind == WaveRecord.PlacementRow.Wall) walls++;
                else if (p.Kind == WaveRecord.PlacementRow.Tower) towers++;
                else if (p.Kind == WaveRecord.PlacementRow.SellWall || p.Kind == WaveRecord.PlacementRow.SellTower) sold++;
            }
            sb.Append("  built ").Append(walls).Append(" wall(s) ").Append(towers).Append(" tower(s)");
            if (sold > 0) sb.Append(", sold ").Append(sold);
            if (r.Shop.Offered.Length > 0) sb.Append("  offered ").Append(string.Join(",", r.Shop.Offered));
            if (r.Chosen != null) sb.Append("  chose ").Append(r.Chosen);
            if (r.Fallback != null) sb.Append("  FALLBACK ").Append(r.Fallback);
            return sb.ToString();
        }

        private static string Detail(WaveRecord r)
        {
            var sb = new StringBuilder();
            sb.Append("wave ").Append(r.Wave).Append("  session ").Append(r.Session).Append("  condition ").Append(r.Condition)
              .Append("  player ").Append(r.Player).Append("  seed ").Append(r.RngSeed).Append('\n');
            sb.Append("window  ticks ").Append(r.TickStart).Append(" to ").Append(r.TickEnd);
            if (r.TickResolved >= 0) sb.Append(", resolved at ").Append(r.TickResolved);
            if (r.StateHash != null) sb.Append("  state ").Append(r.StateHash);
            sb.Append('\n');

            if (r.Plan != null)
            {
                sb.Append("plan    ").Append(r.Plan.Strategy).Append("  threat ").Append(N(r.Plan.ThreatSpent)).Append('\n');
                foreach (WaveRecord.GroupRow g in r.Plan.Groups)
                {
                    sb.Append("          ").Append(g.Count).Append(" x ").Append(g.Archetype).Append(" (").Append(g.Movement).Append(")  hp ").Append(N(g.Hp))
                      .Append("  speed ").Append(N(g.MoveSpeed)).Append("  dig ").Append(N(g.DigRate)).Append("  every ").Append(g.IntervalTicks).Append(" ticks")
                      .Append(g.StartDelayTicks > 0 ? " after " + g.StartDelayTicks : "").Append("  spawn ").Append(g.Spawn).Append('\n');
                }
            }

            if (r.Outcome != null)
            {
                WaveRecord.OutcomeRow o = r.Outcome;
                sb.Append("outcome spawned ").Append(o.Spawned).Append("  killed ").Append(o.Killed).Append("  leaked ").Append(o.Leaked);
                if (o.Removed > 0) sb.Append("  removed ").Append(o.Removed);
                if (o.TimedOut) sb.Append("  TIMED OUT");
                sb.Append("  breaches ").Append(o.Breaches).Append("  towers lost ").Append(o.TowersDestroyed).Append('\n');
                sb.Append("        core ").Append(o.CoreHpBefore).Append(" -> ").Append(o.CoreHpAfter).Append(o.CoreDestroyed ? " (destroyed)" : "")
                  .Append("  budget ").Append(N(o.BudgetBefore)).Append(" -> ").Append(N(o.BudgetAfter)).Append("  pressure ").Append(N(o.Pressure)).Append('\n');
                sb.Append("        damage ").Append(Named(o.DamageByType)).Append('\n');
                sb.Append("        by class  spawned ").Append(Named(o.SpawnedByClass)).Append("  killed ").Append(Named(o.KilledByClass)).Append("  leaked ").Append(Named(o.LeakedByClass)).Append('\n');
            }
            else if (r.Wave > 0)
            {
                sb.Append("outcome (the session ended before this wave resolved)\n");
            }

            sb.Append("shop    offered ").Append(r.Shop.Offered.Length == 0 ? "(no roll in this window)" : string.Join(", ", r.Shop.Offered))
              .Append("  bought ").Append(r.Shop.Bought.Length == 0 ? "nothing" : string.Join(", ", r.Shop.Bought));
            if (r.Shop.Rerolls > 0) sb.Append("  rerolls ").Append(r.Shop.Rerolls);
            sb.Append('\n');

            sb.Append("built   ").Append(r.Placements.Length == 0 ? "nothing" : r.Placements.Length + " action(s)").Append('\n');
            foreach (WaveRecord.PlacementRow p in r.Placements)
            {
                sb.Append("          tick ").Append(p.Tick).Append("  ").Append(p.Kind).Append(' ').Append(p.Name);
                if (p.Kind != WaveRecord.PlacementRow.Reroll) sb.Append(" at (").Append(p.X).Append(",").Append(p.Y).Append(")  ").Append(p.Tiles.Length).Append(" tile(s)");
                if (p.Cost > 0f) sb.Append("  cost ").Append(N(p.Cost));
                if (p.Kind == WaveRecord.PlacementRow.SellWall || p.Kind == WaveRecord.PlacementRow.SellTower) sb.Append("  refund ").Append(N(p.Refund));
                sb.Append('\n');
            }

            if (r.Strategies != null)
            {
                sb.Append("director bin ").Append(r.Bin.HasValue ? r.Bin.Value.ToString(CultureInfo.InvariantCulture) : "?")
                  .Append("  budget ").Append(r.ThreatBudget.HasValue ? N(r.ThreatBudget.Value) : "?").Append("  chose ").Append(r.Chosen ?? "?").Append('\n');
                if (r.Profile != null) sb.Append("        profile ").Append(Named(r.Profile)).Append('\n');
                foreach (WaveRecord.StrategyScore s in r.Strategies)
                {
                    sb.Append("          ").Append((s.Id ?? "?").PadRight(18));
                    if (s.Veto != null) sb.Append(" VETO ").Append(s.Veto);
                    else sb.Append(" heuristic ").Append(Opt(s.Heuristic)).Append("  theta ").Append(Opt(s.Theta)).Append(s.Cold == true ? " (cold)" : "")
                           .Append("  correction ").Append(Opt(s.Correction)).Append("  score ").Append(Opt(s.Score));
                    sb.Append('\n');
                }
            }
            if (r.Fallback != null) sb.Append("fallback ").Append(r.Fallback).Append('\n');
            if (r.Reward != null)
            {
                sb.Append("reward  total ").Append(N(r.Reward.Total)).Append("  = restructure ").Append(N(r.Reward.Restructure)).Append("  survived ").Append(N(r.Reward.Survived))
                  .Append("  pressure ").Append(N(r.Reward.Pressure)).Append("  novelty ").Append(N(r.Reward.Novelty)).Append("  (binary ").Append(r.Reward.Binary).Append(")\n");
            }
            return sb.ToString();
        }

        private static string Opt(float? value) { return value.HasValue ? N(value.Value) : "-"; }

        private static string Named(NamedValues values)
        {
            if (values == null) return "-";
            var parts = new List<string>();
            for (int i = 0; i < values.Count; i++) parts.Add(values.NameAt(i) + " " + Json.FormatNumber(values.ValueAt(i), Json.FloatDigits));
            return string.Join(", ", parts);
        }
    }
}
