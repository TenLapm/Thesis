using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

// Scratch measurement for the committee deck. Not part of the repository.
static class Program
{
    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static string Stat(List<double> v)
    {
        v.Sort();
        return $"mean {v.Average():F3} ms, median {v[v.Count / 2]:F3}, p99 {v[(int)(v.Count * 0.99)]:F3}, max {v[v.Count - 1]:F3} (n={v.Count})";
    }

    static List<double> Time(int n, Action a)
    {
        var r = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            long t = Stopwatch.GetTimestamp();
            a();
            r.Add(Ms(Stopwatch.GetTimestamp() - t));
        }
        return r;
    }

    static string Case(string name, string ascii)
    {
        AsciiFixture f = AsciiMap.Parse(ascii, name);
        var set = new FlowFieldSet(0.2f);
        set.Generate(f.Grid, f.Map.Core);
        SimNode start = f.Grid.Get(f.Map.Spawns[0]);
        var g = new List<SimNode>(); var p = new List<SimNode>();
        Route.Collect(f.Grid, start, g, MovementClass.Ground);
        Route.Collect(f.Grid, start, p, MovementClass.Sapper);
        Func<List<SimNode>, string> path = l => "[" + string.Join(",", l.Select(n => "[" + n.X + "," + n.Y + "]")) + "]";
        var walls = new List<SimNode>();
        for (int i = 0; i < f.Grid.NodeCount; i++) if (f.Grid.ByIndex(i).HasWall) walls.Add(f.Grid.ByIndex(i));
        return "{\"name\":\"" + name + "\",\"w\":" + f.Map.Width + ",\"h\":" + f.Map.Height + ",\"walkerCost\":" + start.BestCost + ",\"sapperCost\":" + start.SapperCost
             + ",\"walkerThrough\":" + g.Count(n => n.HasWall) + ",\"sapperThrough\":" + p.Count(n => n.HasWall)
             + ",\"walker\":" + path(g) + ",\"sapper\":" + path(p) + ",\"walls\":" + path(walls)
             + ",\"spawn\":[" + f.Map.Spawns[0].X + "," + f.Map.Spawns[0].Y + "],\"core\":[" + f.Map.Core.X + "," + f.Map.Core.Y + "]}";
    }

    static string Board(int gapFrom, int gapTo, int wallFrom, int wallTo)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 10; y >= 0; y--)
        {
            for (int x = 0; x < 21; x++)
            {
                char c = '.';
                if (x == 1 && y == 5) c = 'S';
                if (x == 19 && y == 5) c = 'C';
                if (x == 10 && y >= wallFrom && y <= wallTo) c = '#';
                sb.Append(c);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static void Routes(string outPath)
    {
        var all = new[]
        {
            Case("open", Board(0, 0, 99, 99)),
            Case("short", Board(0, 0, 3, 7)),
            Case("gap", Board(0, 0, 0, 8)),
            Case("sealed", Board(0, 0, 0, 10)),
        };
        File.WriteAllText(outPath, "[" + string.Join("," + Environment.NewLine, all) + "]");
        foreach (string a in all) Console.WriteLine(a.Substring(0, Math.Min(120, a.Length)));
    }

    static void Main(string[] argv)
    {
        if (argv.Length > 1 && argv[0] == "routes") { Routes(argv[1]); return; }
        string root = argv[0];
        Console.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, cores {Environment.ProcessorCount}, os {RuntimeInformation.OSDescription}");
        var config = Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(root, "Maps", "SimConfig.json")));
        config.Validate();
        ShapeDef[] shapes = ShapeLibraryFile.Load(Path.Combine(root, "Maps", "Shapes.json"));
        TowerDef[] towers = TowerLibraryFile.Load(Path.Combine(root, "Maps", "Towers.json"));

        // Warm up the JIT on one throwaway pass of everything below, then measure.
        for (int pass = 0; pass < 2; pass++)
        {
            bool report = pass == 1;
            var fieldR = new List<double>(); var groundR = new List<double>(); var sapperR = new List<double>(); var raidR = new List<double>(); var lureR = new List<double>();
            var hashR = new List<double>(); var cloneR = new List<double>(); var powerR = new List<double>(); var planR = new List<double>();
            int builtTiles = 0, boards = 0, nodes = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapData map = MapGenerator.Generate(seed, 48, 48, 1f, 2, 22, 0f, 0f, 28, 4);
                var director = new RoughDirector(config, map);
                var sim = new Simulation(config, map, shapes, towers, seed, director);
                // A built-up mid-game board: every deposit collected and walls with towers near them.
                sim.State.BuildBudget = 100000f;
                sim.Enqueue(SimCommand.Research()); sim.Enqueue(SimCommand.Research());
                foreach (var d in map.Deposits) sim.Enqueue(SimCommand.BuildCollector(d.Tile.X, d.Tile.Y));
                sim.Tick();
                var rng = new Pcg32(seed, 77UL);
                int placed = 0;
                for (int k = 0; k < 400 && placed < 180; k++)
                {
                    int x = rng.NextInt(3, 45), y = rng.NextInt(3, 45);
                    int before = sim.State.PlacementLog.Count;
                    sim.Enqueue(SimCommand.PlaceShape(x, y, rng.NextInt(0, 3)));
                    sim.FlushInput();
                    if (sim.State.PlacementLog.Count > before) placed++;
                    if (placed % 6 == 0 && sim.State.PlacementLog.Count > before) { sim.Enqueue(SimCommand.BuyTower(rng.NextInt(0, 8), x, y)); sim.FlushInput(); }
                }
                SimState s = sim.State;
                SimGrid grid = s.Grid;
                nodes = grid.NodeCount;
                builtTiles = 0;
                for (int i = 0; i < grid.NodeCount; i++) if (grid.ByIndex(i).HasWall && grid.ByIndex(i).PieceId != SimNode.RockPiece) builtTiles++;
                boards++;
                // Lure on one tower so the fourth flood really runs.
                for (int i = 0; i < grid.NodeCount; i++) if (grid.ByIndex(i).Occupant == Occupant.Tower) { grid.ByIndex(i).Lure = true; break; }

                var set = new FlowFieldSet(config.SapperDigCostFactor);
                var one = new FlowField();
                fieldR.AddRange(Time(50, () => set.Generate(grid, map.Core)));
                groundR.AddRange(Time(50, () => one.Generate(grid, map.Core)));
                sapperR.AddRange(Time(50, () => one.GenerateSapper(grid, map.Core, config.SapperDigCostFactor)));
                raidR.AddRange(Time(50, () => one.GenerateRaid(grid)));
                lureR.AddRange(Time(50, () => one.GenerateLure(grid)));
                set.Generate(grid, map.Core);
                hashR.AddRange(Time(200, () => StateHasher.Compute(s)));
                var cloneM = typeof(SimState).GetMethod("Clone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); cloneR.AddRange(Time(50, () => cloneM.Invoke(s, null)));
                powerR.AddRange(Time(50, () => sim.RecomputePower(s)));
                var ctx = new WaveContext(12, s, map, config);
                planR.AddRange(Time(50, () => director.PlanWave(ctx)));
            }
            if (report)
            {
                Console.WriteLine($"board: {nodes} nodes, {builtTiles} built tiles on the last map, {boards} generated maps");
                Console.WriteLine("all four fields   " + Stat(fieldR));
                Console.WriteLine("  ground          " + Stat(groundR));
                Console.WriteLine("  sapper          " + Stat(sapperR));
                Console.WriteLine("  raid            " + Stat(raidR));
                Console.WriteLine("  lure            " + Stat(lureR));
                Console.WriteLine("StateHasher       " + Stat(hashR));
                Console.WriteLine("SimState.Clone    " + Stat(cloneR));
                Console.WriteLine("RecomputePower    " + Stat(powerR));
                Console.WriteLine("RoughDirector.Plan" + Stat(planR));
            }
        }

        // Whole episodes: the game's numbers, a scripted player, 25 waves.
        foreach (bool hashes in new[] { false, true })
        foreach (string planner in new[] { RoughDirector.Id, EscalationPlanner.Id })
        foreach (string policyName in new[] { RaidPolicy.Id })
        {
            var cfg = Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(root, "Maps", "SimConfig.json")));
            cfg.CoreMaxHp = 100000; // so that a weak scripted player still sees 25 waves
            double total = 0; long allTicks = 0; int runs = 0, waves = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var options = new EpisodeOptions
                {
                    Config = cfg,
                    Map = MapGenerator.Generate(seed, 48, 48, 1f, 2, 22, 0f, 0f, 28, 4),
                    Shapes = shapes, Towers = towers, Seed = seed, Planner = planner,
                    Policy = Registry.CreatePolicy(policyName), MaxWaves = 25, RecordTickHashes = hashes,
                    Build = "bench", Session = "bench",
                };
                long t = Stopwatch.GetTimestamp();
                EpisodeResult r = EpisodeRunner.Run(options);
                total += Ms(Stopwatch.GetTimestamp() - t);
                allTicks += r.Ticks; runs++; waves += r.WavesResolved;
            }
            double perEp = total / runs;
            Console.WriteLine($"episode {planner} + {policyName}, tick hashes {hashes}: {runs} episodes, {waves} waves resolved, {allTicks / runs} ticks each, {perEp:F0} ms each, {allTicks * 1000.0 / total:F0} ticks/s, 10^6 episodes on one core = {perEp * 1e6 / 3.6e6:F0} hours");
        }
    }
}
