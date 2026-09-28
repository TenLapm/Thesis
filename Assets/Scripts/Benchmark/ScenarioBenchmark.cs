using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Diag = System.Diagnostics;

// Scene-local performance evaluation. Builds a deterministic wall layout
// (the scenario), then measures:
//   1) flow-field rebuild time on this exact layout,
//   2) path metrics from spawn to goal (tiles + cost),
//   3) frame time at increasing live agent counts.
// Writes Runs/Benchmark_<label>.json and logs "[Bench] COMPLETE" when done. One of
// these sits in every Bench_* scene; the scenes double as stress demos because the
// agents keep marching after the measurements finish.
//
// WP4 port: the layout, the field and the agents are the simulation's
// (Thesis.Sim.SimGrid / FlowField / AgentSystem, stepped at the game's fixed 50 Hz),
// drawn by FlowAgent views. Output moved from the project root to Runs/ so a run
// never overwrites the committed Benchmark_*.json files, which are the parity
// oracle recorded by the pre-port code (BenchParityTests reads them).
public class ScenarioBenchmark : MonoBehaviour
{
    // Values mirror Thesis.Sim.BenchScenario; kept as its own enum so the scenes'
    // serialized "scenario: N" keeps its meaning.
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

    private const float TickSeconds = 0.02f; // the game's fixed step
    private const int MaxTicksPerFrame = 20;

    private MapData map;
    private SimGrid grid;
    private readonly FlowField field = new FlowField();
    private OccupancyMap occupancy;
    private readonly List<AgentState> agents = new List<AgentState>();
    private readonly List<FlowAgent> views = new List<FlowAgent>();
    private readonly Pcg32 jitter = new Pcg32(20260915UL, RngStreams.Spawn);
    private float accumulator;

    private System.Text.StringBuilder json;
    private bool running = false;
    private int wallCount = 0;
    private Material wallSharedMaterial;

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

