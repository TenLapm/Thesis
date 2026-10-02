using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // pin [--out dir] [--map f] [--shapes f]
    // Records the pinned episodes under .NET (see Thesis.Harness.PinnedEpisodes).
    // The other half is recorded inside Unity: menu Thesis > Replay > Record Pinned
    // Episodes (Mono). Run both after any deliberate change to the game rules.
    internal static class PinCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--out", "--map", "--shapes" }, new string[0]);
            args.NoMorePositionals(0);

            string outDir = args.Get("--out", PinnedEpisodes.Folder);
            MapData map = MapData.Load(args.Get("--map", Path.Combine("Maps", "SampleScene.map.json")));
            ShapeDef[] shapes = ShapeLibraryFile.Load(args.Get("--shapes", Path.Combine("Maps", "Shapes.json")));

            List<string> written = PinnedEpisodes.RecordAll(outDir, PinnedEpisodes.DotnetTag, "headless " + RuntimeInformation.FrameworkDescription, map, shapes, TowerRoster.Placeholder());
            foreach (string path in written)
            {
                ReplayFile file = ReplayFile.Load(path);
                Console.WriteLine("[Replay] pinned " + Path.GetFileName(path) + ": " + file.WaveHashes.Count + " waves, " + file.FinalTick + " ticks, " + file.Commands.Count + " commands, final " + file.FinalHash);
            }
            Console.WriteLine("[Replay] Now record the Mono half in Unity: Thesis > Replay > Record Pinned Episodes (Mono). Then run the tests in both runners.");
            return 0;
        }
    }
}
