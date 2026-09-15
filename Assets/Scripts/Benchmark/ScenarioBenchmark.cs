using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using Diag = System.Diagnostics;

// Scene-local performance evaluation. Builds a deterministic wall layout
// (the scenario), then measures:
//   1) flow-field rebuild time on this exact layout,
//   2) path metrics from spawn to goal (tiles + cost),
//   3) frame time at increasing live agent counts.
// Writes Benchmark_<label>.json to the project root and logs
// "[Bench] COMPLETE" when done. One of these sits in every Bench_* scene;
// the scenes double as stress demos because the agents keep marching after
// the measurements finish.
public class ScenarioBenchmark : MonoBehaviour
{
    public enum ScenarioKind { OpenField, Maze, ChokePoints, RandomScatter }

    [Header("Scenario")]
    public ScenarioKind scenario = ScenarioKind.OpenField;
    [Tooltip("Used by RandomScatter: fraction of tiles turned into walls.")]
    [Range(0f, 0.4f)] public float scatterDensity = 0.10f;
    [Tooltip("File label: Benchmark_<label>.json. Falls back to the scenario name.")]
    public string resultLabel = "";
    public bool autoRun = true;
    public Key rerunKey = Key.B;

    [Header("Scene References (auto-found when empty)")]
    public GridManager gridManager;
    public FlowFieldManager flowManager;
    public Transform spawnPoint;
    public GameObject agentPrefab;

    [Header("Benchmark Settings")]
    public int[] agentTiers = { 100, 250, 500, 1000 };
    public float warmupSeconds = 1f;
    public float sampleSeconds = 4f;
    public int rebuildIterations = 50;
    public float agentSpeed = 5f;

    public static bool Done = false;
    public static string LastResultPath = "";

    // Scenario walls are terrain nobody can afford to dig through: cost 200
    // reads as "a 200-tile detour", and health 99999 means agents standing on
    // one (never happens - the field routes around) could not chew through it
    // during a run. This keeps the layout static so runs are comparable.
    private const int WALL_COST = 200;
    private const float WALL_HEALTH = 99999f;

    private readonly List<GameObject> pool = new List<GameObject>();
    private System.Text.StringBuilder json;
    private bool running = false;
    private int wallCount = 0;

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
        if (flowManager == null) flowManager = FindFirstObjectByType<FlowFieldManager>();
        if (spawnPoint == null)
        {
            GameObject sp = GameObject.Find("Spawner");
            if (sp != null) spawnPoint = sp.transform;
        }
        if (agentPrefab == null) agentPrefab = Resources.Load<GameObject>("Agent");

        BuildScenario();

