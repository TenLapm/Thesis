using UnityEngine;

// Scene-side grid: tile positions in the world and world-to-tile lookup, for input
// and visuals. The simulation's grid (walkability, walls, costs, the flow field)
// is Thesis.Sim.SimGrid, built from these same settings by SceneMapBuilder; the
// two use identical arithmetic (MapExporter cross-checks it, 0 mismatches).
//
// Removed in WP4 with the gameplay fields of Node: the Physics.CheckSphere
// walkability scan (now SceneMapBuilder) and GetNeighbors (now SimGrid).
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public Vector2 gridWorldSize = new Vector2(50, 50);
    public float nodeRadius = 0.5f;
    [Tooltip("Static geometry layer. Read by SceneMapBuilder when the simulation starts.")]
    public LayerMask unwalkableMask;

    public Node[,] grid;
    float nodeDiameter;
    [HideInInspector] public int gridSizeX, gridSizeY;

    void Awake()
    {
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
        CreateGrid();
    }

    void CreateGrid()
    {
        grid = new Node[gridSizeX, gridSizeY];
        Vector3 worldBottomLeft = transform.position - Vector3.right * gridWorldSize.x / 2 - Vector3.forward * gridWorldSize.y / 2;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector3 worldPoint = worldBottomLeft + Vector3.right * (x * nodeDiameter + nodeRadius) + Vector3.forward * (y * nodeDiameter + nodeRadius);
                grid[x, y] = new Node(worldPoint, x, y);
            }
        }
    }

    // Same math as SimGrid.NodeFromPosition (including its origin and rounding
    // quirks, kept on purpose - see that method's comment).
    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        if (grid == null) return null;

        float percentX = (worldPosition.x + gridWorldSize.x / 2) / gridWorldSize.x;
        float percentY = (worldPosition.z + gridWorldSize.y / 2) / gridWorldSize.y;

        percentX = Mathf.Clamp01(percentX);
        percentY = Mathf.Clamp01(percentY);

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);

        return grid[x, y];
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 1, gridWorldSize.y));

        if (grid == null) return;

        // In play mode, colour tiles from the simulation's grid.
        FlowFieldManager flow = FindFirstObjectByType<FlowFieldManager>();
        Thesis.Sim.SimGrid simGrid = flow != null ? flow.Grid : null;

        foreach (Node n in grid)
        {
            Gizmos.color = Color.white;
            if (simGrid != null && simGrid.InBounds(n.gridX, n.gridY))
            {
                Thesis.Sim.SimNode s = simGrid[n.gridX, n.gridY];
                if (!s.IsWalkable) Gizmos.color = Color.red;                   // static blocker
                else if (s.HasWall) Gizmos.color = new Color(1f, 0.6f, 0f);    // player wall (diggable)
            }
            Gizmos.DrawCube(n.worldPosition, Vector3.one * (nodeDiameter - .1f));
        }
    }
}
