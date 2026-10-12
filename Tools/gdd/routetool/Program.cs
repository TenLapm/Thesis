// The walker and sapper routes on a drawn board, by the game's own flow field. Used only
// for the pictures of Docs/GDD.md (Tools/gdd/README.md).
//   routetool <board.txt> <towers.txt> <out.txt>
// board.txt is an AsciiMap fixture (X rock, # wall, S spawn, C core); towers.txt has one
// "x,y" per line. A tower is built ground that counts 20 (a wall counts 15).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thesis.Core;
using Thesis.Sim;

public static class Program
{
    public static int Main(string[] args)
    {
        AsciiFixture fixture = AsciiMap.Parse(File.ReadAllText(args[0]), "gdd");
        SimGrid grid = fixture.Grid;
        MapData map = fixture.Map;

        foreach (string line in File.ReadAllLines(args[1]))
        {
            string[] p = line.Split(',');
            if (p.Length < 2) continue;
            grid.SetWall(grid[int.Parse(p[0]), int.Parse(p[1])], 20, 10f);
        }

        new FlowFieldSet(0.2f).Generate(grid, map.Core);

        var sb = new StringBuilder();
        var route = new List<SimNode>();
        foreach (MovementClass movement in new[] { MovementClass.Ground, MovementClass.Sapper })
        {
            for (int s = 0; s < map.Spawns.Length; s++)
            {
                Route.Collect(grid, grid.Get(map.Spawns[s]), route, movement);
                int through = 0;
                sb.Append(movement).Append(' ').Append(s).Append(' ')
                  .Append(Route.CostInTiles(grid.Get(map.Spawns[s]), movement).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                foreach (SimNode n in route)
                {
                    if (n.HasWall) through++;
                    sb.Append(n.X).Append(',').Append(n.Y).Append(n.HasWall ? "!" : "").Append(' ');
                }
                sb.Append('\n');
                Console.WriteLine(movement + " from spawn " + s + ": " + route.Count + " tiles, cost " + Route.CostInTiles(grid.Get(map.Spawns[s]), movement).ToString("0.0") + ", through " + through + " built tiles");
            }
        }
        File.WriteAllText(args[2], sb.ToString());

        Console.WriteLine(AsciiMap.Render(grid, map, AsciiLayer.Route, true, MovementClass.Ground));
        return 0;
    }
}
