using System.Collections.Generic;
using System.Text;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;

// Builds the simulation's static map from a live scene: the same Physics.CheckSphere
// scan GridManager.CreateGrid used to do, plus the core and spawn transforms. Used
// at runtime by SimHost and ScenarioBenchmark, and in the editor by MapExporter
// (which additionally cross-checks the result against Unity's own math).
public static class SceneMapBuilder
{
    public static MapData Build(GridManager gridManager, Transform core, IList<Transform> spawns, string name)
    {
        float nodeRadius = gridManager.nodeRadius;
        float nodeDiameter = nodeRadius * 2;
        int width = Mathf.RoundToInt(gridManager.gridWorldSize.x / nodeDiameter);
        int height = Mathf.RoundToInt(gridManager.gridWorldSize.y / nodeDiameter);
        Vector3 origin = gridManager.transform.position;

        // Same arithmetic, same order, as GridManager.CreateGrid.
        Physics.SyncTransforms();
        Vector3 worldBottomLeft = origin - Vector3.right * gridManager.gridWorldSize.x / 2 - Vector3.forward * gridManager.gridWorldSize.y / 2;
        var rows = new string[height];
        for (int y = height - 1; y >= 0; y--)
        {
            var row = new StringBuilder(width);
            for (int x = 0; x < width; x++)
            {
                Vector3 worldPoint = worldBottomLeft + Vector3.right * (x * nodeDiameter + nodeRadius) + Vector3.forward * (y * nodeDiameter + nodeRadius);
                row.Append(Physics.CheckSphere(worldPoint, nodeRadius, gridManager.unwalkableMask) ? 'X' : '.');
            }
            rows[height - 1 - y] = row.ToString();
        }

        var map = new MapData
        {
            Name = name,
            Width = width,
            Height = height,
            WorldSizeX = gridManager.gridWorldSize.x,
            WorldSizeY = gridManager.gridWorldSize.y,
            NodeRadius = nodeRadius,
            OriginX = NoNegativeZero(origin.x),
            OriginZ = NoNegativeZero(origin.z),
            Rows = rows,
        };

        // Tile lookups go through the pure port, so the tiles are exactly what the
        // simulation will compute for these positions.
        var grid = new SimGrid(map);
        map.Core = Tile(grid, core.position);
        map.CoreWorld = Point(core.position);
        map.Spawns = new TileCoord[spawns.Count];
        map.SpawnWorlds = new WorldPoint[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            Vector3 p = spawns[i].position;
            map.Spawns[i] = Tile(grid, p);
            map.SpawnWorlds[i] = Point(p);
        }

        map.Validate();
        return map;
    }

    public static Vector3 ToWorld(Vec2f xz, float y) { return new Vector3(xz.X, y, xz.Y); }

    // A Unity transform can sit at -0.0 (SampleScene's GridManager does, on Z). It
    // behaves exactly like 0 in every sum here, but its bit pattern is different, and
    // the JSON writer drops the sign: -0.0 is written as "0.0" and read back as +0.0
    // (seen under both Unity's Mono and .NET). The first replay recorded in Unity
    // therefore failed its own setup check when read back (WP5): the simulation had
    // run on -0.0 and the file said +0.0. Map data never carries a negative zero.
    private static float NoNegativeZero(float v) { return v == 0f ? 0f : v; }

    private static WorldPoint Point(Vector3 p) { return new WorldPoint(NoNegativeZero(p.x), NoNegativeZero(p.y), NoNegativeZero(p.z)); }

    private static TileCoord Tile(SimGrid grid, Vector3 world)
    {
        SimNode n = grid.NodeFromPosition(new Vec2f(world.x, world.z));
        return new TileCoord(n.X, n.Y);
    }
}
