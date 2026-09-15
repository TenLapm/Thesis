using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Diag = System.Diagnostics;

// Automated performance evaluation for the flow-field pathfinder.
// Runs four scenarios and writes BenchmarkResults.json to the project root:
//   A) Field rebuild time vs grid size x wall density (micro-benchmark)
//   B) One shared flow field vs per-agent A* (same grid, same cost model)
//   C) Frame time vs live agent count (real agents following the scene field)
//   D) Frame stability while the map is edited continuously under agent load
public class BenchmarkRunner : MonoBehaviour
{
    [Header("Scene References")]
    public GridManager gridManager;
    public FlowFieldManager flowManager;
    public Transform spawnPoint;
    public GameObject agentPrefab;

    [Header("Scenario Settings")]
    public int rebuildIterations = 30;
    public int[] gridSizes = new int[] { 25, 38, 50, 75, 100 };
    public float[] wallDensities = new float[] { 0f, 0.10f, 0.25f };
    public int[] agentTiers = new int[] { 100, 250, 500, 1000 };
    public int[] astarCounts = new int[] { 100, 500, 1000 };
    public float tierWarmupSeconds = 1.0f;
    public float tierSampleSeconds = 4.0f;
    public float editIntervalSeconds = 0.5f;
    public float editPhaseSeconds = 8.0f;

    public static bool Done = false;
    public static string LastResultPath = "";

