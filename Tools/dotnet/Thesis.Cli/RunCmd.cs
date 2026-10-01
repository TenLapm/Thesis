using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // run [--map f] [--shapes f] [--config f] [--planner p] [--policy p] [--seed s]
    //     [--waves n] [--out dir] [--per-tick] [--wait]
    // One headless episode. Writes <out>/replay.json and prints one line per wave.
    //
    // --config is a JSON file of SimConfig fields. It may list only the fields to
    // change, e.g. {"CoreMaxHp": 100000}; everything else keeps SimConfig's value.
    internal static class RunCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv,
                new[] { "--map", "--shapes", "--config", "--planner", "--policy", "--seed", "--waves", "--out" },
                new[] { "--per-tick", "--wait" });
            args.NoMorePositionals(0);

            string policyName = args.Get("--policy", GreedyDetourPolicy.Id);
            ulong seed = args.GetULong("--seed", 1);
            string outDir = args.Get("--out", Path.Combine("Runs", "run_" + policyName + "_seed" + seed.ToString(CultureInfo.InvariantCulture)));

            var options = new EpisodeOptions
            {
                Config = LoadConfig(args.Get("--config", null)),
                Map = MapData.Load(args.Get("--map", Path.Combine("Maps", "SampleScene.map.json"))),
                Shapes = ShapeLibraryFile.Load(args.Get("--shapes", Path.Combine("Maps", "Shapes.json"))),
                Seed = seed,
                Planner = args.Get("--planner", EscalationPlanner.Id),
                Policy = Registry.CreatePolicy(policyName),
                MaxWaves = args.GetInt("--waves", 25),
                StartWavesEarly = !args.Flag("--wait"),
                RecordTickHashes = args.Flag("--per-tick"),
                Build = "headless " + RuntimeInformation.FrameworkDescription,
                Session = Path.GetFileName(Path.GetFullPath(outDir)),
            };

            var clock = Stopwatch.StartNew();
            EpisodeResult result = EpisodeRunner.Run(options);
            clock.Stop();

            foreach (WaveOutcome o in result.Outcomes) Console.WriteLine("  " + o);

            string file = Path.Combine(outDir, "replay.json");
            result.Replay.Save(file);

            string how = result.GameOver ? "core destroyed" : result.HitTickLimit ? "tick limit reached" : "wave limit reached";
            Console.WriteLine("[Harness] " + options.Policy.Name + " vs " + options.Planner + " on " + options.Map.Name + ", seed " + seed + ": "
                              + result.WavesResolved + " waves, " + how + " at tick " + result.Ticks + ", core " + result.CoreHp + "/" + options.Config.CoreMaxHp
                              + ", " + result.Placements + " placements, final hash " + result.Replay.FinalHash
                              + "  (" + clock.ElapsedMilliseconds + " ms)");
            Console.WriteLine("[Replay] -> " + Path.GetFullPath(file));
            return 0;
        }

        private static SimConfig LoadConfig(string path)
        {
            if (path == null) return new SimConfig();
            if (!File.Exists(path)) throw new FileNotFoundException("Config file not found.", path);
            try
            {
                string json = File.ReadAllText(path);

                // The shared JSON settings skip unknown members, which is right for
                // reading old telemetry but wrong here: a misspelt field would be
                // ignored and the run would quietly use the default.
                foreach (Newtonsoft.Json.Linq.JProperty p in Newtonsoft.Json.Linq.JObject.Parse(json).Properties())
                {
                    if (typeof(SimConfig).GetField(p.Name) == null)
                        throw new InvalidDataException("Config file '" + path + "': SimConfig has no field '" + p.Name + "'.");
                }

                // Newtonsoft fills a new SimConfig, so fields the file leaves out
                // keep their initialisers.
                SimConfig config = Json.Deserialize<SimConfig>(json);
                if (config == null) throw new InvalidDataException("Config file '" + path + "' is empty.");
                config.Validate();
                return config;
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                throw new InvalidDataException("Config file '" + path + "' is not valid JSON: " + e.Message, e);
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Config file '" + path + "': " + e.Message, e);
            }
        }
    }
}
