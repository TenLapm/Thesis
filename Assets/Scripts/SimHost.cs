using System;
using System.Collections.Generic;
using System.IO;
using Thesis.Core;
using Thesis.Director;
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
// Telemetry (WP-C3): the same session also writes Sessions/<session>/telemetry.jsonl,
// one row per wave, appended as each wave's window closes. It holds nothing the
// replay does not: `thesis telemetry replay.json --check telemetry.jsonl` makes the
// rows again and compares them byte for byte.
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

    [Header("Map")]
    [Tooltip("Make the map from the seed (MapGenerator) instead of reading the scene. The GridManager must be as big as the generated map.")]
    public bool generateMap = false;

    [Tooltip("Ore deposits on a generated map (2026-10-14); 0 = none.")]
    public int mapDeposits = 0;

    [Tooltip("Deposits grouped this many to a cluster (2026-10-24); 1 = scattered one by one.")]
    public int depositsPerCluster = 1;
    [Tooltip("The ring along the edge on which nothing is built, in tiles.")]
    public int mapMargin = 2;
    public int mapRidges = 10;
    [Tooltip("Upper bound on ticks per rendered frame. After a long hitch the game slows down instead of freezing to catch up.")]
    public int maxTicksPerFrame = 40; // x6 at 15 fps is 20 ticks a frame (2026-10-28)

    [Header("Replay and telemetry")]
    [Tooltip("Record this session to Sessions/<session>/replay.json and telemetry.jsonl (project Runs/ folder in the editor, persistentDataPath in a build).")]
    public bool recordReplay = true;
    [Tooltip("Also store one state hash per tick. Costs one hash per tick and about 1 MB per 25 minutes; without it a divergence can only be narrowed to a wave, not a tick.")]
    public bool recordTickHashes = true;

    [Header("Tower roster")]
    [Tooltip("The towers the shop sells from. ORDER MATTERS: an offer names a tower by its place in this list, so reordering it changes what every seeded shop offers. Export with Thesis/Export Towers after any change.")]
    public List<TowerDefAsset> towerRoster = new List<TowerDefAsset>();

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
    private SimConfig runConfig;
    private readonly System.Collections.Generic.List<SimEvent> dispatchQueue = new System.Collections.Generic.List<SimEvent>();
    private bool dispatching;
    private float accumulator;

    // --- trying a lost wave again (2026-10-14) ---
    //
    // A lost wave is not the end of the run: the player goes back to the build phase
    // before it. Nothing is rewound. The scene is loaded again and the SAME run is
    // played up to that build phase from its own recording (same seed, same commands,
    // so the same state, I1), and from there the player goes on. The new session is a
    // recording of its own, valid from tick 0; the lost one keeps its files.
    private sealed class Retry
    {
        public ulong Seed;
        public List<ReplayCommand> Commands;
        public int UntilTick;
        public int Wave;
        public string From;
    }

    private static Retry pendingRetry;
    private string sessionPolicy = "human";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ForgetRetry() { pendingRetry = null; }

    // State.Tick when the build phase began that the current wave followed.
    private int phaseStartTick;
    private bool wasInWave;

    // Playing a wave again (2026-10-24): the simulation keeps the state each build phase
    // began with and the command RetryWave puts the one before the last wave back, after a
    // loss or a win. It is recorded like any command, so the session goes on in the same
    // files and its replay stays exact; there is no scene to load and no run to play again
    // (which is what made the old way slow).
    public bool CanRetryWave => sim != null && sim.CanRetry;

    // The wave that would be played again.
    public int RetryWaveNumber => sim != null ? Mathf.Max(1, sim.RetryWaveNumber) : 1;

    public void RetryWave()
    {
        if (!CanRetryWave) return;
        Submit(SimCommand.RetryWave());
        accumulator = 0f;
    }

    // Plays the run again up to the build phase the player returns to. No frame is
    // drawn and no event is handed out: the views draw the state they find.
    private void FastForward(Retry retry)
    {
        int next = 0;
        while (sim.State.Tick < retry.UntilTick && !sim.State.IsGameOver)
        {
            while (next < retry.Commands.Count && retry.Commands[next].Tick <= sim.State.Tick)
            {
                SimCommand command = retry.Commands[next++].ToCommand();
                recorder?.OnCommand(command);
                sim.Enqueue(command);
                sim.FlushInput();
            }
            sim.Tick();
            recorder?.AfterTick();
            if (telemetry != null) WriteTelemetry(telemetry.AfterTick());
        }
        phaseStartTick = sim.State.Tick;
        WriteReplay();
        Debug.Log("[Sim] Wave " + retry.Wave + " again: the run of session " + retry.From + " was played up to tick " + sim.State.Tick + " (" + retry.Commands.Count + " commands).");
    }
    private ReplayRecorder recorder;
    private string replayPath;
    private bool replayWriteFailed;
    private TelemetryRecorder telemetry;
    private TelemetryWriter telemetryWriter;
    private string telemetryPath;

    // Where this session's replay is written, or null when recording is off.
    public string ReplayPath => replayPath;

    // Where this session's telemetry is written, or null when recording is off.
    public string TelemetryPath => telemetryPath;

    public Simulation Sim
    {
        get
        {
            EnsureStarted();
            return sim;
        }
    }

    public SimState State => Sim.State;

    // The simulation's map: the one it was started with, with the core where the player put it.
    public MapData Map
    {
        get
        {
            EnsureStarted();
            return sim.Map;
        }
    }

    // Puts the scene's core object where the simulation's core is.
    public void ShowCore()
    {
        if (flowManager == null || flowManager.targetGoal == null || sim == null) return;
        Vec2f p = sim.State.Grid.Get(sim.State.Core).Position;
        flowManager.targetGoal.position = new Vector3(p.X, flowManager.targetGoal.position.y, p.Y);
    }

    // The running session's own copy of the config, taken when the simulation
    // starts. Editing the asset in the inspector during play therefore changes the
    // NEXT session, never the one in progress (which would make it unreproducible).
    public SimConfig Config
    {
        get
        {
            if (runConfig == null) EnsureStarted();
            return runConfig;
        }
    }

    public ulong Seed { get; private set; }

    // How far (0..1) the accumulator is toward the next tick: views interpolate
    // between the previous and the current tick's positions by this.
    public float TickAlpha { get; private set; }

    // Height of the grid plane; the simulation is 2-D (XZ), views need a Y.
    public float GroundY => gridManager != null ? gridManager.transform.position.y : 0f;

    public static SimHost Find() { return FindFirstObjectByType<SimHost>(); }

    // The simulation's copy of the roster, in towerRoster order (TowerExporter makes the same call).
    public TowerDef[] BuildTowerRoster()
    {
        if (towerRoster == null || towerRoster.Count == 0)
            throw new InvalidOperationException("[Sim] SimHost.towerRoster is empty: the shop would have nothing to sell.");
        var defs = new TowerDef[towerRoster.Count];
        for (int i = 0; i < defs.Length; i++)
        {
            if (towerRoster[i] == null) throw new InvalidOperationException("[Sim] SimHost.towerRoster[" + i + "] is not set.");
            defs[i] = towerRoster[i].ToDef();
            defs[i].Validate();
        }
        return defs;
    }

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

        Retry retry = pendingRetry;
        pendingRetry = null;
        Seed = retry != null ? retry.Seed : randomSeed ? (ulong)(uint)Environment.TickCount : (ulong)(uint)seed;
        if (generateMap)
        {
            float diameter = gridManager.nodeRadius * 2f;
            int width = Mathf.RoundToInt(gridManager.gridWorldSize.x / diameter);
            int height = Mathf.RoundToInt(gridManager.gridWorldSize.y / diameter);
            Vector3 origin = gridManager.transform.position;
            map = MapGenerator.Generate(Seed, width, height, gridManager.nodeRadius, mapMargin, mapRidges, origin.x == 0f ? 0f : origin.x, origin.z == 0f ? 0f : origin.z, mapDeposits, depositsPerCluster);
            // Entries are drawn wave by wave (PathPreviewer); the scene's one spawn marker is not one of them.
            Renderer marker = waveSpawner.spawnPoint.GetComponent<Renderer>();
            if (marker != null) marker.enabled = false;
        }
        else
        map = SceneMapBuilder.Build(gridManager, flowManager.targetGoal, new[] { waveSpawner.spawnPoint }, SceneManager.GetActiveScene().name);
        runConfig = (configAsset != null ? configAsset.config : new SimConfig()).Clone();
        // The gear inventory needs the bottom bar to be seen and used: a scene without it plays without one.
        if (FindFirstObjectByType<DeckBar>() == null) runConfig.GearInventorySlots = 0;
        IWavePlanner planner = directorHost != null ? directorHost.CreatePlanner(runConfig, map) : new EscalationPlanner(runConfig, map);
        sim = new Simulation(runConfig, map, blockManager.BuildShapeLibrary(), BuildTowerRoster(), Seed, planner);
        flowManager.Bind(sim.State.Grid);
        if (generateMap) ShowCore();

        Debug.Log("[Sim] Started: map '" + map.Name + "' " + map.Width + "x" + map.Height + ", seed " + Seed + ", planner '" + planner.Name
                  + "', " + (configAsset != null ? "config '" + configAsset.name + "'" : "default config") + ".");

        // The label goes into the replay and into every telemetry row, so it is set before either exists.
        sessionPolicy = retry != null ? "human, wave " + retry.Wave + " again after " + retry.From : "human";
        if (recordReplay) StartRecording();
        if (retry != null)
        {
            FastForward(retry);
            if (generateMap) ShowCore();
        }
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
            Policy = sessionPolicy,
        };
        replayPath = Path.Combine(SessionsRoot(), session, "replay.json");
        Debug.Log("[Replay] Recording to " + replayPath);

        // The same labels as the replay: telemetry made again from the replay must
        // come out as this file, byte for byte.
        telemetry = new TelemetryRecorder(sim, recorder.Session, recorder.Build, recorder.Policy);
        telemetryPath = Path.Combine(SessionsRoot(), session, "telemetry.jsonl");
        try
        {
            telemetryWriter = new TelemetryWriter(telemetryPath);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogError("[Telemetry] Could not create " + telemetryPath + ": " + e.Message + " This session writes no telemetry; it can be made from the replay.");
        }
    }

    // Appends the rows a tick (or the end of the session) completed. A failed write
    // is logged once and never stops the game: the replay still holds everything.
    private void WriteTelemetry(System.Collections.Generic.IReadOnlyList<WaveRecord> rows)
    {
        if (telemetryWriter == null || rows.Count == 0) return;
        try
        {
            telemetryWriter.Write(rows);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException)
        {
            Debug.LogError("[Telemetry] Could not write " + telemetryPath + ": " + e.Message + " The rest of this session's telemetry can be made from the replay.");
            CloseTelemetry();
        }
    }

    private void CloseTelemetry()
    {
        if (telemetryWriter == null) return;
        try
        {
            telemetryWriter.Dispose();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Nothing more to do: the file keeps the rows already flushed.
        }
        telemetryWriter = null;
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

        // The window that was still open: the wave in progress, or the build phase
        // the session was stopped in.
        if (telemetry != null)
        {
            WriteTelemetry(telemetry.Finish());
            if (telemetryWriter != null) Debug.Log("[Telemetry] " + telemetryWriter.RowsWritten + " rows -> " + telemetryPath);
            CloseTelemetry();
        }
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

        float dt = runConfig.TickSeconds;
        accumulator += Time.deltaTime;
        int ticks = 0;
        while (accumulator >= dt && ticks < maxTicksPerFrame)
        {
            sim.Tick();
            accumulator -= dt;
            ticks++;
            // Remember where each build phase begins: that is where a lost wave is taken up again.
            bool inWave = sim.State.Phase == SimPhase.Spawning || sim.State.Phase == SimPhase.Resolving;
            if (wasInWave && !inWave && !sim.State.IsGameOver) phaseStartTick = sim.State.Tick;
            wasInWave = inWave;
            // Before Dispatch: a view reacting to an event may Submit(), which
            // replaces LastTickEvents, and the recorder reads them.
            bool waveBoundary = recorder != null && recorder.AfterTick();
            if (waveBoundary) WriteReplay();
            if (telemetry != null) WriteTelemetry(telemetry.AfterTick());
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

    // Hands the simulation's latest events to the views, in order.
    //
    // The events are copied into a queue first. A view may answer an event by
    // calling Submit(), which makes the simulation replace LastTickEvents while this
    // loop is still walking it. With the queue, such a nested call only appends its
    // own events behind the ones in progress, and the outer loop delivers them.
    private void Dispatch()
    {
        // An Undo, a Redo or a retried wave puts a whole other state in place, grid and all: the
        // route lines and the field view read the one that runs now (2026-10-28, the student: the
        // raiders' line did not follow new walls; it was drawing the grid of before the undo).
        if (flowManager != null && flowManager.Grid != sim.State.Grid) flowManager.Bind(sim.State.Grid);
        if (SimEventRaised == null) return;

        var events = sim.LastTickEvents;
        for (int i = 0; i < events.Count; i++) dispatchQueue.Add(events[i]);
        if (dispatching) return;

        dispatching = true;
        try
        {
            for (int i = 0; i < dispatchQueue.Count; i++) SimEventRaised?.Invoke(dispatchQueue[i]);
        }
        finally
        {
            dispatchQueue.Clear();
            dispatching = false;
        }
    }
}
