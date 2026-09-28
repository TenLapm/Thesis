using UnityEngine;

// View-side grid cell (WP4). Everything that is game state - terrain cost, wall
// health, the flow field's cost and next pointer, walkability - moved to
// Thesis.Sim.SimNode, the single copy the simulation owns. What is left is what
// only the scene needs: where the tile is in the world, and the wall GameObject
// standing on it.
//
// The design note that used to live here still holds, on SimNode: player walls are
// NOT unwalkable - they are expensive terrain with health, so a path to the goal
// always exists, and agents trade "walk around" against "dig through" on their own.
public class Node
{
    public Vector3 worldPosition;
    public int gridX;
    public int gridY;

    // The wall visual on this tile, if any (PlayerBuilder / ScenarioBenchmark).
    public GameObject visualObject;

    public Node(Vector3 worldPos, int x, int y)
    {
        worldPosition = worldPos;
        gridX = x;
        gridY = y;
    }
}
