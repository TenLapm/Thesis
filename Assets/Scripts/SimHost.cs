using System;
using System.IO;
using Thesis.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs the headless Thesis.Sim.Simulation inside the scene (ARCHITECTURE.md §6).
// Everything that used to be game rules in MonoBehaviours now lives in the
// simulation; the scripts around it only feed input in (Submit) and draw state out
// (events, State, TickAlpha).
//
// Time: scaled Time.deltaTime fills an accumulator that is drained in fixed
// SimConfig.TickSeconds steps. GameSpeedController needs no change - x3 speed is
// simply three times as many ticks per frame, and pause (timeScale 0) is no ticks.
//
// Replay: every session is recorded (seed, every command with its tick, a state
// hash per wave and per tick) and written to Sessions/<session>/replay.json. The
// headless CLI re-runs that file and checks the hashes, which is the proof that
// Unity and the headless build play the same game (CLAUDE.md I1).
//
// Runs before every view (execution order -100) so they all see this frame's state.
// The simulation is created lazily on first access, so any script may touch it from
// its own Awake/Start regardless of Unity's Awake order.
[DefaultExecutionOrder(-100)]
public class SimHost : MonoBehaviour
{
    [Header("Simulation")]
    [Tooltip("All simulation tunables. Leave empty to run on SimConfig's defaults (SampleScene's values).")]
    public SimConfigAsset configAsset;
    [Tooltip("The run's seed. Same seed + same input = the same game (CLAUDE.md I1).")]
    public int seed = 1;
    [Tooltip("Pick a fresh seed each play session instead. It is logged, so a session can still be reproduced.")]
    public bool randomSeed = false;
    [Tooltip("Upper bound on ticks per rendered frame. After a long hitch the game slows down instead of freezing to catch up.")]
    public int maxTicksPerFrame = 20;

    [Header("Replay")]
    [Tooltip("Record this session to Sessions/<session>/replay.json (project Runs/ folder in the editor, persistentDataPath in a build).")]
    public bool recordReplay = true;
    [Tooltip("Also store one state hash per tick. Costs one hash per tick and about 1 MB per 25 minutes; without it a divergence can only be narrowed to a wave, not a tick.")]
    public bool recordTickHashes = true;

    [Header("Scene references (auto-found when empty)")]
    public GridManager gridManager;
    public FlowFieldManager flowManager;
    public WaveSpawner waveSpawner;
    public BlockManager blockManager;
    public DirectorHost directorHost;

    // Every event from FlushInput and from each Tick, in order.
    public event Action<SimEvent> SimEventRaised;

    // After each Tick, once its events are dispatched. Views snapshot positions here.
    public event Action Ticked;

    private Simulation sim;
    private MapData map;
    private SimConfig fallbackConfig;
    private float accumulator;
    private ReplayRecorder recorder;
    private string replayPath;
    private bool replayWriteFailed;

    // Where this session's replay is written, or null when recording is off.
    public string ReplayPath => replayPath;

    public Simulation Sim
    {
        get
        {
            EnsureStarted();
            return sim;
        }
    }

    public SimState State => Sim.State;

    public MapData Map
    {
        get
        {
            EnsureStarted();
            return map;
        }
    }

    public SimConfig Config => configAsset != null ? configAsset.config : fallbackConfig ?? (fallbackConfig = new SimConfig());

    public ulong Seed { get; private set; }

    // How far (0..1) the accumulator is toward the next tick: views interpolate
    // between the previous and the current tick's positions by this.
    public float TickAlpha { get; private set; }

    // Height of the grid plane; the simulation is 2-D (XZ), views need a Y.
    public float GroundY => gridManager != null ? gridManager.transform.position.y : 0f;

    public static SimHost Find() { return FindFirstObjectByType<SimHost>(); }

    void Awake()
    {
        EnsureStarted();
    }

