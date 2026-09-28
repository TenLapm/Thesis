using System.Collections.Generic;
using Thesis.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

// Runtime visualization of the flow field, drawn as flat procedural geometry
// floating just above the ground. Unlike FlowFieldManager.OnDrawGizmos (which
// only shows in the editor's Scene view), this works in play mode, fullscreen
// game view and standalone builds - i.e. during a presentation with no editor.
//
// Press the toggle key (default V) to cycle:
//   Off -> Arrows -> Heatmap -> Arrows+Heatmap -> Off
//
// Geometry is rebuilt only when the field actually changes
// (FlowFieldManager.FieldVersion) or the mode changes, so the steady-state
// cost is a single int comparison per frame.
//
// WP4: reads the simulation's grid (FlowFieldManager.Grid, a Thesis.Sim.SimGrid).
// Arrow directions are derived from each tile's next-tile pointer.
public class FlowFieldVisualizer : MonoBehaviour
{
    public enum Mode { Off = 0, Arrows = 1, Heatmap = 2, ArrowsAndHeatmap = 3 }

    [Header("References (auto-found when empty)")]
    public FlowFieldManager flowManager;

    [Header("Input")]
    public Key toggleKey = Key.V;

    [Header("Appearance")]
    [Tooltip("Vertex-color material. Left empty it falls back to Resources/FlowFieldViz, then to a runtime Sprites/Default material.")]
    public Material overlayMaterial;
    public Color arrowColor = new Color(0.08f, 0.08f, 0.15f, 0.95f);
    [Range(0f, 1f)] public float heatmapAlpha = 0.55f;
    [Tooltip("Height above the node's world position, to avoid z-fighting with the ground plane.")]
    public float groundOffset = 0.04f;
    public bool showHint = true;

    public Mode CurrentMode { get; private set; } = Mode.Off;

    private GameObject arrowGO, heatGO;
    private Mesh arrowMesh, heatMesh;
    private int builtVersion = -1;
    private Mode builtMode = Mode.Off;

    // Scratch buffers reused across rebuilds to avoid per-rebuild allocations.
    private readonly List<Vector3> verts = new List<Vector3>(8192);
    private readonly List<Color32> cols = new List<Color32>(8192);
    private readonly List<int> tris = new List<int>(16384);

    void Awake()
    {
        if (flowManager == null) flowManager = FindFirstObjectByType<FlowFieldManager>();

        if (overlayMaterial == null) overlayMaterial = Resources.Load<Material>("FlowFieldViz");
        if (overlayMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s != null) overlayMaterial = new Material(s);
        }

