using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // `thesis profile`: how the build profile's features spread over real boards. BuilderPolicy
    // plays the game against the static baseline on game maps; at every build phase, after the
    // player has built, the profile is read. Prints, per feature, the share of readings with data
    // and the spread (min, quintile edges, max) of those that have it. This is what spec gap S1
    // (which feature is binned, and its bin edges) is decided from, and what the threshold freeze
    // will look at again with the pilot's data.
    public static class ProfileCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--seeds", "--waves", "--config" }, new string[0]);
            args.NoMorePositionals(0);
            int seeds = args.GetInt("--seeds", 20);
            int waves = args.GetInt("--waves", 20);
            SimConfig config = RunCmd.LoadConfig(args.Get("--config", null));
            ShapeDef[] shapes = ShapeLibraryFile.Load(System.IO.Path.Combine("Maps", "Shapes.json"));
            TowerDef[] towers = TowerLibraryFile.Load(System.IO.Path.Combine("Maps", "Towers.json"));

            BuildProfiler profiler = BuildProfiler.Default();
            var values = new List<float>[profiler.Names.Count];
            var total = new int[profiler.Names.Count];
            for (int f = 0; f < values.Length; f++) values[f] = new List<float>();
            int readings = 0;

            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                MapData map = GameMaps.Generate(seed);
                var sim = new Simulation(config, map, shapes, towers, seed, new EscalationPlanner(config, map));
                var policy = new BuilderPolicy();
                var rng = new Pcg32(seed, RngStreams.Policy);
                Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };
                EmptyBoard empty = null;
                TileCoord emptyFor = new TileCoord(-1, -1);
                bool built = false;
                for (int guard = 0; guard < 3000000 && !sim.State.IsGameOver; guard++)
                {
                    SimState s = sim.State;
                    bool buildPhase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                    if (buildPhase && !built && (!config.PlanAhead || s.NextPlan != null))
                    {
                        policy.OnIntermission(sim, send, rng);
                        s = sim.State;
                        if (config.PlanAhead && s.NextPlan == null) continue;
                        built = true;
                        if (s.WaveIndex >= waves) break;
                        if (empty == null || !emptyFor.Equals(map.Core)) { empty = EmptyBoard.For(map, config); emptyFor = map.Core; }
                        BuildProfile p = profiler.Profile(ProfileInput.From(s, map, empty));
                        readings++;
                        for (int f = 0; f < values.Length; f++)
                        {
                            total[f]++;
                            if (p.HasData[f]) values[f].Add(p.Values[f]);
                        }
                        for (int g = 0; g < 64 && s.GearOverflow.Count > 0; g++) send(SimCommand.SellOverflow(0));
                        send(SimCommand.StartWaveNow());
                    }
                    else if (!buildPhase) built = false;
                    sim.Tick();
                }
            }

            var md = new StringBuilder();
            md.Append("BuilderPolicy vs the static baseline, game maps 1 to ").Append(seeds).Append(", build phases up to wave ").Append(waves)
              .Append(": ").Append(readings).Append(" readings.\n\n");
            md.Append("| Feature | With data | Min | 5 % | 20 % | 40 % | 60 % | 80 % | 95 % | Max |\n|---|---|---|---|---|---|---|---|---|---|\n");
            for (int f = 0; f < values.Length; f++)
            {
                List<float> v = values[f];
                v.Sort();
                md.Append("| ").Append(profiler.Names[f]).Append(" | ").Append(total[f] == 0 ? "-" : ((double)v.Count / total[f]).ToString("0.00", Inv));
                if (v.Count == 0) { md.Append(" | - | - | - | - | - | - |\n"); continue; }
                md.Append(" | ").Append(F(v[0]));
                foreach (double q in new[] { 0.05, 0.2, 0.4, 0.6, 0.8, 0.95 }) md.Append(" | ").Append(F(v[(int)(q * (v.Count - 1))]));
                md.Append(" | ").Append(F(v[v.Count - 1])).Append(" |\n");
            }
            Console.Write(md.ToString());
            return 0;
        }

        private static string F(float v) { return v.ToString("0.000", Inv); }
    }
}
