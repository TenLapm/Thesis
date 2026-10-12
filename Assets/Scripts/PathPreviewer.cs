using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;

// Draws where enemies will walk (GDD §5.3): one line per kind of enemy.
//
//   walkers   the ground field's next-tile chain from the spawn to the core
//   sappers   the same chain on the sapper field, where built tiles are cheap
//   flyers    a straight line from the spawn to the core
//
// Which lines are drawn (WP-C4, the student's decision of 2026-10-12): in a build
// phase, one for every kind the player has MET so far in this session; during a
// wave, the kinds in that wave. Walkers count as met from the start, so under the
// static baseline, which only ever sends walkers, this is the one line the game has
// always drawn. The rule is the same in both study conditions; only what the waves
// contain differs.
//
// View only. "Met" is read from the plans of the waves the simulation started and is
// never written back. The lines are rebuilt when a field changes or the set of
// lines does, not every frame.
[RequireComponent(typeof(LineRenderer))]
public class PathPreviewer : MonoBehaviour
{
    [Header("Engine References")]
    public FlowFieldManager flowManager;
    public Transform enemySpawnPoint;
    public SimHost simHost;

    [Header("Visuals")]
    public float lineHoverHeight = 0.5f;
    public float lineWidth = 0.32f;
    public Color walkerColor = new Color(1f, 0.24f, 0.94f);
    public Color sapperColor = new Color(0.98f, 0.57f, 0.24f);
    public Color flyerColor = new Color(0.38f, 0.65f, 0.98f);
    [Tooltip("The sappers' line is drawn this far to one side, so it stays visible where it shares a stretch with the walkers' line.")]
    public float sapperOffset = 0.45f;

    private LineRenderer walkerLine;
    private LineRenderer sapperLine;
    private LineRenderer flyerLine;
    private readonly List<SimNode> route = new List<SimNode>();
    private readonly List<Vector3> points = new List<Vector3>();
    private int drawnVersion = int.MinValue;
    private int drawnMask = -1;

    // Bit per MovementClass. Ground is met from the start.
    private int met = 1 << (int)MovementClass.Ground;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // The kinds whose lines are on the board right now (for the key in the HUD).
    public bool Shows(MovementClass movement) { return drawnMask >= 0 && (drawnMask & (1 << (int)movement)) != 0; }

    void Awake()
    {
        walkerLine = GetComponent<LineRenderer>();
        walkerLine.useWorldSpace = true;
        Style(walkerLine, walkerColor);
        sapperLine = TowerView.Line(transform, "SapperRoute", sapperColor, lineWidth, false);
        flyerLine = TowerView.Line(transform, "FlyerRoute", flyerColor, lineWidth, false);
        Style(sapperLine, sapperColor);
        Style(flyerLine, flyerColor);
    }

    private void Style(LineRenderer line, Color color)
    {
        line.startWidth = line.endWidth = lineWidth;
        line.startColor = line.endColor = color;
    }

    void Update()
    {
        if (flowManager == null || flowManager.Grid == null || enemySpawnPoint == null)
        {
            walkerLine.positionCount = 0;
            sapperLine.enabled = flyerLine.enabled = false;
            drawnVersion = int.MinValue;
            drawnMask = -1;
            return;
        }

        int mask = VisibleMask();
        if (flowManager.FieldVersion == drawnVersion && mask == drawnMask) return;
        drawnVersion = flowManager.FieldVersion;
        drawnMask = mask;
        Draw(flowManager.Grid, mask);
    }

    // The benchmark scenes have no SimHost: there it is the walkers' line, as before.
    private int VisibleMask()
    {
        SimHost host = Host;
        if (host == null) return 1 << (int)MovementClass.Ground;

        SimState state = host.State;
        bool inWave = state.Phase == SimPhase.Spawning || state.Phase == SimPhase.Resolving;
        if (!inWave || state.CurrentPlan == null || state.CurrentPlan.Groups == null) return met;

        int wave = 0;
        foreach (AgentGroup group in state.CurrentPlan.Groups) wave |= 1 << (int)group.Movement;
        met |= wave;
        return wave;
    }

    private void Draw(SimGrid grid, int mask)
    {
        Vector3 spawn = enemySpawnPoint.position;
        SimNode start = grid.NodeFromPosition(new Vec2f(spawn.x, spawn.z));
        float y = (flowManager.gridManager != null ? flowManager.gridManager.transform.position.y : 0f) + lineHoverHeight;

        // A line MAY legitimately pass through a wall - that simply means the
        // enemies have decided chewing through it is cheaper than the detour.
        bool reachable = start.IsWalkable && start.BestCost != SimNode.Infinity;

        if (reachable && (mask & (1 << (int)MovementClass.Ground)) != 0)
        {
            Collect(grid, start, MovementClass.Ground, y, Vector3.zero);
            walkerLine.positionCount = points.Count;
            walkerLine.SetPositions(points.ToArray());
        }
        else walkerLine.positionCount = 0;

        if (reachable && (mask & (1 << (int)MovementClass.Sapper)) != 0)
        {
            Collect(grid, start, MovementClass.Sapper, y + 0.05f, new Vector3(sapperOffset, 0f, -sapperOffset));
            sapperLine.positionCount = points.Count;
            sapperLine.SetPositions(points.ToArray());
            sapperLine.enabled = true;
        }
        else sapperLine.enabled = false;

        if ((mask & (1 << (int)MovementClass.Flying)) != 0 && flowManager.targetGoal != null)
        {
            Vector3 core = flowManager.targetGoal.position;
            flyerLine.positionCount = 2;
            flyerLine.SetPosition(0, new Vector3(spawn.x, y + 0.1f, spawn.z));
            flyerLine.SetPosition(1, new Vector3(core.x, y + 0.1f, core.z));
            flyerLine.enabled = true;
        }
        else flyerLine.enabled = false;
    }

    private void Collect(SimGrid grid, SimNode start, MovementClass movement, float y, Vector3 offset)
    {
        Route.Collect(grid, start, route, movement);
        points.Clear();
        for (int i = 0; i < route.Count; i++) points.Add(SceneMapBuilder.ToWorld(route[i].Position, y) + offset);
    }
}
