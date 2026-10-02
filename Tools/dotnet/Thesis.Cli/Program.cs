using System;
using System.IO;
using Thesis.Harness;

namespace Thesis.Cli
{
    // Entry point for headless runs, replays and gates (Docs/ARCHITECTURE.md §7).
    // Commands are added by the work packages that need them.
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                PrintUsage();
                return 0;
            }

            try
            {
                switch (args[0])
                {
                    case "bench": return Bench(args);
                    case "run": return RunCmd.Run(args);
                    case "replay": return ReplayCmd.Run(args);
                    case "ascii": return AsciiCmd.Run(args);
                    case "pin": return PinCmd.Run(args);
                    default:
                        Console.Error.WriteLine("Unknown command: " + args[0]);
                        PrintUsage();
                        return 2;
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 2;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                // A missing or malformed map, shape library, config or replay file.
                // (InvalidDataException is not an IOException, hence both.)
                Console.Error.WriteLine("error: " + e.Message);
                return 2;
            }
        }

        // bench [--out <file>] [--iterations N] [--seed S]
        // Flow-field rebuild sweep + A* comparison (the two BenchmarkRunner scenarios
        // that never needed Unity). Default output: Runs/BenchmarkResults_headless.json.
        private static int Bench(string[] args)
        {
            var o = new PathfindingBench.Options();
            string outPath = Path.Combine("Runs", "BenchmarkResults_headless.json");
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": outPath = Value(args, ref i); break;
                    case "--iterations": o.RebuildIterations = int.Parse(Value(args, ref i)); break;
                    case "--seed": o.Seed = ulong.Parse(Value(args, ref i)); break;
                    default: throw new ArgumentException("bench: unknown option " + args[i]);
                }
            }

            string json = PathfindingBench.RunToJson(o);
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, json);
            Console.WriteLine("[Harness] bench -> " + Path.GetFullPath(outPath));
            return 0;
        }

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: thesis <command> [options]");
            Console.WriteLine();
            Console.WriteLine("commands:");
            Console.WriteLine("  run      one headless episode, recorded as <out>/replay.json");
            Console.WriteLine("             [--map f] [--shapes f] [--config f] [--planner escalation|class-cycle] [--policy sentry|mixed|greedy|idle]");
            Console.WriteLine("             [--seed s] [--waves n] [--out dir] [--per-tick] [--wait]");
            Console.WriteLine("  replay   run a recording again and check its hashes (exit 1 on divergence)");
            Console.WriteLine("             <replay.json> [--per-tick] [--outcomes] [--dump-tick T] [--dump-out f]");
            Console.WriteLine("  ascii    render a recording's state as text, or a bare map and the route across it");
            Console.WriteLine("             <replay.json> [--wave N | --tick T] [--layer route|terrain|occupancy|cost] [--class ground|sapper]");
            Console.WriteLine("             --map f [--scenario open|maze|choke|scatter] [--layer route|terrain] [--class ground|sapper] [--sapper-factor 0..1]");
            Console.WriteLine("  pin      re-record the pinned episodes under .NET (after a deliberate rule change)");
            Console.WriteLine("             [--out dir] [--map f] [--shapes f]");
            Console.WriteLine("  bench    flow-field rebuild sweep + A* comparison   [--out f] [--iterations n] [--seed s]");
            Console.WriteLine();
            Console.WriteLine("planned (see Docs/WORKPLAN.md):");
            Console.WriteLine("  trace    print one director decision (WP6)");
            Console.WriteLine("  synth    synthetic bandit runs       (WP7)");
            Console.WriteLine("  balance  tower roster sanity sweep   (WP-C5)");
            Console.WriteLine("  ladder   offline ladder              (WP12)");
            Console.WriteLine("  gate     G1 | G2 | G3                (WP13)");
        }
    }
}
