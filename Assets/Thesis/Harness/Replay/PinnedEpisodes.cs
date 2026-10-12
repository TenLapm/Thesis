using System;
using System.Collections.Generic;
using System.IO;
using Thesis.Director;
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

        public const string ReplaySuffix = ".replay.json";
        public const string TelemetrySuffix = ".telemetry.jsonl";

        // The telemetry file that belongs to a replay file in this folder.
        public static string TelemetryPathFor(string replayPath)
        {
            if (!replayPath.EndsWith(ReplaySuffix, StringComparison.Ordinal))
                throw new ArgumentException("'" + replayPath + "' is not a " + ReplaySuffix + " file.", nameof(replayPath));
            return replayPath.Substring(0, replayPath.Length - ReplaySuffix.Length) + TelemetrySuffix;
        }

        private sealed class Spec
        {
            public string Name;
            public Func<SimConfig> Config;
            public Func<IPlayerPolicy> Policy;
            public string Planner = EscalationPlanner.Id;
            public ulong Seed;
            public int MaxWaves;
            public bool TickHashes;
        }

        // Small on purpose: every file is re-run on every test run, twice. Between
        // them they cover towers of every kind (single target, splash, slow), kills,
        // leaks, wall building, digging and breaches, several waves, a game over,
        // (WP-C2) all three movement classes, and (WP-C3) buying from the shop and
        // selling towers and wall pieces. Four carry a hash for every tick.
        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Name = "sentry-6waves",
                Config = () => new SimConfig(),
                Policy = () => new SentryPolicy(),
                Seed = 7,
                MaxWaves = 6,
                TickHashes = false,
            },
            new Spec
            {
                Name = "mixed-3waves",
                Config = () => new SimConfig(),
                Policy = () => SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 6, wallSearchOrigins: 12),
                Seed = 3,
                MaxWaves = 3,
                TickHashes = true,
            },
            new Spec
            {
                Name = "walls-only-to-gameover",
                Config = () => new SimConfig(),
                Policy = SmallGreedy,
                Seed = 5,
                MaxWaves = 25,
                TickHashes = true,
            },
            // Sappers and flyers: a ground wave, a sapper wave, a flyer wave, all three
            // together, and round again. This is the only recording in which the
            // sapper field, straight-line flight and anti-air targeting run on both
            // runtimes. The core is given enough hit points to see all six waves, and
            // the player few enough towers that every class is both killed and let
            // through: sappers breach walls and a tower, flyers are shot down and leak.
            // (Chosen by trying a few seeds; `thesis replay <file> --outcomes` shows it.)
            new Spec
            {
                Name = "class-cycle-6waves",
                Config = () => new SimConfig { CoreMaxHp = 1000 },
                Policy = () => SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 3, wallSearchOrigins: 12),
                Planner = ClassCyclePlanner.Id,
                Seed = 7,
                MaxWaves = 6,
                TickHashes = true,
            },
            // Selling (WP-C3): every build phase after the first wave this player sells
            // its weakest tower and its oldest wall piece, then builds and buys again
            // with what it has. The only recording in which SellTower and SellWall
            // commands, the refunds and the piece ids run on both runtimes.
            // (Seed chosen so that it lives through all five waves with the real core.)
            new Spec
            {
                Name = "restless-5waves",
                Config = () => new SimConfig(),
                Policy = () => SequencePolicy.Restless(),
                Seed = 4,
                MaxWaves = 5,
                TickHashes = true,
            },
        };

        // A narrower search than the default keeps recording to a few seconds.
        private static IPlayerPolicy SmallGreedy() { return new GreedyDetourPolicy { MaxPlacementsPerIntermission = 3, MaxOriginsPerPlacement = 12 }; }

        // Records every episode on the runtime this is called from and writes
        // <directory>/<runtimeTag>-<name>.replay.json, and next to it the telemetry
        // the same run produced (<...>.telemetry.jsonl). Older files with the same
        // tag are removed first, so a renamed or dropped episode leaves nothing stale.
        //
        // The telemetry files are the golden files of WP-C3: the test makes each one
        // again from its replay, on both runtimes, and compares bytes.
        public static List<string> RecordAll(string directory, string runtimeTag, string build, MapData map, ShapeDef[] shapes, TowerDef[] towers)
        {
            if (string.IsNullOrEmpty(runtimeTag)) throw new ArgumentException("A runtime tag is required.", nameof(runtimeTag));
            Directory.CreateDirectory(directory);
            foreach (string old in Directory.GetFiles(directory, runtimeTag + "-*" + ReplaySuffix)) File.Delete(old);
            foreach (string old in Directory.GetFiles(directory, runtimeTag + "-*" + TelemetrySuffix)) File.Delete(old);

            var written = new List<string>();
            foreach (Spec spec in Specs)
            {
                var options = new EpisodeOptions
                {
                    Config = spec.Config(),
                    Map = map,
                    Shapes = shapes,
                    Towers = towers,
                    Seed = spec.Seed,
                    Planner = spec.Planner,
                    Policy = spec.Policy(),
                    MaxWaves = spec.MaxWaves,
                    RecordTickHashes = spec.TickHashes,
                    Build = build,
                    Session = runtimeTag + "-" + spec.Name,
                };
                EpisodeResult result = EpisodeRunner.Run(options);

                string path = Path.Combine(directory, runtimeTag + "-" + spec.Name + ReplaySuffix);
                result.Replay.Save(path);
                using (var writer = new TelemetryWriter(TelemetryPathFor(path))) writer.Write(result.Telemetry);
                written.Add(path);
            }
            return written;
        }
    }
}
