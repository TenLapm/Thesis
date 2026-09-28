using Thesis.Sim;
using UnityEngine;

// Scene-side handle on the flow field (WP4). The field itself - the synchronous
// SPFA rebuild and its reasons (no chunking, no threads, microseconds per rebuild)
// - is Thesis.Sim.FlowField, and is rebuilt by the simulation, never from here.
//
// What stays: the scene reference to the goal (the PlayerCore transform), and a
// binding to whichever SimGrid is running (SimHost's, or ScenarioBenchmark's), so
// views like FlowFieldVisualizer and PathPreviewer keep their existing wiring.
public class FlowFieldManager : MonoBehaviour
{
    public GridManager gridManager;
    public Transform targetGoal; // The PlayerCore

    public SimGrid Grid { get; private set; }

    // Bumped by FlowField.Generate every time the field changes; -1 while unbound.
    public int FieldVersion => Grid != null ? Grid.FieldVersion : -1;

    public void Bind(SimGrid grid)
    {
        Grid = grid;
    }

    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (Grid == null) return;
        float y = gridManager != null ? gridManager.transform.position.y : 0f;
        float r = gridManager != null ? gridManager.nodeRadius : 0.5f;

        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.black;
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 12;

        for (int i = 0; i < Grid.NodeCount; i++)
        {
            SimNode n = Grid.ByIndex(i);
            if (!n.IsWalkable || n.BestCost == SimNode.Infinity) continue;

            Vector3 p = SceneMapBuilder.ToWorld(n.Position, y);
            SimNode next = Grid.NextOf(n);
            if (next != null)
            {
                Vector3 dir = (SceneMapBuilder.ToWorld(next.Position, y) - p).normalized;
                Gizmos.color = Color.blue;
                Vector3 startPos = p + Vector3.up * 0.1f;
                Vector3 endPos = startPos + dir * (r * 1.5f);
                Gizmos.DrawLine(startPos, endPos);
                Gizmos.DrawSphere(endPos, 0.1f);
            }

            // Costs are x10 internally (diagonal pricing), so show tile units.
            UnityEditor.Handles.Label(p + Vector3.up * 0.5f, (n.BestCost / 10f).ToString("0.#"), style);
        }
#endif
    }
}
