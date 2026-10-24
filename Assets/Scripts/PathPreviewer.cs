using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;

// Draws where enemies will walk (GDD §5.3): one line per kind of enemy AND per place
// they come in.
//
//   walkers   the ground field's next-tile chain from the entry to the core
//   sappers   the same chain on the sapper field, where built tiles are cheap
//   flyers    a straight line from the entry to the core
//
// Which lines are drawn: in a build phase with the next wave announced
// (SimConfig.PlanAhead), that wave's; during a wave, the wave's own; otherwise one for
// every kind the player has met, from the map's first entry. Since 2026-10-13 a wave
// may come in at up to three places (RaidPlanner), each drawn with a marker on its
// entry tile: nothing is drawn on the edge of the board until a wave is announced.
//
// View only: read from the plans the simulation holds, never written back. The lines
// are rebuilt when a field changes or the set of lines does, not every frame.
[RequireComponent(typeof(LineRenderer))]
public class PathPreviewer : MonoBehaviour
{
    [Header("Engine References")]
    public FlowFieldManager flowManager;
    [Tooltip("Used only in scenes without a SimHost (the benchmarks): the one place enemies start.")]
    public Transform enemySpawnPoint;
    public SimHost simHost;

    [Header("Visuals")]
    public float lineHoverHeight = 0.5f;
    public float lineWidth = 0.32f;
    public Color walkerColor = new Color(1f, 0.24f, 0.94f);
    public Color sapperColor = new Color(0.98f, 0.57f, 0.24f);
    public Color flyerColor = new Color(0.38f, 0.65f, 0.98f);
    // Raiders (2026-10-20): the way to the nearest collector they go for first.
    public Color raiderColor = new Color(0.25f, 0.9f, 0.35f);
    [Tooltip("The sappers' line is drawn this far to one side, so it stays visible where it shares a stretch with the walkers' line.")]
    public float sapperOffset = 0.45f;
    [Tooltip("While a wave is on the board the lines are this much thinner and fainter, so they do not cover the enemies walking along them.")]
    public float waveWidthFactor = 0.3f;
    [Range(0f, 1f)] public float waveAlpha = 0.3f;
    public Color entryColor = new Color(1f, 0.3f, 0.25f);

    // One line: where it starts and what walks it.
    private struct Lane
    {
        public int Spawn;
        public MovementClass Movement;
        public bool Raid;
    }

    private readonly List<Lane> lanes = new List<Lane>();
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly List<GameObject> markers = new List<GameObject>();
    private readonly List<SimNode> route = new List<SimNode>();
    private readonly List<Vector3> points = new List<Vector3>();
    private int drawnVersion = int.MinValue;
    private ulong drawnLanes;
    private int shownMask = -1;
    private bool faint;

    // Bit per MovementClass. Ground is met from the start.
    private int met = 1 << (int)MovementClass.Ground;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // The kinds whose lines are on the board right now (for the key in the HUD).
    public bool Shows(MovementClass movement) { return shownMask >= 0 && (shownMask & (1 << (int)movement)) != 0; }

    // Whether a raiders' line is on the board right now.
    public bool ShowsRaiders => shownMask >= 0 && (shownMask & RaidBit) != 0;

    private const int RaidBit = 1 << 8;

    // How many places the announced (or running) wave comes in at.
    public int EntryCount { get; private set; }

    void Awake()
    {
        // The component's own LineRenderer was the walkers' line; the lines are pooled now.
        GetComponent<LineRenderer>().positionCount = 0;
    }

    void Update()
    {
        if (flowManager == null || flowManager.Grid == null)
        {
            Show(0, 0);
            drawnVersion = int.MinValue;
            shownMask = -1;
            return;
        }

        SimHost host = Host;
        bool inWave = host != null && (host.State.Phase == SimPhase.Spawning || host.State.Phase == SimPhase.Resolving);
        CollectLanes(host, inWave);

        // A fingerprint of what is to be drawn, so nothing is rebuilt while it stays the same.
        var h = new Fnv1a64();
        h.Add(inWave);
        h.Add(lanes.Count);
        for (int i = 0; i < lanes.Count; i++) { h.Add(lanes[i].Spawn); h.Add((int)lanes[i].Movement); h.Add(lanes[i].Raid); }
        if (host != null) { h.Add(host.State.Core.X); h.Add(host.State.Core.Y); }
        if (flowManager.FieldVersion == drawnVersion && h.Value == drawnLanes) return;
        drawnVersion = flowManager.FieldVersion;
        drawnLanes = h.Value;
        faint = inWave;
        Draw(host);
    }

    private void CollectLanes(SimHost host, bool inWave)
    {
        lanes.Clear();
        if (host == null)
        {
            lanes.Add(new Lane { Spawn = -1, Movement = MovementClass.Ground }); // a benchmark scene
            return;
        }

        SimState state = host.State;
        WavePlan plan = inWave ? state.CurrentPlan : state.NextPlan;
        if (plan != null && plan.Groups != null)
        {
            foreach (AgentGroup group in plan.Groups)
            {
                if (inWave) met |= 1 << (int)group.Movement;
                bool raid = group.TargetsCollectors;
                bool known = false;
                for (int i = 0; i < lanes.Count; i++) if (lanes[i].Spawn == group.SpawnIndex && lanes[i].Movement == group.Movement && lanes[i].Raid == raid) known = true;
                if (!known) lanes.Add(new Lane { Spawn = group.SpawnIndex, Movement = group.Movement, Raid = raid });
            }
            return;
        }

        // No wave is announced: every kind met so far, from the first entry.
        for (int m = 0; m < 3; m++) if ((met & (1 << m)) != 0) lanes.Add(new Lane { Spawn = 0, Movement = (MovementClass)m });
    }