        StepAgents();
    }

    // ================= scenario layout =================

    void BuildScenario()
    {
        map = SceneMapBuilder.Build(gridManager, flowManager.targetGoal, new[] { spawnPoint }, gameObject.scene.name);
        grid = new SimGrid(map);
        occupancy = new OccupancyMap(grid.NodeCount);
        wallCount = BenchScenarios.Apply(grid, map, (BenchScenario)scenario, scatterDensity);
        field.Generate(grid, map.Core);
        flowManager.Bind(grid);

        // One cube per wall tile.
        float d = gridManager.nodeRadius * 2f;
        for (int i = 0; i < grid.NodeCount; i++)
        {
            SimNode n = grid.ByIndex(i);
            if (!n.HasWall) continue;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "BenchWall";
            cube.transform.SetParent(transform, true);
            cube.transform.position = SceneMapBuilder.ToWorld(n.Position, gridManager.transform.position.y) + Vector3.up * 0.5f;
            cube.transform.localScale = new Vector3(d * 0.95f, 1f, d * 0.95f);
            Destroy(cube.GetComponent<BoxCollider>()); // nothing uses physics against these
            if (wallSharedMaterial == null)
            {
                wallSharedMaterial = new Material(cube.GetComponent<Renderer>().sharedMaterial);
                wallSharedMaterial.color = new Color(0.65f, 0.3f, 0.25f);
            }
            cube.GetComponent<Renderer>().sharedMaterial = wallSharedMaterial;
            gridManager.grid[n.X, n.Y].visualObject = cube;
        }

        if (grid.Get(map.Spawns[0]).BestCost == SimNode.Infinity)
            Debug.LogError("[Bench] Scenario blocked the spawn off from the goal!");
    }

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
        json.Append("\"grid\":{\"x\":" + grid.Width + ",\"y\":" + grid.Height +
                    ",\"nodes\":" + grid.NodeCount +
                    ",\"walls\":" + wallCount + ",\"scatterDensity\":" + F(scenario == ScenarioKind.RandomScatter ? scatterDensity : 0f) + "},\n");

        yield return null;

        // ---- 1) rebuild timing on this exact layout ----
        var sw = new Diag.Stopwatch();
        for (int i = 0; i < 3; i++) field.Generate(grid, map.Core); // warmup
        double sum = 0, mn = double.MaxValue, mx = 0;
        for (int i = 0; i < rebuildIterations; i++)
        {
            sw.Restart();
            field.Generate(grid, map.Core);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            sum += ms; if (ms < mn) mn = ms; if (ms > mx) mx = ms;
        }
        json.Append("\"rebuild\":{\"iterations\":" + rebuildIterations + ",\"meanMs\":" + F(sum / rebuildIterations) +
                    ",\"minMs\":" + F(mn) + ",\"maxMs\":" + F(mx) + "},\n");
        Debug.Log("[Bench] rebuild mean " + (sum / rebuildIterations).ToString("0.###") + " ms");

        // ---- 2) path metrics from spawn ----
        SimNode spawnNode = grid.Get(map.Spawns[0]);
        int hops = Route.CountHops(grid, spawnNode);
        double cost = Route.CostInTiles(spawnNode);
        json.Append("\"path\":{\"tiles\":" + hops + ",\"costTileUnits\":" + (cost < 0 ? "-1" : F(cost)) + "},\n");
        Debug.Log("[Bench] path " + hops + " tiles, cost " + (cost < 0 ? "unreachable" : cost.ToString("0.#", CultureInfo.InvariantCulture)));

        // ---- 3) frame time vs live agent count ----
        json.Append("\"tiers\":[");
        bool first = true;
        foreach (int n in agentTiers)
        {
            EnsureAgents(n);
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < warmupSeconds) yield return null;

            var s = new FrameSampler();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < sampleSeconds)
            {
                yield return null;
                s.Add(Time.unscaledDeltaTime);
            }
            if (!first) json.Append(",");
            first = false;
            json.Append("\n {\"agents\":" + n + "," + s.ToJson() + "}");
            Debug.Log("[Bench] tier " + n + ": " + s.Summary());
        }
        json.Append("],\n\"done\":true\n}");

        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Runs"));
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "Benchmark_" + label + ".json");
        System.IO.File.WriteAllText(path, json.ToString());
        LastResultPath = path;
        Done = true;
        running = false;
        Debug.Log("[Bench] COMPLETE -> " + path);
    }

    // ================= agents (simulation + views) =================

    // Keeps `n` agents alive: effectively immortal (life 999999) and respawned at
    // the spawn when they reach the core, so the tier's count stays constant.
    void EnsureAgents(int n)
    {
        while (agents.Count < n)
        {
            var agent = new AgentState(agents.Count, SpawnPosition(), agentSpeed, 999999f, 1f, 0f, 0f);
            agents.Add(agent);

            GameObject go = Instantiate(agentPrefab);
            FlowAgent view = go.GetComponent<FlowAgent>();
            view.Bind(agent);
            views.Add(view);
        }
    }

    Vec2f SpawnPosition()
    {
        // A ring of radius <= 2 around the spawn, as the original did, but seeded.
        Vector3 p = spawnPoint.position;
        double angle = jitter.NextDouble() * 2.0 * System.Math.PI;
        double radius = 2.0 * System.Math.Sqrt(jitter.NextDouble());
        return new Vec2f(p.x + (float)(radius * System.Math.Cos(angle)), p.z + (float)(radius * System.Math.Sin(angle)));
    }

    void StepAgents()
    {
        if (agents.Count == 0) return;

        accumulator += Time.deltaTime;
        int ticks = 0;
        float budget = 0f;
        int coreHp = int.MaxValue;
        while (accumulator >= TickSeconds && ticks < MaxTicksPerFrame)
        {
            bool dirty = AgentSystem.Step(grid, agents, TickSeconds, occupancy, ref budget, ref coreHp, null);
            if (dirty) field.Generate(grid, map.Core);

            for (int i = 0; i < agents.Count; i++)
            {
                AgentState a = agents[i];
                if (!a.IsAlive)
                {
                    // Reached the core: respawn (a fresh state object, same id/slot).
                    a = new AgentState(a.Id, SpawnPosition(), agentSpeed, 999999f, 1f, 0f, 0f);
                    agents[i] = a;
                    views[i].Bind(a);
                }
                else
                {
                    views[i].OnSimTick(a);
                }
            }
            accumulator -= TickSeconds;
            ticks++;
        }
        if (accumulator > TickSeconds) accumulator = TickSeconds;

        float alpha = accumulator / TickSeconds;
        for (int i = 0; i < views.Count; i++) views[i].Render(alpha);
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
        json.Append("\"mode\":\"Editor play mode (vSync off, uncapped), Thesis.Sim fixed 50 Hz\"");
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
            double sum = 0; foreach (var d in dts) sum += d;
            double avg = sum / Mathf.Max(1, dts.Count);
            return dts.Count + " frames, avg " + (avg * 1000.0).ToString("0.00") + " ms (" + (1.0 / avg).ToString("0") + " fps)";
        }
    }

    static string F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
    static string Esc(string s) { return s == null ? "" : s.Replace("\\", "/").Replace("\"", "'"); }
}