        if (autoRun) StartCoroutine(RunAll());
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (!running && kb != null && kb[rerunKey].wasPressedThisFrame) StartCoroutine(RunAll());
    }

    // ================= scenario layouts =================

    void BuildScenario()
    {
        int W = gridManager.gridSizeX, H = gridManager.gridSizeY;
        Node spawnNode = gridManager.NodeFromWorldPoint(spawnPoint.position);
        Node goalNode = gridManager.NodeFromWorldPoint(flowManager.targetGoal.position);

        switch (scenario)
        {
            case ScenarioKind.OpenField:
                break;

            case ScenarioKind.Maze:
                // Horizontal serpentine: a full wall row every 5 tiles with a
                // 4-tile gap alternating between the left and right end.
                bool gapLeft = true;
                for (int y = 5; y <= H - 6; y += 5)
                {
                    for (int x = 0; x < W; x++)
                    {
                        bool inGap = gapLeft ? (x < 4) : (x >= W - 4);
                        if (!inGap) TrySetWall(x, y, spawnNode, goalNode);
                    }
                    gapLeft = !gapLeft;
                }
                break;

            case ScenarioKind.ChokePoints:
                // Three full walls, each with a single 3-tile gap; the gaps are
                // staggered so the crowd has to swing across the whole map.
                int[] rows = { H / 4, H / 2, (3 * H) / 4 };
                int[] gaps = { W / 2, W / 6, (5 * W) / 6 };
                for (int i = 0; i < rows.Length; i++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        if (Mathf.Abs(x - gaps[i]) > 1) TrySetWall(x, rows[i], spawnNode, goalNode);
                    }
                }
                break;

            case ScenarioKind.RandomScatter:
                Random.InitState(12345); // deterministic layout across runs
                int want = Mathf.RoundToInt(W * H * scatterDensity);
                int placed = 0, guard = 0;
                while (placed < want && guard++ < want * 30)
                {
                    if (TrySetWall(Random.Range(0, W), Random.Range(0, H), spawnNode, goalNode)) placed++;
                }
                break;
        }

        flowManager.GenerateFlowField();

        if (spawnNode.bestCost == Node.INFINITY)
            Debug.LogError("[Bench] Scenario blocked the spawn off from the goal!");
    }

    // Places a wall tile with a visual cube. Keeps a 2-tile clearance around
    // spawn and goal so agents always have room to enter and leave the maze.
    bool TrySetWall(int x, int y, Node spawnNode, Node goalNode)
    {
        Node n = gridManager.grid[x, y];
        if (!n.isWalkable || n.HasWall) return false;
        if (Mathf.Abs(x - spawnNode.gridX) <= 2 && Mathf.Abs(y - spawnNode.gridY) <= 2) return false;
        if (Mathf.Abs(x - goalNode.gridX) <= 2 && Mathf.Abs(y - goalNode.gridY) <= 2) return false;

        n.terrainCost = WALL_COST;
        n.wallHealth = WALL_HEALTH;
        n.maxWallHealth = WALL_HEALTH;

        float d = gridManager.nodeRadius * 2f;
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "BenchWall";
        cube.transform.SetParent(transform, true);
        cube.transform.position = n.worldPosition + Vector3.up * 0.5f;
        cube.transform.localScale = new Vector3(d * 0.95f, 1f, d * 0.95f);
        Destroy(cube.GetComponent<BoxCollider>()); // nothing uses physics against these
        if (wallSharedMaterial == null)
        {
            wallSharedMaterial = new Material(cube.GetComponent<Renderer>().sharedMaterial);
            wallSharedMaterial.color = new Color(0.65f, 0.3f, 0.25f);
        }
        cube.GetComponent<Renderer>().sharedMaterial = wallSharedMaterial;
        n.visualObject = cube;
        wallCount++;
        return true;
    }

    private Material wallSharedMaterial;

    // ================= benchmark =================

    IEnumerator RunAll()
    {
        running = true;
        Done = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Time.timeScale = 1f;

        string label = string.IsNullOrEmpty(resultLabel) ? scenario.ToString() : resultLabel;
        json = new System.Text.StringBuilder();
        json.Append("{\n");
        json.Append("\"scenario\":\"" + scenario + "\",\n");
        json.Append("\"label\":\"" + label + "\",\n");
        AppendEnvironment();
        json.Append("\"grid\":{\"x\":" + gridManager.gridSizeX + ",\"y\":" + gridManager.gridSizeY +
                    ",\"nodes\":" + (gridManager.gridSizeX * gridManager.gridSizeY) +
                    ",\"walls\":" + wallCount + ",\"scatterDensity\":" + F(scenario == ScenarioKind.RandomScatter ? scatterDensity : 0f) + "},\n");

        yield return null;

        // ---- 1) rebuild timing on this exact layout ----
        var sw = new Diag.Stopwatch();
        for (int i = 0; i < 3; i++) flowManager.GenerateFlowField(); // warmup
        double sum = 0, mn = double.MaxValue, mx = 0;
        for (int i = 0; i < rebuildIterations; i++)
        {
            sw.Restart();
            flowManager.GenerateFlowField();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            sum += ms; if (ms < mn) mn = ms; if (ms > mx) mx = ms;
        }
        json.Append("\"rebuild\":{\"iterations\":" + rebuildIterations + ",\"meanMs\":" + F(sum / rebuildIterations) +
                    ",\"minMs\":" + F(mn) + ",\"maxMs\":" + F(mx) + "},\n");
        Debug.Log("[Bench] rebuild mean " + (sum / rebuildIterations).ToString("0.###") + " ms");

        // ---- 2) path metrics from spawn ----
        Node spawnNode = gridManager.NodeFromWorldPoint(spawnPoint.position);
        int hops = 0;
        Node walk = spawnNode;
        int guard = gridManager.gridSizeX * gridManager.gridSizeY + 5;
        while (walk != null && walk.bestCost != 0 && guard-- > 0) { walk = walk.nextNode; hops++; }
        json.Append("\"path\":{\"tiles\":" + hops + ",\"costTileUnits\":" +
                    (spawnNode.bestCost == Node.INFINITY ? "-1" : F(spawnNode.bestCost / 10.0)) + "},\n");

        // ---- 3) frame time vs live agent count ----
        json.Append("\"tiers\":[");
        bool first = true;
        foreach (int n in agentTiers)
        {
            yield return StartCoroutine(EnsureAgents(n));
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < warmupSeconds) { KeepAliveAgents(); yield return null; }

            var s = new FrameSampler();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < sampleSeconds)
            {
                KeepAliveAgents();
                yield return null;
                s.Add(Time.unscaledDeltaTime);
            }
            if (!first) json.Append(",");
            first = false;
            json.Append("\n {\"agents\":" + n + "," + s.ToJson() + "}");
            Debug.Log("[Bench] tier " + n + ": " + s.Summary());
        }
        json.Append("],\n\"done\":true\n}");

        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Benchmark_" + label + ".json"));
        System.IO.File.WriteAllText(path, json.ToString());
        LastResultPath = path;
        Done = true;
        running = false;
        Debug.Log("[Bench] COMPLETE -> " + path);
    }

    // ================= agent pool =================

    IEnumerator EnsureAgents(int n)
    {
        int spawnedThisFrame = 0;
        while (ActiveCount() < n)
        {
            GameObject a = GetFromPool();
            PlaceAtSpawn(a);
            if (++spawnedThisFrame >= 50) { spawnedThisFrame = 0; yield return null; }
        }
    }

    int ActiveCount()
    {
        int c = 0;
        foreach (var a in pool) if (a != null && a.activeInHierarchy) c++;
        return c;
    }

    GameObject GetFromPool()
    {
        foreach (var a in pool) if (a != null && !a.activeInHierarchy) return a;
        var fresh = Instantiate(agentPrefab);
        fresh.SetActive(false);
        pool.Add(fresh);
        return fresh;
    }

    void PlaceAtSpawn(GameObject a)
    {
        Vector2 ring = Random.insideUnitCircle * 2f;
        a.transform.position = spawnPoint.position + new Vector3(ring.x, 0, ring.y);
        a.SetActive(true);
        var fa = a.GetComponent<FlowAgent>();
        if (fa != null) fa.Initialize(flowManager, null, 999999f, agentSpeed, null);
    }

    void KeepAliveAgents()
    {
        foreach (var a in pool) if (a != null && !a.activeInHierarchy) PlaceAtSpawn(a);
    }

    // ================= misc =================

    void AppendEnvironment()
    {
        json.Append("\"environment\":{");
        json.Append("\"unity\":\"" + Application.unityVersion + "\",");
        json.Append("\"os\":\"" + Esc(SystemInfo.operatingSystem) + "\",");
        json.Append("\"cpu\":\"" + Esc(SystemInfo.processorType) + "\",");
        json.Append("\"cores\":" + SystemInfo.processorCount + ",");
        json.Append("\"ramMB\":" + SystemInfo.systemMemorySize + ",");
        json.Append("\"gpu\":\"" + Esc(SystemInfo.graphicsDeviceName) + "\",");
        json.Append("\"mode\":\"Editor play mode (vSync off, uncapped)\"");
        json.Append("},\n");
    }

    class FrameSampler
    {
        readonly List<float> dts = new List<float>(4096);
        public void Add(float dt) { dts.Add(dt); }
        public string ToJson()
        {
            if (dts.Count == 0) return "\"frames\":0";
            var sorted = new List<float>(dts); sorted.Sort();
            double sum = 0; foreach (var d in dts) sum += d;
            double avg = sum / dts.Count;
            float p95 = sorted[Mathf.Min(sorted.Count - 1, Mathf.FloorToInt(sorted.Count * 0.95f))];
            float worst = sorted[sorted.Count - 1];
            return "\"frames\":" + dts.Count +
                   ",\"avgMs\":" + F(avg * 1000.0) +
                   ",\"fps\":" + F(1.0 / avg) +
                   ",\"p95Ms\":" + F(p95 * 1000f) +
                   ",\"worstMs\":" + F(worst * 1000f);
        }
        public string Summary()
        {
            var sorted = new List<float>(dts); sorted.Sort();
            double sum = 0; foreach (var d in dts) sum += d;
            double avg = sum / Mathf.Max(1, dts.Count);
            return dts.Count + " frames, avg " + (avg * 1000.0).ToString("0.00") + " ms (" + (1.0 / avg).ToString("0") + " fps)";
        }
    }

    static string F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
    static string Esc(string s) { return s == null ? "" : s.Replace("\\", "/").Replace("\"", "'"); }
}