    private void Draw(SimHost host)
    {
        SimGrid grid = flowManager.Grid;
        float y = (flowManager.gridManager != null ? flowManager.gridManager.transform.position.y : 0f) + lineHoverHeight;
        float cell = flowManager.gridManager != null ? flowManager.gridManager.nodeRadius * 2f : 1f;

        Vector3 core = host != null ? Tile(grid, host.State.Core, y)
                     : flowManager.targetGoal != null ? flowManager.targetGoal.position : Vector3.zero;

        var entries = new List<int>();
        shownMask = 0;
        int used = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            Lane lane = lanes[i];
            Vector3 from;
            SimNode start;
            if (lane.Spawn < 0)
            {
                if (enemySpawnPoint == null) continue;
                from = enemySpawnPoint.position;
                start = grid.NodeFromPosition(new Vec2f(from.x, from.z));
            }
            else
            {
                if (lane.Spawn >= host.Map.Spawns.Length) continue;
                start = grid.Get(host.Map.Spawns[lane.Spawn]);
                from = SceneMapBuilder.ToWorld(start.Position, y);
                if (!entries.Contains(lane.Spawn)) entries.Add(lane.Spawn);
            }

            points.Clear();
            Color color;
            if (lane.Raid && grid.RaidGoals > 0 && start.IsWalkable && start.RaidCost != SimNode.Infinity)
            {
                // Raiders: along the raid field to the collector they reach first.
                color = raiderColor;
                Vector3 lift = new Vector3(-sapperOffset, 0.08f, sapperOffset);
                SimNode at = start;
                for (int guard = 0; at != null && guard < grid.NodeCount; guard++)
                {
                    points.Add(SceneMapBuilder.ToWorld(at.Position, y) + lift);
                    if (at.RaidCost == 0) break;
                    at = grid.NextRaid(at);
                }
            }
            else if (lane.Movement == MovementClass.Flying)
            {
                color = flyerColor;
                points.Add(new Vector3(from.x, y + 0.1f, from.z));
                points.Add(new Vector3(core.x, y + 0.1f, core.z));
            }
            else
            {
                // A line MAY legitimately pass through a wall - that simply means the
                // enemies have decided chewing through it is cheaper than the detour.
                if (!start.IsWalkable || start.BestCost == SimNode.Infinity) continue;
                bool sapper = lane.Movement == MovementClass.Sapper;
                color = sapper ? sapperColor : walkerColor;
                Vector3 offset = sapper ? new Vector3(sapperOffset, 0.05f, -sapperOffset) : Vector3.zero;
                Route.Collect(grid, start, route, lane.Movement);
                for (int k = 0; k < route.Count; k++) points.Add(SceneMapBuilder.ToWorld(route[k].Position, y) + offset);
            }

            LineRenderer line = LineAt(used++);
            line.startWidth = line.endWidth = faint ? lineWidth * waveWidthFactor : lineWidth;
            if (faint) color.a *= waveAlpha;
            line.startColor = line.endColor = color;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            shownMask |= lane.Raid && grid.RaidGoals > 0 ? RaidBit : 1 << (int)lane.Movement;
        }

        // A marker on every entry tile of the wave.
        for (int i = 0; i < entries.Count; i++)
        {
            GameObject marker = MarkerAt(i, cell);
            Vector3 p = SceneMapBuilder.ToWorld(grid.Get(host.Map.Spawns[entries[i]]).Position, y - lineHoverHeight + 0.06f);
            marker.transform.position = p;
        }
        EntryCount = entries.Count;
        Show(used, entries.Count);
    }

    private static Vector3 Tile(SimGrid grid, TileCoord tile, float y) { return SceneMapBuilder.ToWorld(grid.Get(tile).Position, y); }

    private LineRenderer LineAt(int index)
    {
        while (lines.Count <= index) lines.Add(TowerView.Line(transform, "Route " + lines.Count, walkerColor, lineWidth, false));
        return lines[index];
    }

    private GameObject MarkerAt(int index, float cell)
    {
        while (markers.Count <= index)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Entry " + markers.Count;
            Destroy(marker.GetComponent<Collider>());
            TowerView.UseSolidMaterial(marker);
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = new Vector3(cell * 1.5f, 0.05f, cell * 1.5f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", entryColor);
            block.SetColor("_EmissionColor", entryColor * 1.5f);
            marker.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            markers.Add(marker);
        }
        return markers[index];
    }

    private void Show(int lineCount, int markerCount)
    {
        for (int i = 0; i < lines.Count; i++) lines[i].enabled = i < lineCount;
        for (int i = 0; i < markers.Count; i++) if (markers[i].activeSelf != (i < markerCount)) markers[i].SetActive(i < markerCount);
    }
}
