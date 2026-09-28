using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;

// Traces the enemies' current route: the flow field's next-tile chain from the
// spawn to the core, read from the simulation's grid (FlowFieldManager.Grid).
[RequireComponent(typeof(LineRenderer))]
public class PathPreviewer : MonoBehaviour
{
    [Header("Engine References")]
    public FlowFieldManager flowManager;
    public Transform enemySpawnPoint;

    [Header("Visuals")]
    public float lineHoverHeight = 0.5f;

    private LineRenderer lineRenderer;
    private readonly List<SimNode> route = new List<SimNode>();
    private Vector3[] positions = new Vector3[0];
    private int drawnVersion = int.MinValue;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = 0.3f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.useWorldSpace = true;
    }

    void Update()
    {
        if (flowManager == null || flowManager.Grid == null || enemySpawnPoint == null)
        {
            lineRenderer.positionCount = 0;
            drawnVersion = int.MinValue;
            return;
        }

        // The route only changes when the field does.
        if (flowManager.FieldVersion == drawnVersion) return;
        drawnVersion = flowManager.FieldVersion;
        DrawHologramPath(flowManager.Grid);
    }

    private void DrawHologramPath(SimGrid grid)
    {
        Vector3 spawn = enemySpawnPoint.position;
        SimNode startNode = grid.NodeFromPosition(new Vec2f(spawn.x, spawn.z));

        if (!startNode.IsWalkable || startNode.BestCost == SimNode.Infinity)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        // The line MAY legitimately pass through a wall - that simply means the
        // enemies have decided chewing through it is cheaper than the detour.
        Route.Collect(grid, startNode, route);

        float y = (flowManager.gridManager != null ? flowManager.gridManager.transform.position.y : 0f) + lineHoverHeight;
        if (positions.Length != route.Count) positions = new Vector3[route.Count];
        for (int i = 0; i < route.Count; i++) positions[i] = SceneMapBuilder.ToWorld(route[i].Position, y);

        lineRenderer.positionCount = positions.Length;
        lineRenderer.SetPositions(positions);
    }
}
