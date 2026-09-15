using System.Collections.Generic;
using System.IO;
using System.Text;
using Thesis.Core;
using Thesis.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exports a scene's static grid geometry to Maps/<SceneName>.map.json so the
// simulation (Thesis.Sim) can run without the scene. It replicates
// GridManager.CreateGrid's Physics.CheckSphere scan in the editor, then
// cross-checks the pure port against Unity's own math before writing anything.
public static class MapExporter
{
    private static readonly string[] AllScenes =
    {
        "Assets/Scenes/SampleScene.unity",
        "Assets/Scenes/Bench_Open.unity",
        "Assets/Scenes/Bench_Maze.unity",
        "Assets/Scenes/Bench_Choke.unity",
    };

    [MenuItem("Thesis/Export Map (Active Scene)")]
    public static void ExportActiveScene()
    {
        ExportScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Thesis/Export All Maps")]
    public static void ExportAllMaps()
    {
        // Exporting opens other scenes. Refuse rather than prompt: a modal dialog
        // would hang automation, and silently discarding edits is worse.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (open.isDirty)
            {
                Debug.LogError("[MapExporter] Scene '" + open.name + "' has unsaved changes. Save or discard them, then export again.");
                return;
            }
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        int exported = 0;
        try
        {
            foreach (string path in AllScenes)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (ExportScene(scene)) exported++;
            }
        }
        finally
        {
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        Debug.Log("[MapExporter] Exported " + exported + "/" + AllScenes.Length + " maps.");
    }

    private static bool ExportScene(Scene scene)
    {
        GridManager gridManager = FindInScene<GridManager>(scene);
        FlowFieldManager flowManager = FindInScene<FlowFieldManager>(scene);
        if (gridManager == null || flowManager == null || flowManager.targetGoal == null)
        {
            Debug.LogError("[MapExporter] '" + scene.name + "': needs a GridManager and a FlowFieldManager with targetGoal.");
            return false;
        }

        List<Transform> spawnTransforms = FindSpawns(scene);
        if (spawnTransforms.Count == 0)
        {
            Debug.LogError("[MapExporter] '" + scene.name + "': no spawn found (WaveSpawner.spawnPoint, ScenarioBenchmark.spawnPoint or a GameObject named 'Spawner').");
            return false;
        }

        // Same derivation as GridManager.Awake / CreateGrid.
        float nodeRadius = gridManager.nodeRadius;
        float nodeDiameter = nodeRadius * 2;
        int width = Mathf.RoundToInt(gridManager.gridWorldSize.x / nodeDiameter);
        int height = Mathf.RoundToInt(gridManager.gridWorldSize.y / nodeDiameter);
        Vector3 origin = gridManager.transform.position;

        if (Mathf.Abs(origin.x) > 1e-4f || Mathf.Abs(origin.z) > 1e-4f)
        {
            // NodeFromWorldPoint ignores the origin; CreateGrid does not. Both are
            // ported as-is, but an off-centre grid means the two disagree.
            Debug.LogWarning("[MapExporter] '" + scene.name + "': GridManager is not at world (0, 0) (" + origin + "). NodeFromWorldPoint ignores the origin, so tile lookups are offset in the original game too.");
        }

        Physics.SyncTransforms();
        Vector3 worldBottomLeft = origin - Vector3.right * gridManager.gridWorldSize.x / 2 - Vector3.forward * gridManager.gridWorldSize.y / 2;
        var unityCentres = new Vector3[width, height];
        var rows = new string[height];
        var rowBuilders = new StringBuilder[height];
        for (int r = 0; r < height; r++) rowBuilders[r] = new StringBuilder(width);

        for (int y = height - 1; y >= 0; y--)
        {
            StringBuilder row = rowBuilders[height - 1 - y];
            for (int x = 0; x < width; x++)
            {
                Vector3 worldPoint = worldBottomLeft + Vector3.right * (x * nodeDiameter + nodeRadius) + Vector3.forward * (y * nodeDiameter + nodeRadius);
                unityCentres[x, y] = worldPoint;
                bool walkable = !Physics.CheckSphere(worldPoint, nodeRadius, gridManager.unwalkableMask);
                row.Append(walkable ? '.' : 'X');
            }
        }
        for (int r = 0; r < height; r++) rows[r] = rowBuilders[r].ToString();

        var map = new MapData
        {
            Name = scene.name,
            Width = width,
            Height = height,
            WorldSizeX = gridManager.gridWorldSize.x,
            WorldSizeY = gridManager.gridWorldSize.y,
            NodeRadius = nodeRadius,
            OriginX = origin.x,
            OriginZ = origin.z,
            Rows = rows,
        };

        // Tile lookups go through the pure port so the exported tiles are exactly
        // what the simulation will compute.
        var grid = new SimGrid(map);
        Vector3 corePos = flowManager.targetGoal.position;
        map.Core = Tile(grid, corePos);
        map.CoreWorld = new WorldPoint(corePos.x, corePos.y, corePos.z);
        map.Spawns = new TileCoord[spawnTransforms.Count];
        map.SpawnWorlds = new WorldPoint[spawnTransforms.Count];
        for (int i = 0; i < spawnTransforms.Count; i++)
        {
            Vector3 p = spawnTransforms[i].position;
            map.Spawns[i] = Tile(grid, p);
            map.SpawnWorlds[i] = new WorldPoint(p.x, p.y, p.z);
        }

        int mismatches = CrossCheckPort(scene.name, gridManager, grid, unityCentres, width, height);
        if (mismatches > 0)
        {
            Debug.LogError("[MapExporter] '" + scene.name + "': the pure port disagrees with Unity's math in " + mismatches + " checks. Not writing the map.");
            return false;
        }

        try
        {
            map.Validate();
        }
        catch (InvalidDataException e)
        {
            Debug.LogError("[MapExporter] '" + scene.name + "': " + e.Message);
            return false;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Maps"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, scene.name + ".map.json");
        File.WriteAllText(file, map.ToJson());

        int blocked = 0;
        foreach (string row in rows) foreach (char c in row) if (c == 'X') blocked++;
        Debug.Log("[MapExporter] '" + scene.name + "' -> " + file + "  (" + width + "x" + height + ", " + blocked + " static blockers, core " + map.Core + ", spawns " + string.Join(" ", map.Spawns) + ")");
        return true;
    }

    // Compares SimGrid against Unity's own arithmetic:
    //   * every tile centre must equal GridManager.CreateGrid's worldPoint exactly;
    //   * NodeFromPosition must equal a verbatim copy of NodeFromWorldPoint (using
    //     Unity's Mathf.Clamp01 / Mathf.RoundToInt) at 4,000 sample positions,
    //     including positions off the map (clamping) and near tile boundaries.
    private static int CrossCheckPort(string sceneName, GridManager gm, SimGrid grid, Vector3[,] unityCentres, int width, int height)
    {
        int mismatches = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vec2f p = grid[x, y].Position;
                if (p.X != unityCentres[x, y].x || p.Y != unityCentres[x, y].z)
                {
                    if (mismatches < 5) Debug.LogError("[MapExporter] '" + sceneName + "': tile (" + x + "," + y + ") centre " + p + " != Unity " + unityCentres[x, y]);
                    mismatches++;
                }
            }
        }

        // Deterministic samples; System.Random is fine here (editor tool, not simulation).
        var rng = new System.Random(20260915);
        float spanX = gm.gridWorldSize.x * 0.6f;
        float spanZ = gm.gridWorldSize.y * 0.6f;
        for (int i = 0; i < 4000; i++)
        {
            float wx = (float)(rng.NextDouble() * 2 - 1) * spanX;
            float wz = (float)(rng.NextDouble() * 2 - 1) * spanZ;

            float percentX = Mathf.Clamp01((wx + gm.gridWorldSize.x / 2) / gm.gridWorldSize.x);
            float percentY = Mathf.Clamp01((wz + gm.gridWorldSize.y / 2) / gm.gridWorldSize.y);
            int ux = Mathf.RoundToInt((width - 1) * percentX);
            int uy = Mathf.RoundToInt((height - 1) * percentY);

            SimNode n = grid.NodeFromPosition(new Vec2f(wx, wz));
            if (n.X != ux || n.Y != uy)
            {
                if (mismatches < 5) Debug.LogError("[MapExporter] '" + sceneName + "': NodeFromPosition(" + wx + ", " + wz + ") = (" + n.X + "," + n.Y + ") but NodeFromWorldPoint = (" + ux + "," + uy + ")");
                mismatches++;
            }
        }

        return mismatches;
    }

    private static TileCoord Tile(SimGrid grid, Vector3 world)
    {
        SimNode n = grid.NodeFromPosition(new Vec2f(world.x, world.z));
        return new TileCoord(n.X, n.Y);
    }

    private static List<Transform> FindSpawns(Scene scene)
    {
        var result = new List<Transform>();

        WaveSpawner waveSpawner = FindInScene<WaveSpawner>(scene);
        if (waveSpawner != null && waveSpawner.spawnPoint != null) result.Add(waveSpawner.spawnPoint);

        if (result.Count == 0)
        {
            ScenarioBenchmark bench = FindInScene<ScenarioBenchmark>(scene);
            if (bench != null && bench.spawnPoint != null) result.Add(bench.spawnPoint);
        }

        if (result.Count == 0)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Spawner")
                    {
                        result.Add(t);
                        return result;
                    }
                }
            }
        }

        return result;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }
}
