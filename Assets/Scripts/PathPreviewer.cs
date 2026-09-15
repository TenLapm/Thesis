using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class PathPreviewer : MonoBehaviour
{
    [Header("Engine References")]
    public FlowFieldManager flowManager;
    public Transform enemySpawnPoint;

    [Header("Visuals")]
    public float lineHoverHeight = 0.5f;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = 0.3f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.useWorldSpace = true;
    }

    void Update()
    {
        if (flowManager == null || flowManager.gridManager == null || enemySpawnPoint == null)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        DrawHologramPath();
    }

    private void DrawHologramPath()
    {
        Node startNode = flowManager.gridManager.NodeFromWorldPoint(enemySpawnPoint.position);

        if (startNode == null || !startNode.isWalkable || startNode.bestCost == Node.INFINITY)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        List<Vector3> pathPositions = new List<Vector3>();
        Node current = startNode;

        // nextNode chains are rebuilt from scratch on every recalculation, so
        // this trace can no longer wander through stale pointers. Note the line
        // MAY legitimately pass through a wall now - that simply means the
        // enemies have decided chewing through it is cheaper than the detour.
        while (current != null && current.bestCost != 0)
        {
            pathPositions.Add(current.worldPosition + Vector3.up * lineHoverHeight);
            current = current.nextNode;

            // Failsafe: never spin forever even if the field is mid-edit.
            if (pathPositions.Count > 2000) break;
        }

        if (current != null)
        {
            pathPositions.Add(current.worldPosition + Vector3.up * lineHoverHeight); // include the goal tile
        }

        lineRenderer.positionCount = pathPositions.Count;
        lineRenderer.SetPositions(pathPositions.ToArray());
    }
}