    private readonly List<GameObject> pool = new List<GameObject>();
    private System.Text.StringBuilder json;

    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Done = false;
        StartCoroutine(RunAll());
    }

    IEnumerator RunAll()
    {
        json = new System.Text.StringBuilder();
        json.Append("{\n");
        AppendEnvironment();

        yield return null; // let the scene settle one frame

        // ---------- A) rebuild sweep ----------
        json.Append("\"rebuildSweep\":[");
        bool firstA = true;
        foreach (int size in gridSizes)
        {
            foreach (float density in wallDensities)
            {
                var r = BenchRebuild(size, density);
                if (!firstA) json.Append(",");
                firstA = false;
                json.Append("\n {\"size\":" + size + ",\"density\":" + F(density) +
                            ",\"meanMs\":" + F(r.x) + ",\"minMs\":" + F(r.y) + ",\"maxMs\":" + F(r.z) + "}");
                yield return null;
            }
        }
        json.Append("],\n");
        Debug.Log("[Bench] A: rebuild sweep done");

        // ---------- B) flow field vs per-agent A* ----------
        json.Append("\"astarComparison\":");
        yield return null;
        BenchAStar();
        Debug.Log("[Bench] B: A* comparison done");

        // ---------- C) frame time vs agent count ----------
        json.Append("\"agentTiers\":[");
        bool firstC = true;
        foreach (int n in agentTiers)
        {
            yield return StartCoroutine(EnsureAgents(n));
            yield return new WaitForSecondsRealtime(tierWarmupSeconds);
            var s = new FrameSampler();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < tierSampleSeconds)
            {
                KeepAliveAgents();
                yield return null;
                s.Add(Time.unscaledDeltaTime);
            }
            if (!firstC) json.Append(",");
            firstC = false;
            json.Append("\n {\"agents\":" + n + "," + s.ToJson() + "}");
            Debug.Log("[Bench] C tier " + n + ": " + s.Summary());
        }
        json.Append("],\n");

        // ---------- D) dynamic edits under load (500 agents) ----------
        yield return StartCoroutine(EnsureAgents(500));
        yield return new WaitForSecondsRealtime(0.5f);
        var sd = new FrameSampler();
        int edits = 0;
        float nextEdit = 0f, tStart = Time.realtimeSinceStartup;
        var editedTiles = new List<Node>();
        while (Time.realtimeSinceStartup - tStart < editPhaseSeconds)
        {
            KeepAliveAgents();
            if (Time.realtimeSinceStartup - tStart > nextEdit)
            {
                nextEdit += editIntervalSeconds;
                ToggleRandomWallBlock(editedTiles);
                flowManager.GenerateFlowField();
                edits++;
            }
            yield return null;
            sd.Add(Time.unscaledDeltaTime);
        }
        foreach (var n in editedTiles) ClearWall(n); // leave the grid clean
        flowManager.GenerateFlowField();
        json.Append("\"dynamicEdits\":{\"agents\":500,\"edits\":" + edits + "," + sd.ToJson() + "},\n");
        Debug.Log("[Bench] D: dynamic edits done (" + edits + " edits): " + sd.Summary());

        // ---------- write ----------
        json.Append("\"done\":true\n}");
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../BenchmarkResults.json"));
        System.IO.File.WriteAllText(path, json.ToString());
        LastResultPath = path;
        Done = true;
        Debug.Log("[Bench] COMPLETE -> " + path);
    }

    // ============ Scenario A helpers ============
    // Returns (meanMs, minMs, maxMs) for a full flow-field rebuild.
    Vector3 BenchRebuild(int size, float wallDensity)
    {
        GameObject g = new GameObject("BenchGrid_" + size);
        g.SetActive(false);
        var grid = g.AddComponent<GridManager>();
        grid.gridWorldSize = new Vector2(size * 2f, size * 2f);
        grid.nodeRadius = 1f;
        grid.unwalkableMask = 0;
        var flow = g.AddComponent<FlowFieldManager>();
        flow.gridManager = grid;
        var goalGO = new GameObject("BenchGoal");
        goalGO.transform.position = new Vector3(size * 0.8f, 0, size * 0.8f);
        flow.targetGoal = goalGO.transform;
        g.SetActive(true); // Awake builds the grid

        Node goalNode = grid.NodeFromWorldPoint(flow.targetGoal.position);
        int walls = 0, want = Mathf.RoundToInt(size * size * wallDensity);
        int guard = 0;
        while (walls < want && guard++ < want * 20)
        {
            var n = grid.grid[Random.Range(0, grid.gridSizeX), Random.Range(0, grid.gridSizeY)];
            if (n == goalNode || n.HasWall) continue;
            n.terrainCost = 15; n.wallHealth = 6f; n.maxWallHealth = 6f;
            walls++;
        }

        for (int i = 0; i < 3; i++) flow.GenerateFlowField(); // warmup
        var sw = new Diag.Stopwatch();
        double sum = 0, mn = double.MaxValue, mx = 0;
        for (int i = 0; i < rebuildIterations; i++)
        {
            sw.Restart();
            flow.GenerateFlowField();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            sum += ms; if (ms < mn) mn = ms; if (ms > mx) mx = ms;
        }
        Destroy(goalGO);
        Destroy(g);
        return new Vector3((float)(sum / rebuildIterations), (float)mn, (float)mx);
    }

    // ============ Scenario B: per-agent A* ============
    void BenchAStar()
    {
        // Dedicated 38x38 grid with 10% walls - mirrors the game map scale.
        GameObject g = new GameObject("AStarGrid");
        g.SetActive(false);
        var grid = g.AddComponent<GridManager>();
        grid.gridWorldSize = new Vector2(76, 76);
        grid.nodeRadius = 1f;
        grid.unwalkableMask = 0;
        var flow = g.AddComponent<FlowFieldManager>();
        grid.gameObject.SetActive(true);
        flow.gridManager = grid;
        var goalGO = new GameObject("AStarGoal");
        goalGO.transform.position = new Vector3(30, 0, 30);
        flow.targetGoal = goalGO.transform;

        Node goalNode = grid.NodeFromWorldPoint(goalGO.transform.position);
        int want = Mathf.RoundToInt(grid.gridSizeX * grid.gridSizeY * 0.10f), walls = 0, guard = 0;
        while (walls < want && guard++ < want * 20)
        {
            var n = grid.grid[Random.Range(0, grid.gridSizeX), Random.Range(0, grid.gridSizeY)];
            if (n == goalNode || n.HasWall) continue;
            n.terrainCost = 15; n.wallHealth = 6f; n.maxWallHealth = 6f;
            walls++;
        }

        // time ONE field rebuild that serves every agent
        var sw = new Diag.Stopwatch();
        for (int i = 0; i < 3; i++) flow.GenerateFlowField();
        sw.Restart(); flow.GenerateFlowField(); sw.Stop();
        double fieldMs = sw.Elapsed.TotalMilliseconds;

        json.Append("{\"gridSize\":" + grid.gridSizeX + ",\"wallDensity\":0.10,\"fieldRebuildMs\":" + F(fieldMs) + ",\"runs\":[");
        bool first = true;
        foreach (int count in astarCounts)
        {
            // N random walkable starts
            var starts = new List<Node>();
            while (starts.Count < count)
            {
                var n = grid.grid[Random.Range(0, grid.gridSizeX), Random.Range(0, grid.gridSizeY)];
                if (!n.HasWall && n != goalNode) starts.Add(n);
            }
            AStar(grid, starts[0], goalNode); // warmup
            sw.Restart();
            foreach (var s0 in starts) AStar(grid, s0, goalNode);
            sw.Stop();
            double totalMs = sw.Elapsed.TotalMilliseconds;
            if (!first) json.Append(",");
            first = false;
            json.Append("{\"agents\":" + count + ",\"totalMs\":" + F(totalMs) + ",\"perAgentMs\":" + F(totalMs / count) + "}");
        }
        json.Append("]},\n");
        Destroy(goalGO);
        Destroy(g);
    }

    // Standard A* with a binary heap, same 10/14 x terrainCost step model as the field.
    void AStar(GridManager grid, Node start, Node goal)
    {
        int w = grid.gridSizeX, h = grid.gridSizeY, total = w * h;
        int[] gCost = new int[total];
        bool[] closed = new bool[total];
        for (int i = 0; i < total; i++) gCost[i] = int.MaxValue;
        int Idx(Node n) => n.gridX * h + n.gridY;
        int Heu(Node n)
        {
            int dx = Mathf.Abs(n.gridX - goal.gridX), dy = Mathf.Abs(n.gridY - goal.gridY);
            int lo = Mathf.Min(dx, dy), hi = Mathf.Max(dx, dy);
            return lo * 14 + (hi - lo) * 10; // octile, admissible with min terrainCost = 1
        }
        var heap = new MinHeap(total);
        gCost[Idx(start)] = 0;
        heap.Push(Idx(start), Heu(start));
        var byIndex = new Node[total];
        foreach (Node n in grid.grid) byIndex[Idx(n)] = n;

        while (heap.Count > 0)
        {
            int ci = heap.Pop();
            if (closed[ci]) continue;
            closed[ci] = true;
            Node cur = byIndex[ci];
            if (cur == goal) return;
            foreach (Node nb in grid.GetNeighbors(cur))
            {
                if (!nb.isWalkable) continue;
                int ni = Idx(nb);
                if (closed[ni]) continue;
                bool diag = nb.gridX != cur.gridX && nb.gridY != cur.gridY;
                int step = (diag ? 14 : 10) * nb.terrainCost;
                int ng = gCost[ci] + step;
                if (ng < gCost[ni])
                {
                    gCost[ni] = ng;
                    heap.Push(ni, ng + Heu(nb));
                }
            }
        }
    }

    class MinHeap
    {
        int[] items; long[] keys; public int Count;
        public MinHeap(int cap) { items = new int[cap * 4]; keys = new long[cap * 4]; }
        public void Push(int item, long key)
        {
            if (Count == items.Length) { System.Array.Resize(ref items, Count * 2); System.Array.Resize(ref keys, Count * 2); }
            items[Count] = item; keys[Count] = key;
            int i = Count++;
            while (i > 0) { int p = (i - 1) / 2; if (keys[p] <= keys[i]) break; Swap(i, p); i = p; }
        }
        public int Pop()
        {
            int root = items[0];
            Count--; items[0] = items[Count]; keys[0] = keys[Count];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < Count && keys[l] < keys[s]) s = l;
                if (r < Count && keys[r] < keys[s]) s = r;
                if (s == i) break;
                Swap(i, s); i = s;
            }
            return root;
        }
        void Swap(int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            (keys[a], keys[b]) = (keys[b], keys[a]);
        }
    }

    // ============ Scenario C/D helpers ============
    IEnumerator EnsureAgents(int n)
    {
        int spawnedThisFrame = 0;
        while (ActiveCount() < n)
        {
            GameObject a = GetFromPool();
            PlaceAtSpawn(a);
            if (++spawnedThisFrame >= 50) { spawnedThisFrame = 0; yield return null; }
        }
        // deactivate extras when stepping down (not used in the current tier order)
        if (ActiveCount() > n)
        {
            int extra = ActiveCount() - n;
            foreach (var a in pool)
            {
                if (extra <= 0) break;
                if (a.activeInHierarchy) { a.SetActive(false); extra--; }
            }
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
        if (fa != null) fa.Initialize(flowManager, null, 999999f, 5f, null);
    }

    // Agents that reach the goal deactivate; respawn them so the tier count stays constant.
    void KeepAliveAgents()
    {
        foreach (var a in pool) if (a != null && !a.activeInHierarchy) PlaceAtSpawn(a);
    }

    void ToggleRandomWallBlock(List<Node> edited)
    {
        Node goalNode = gridManager.NodeFromWorldPoint(flowManager.targetGoal.position);
        int x = Random.Range(1, gridManager.gridSizeX - 2);
        int y = Random.Range(1, gridManager.gridSizeY - 2);
        for (int dx = 0; dx < 2; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                Node n = gridManager.grid[x + dx, y + dy];
                if (n == goalNode) continue;
                if (n.HasWall) ClearWall(n);
                else { n.terrainCost = 15; n.wallHealth = 6f; n.maxWallHealth = 6f; edited.Add(n); }
            }
        }
    }

    void ClearWall(Node n)
    {
        n.terrainCost = 1; n.wallHealth = 0f; n.maxWallHealth = 0f;
        if (n.visualObject != null) { Destroy(n.visualObject); n.visualObject = null; }
    }

    // ============ misc ============
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
