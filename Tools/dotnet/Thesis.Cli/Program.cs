using System;

namespace Thesis.Cli
{
    // Entry point for headless runs, replays and gates (Docs/ARCHITECTURE.md §7).
    // Commands are added by the work packages that need them; WP0 only proves the
    // pure assemblies link into a runnable program.
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                PrintUsage();
                return 0;
            }

            Console.Error.WriteLine("Unknown command: " + args[0]);
            PrintUsage();
            return 2;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: thesis <command> [options]");
            Console.WriteLine();
            Console.WriteLine("commands (added per work package, see Docs/WORKPLAN.md):");
            Console.WriteLine("  run      headless episode            (WP5)");
            Console.WriteLine("  replay   verify / bisect a replay    (WP5)");
            Console.WriteLine("  ascii    render map state as text    (WP5)");
            Console.WriteLine("  trace    print one director decision (WP6)");
            Console.WriteLine("  synth    synthetic bandit runs       (WP7)");
            Console.WriteLine("  balance  tower roster sanity sweep   (WP-C5)");
            Console.WriteLine("  ladder   offline ladder              (WP12)");
            Console.WriteLine("  gate     G1 | G2 | G3                (WP13)");
            Console.WriteLine("  bench    flow-field timing           (WP4)");
        }
    }
}
