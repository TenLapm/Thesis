using UnityEngine;

// Plain C# grid cell. Player walls are no longer "unwalkable" - they are
// expensive terrain (high terrainCost) with health, so a path to the goal
// ALWAYS exists: enemies route around walls when the detour is cheap and chew
// through them when it isn't. This one design change is what removed the old
// chunk/sinkhole/validate/revert machinery and the bug class that lived in it.
public class Node
{
    public const int INFINITY = int.MaxValue;

    // Static geometry only (scanned from the unwalkable layer at startup).
    // Player walls never touch this flag.
    public bool isWalkable;

    public Vector3 worldPosition;
    public int gridX;
    public int gridY;

    // Pathfinding weight for ENTERING this tile. 1 = open ground. Player walls
    // raise this to their shape's digCost, so the integration field naturally
    // trades "walk around" against "dig through".
    public int terrainCost = 1;

    // > 0 means a player wall stands here, measured in seconds of single-agent
    // chewing. maxWallHealth is kept so the visual can shrink proportionally.
    public float wallHealth = 0f;
    public float maxWallHealth = 0f;

    public int bestCost = INFINITY;
    public Vector3 bestDirection = Vector3.zero;
    public Node nextNode;
    public GameObject visualObject;

    public bool HasWall { get { return wallHealth > 0f; } }

    // Used by GetNeighbors' corner-cut rule: agents may enter a wall tile
    // head-on to dig it, but may not slip diagonally BETWEEN two solid tiles.
    public bool BlocksCorner { get { return !isWalkable || HasWall; } }

    public Node(bool _isWalkable, Vector3 _worldPos, int _gridX, int _gridY)
    {
        isWalkable = _isWalkable;
        worldPosition = _worldPos;
        gridX = _gridX;
        gridY = _gridY;
        visualObject = null;
    }
}
