using System;
using System.IO;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // replay <file> [--per-tick] [--dump-tick T] [--dump-out f]
    // Runs a recorded session again and checks every recorded hash. Exit code 0 when
    // it matches, 1 when it diverges. --dump-tick T also writes the re-run's state at
    // State.Tick == T as JSON, to diff against the same tick from another run.
    internal static class ReplayCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--dump-tick", "--dump-out" }, new[] { "--per-tick" });
            string path = args.Positional(0, "the replay file");
            args.NoMorePositionals(1);

            ReplayFile file = ReplayFile.Load(path);
            Console.WriteLine("[Replay] " + Path.GetFullPath(path));
            Console.WriteLine("  recorded by " + (file.Build ?? "?") + ", session " + (file.Session ?? "?") + ", player " + (file.Policy ?? "?"));
            Console.WriteLine("  map " + file.Map + ", seed " + file.RngSeed + ", planner " + file.Planner + ", " + file.Commands.Count + " commands, "
                              + file.WaveHashes.Count + " waves, " + file.FinalTick + " ticks, per-tick hashes " + (string.IsNullOrEmpty(file.TickHashes) ? "no" : "yes"));

            ReplayReport report = ReplayRunner.Verify(file, args.Flag("--per-tick"));
            Console.WriteLine(report.Describe());

            if (args.Has("--dump-tick"))
            {
                int tick = args.GetInt("--dump-tick", 0);
                Simulation sim = ReplayRunner.RunToTick(file, tick, includeCommandsAtTick: false);
                string dumpPath = args.Get("--dump-out", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "tick" + tick + ".dotnet.json"));
                File.WriteAllText(dumpPath, StateDump.ToJson(sim.State));
                Console.WriteLine("[Replay] state at tick " + sim.State.Tick + " -> " + Path.GetFullPath(dumpPath));
            }

            return report.Ok ? 0 : 1;
        }
    }
}
