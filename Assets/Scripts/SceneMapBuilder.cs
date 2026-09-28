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
            OriginX = origin.x,
            OriginZ = origin.z,
            Rows = rows,
        };

        // Tile lookups go through the pure port, so the tiles are exactly what the
        // simulation will compute for these positions.
        var grid = new SimGrid(map);
        map.Core = Tile(grid, core.position);
        map.CoreWorld = new WorldPoint(core.position.x, core.position.y, core.position.z);
        map.Spawns = new TileCoord[spawns.Count];
        map.SpawnWorlds = new WorldPoint[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            Vector3 p = spawns[i].position;
            map.Spawns[i] = Tile(grid, p);
            map.SpawnWorlds[i] = new WorldPoint(p.x, p.y, p.z);
        }

        map.Validate();
        return map;
    }

    public static Vector3 ToWorld(Vec2f xz, float y) { return new Vector3(xz.X, y, xz.Y); }

    private static TileCoord Tile(SimGrid grid, Vector3 world)
    {
        SimNode n = grid.NodeFromPosition(new Vec2f(world.x, world.z));
        return new TileCoord(n.X, n.Y);
    }
}