    private void EnsureStarted()
    {
        if (sim != null) return;

        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
        if (flowManager == null) flowManager = FindFirstObjectByType<FlowFieldManager>();
        if (waveSpawner == null) waveSpawner = FindFirstObjectByType<WaveSpawner>();
        if (blockManager == null) blockManager = FindFirstObjectByType<BlockManager>();
        if (directorHost == null) directorHost = FindFirstObjectByType<DirectorHost>();

        if (gridManager == null || flowManager == null || flowManager.targetGoal == null || waveSpawner == null || waveSpawner.spawnPoint == null || blockManager == null)
        {
            throw new InvalidOperationException("[Sim] SimHost needs a GridManager, a FlowFieldManager with targetGoal, a WaveSpawner with spawnPoint and a BlockManager in the scene.");
        }

        Seed = randomSeed ? (ulong)(uint)Environment.TickCount : (ulong)(uint)seed;
        map = SceneMapBuilder.Build(gridManager, flowManager.targetGoal, new[] { waveSpawner.spawnPoint }, SceneManager.GetActiveScene().name);
        IWavePlanner planner = directorHost != null ? directorHost.CreatePlanner(Config, map) : new EscalationPlanner(Config, map);
        sim = new Simulation(Config, map, blockManager.BuildShapeLibrary(), Seed, planner);
        flowManager.Bind(sim.State.Grid);

        Debug.Log("[Sim] Started: map '" + map.Name + "' " + map.Width + "x" + map.Height + ", seed " + Seed + ", planner '" + planner.Name
                  + "', " + (configAsset != null ? "config '" + configAsset.name + "'" : "default config") + ".");

        if (recordReplay) StartRecording();
    }

    // Must run before the first command and the first tick: the recorder takes the
    // initial state hash here.
    private void StartRecording()
    {
        // Wall-clock time only names the folder; it never reaches the simulation.
        // Invariant culture: a machine set to a non-Gregorian calendar would otherwise
        // name the folder with a different year.
        string session = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "_seed" + Seed;
        recorder = new ReplayRecorder(sim, recordTickHashes)
        {
            Build = "Unity " + Application.unityVersion + (Application.isEditor ? " editor" : " player " + Application.version),
            Session = session,
            Policy = "human",
        };
        replayPath = Path.Combine(SessionsRoot(), session, "replay.json");
        Debug.Log("[Replay] Recording to " + replayPath);
    }

    // In the editor the project's Runs/ folder (gitignored), next to the headless
    // runs, so `cli replay` can be pointed at it directly. In a build there is no
    // project folder, so persistentDataPath.
    public static string SessionsRoot()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Runs", "Sessions"));
#else
        return Path.Combine(Application.persistentDataPath, "Sessions");
#endif
    }

    // Rewrites the whole file with the recording so far. Called at every wave
    // boundary, so a crash loses at most the wave in progress, and once more when
    // the session ends. A failed write is logged once and never stops the game.
    private void WriteReplay()
    {
        if (recorder == null || replayWriteFailed) return;
        try
        {
            recorder.Snapshot().Save(replayPath);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            replayWriteFailed = true;
            Debug.LogError("[Replay] Could not write " + replayPath + ": " + e.Message + " This session will not be recorded.");
        }
    }

    void OnDestroy()
    {
        if (recorder == null) return;
        WriteReplay();
        if (!replayWriteFailed) Debug.Log("[Replay] Session saved: " + sim.State.Tick + " ticks, " + recorder.CommandCount + " commands, " + recorder.WaveCount + " waves -> " + replayPath);
    }

    // Player input. Applied at once (FlushInput), not at the next tick, so it feels
    // instant and rotate/hold still work while paused; this is replay-equivalent
    // (InputTests.FlushInputEqualsApplyingAtTheNextTick).
    public void Submit(SimCommand command)
    {
        Simulation s = Sim;
        recorder?.OnCommand(command); // stamped with the tick it is applied at
        s.Enqueue(command);
        s.FlushInput();
        Dispatch();
    }

    void Update()
    {
        if (sim == null) return;
        if (sim.State.IsGameOver)
        {
            TickAlpha = 1f;
            return;
        }

        float dt = Config.TickSeconds;
        accumulator += Time.deltaTime;
        int ticks = 0;
        while (accumulator >= dt && ticks < maxTicksPerFrame)
        {
            sim.Tick();
            accumulator -= dt;
            ticks++;
            // Before Dispatch: a view reacting to an event may Submit(), which
            // replaces LastTickEvents, and the recorder reads them.
            bool waveBoundary = recorder != null && recorder.AfterTick();
            if (waveBoundary) WriteReplay();
            Dispatch();
            Ticked?.Invoke();
            if (sim.State.IsGameOver)
            {
                accumulator = 0f;
                break;
            }
        }

        // Spiral-of-death guard: drop backlog beyond one tick rather than try to
        // catch up forever. The game runs slower during a hitch; it never desyncs,
        // because the simulation only ever sees whole ticks.
        if (accumulator > dt) accumulator = dt;
        TickAlpha = Mathf.Clamp01(accumulator / dt);
    }

    private void Dispatch()
    {
        if (SimEventRaised == null) return;
        var events = sim.LastTickEvents;
        for (int i = 0; i < events.Count; i++) SimEventRaised(events[i]);
    }
}
