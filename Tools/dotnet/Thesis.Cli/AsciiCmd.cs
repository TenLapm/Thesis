using System;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // ascii <file> [--wave N | --tick T] [--layer route|terrain|occupancy|cost]
    // Renders the state of a recorded session as text.
    //   --tick T   the state when State.Tick == T (before that tick's commands)
    //   --wave N   the state right after wave N resolved: the walls it left standing
    //              and, with --layer occupancy, where its agents spent their time
    //   neither    the end of the recording
    internal static class AsciiCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--wave", "--tick", "--layer" }, new string[0]);
            string path = args.Positional(0, "the replay file");
            args.NoMorePositionals(1);
            if (args.Has("--wave") && args.Has("--tick")) throw new ArgumentException("ascii: give --wave or --tick, not both");

            ReplayFile file = ReplayFile.Load(path);

            AsciiLayer layer;
            switch (args.Get("--layer", "route"))
            {
                case "route": layer = AsciiLayer.Route; break;
                case "terrain": layer = AsciiLayer.Terrain; break;
                case "occupancy": layer = AsciiLayer.Occupancy; break;
                case "cost": layer = AsciiLayer.Cost; break;
                default: throw new ArgumentException("ascii: --layer must be route, terrain, occupancy or cost");
            }

            Simulation sim;
            if (args.Has("--tick"))
            {
                sim = ReplayRunner.RunToTick(file, args.GetInt("--tick", 0), includeCommandsAtTick: false);
            }
            else if (args.Has("--wave"))
            {
                int wave = args.GetInt("--wave", 0);
                WaveHash found = null;
                foreach (WaveHash w in file.WaveHashes) if (w.Wave == wave) found = w;
                if (found == null) throw new ArgumentException("ascii: the recording has no resolved wave " + wave + " (it has " + file.WaveHashes.Count + " waves)");
                sim = ReplayRunner.RunToTick(file, found.Tick, includeCommandsAtTick: false);
            }
            else
            {
                sim = ReplayRunner.RunToTick(file, file.FinalTick, includeCommandsAtTick: true);
            }

            Console.Write(AsciiState.Render(sim, layer));
            return 0;
        }
    }
}
