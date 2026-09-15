using System.Collections.Generic;
using UnityEngine;

// One synchronous weighted flood fill (Dijkstra-style relaxation), run on the
// main thread whenever the map changes. On a 25x25 grid this costs microseconds
// per call, which is why the old chunk system, background threads, the
// isCalculating lock, ValidatePath, sinkhole detection and the global fallback
// could all be deleted: there is nothing left to go stale between chunks, and
// no background thread for agents or previews to race against.
public class FlowFieldManager : MonoBehaviour
{
    public GridManager gridManager;
    public Transform targetGoal; // The PlayerCore

    // Integer step costs, x10 so diagonals can cost ~sqrt(2) without floats.
    // (The old BFS priced diagonal steps the same as cardinal ones, which made
    // every path drift diagonally for free.)
    private const int CARDINAL_COST = 10;
    private const int DIAGONAL_COST = 14;

    // Bumped every time the field data actually changes, so runtime observers
    // (FlowFieldVisualizer) can rebuild their geometry only when needed.
    public int FieldVersion { get; private set; }

    void Start()
    {
        GenerateFlowField();
    }

    // Call whenever the map changes: a wall is placed or chewed through.
    public void GenerateFlowField()
    {
        if (targetGoal == null || gridManager == null || gridManager.grid == null) return;

        // 1. Reset EVERYTHING up front - cost, direction AND nextNode - so no
        // stale pointer can ever survive a recalculation. (The old full-map
        // vector pass forgot nextNode, which left unreachable pockets pointing
        // at whatever they liked before.)
        foreach (Node n in gridManager.grid)
        {
            n.bestCost = Node.INFINITY;
            n.bestDirection = Vector3.zero;
            n.nextNode = null;
        }

        Node targetNode = gridManager.NodeFromWorldPoint(targetGoal.position);
        if (targetNode == null || !targetNode.isWalkable) { FieldVersion++; return; }

        targetNode.bestCost = 0;
        Queue<Node> open = new Queue<Node>();
        open.Enqueue(targetNode);

        // 2. Weighted relaxation (SPFA - a BFS that re-queues nodes whenever a
        // cheaper route is found). Entering a tile costs its terrainCost, so a
        // wall with digCost 15 reads as "as bad as a 15-tile detour" and the
        // field automatically decides between routing around and digging through.
        while (open.Count > 0)
        {
            Node current = open.Dequeue();

            foreach (Node neighbor in gridManager.GetNeighbors(current))
            {
                if (!neighbor.isWalkable) continue; // static geometry only

                bool diagonal = neighbor.gridX != current.gridX && neighbor.gridY != current.gridY;
                int stepCost = (diagonal ? DIAGONAL_COST : CARDINAL_COST) * neighbor.terrainCost;
                int newCost = current.bestCost + stepCost;

                if (newCost < neighbor.bestCost)
                {
                    neighbor.bestCost = newCost;
                    neighbor.nextNode = current;
                    neighbor.bestDirection = (current.worldPosition - neighbor.worldPosition).normalized;
                    open.Enqueue(neighbor);
                }
            }
        }

        FieldVersion++;
    }

    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (gridManager == null || gridManager.grid == null) return;

        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.black;
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 12;

        foreach (Node n in gridManager.grid)
        {
            if (!n.isWalkable || n.bestCost == Node.INFINITY) continue;

            if (n.bestDirection != Vector3.zero)
            {
                Gizmos.color = Color.blue;
                Vector3 startPos = n.worldPosition + Vector3.up * 0.1f;
                Vector3 endPos = startPos + n.bestDirection * (gridManager.nodeRadius * 1.5f);
                Gizmos.DrawLine(startPos, endPos);
                Gizmos.DrawSphere(endPos, 0.1f);
            }

            // Costs are x10 internally (diagonal pricing), so show tile units.
            UnityEditor.Handles.Label(n.worldPosition + Vector3.up * 0.5f, (n.bestCost / 10f).ToString("0.#"), style);
        }
#endif
    }
}
