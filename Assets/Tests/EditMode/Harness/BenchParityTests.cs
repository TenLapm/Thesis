using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // Parity oracle for the flow-field port (WORKPLAN WP1). The committed
    // Benchmark_<label>.json files were written by ScenarioBenchmark running the
    // ORIGINAL Unity FlowFieldManager on these exact layouts. The expected numbers
    // are read from those files, not copied into this test, so the oracle cannot
    // drift from its source.
    //
    // Requires Maps/Bench_*.map.json (Unity menu: Thesis/Export All Maps).
    public class BenchParityTests
    {
        [TestCase("Bench_Open", "Benchmark_Open.json", BenchScenario.OpenField)]
        [TestCase("Bench_Maze", "Benchmark_Maze.json", BenchScenario.Maze)]
        [TestCase("Bench_Choke", "Benchmark_Choke.json", BenchScenario.ChokePoints)]
        public void PortReproducesRecordedPathAndWallCount(string mapName, string benchFile, BenchScenario scenario)
        {
            Recorded recorded = ReadRecorded(Path.Combine(TestPaths.ProjectRoot, benchFile));

            MapData map = MapData.Load(TestPaths.MapFile(mapName));
            var grid = new SimGrid(map);
            int walls = BenchScenarios.Apply(grid, map, scenario);
            new FlowField().Generate(grid, map.Core);

            SimNode spawn = grid.Get(map.Spawns[0]);
            int tiles = Route.CountHops(grid, spawn);
            double cost = Route.CostInTiles(spawn);

            string context = "\n" + AsciiMap.Render(grid, map, AsciiLayer.Route);
            Assert.AreEqual(recorded.GridX, grid.Width, "grid width" + context);
            Assert.AreEqual(recorded.GridY, grid.Height, "grid height" + context);
            Assert.AreEqual(recorded.Walls, walls, "wall count" + context);
            Assert.AreEqual(recorded.Tiles, tiles, "path tiles" + context);
            Assert.AreEqual(recorded.CostTileUnits, cost, 1e-9, "path cost (tile units)" + context);
        }

        private sealed class Recorded
        {
            public int GridX, GridY, Walls, Tiles;
            public double CostTileUnits;
        }

        // The benchmark JSON is hand-built by StringBuilder; a regex over its two
        // fixed-shape objects is simpler and stricter than a full model.
        private static Recorded ReadRecorded(string path)
        {
            Assert.IsTrue(File.Exists(path), "missing recorded benchmark: " + path);
            string text = File.ReadAllText(path);

            Match grid = Regex.Match(text, "\"grid\":\\{\"x\":(\\d+),\"y\":(\\d+),\"nodes\":\\d+,\"walls\":(\\d+)");
            Match route = Regex.Match(text, "\"path\":\\{\"tiles\":(\\d+),\"costTileUnits\":(-?[\\d.]+)\\}");
            Assert.IsTrue(grid.Success && route.Success, "could not read grid/path metrics from " + path);

            return new Recorded
            {
                GridX = int.Parse(grid.Groups[1].Value, CultureInfo.InvariantCulture),
                GridY = int.Parse(grid.Groups[2].Value, CultureInfo.InvariantCulture),
                Walls = int.Parse(grid.Groups[3].Value, CultureInfo.InvariantCulture),
                Tiles = int.Parse(route.Groups[1].Value, CultureInfo.InvariantCulture),
                CostTileUnits = double.Parse(route.Groups[2].Value, CultureInfo.InvariantCulture),
            };
        }
    }
}
