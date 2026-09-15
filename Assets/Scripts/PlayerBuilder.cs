using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class PlayerBuilder : MonoBehaviour
{
    [Header("System References")]
    public GridManager gridManager;
    public FlowFieldManager flowManager;
    public BlockManager blockManager;
    public PlayerCore playerCore;
    [Tooltip("Used to forbid building while the game is paused - pause is now purely a look-and-think state, not a build-while-frozen state.")]
    public GameSpeedController speedController;

    [Tooltip("The enemy spawn point. Its tile (and the goal's tile) can never be built on.")]
    public Transform enemySpawnPoint;

    [Header("Prefabs & Layer")]
    public GameObject standardWallPrefab;
    public LayerMask clickLayer;

    // Reused across placements to avoid allocating a new block every wall.
    private MaterialPropertyBlock wallPropertyBlock;
    private bool isGameOver = false;

    // True while the player is not allowed to build. Shared with GhostPreviewer
    // so the hover preview hides itself instead of dangling a "valid" ghost over
    // a click that would do nothing.
    public bool IsBuildingBlocked
    {
        get { return isGameOver || (speedController != null && speedController.IsPaused); }
    }

    void Awake()
    {
        if (playerCore != null)
        {
            playerCore.OnGameOver.AddListener(() => isGameOver = true);
        }
    }

    void Update()
    {
        if (isGameOver) return;
        if (Mouse.current == null || Keyboard.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // No building while paused - pause is a plan-and-look state now, not a
            // build-while-frozen one. (Rotating/holding the piece still works, so
            // the player can still line up their NEXT move while paused.)
            if (speedController != null && speedController.IsPaused) return;

            // Without this, clicking Start/Restart (or any other HUD button)
            // would also raycast into the scene underneath and place a wall.
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                HandlePlacement();
            }
        }

        if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
        {
            blockManager.SwapHoldShape();
        }
    }

    // Placement is fully synchronous now. Because walls are expensive terrain
    // rather than absolute blockers, NO placement can ever disconnect the map -
    // so there is nothing to validate asynchronously, nothing to revert, and no
    // background thread to race against. Check-and-spend happens in one frame,
    // which is what fixed the old double-click negative-budget exploit.
    private void HandlePlacement()
    {
        if (standardWallPrefab == null)
        {
            Debug.LogError("PlayerBuilder: standardWallPrefab is not assigned in the Inspector!");
            return;
        }
        if (blockManager == null || blockManager.currentShape == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, clickLayer)) return;

        Node originNode = gridManager.NodeFromWorldPoint(hit.point);
        if (originNode == null) return;

        List<Vector2Int> targetTiles = blockManager.GetTargetGridPositions(originNode.gridX, originNode.gridY);

        if (!AreTilesPlaceable(targetTiles))
        {
            Debug.LogWarning("Placement blocked: out of bounds, overlapping a wall, or on a protected tile (spawn/core).");
            return;
        }

        BlockShape shape = blockManager.currentShape;
        int cost = shape.buildCost;
        if (blockManager.buildBudget < cost)
        {
            Debug.LogWarning($"Placement blocked: not enough build budget ({blockManager.buildBudget}/{cost}).");
            return;
        }

        // Deduct BEFORE building - budget can never go negative.
        blockManager.buildBudget -= cost;

        float cellSize = gridManager.nodeRadius * 2f;

        int tileIndex = 0;
        foreach (Vector2Int pos in targetTiles)
        {
            Node node = gridManager.grid[pos.x, pos.y];

            // Walls are diggable terrain, not blockers: raise the tile's path
            // cost and give it chew-through health from the shape's material.
            node.terrainCost = Mathf.Max(2, shape.digCost);
            node.wallHealth = Mathf.Max(0.5f, shape.wallHealth);
            node.maxWallHealth = node.wallHealth;

            GameObject newWall = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, cellSize / 2f, 0), Quaternion.identity);
            newWall.transform.localScale = new Vector3(cellSize, cellSize, cellSize);
            ApplyShapeColor(newWall, shape.shapeColor);

            // Pop-in flourish, staggered per tile so a multi-tile shape ripples in.
            // Added AFTER the final scale is set so the animator captures it as the
            // pop's target.
            WallSpawnAnimator anim = newWall.AddComponent<WallSpawnAnimator>();
            anim.Play(tileIndex * 0.04f);

            node.visualObject = newWall;
            tileIndex++;
        }

        // Synchronous rebuild - by the time this frame's agents move, every
        // cost, direction and nextNode on the map is already consistent.
        flowManager.GenerateFlowField();

        // true = destroy the consumed shape clone (fixes the ScriptableObject leak).
        blockManager.PullNextShape(true);
    }

    // Single source of truth for placement legality. GhostPreviewer calls this
    // too, so the preview can never disagree with an actual click.
    public bool AreTilesPlaceable(List<Vector2Int> tiles)
    {
        if (gridManager == null || gridManager.grid == null) return false;

        Node spawnNode = enemySpawnPoint != null ? gridManager.NodeFromWorldPoint(enemySpawnPoint.position) : null;
        Node goalNode = (flowManager != null && flowManager.targetGoal != null)
            ? gridManager.NodeFromWorldPoint(flowManager.targetGoal.position)
            : null;

        foreach (Vector2Int pos in tiles)
        {
            if (pos.x < 0 || pos.x >= gridManager.gridSizeX || pos.y < 0 || pos.y >= gridManager.gridSizeY)
                return false;

            Node node = gridManager.grid[pos.x, pos.y];
            if (!node.isWalkable) return false;                       // static geometry
            if (node.HasWall) return false;                           // existing wall
            if (node == spawnNode || node == goalNode) return false;  // protected tiles (old spawn-sealing exploit)
        }
        return true;
    }

    // Tints a placed obstacle with its shape's color (base color for normal
    // lighting, boosted emission so it glows / triggers Bloom). Uses a
    // MaterialPropertyBlock rather than a per-instance material so every wall
    // can have its own color without breaking batching.
    private void ApplyShapeColor(GameObject wall, Color shapeColor)
    {
        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        if (wallPropertyBlock == null) wallPropertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(wallPropertyBlock);

        Color emission = shapeColor * 2.5f;
        emission.a = 1f;

        wallPropertyBlock.SetColor("_BaseColor", shapeColor);
        wallPropertyBlock.SetColor("_EmissionColor", emission);
        renderer.SetPropertyBlock(wallPropertyBlock);
    }
}
