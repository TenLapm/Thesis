using System;
using System.Collections.Generic;
using System.IO;
using Thesis.Sim;

namespace Thesis.Harness
{
    // The standing set of recorded episodes behind PinnedReplayTests: the check that
    // Unity's Mono and .NET play the same game, bit for bit.
    //
    // The same episodes are recorded once on each runtime (the CLI command `pin`
    // under .NET, the menu Thesis > Replay > Record Pinned Episodes under Mono) and
    // the files are committed. The test then re-runs every file in both test
    // runners, so each recording is replayed on the runtime that did NOT make it.
    //
    // RE-RECORD WHENEVER THE RULES CHANGE ON PURPOSE (every WP-C package does):
    // run both, commit the new files, say so in DEVLOG. A recording that stops
    // verifying when you did NOT mean to change the rules is a determinism bug.
    public static class PinnedEpisodes
    {
        // Relative to the project root.
        public const string Folder = "Results/pinned-replays";

        public const string DotnetTag = "dotnet";
        public const string MonoTag = "mono";

        private sealed class Spec
        {
            public string Name;
            public Func<SimConfig> Config;
            public Func<IPlayerPolicy> Policy;
            public ulong Seed;
            public int MaxWaves;
            public bool TickHashes;
        }

        // Small on purpose: every file is re-run on every test run, twice. Between
        // them they cover building, digging, breaches, stalls, leaks, several
        // waves, and a game over; two carry a hash for every tick.
        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Name = "greedy-5waves-longcore",
                Config = () => new SimConfig { CoreMaxHp = 100000 },
                Policy = SmallGreedy,
                Seed = 7,
                MaxWaves = 5,
                TickHashes = false,
            },
            new Spec
            {
                Name = "greedy-to-gameover",
                Config = () => new SimConfig(),
                Policy = SmallGreedy,
                Seed = 3,
                MaxWaves = 25,
                TickHashes = true,
            },
            new Spec
            {
                Name = "idle-to-gameover",
                Config = () => new SimConfig(),
                Policy = () => new IdlePolicy(),
                Seed = 1,
                MaxWaves = 25,
                TickHashes = true,
            },
        };

        // A narrower search than the default keeps recording to a few seconds.
        private static IPlayerPolicy SmallGreedy() { return new GreedyDetourPolicy { MaxPlacementsPerIntermission = 4, MaxOriginsPerPlacement = 12 }; }

        // Records every episode on the runtime this is called from and writes
        // <directory>/<runtimeTag>-<name>.replay.json. Older files with the same tag
        // are removed first, so a renamed or dropped episode leaves nothing stale.
        public static List<string> RecordAll(string directory, string runtimeTag, string build, MapData map, ShapeDef[] shapes)
        {
            if (string.IsNullOrEmpty(runtimeTag)) throw new ArgumentException("A runtime tag is required.", nameof(runtimeTag));
            Directory.CreateDirectory(directory);
            foreach (string old in Directory.GetFiles(directory, runtimeTag + "-*.replay.json")) File.Delete(old);

            var written = new List<string>();
            foreach (Spec spec in Specs)
            {
                var options = new EpisodeOptions
                {
                    Config = spec.Config(),
                    Map = map,
                    Shapes = shapes,
                    Seed = spec.Seed,
                    Policy = spec.Policy(),
                    MaxWaves = spec.MaxWaves,
                    RecordTickHashes = spec.TickHashes,
                    Build = build,
                    Session = runtimeTag + "-" + spec.Name,
                };
                EpisodeResult result = EpisodeRunner.Run(options);

                string path = Path.Combine(directory, runtimeTag + "-" + spec.Name + ".replay.json");
                result.Replay.Save(path);
                written.Add(path);
            }
            return written;
        }
    }
}
