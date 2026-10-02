using System;
using System.Globalization;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // ascii <file> [--wave N | --tick T] [--layer route|terrain|occupancy|cost] [--class ground|sapper]
    // Renders the state of a recorded session as text.
    //   --tick T   the state when State.Tick == T (before that tick's commands)
    //   --wave N   the state right after wave N resolved: the walls it left standing
    //              and, with --layer occupancy, where its agents spent their time
    //   neither    the end of the recording
    //   --class    whose flow field the route and cost layers show (default ground)
    //
    // ascii --map <map.json> [--scenario open|maze|choke|scatter] [--layer route|terrain]
    //       [--class ground|sapper] [--sapper-factor F]
    // Renders a bare map, optionally with one of the benchmark wall layouts on it,
    // and the route one movement class takes across it. No recording needed: this is
    // for looking at how a route changes with the class or with the sapper's share.
    internal static class AsciiCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--wave", "--tick", "--layer", "--class", "--map", "--scenario", "--sapper-factor" }, new string[0]);

            MovementClass movement;
            switch (args.Get("--class", "ground"))
            {
                case "ground": movement = MovementClass.Ground; break;
                case "sapper": movement = MovementClass.Sapper; break;
                default: throw new ArgumentException("ascii: --class must be ground or sapper (a flyer follows no field: its route is a straight line)");
            }

            AsciiLayer layer;
            switch (args.Get("--layer", "route"))
            {
                case "route": layer = AsciiLayer.Route; break;
                case "terrain": layer = AsciiLayer.Terrain; break;
                case "occupancy": layer = AsciiLayer.Occupancy; break;
                case "cost": layer = AsciiLayer.Cost; break;
                default: throw new ArgumentException("ascii: --layer must be route, terrain, occupancy or cost");
            }

            if (args.Has("--map")) return RenderMap(args, layer, movement);

            if (args.Has("--scenario") || args.Has("--sapper-factor")) throw new ArgumentException("ascii: --scenario and --sapper-factor go with --map");
            string path = args.Positional(0, "the replay file (or --map <map.json>)");
            args.NoMorePositionals(1);
            if (args.Has("--wave") && args.Has("--tick")) throw new ArgumentException("ascii: give --wave or --tick, not both");

            ReplayFile file = ReplayFile.Load(path);

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

            Console.Write(AsciiState.Render(sim, layer, movement));
            return 0;
        }

        private static int RenderMap(Args args, AsciiLayer layer, MovementClass movement)
        {
            args.NoMorePositionals(0);
            if (args.Has("--wave") || args.Has("--tick")) throw new ArgumentException("ascii: --wave and --tick need a recording, not --map");
            if (layer != AsciiLayer.Route && layer != AsciiLayer.Terrain) throw new ArgumentException("ascii: with --map the layer must be route or terrain (occupancy and cost need a recording)");

            MapData map = MapData.Load(args.Get("--map", null));
            var grid = new SimGrid(map);

            string scenarioName = args.Get("--scenario", "none");
            int walls = 0;
            switch (scenarioName)
            {
                case "none": break;
                case "open": walls = BenchScenarios.Apply(grid, map, BenchScenario.OpenField); break;
                case "maze": walls = BenchScenarios.Apply(grid, map, BenchScenario.Maze); break;
                case "choke": walls = BenchScenarios.Apply(grid, map, BenchScenario.ChokePoints); break;
                case "scatter": walls = BenchScenarios.Apply(grid, map, BenchScenario.RandomScatter); break;
                default: throw new ArgumentException("ascii: --scenario must be open, maze, choke or scatter");
            }

            float factor = new SimConfig().SapperDigCostFactor;
            if (args.Has("--sapper-factor"))
            {
                if (!float.TryParse(args.Get("--sapper-factor", null), NumberStyles.Float, CultureInfo.InvariantCulture, out factor) || !(factor >= 0f) || factor > 1f)
                    throw new ArgumentException("ascii: --sapper-factor needs a number from 0 to 1");
            }
            new FlowFieldSet(factor).Generate(grid, map.Core);

            var sb = new System.Text.StringBuilder();
            sb.Append(map.Name).Append("  ").Append(map.Width).Append('x').Append(map.Height)
              .Append("  scenario ").Append(scenarioName).Append("  walls ").Append(walls)
              .Append("  class=").Append(movement.ToString().ToLowerInvariant());
            if (movement == MovementClass.Sapper) sb.Append(" (pays ").Append(factor.ToString("0.###", CultureInfo.InvariantCulture)).Append(" of a built tile)");
            if (map.Spawns.Length > 0)
            {
                SimNode spawn = grid.Get(map.Spawns[0]);
                double cost = Route.CostInTiles(spawn, movement);
                sb.Append("  route ").Append(Route.CountHops(grid, spawn, movement)).Append(" tiles, cost ")
                  .Append(cost < 0 ? "unreachable" : cost.ToString("0.#", CultureInfo.InvariantCulture));
            }
            Console.WriteLine(sb.ToString());
            Console.Write(AsciiMap.Render(grid, map, layer, labels: true, movement: movement));
            Console.WriteLine("legend  X static  # wall  H heavy wall  S spawn  C core" + (layer == AsciiLayer.Route ? "  * " + (movement == MovementClass.Sapper ? "sapper route" : "route") + "  + route through a wall" : ""));
            return 0;
        }
    }
}