        heatGO = CreateLayer("FlowFieldHeatmap", out heatMesh);
        arrowGO = CreateLayer("FlowFieldArrows", out arrowMesh);
        heatGO.SetActive(false);
        arrowGO.SetActive(false);
    }

    GameObject CreateLayer(string layerName, out Mesh mesh)
    {
        GameObject go = new GameObject(layerName);
        go.transform.SetParent(transform, false);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = overlayMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mesh = new Mesh { name = layerName };
        // Stress grids (100x100 nodes) exceed the 65k vertex limit of 16-bit indices.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mf.sharedMesh = mesh;
        return go;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) CycleMode();

        if (CurrentMode == Mode.Off) return;
        if (flowManager == null || flowManager.gridManager == null || flowManager.Grid == null) return;

        if (builtVersion != flowManager.FieldVersion || builtMode != CurrentMode) Rebuild();
    }

    public void CycleMode() { SetMode((Mode)(((int)CurrentMode + 1) % 4)); }

    public void SetMode(Mode mode)
    {
        CurrentMode = mode;
        bool arrows = mode == Mode.Arrows || mode == Mode.ArrowsAndHeatmap;
        bool heat = mode == Mode.Heatmap || mode == Mode.ArrowsAndHeatmap;
        if (arrowGO != null) arrowGO.SetActive(arrows);
        if (heatGO != null) heatGO.SetActive(heat);
    }

    void Rebuild()
    {
        SimGrid grid = flowManager.Grid;
        builtVersion = flowManager.FieldVersion;
        builtMode = CurrentMode;

        bool wantArrows = CurrentMode == Mode.Arrows || CurrentMode == Mode.ArrowsAndHeatmap;
        bool wantHeat = CurrentMode == Mode.Heatmap || CurrentMode == Mode.ArrowsAndHeatmap;

        if (wantHeat) BuildHeatmap(grid); else heatMesh.Clear();
        if (wantArrows) BuildArrows(grid); else arrowMesh.Clear();
    }

    private float PlaneY => flowManager.gridManager.transform.position.y;

    void BuildArrows(SimGrid grid)
    {
        verts.Clear(); cols.Clear(); tris.Clear();
        float d = flowManager.gridManager.nodeRadius * 2f;
        float y = groundOffset + 0.02f; // arrows sit above the heatmap layer
        Color32 c = arrowColor;

        for (int i = 0; i < grid.NodeCount; i++)
        {
            SimNode n = grid.ByIndex(i);
            if (!n.IsWalkable || n.BestCost == SimNode.Infinity) continue;
            Vector3 world = SceneMapBuilder.ToWorld(n.Position, PlaneY);

            if (n.BestCost == 0)
            {
                // Goal tile: diamond marker instead of an arrow.
                Vector3 gc = world + Vector3.up * y;
                float r = d * 0.28f;
                int b0 = verts.Count;
                verts.Add(gc + new Vector3(0, 0, r));
                verts.Add(gc + new Vector3(r, 0, 0));
                verts.Add(gc + new Vector3(0, 0, -r));
                verts.Add(gc + new Vector3(-r, 0, 0));
                cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
                tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 3);
                continue;
            }

            // Arrows inside standing walls would be hidden by the wall cube.
            SimNode next = grid.NextOf(n);
            if (n.HasWall || next == null) continue;

            Vector3 dir = (SceneMapBuilder.ToWorld(next.Position, PlaneY) - world).normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 center = world + Vector3.up * y;

            Vector3 tail = center - dir * (d * 0.30f);
            Vector3 neck = center + dir * (d * 0.10f);
            Vector3 tip = center + dir * (d * 0.36f);
            Vector3 shaftW = perp * (d * 0.05f);
            Vector3 headW = perp * (d * 0.14f);

            int v0 = verts.Count;
            verts.Add(tail - shaftW); verts.Add(tail + shaftW);
            verts.Add(neck + shaftW); verts.Add(neck - shaftW);
            cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
            tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
            tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);

            int h0 = verts.Count;
            verts.Add(neck - headW); verts.Add(neck + headW); verts.Add(tip);
            cols.Add(c); cols.Add(c); cols.Add(c);
            tris.Add(h0); tris.Add(h0 + 1); tris.Add(h0 + 2);
        }

        ApplyMesh(arrowMesh);
    }

    void BuildHeatmap(SimGrid grid)
    {
        verts.Clear(); cols.Clear(); tris.Clear();
        float half = flowManager.gridManager.nodeRadius * 0.92f; // slight gap so tiles read as tiles
        float y = groundOffset;

        // Normalize the color ramp over reachable open ground; walls and
        // unreachable tiles get their own fixed colors.
        int maxCost = 1;
        for (int i = 0; i < grid.NodeCount; i++)
        {
            SimNode n = grid.ByIndex(i);
            if (n.IsWalkable && !n.HasWall && n.BestCost != SimNode.Infinity && n.BestCost > maxCost)
                maxCost = n.BestCost;
        }

        byte alpha = (byte)(Mathf.Clamp01(heatmapAlpha) * 255);

        for (int i = 0; i < grid.NodeCount; i++)
        {
            SimNode n = grid.ByIndex(i);
            Color32 c;
            if (!n.IsWalkable)
            {
                c = new Color32(25, 25, 25, alpha);                    // static blocker
            }
            else if (n.HasWall)
            {
                c = new Color32(255, 140, 0, alpha);                   // player/scenario wall
            }
            else if (n.BestCost == SimNode.Infinity)
            {
                c = new Color32(120, 60, 160, alpha);                  // unreachable pocket
            }
            else
            {
                // Green (cheap) -> red (expensive) hue ramp.
                float t = Mathf.Clamp01((float)n.BestCost / maxCost);
                Color rgb = Color.HSVToRGB(Mathf.Lerp(0.334f, 0f, t), 0.85f, 0.9f);
                c = new Color32((byte)(rgb.r * 255), (byte)(rgb.g * 255), (byte)(rgb.b * 255), alpha);
            }

            Vector3 p = SceneMapBuilder.ToWorld(n.Position, PlaneY) + Vector3.up * y;
            int v0 = verts.Count;
            verts.Add(p + new Vector3(-half, 0, -half));
            verts.Add(p + new Vector3(-half, 0, half));
            verts.Add(p + new Vector3(half, 0, half));
            verts.Add(p + new Vector3(half, 0, -half));
            cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
            tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
            tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);
        }

        ApplyMesh(heatMesh);
    }

    void ApplyMesh(Mesh mesh)
    {
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    void OnGUI()
    {
        if (!showHint) return;
        string label = "[" + toggleKey + "] Flow field view: " + CurrentMode;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(11, Screen.height - 27, 420, 24), label);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, Screen.height - 28, 420, 24), label);
    }
}
